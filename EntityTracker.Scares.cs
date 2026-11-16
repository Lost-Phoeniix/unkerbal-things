using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Harmless scripted scares (light flicker, radio glitch, false proximity warning, glimpse of a monster).
// None of them damages or kills anything. Master switch: scriptedScares.
public partial class EntityTracker
{
	private enum ScareKind
	{
		Lights,
		Radio,
		Sensor
	}

	private static bool scriptedScares = true;
	private static bool scareLightsFail = true;
	private static bool scareRadioGlitch = true;
	private static bool scareSensorGhost = true;
	private static bool scareLightsDuringChase = true;
	private static float scareCheckSeconds = 60f;
	private static float scareChance = 0.08f;
	private static float scareCooldownSeconds = 600f;

	// Scheduler.
	private float scareClock;
	private float scareRollTimer;
	private float scareLastTime = -1000000f;
	private bool scareHooksAdded;
	private bool scareSceneChangePending;
	private bool scareChaseLightsUsed;
	private bool scareLightsExpectedOn = true;

	// Lights scare.
	private Vessel scareLightsVessel;
	private readonly List<ModuleLight> scareLightModules = new List<ModuleLight>();
	private int scareLightsPhase;
	private float scareLightsTimer;
	private float scareLightsFlickerTimer;
	private float scareLightsOffDuration;
	private bool scareLightsFlickerOn;

	// Radio audio.
	private GameObject scareAudioObject;
	private float scareAudioStopTimer;

	private static readonly string[] ScareRadioLines = new string[]
	{
		"...nobody is left on this frequency...",
		"...we are still counting you...",
		"...three aboard. we count four...",
		"...check the hull. check it again...",
		"...it knows your callsign...",
		"...do not answer the knocking...",
		"...your voice came back to us first...",
		"...the lights were never yours..."
	};

	private static readonly string[] ScareSensorLines = new string[]
	{
		"PROXIMITY ALERT: unidentified contact {0} m, closing",
		"PROXIMITY ALERT: contact {0} m, bearing unstable",
		"WARNING: object {0} m off the hull, no transponder"
	};

	private static void LoadScareSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "scriptedScares", ref scriptedScares);
		ReadBoolScareSetting(node, "scareLightsFail", ref scareLightsFail);
		ReadBoolScareSetting(node, "scareRadioGlitch", ref scareRadioGlitch);
		ReadBoolScareSetting(node, "scareSensorGhost", ref scareSensorGhost);
		ReadBoolScareSetting(node, "scareLightsDuringChase", ref scareLightsDuringChase);
		ReadFloatSetting(node, "scareCheckSeconds", ref scareCheckSeconds);
		ReadFloatSetting(node, "scareChance", ref scareChance);
		ReadFloatSetting(node, "scareCooldownSeconds", ref scareCooldownSeconds);
		scareCheckSeconds = Mathf.Clamp(scareCheckSeconds, 1f, 3600f);
		scareChance = Mathf.Clamp01(scareChance);
		scareCooldownSeconds = Mathf.Clamp(scareCooldownSeconds, 0f, 86400f);
	}

	private static void ReadBoolScareSetting(ConfigNode node, string name, ref bool value)
	{
		if (node != null && node.HasValue(name))
		{
			bool.TryParse(node.GetValue(name), out value);
		}
	}

	// ---- Conditions ----

	private bool IsScareContextOk()
	{
		if (!HighLogic.LoadedSceneIsFlight || scareSceneChangePending || Time.timeScale <= 0f)
		{
			return false;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || !activeVessel.loaded || activeVessel.mainBody == null)
		{
			return false;
		}
		return TimeWarp.CurrentRate <= 1.001f;
	}

	private bool IsRealChaseActive()
	{
		return (bulkEntityActive && bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating) || (bloopEntityActive && bloopEntityChasing);
	}

	private bool IsAnyRealMonsterActive()
	{
		return bulkEntityActive || bloopEntityActive;
	}

	private bool IsRealEncounterBlocking()
	{
		return attackSequenceList.Count > 0;
	}

	private bool IsScareBusy()
	{
		return scareLightsPhase != 0;
	}

	// ---- Scheduler / hooks ----

	private void EnsureScareHooks()
	{
		if (!scareHooksAdded)
		{
			scareHooksAdded = true;
			GameEvents.onGameSceneLoadRequested.Add(OnScareSceneLoadRequested);
			GameEvents.onVesselChange.Add(OnScareVesselChange);
			GameEvents.onLevelWasLoadedGUIReady.Add(OnScareLevelLoaded);
			GameEvents.onFlightReady.Add(OnScareFlightReady);
		}
	}

	private void OnScareSceneLoadRequested(GameScenes scene)
	{
		scareSceneChangePending = true;
		EndAllScares();
	}

	// The addon instance survives scene changes (DontDestroyOnLoad), so the pending flag must be cleared once the next scene is up.
	private void OnScareLevelLoaded(GameScenes scene)
	{
		scareSceneChangePending = false;
		scareRollTimer = 0f;
	}

	private void OnScareFlightReady()
	{
		scareSceneChangePending = false;
	}

	private void OnScareVesselChange(Vessel vessel)
	{
		EndAllScares();
	}

	// Called once per frame from Update.
	private void UpdateScares()
	{
		if (!scriptedScares)
		{
			if (IsScareBusy() || scareAudioObject != null)
			{
				EndAllScares();
			}
			return;
		}
		EnsureScareHooks();
		if (!HighLogic.LoadedSceneIsFlight)
		{
			if (IsScareBusy() || scareAudioObject != null)
			{
				EndAllScares();
			}
			return;
		}
		UpdateScareAudio();
		UpdateLightsScare();
		if (!IsScareContextOk())
		{
			return;
		}
		float deltaTime = Mathf.Min(Time.deltaTime, 0.25f);
		scareClock += deltaTime;
		bool chasing = IsRealChaseActive();
		if (!chasing)
		{
			scareChaseLightsUsed = false;
		}
		if (IsRealEncounterBlocking())
		{
			return;
		}
		scareRollTimer += deltaTime;
		if (scareRollTimer < scareCheckSeconds)
		{
			return;
		}
		scareRollTimer = 0f;
		if (IsScareBusy() || scareClock - scareLastTime < scareCooldownSeconds || UnityEngine.Random.value > scareChance)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		List<ScareKind> list = new List<ScareKind>();
		if (scareLightsFail && (!chasing || (scareLightsDuringChase && !scareChaseLightsUsed)) && CanStartLightsScare(activeVessel, forced: false))
		{
			list.Add(ScareKind.Lights);
		}
		if (!chasing)
		{
			if (scareRadioGlitch)
			{
				list.Add(ScareKind.Radio);
			}
			if (scareSensorGhost)
			{
				list.Add(ScareKind.Sensor);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		ScareKind scareKind = list[UnityEngine.Random.Range(0, list.Count)];
		if (StartScare(scareKind, forced: false))
		{
			scareLastTime = scareClock;
			if (scareKind == ScareKind.Lights && chasing)
			{
				scareChaseLightsUsed = true;
			}
		}
	}

	private bool StartScare(ScareKind kind, bool forced)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!IsScareContextOk() || activeVessel == null)
		{
			return false;
		}
		bool started = false;
		switch (kind)
		{
		case ScareKind.Lights:
			started = StartLightsScare(activeVessel, forced);
			break;
		case ScareKind.Radio:
			StartRadioScare();
			started = true;
			break;
		case ScareKind.Sensor:
			StartSensorScare();
			started = true;
			break;
		}
		if (started)
		{
			DreadOnScare(kind);
		}
		return started;
	}

	private void EndAllScares()
	{
		RestoreScareLights();
		DestroyScareAudio();
	}

	// Called from OnDestroy.
	private void ShutdownScares()
	{
		if (scareHooksAdded)
		{
			scareHooksAdded = false;
			GameEvents.onGameSceneLoadRequested.Remove(OnScareSceneLoadRequested);
			GameEvents.onVesselChange.Remove(OnScareVesselChange);
			GameEvents.onLevelWasLoadedGUIReady.Remove(OnScareLevelLoaded);
			GameEvents.onFlightReady.Remove(OnScareFlightReady);
		}
		EndAllScares();
	}

	// ---- 1. Lights fail ----

	private bool CanStartLightsScare(Vessel vessel, bool forced)
	{
		if (vessel == null || scareLightsPhase != 0)
		{
			return false;
		}
		if (!forced && !IsNightAtVessel(vessel))
		{
			return false;
		}
		List<ModuleLight> list = vessel.FindPartModulesImplementing<ModuleLight>();
		if (list == null)
		{
			return false;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] != null && list[i].isOn)
			{
				return true;
			}
		}
		return false;
	}

	private bool StartLightsScare(Vessel vessel, bool forced)
	{
		if (!CanStartLightsScare(vessel, forced))
		{
			return false;
		}
		scareLightModules.Clear();
		List<ModuleLight> list = vessel.FindPartModulesImplementing<ModuleLight>();
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] != null && list[i].isOn)
			{
				scareLightModules.Add(list[i]);
			}
		}
		if (scareLightModules.Count == 0)
		{
			return false;
		}
		scareLightsVessel = vessel;
		scareLightsPhase = 1;
		scareLightsTimer = 0f;
		scareLightsFlickerTimer = 0f;
		scareLightsFlickerOn = true;
		scareLightsExpectedOn = true;
		scareLightsOffDuration = UnityEngine.Random.Range(5f, 15f);
		return true;
	}

	private void UpdateLightsScare()
	{
		if (scareLightsPhase == 0)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!HighLogic.LoadedSceneIsFlight || scareSceneChangePending || activeVessel == null || activeVessel != scareLightsVessel || scareLightsVessel == null || attackSequenceList.Count > 0)
		{
			RestoreScareLights();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			return;
		}
		DropPlayerToggledScareLights();
		if (scareLightModules.Count == 0)
		{
			RestoreScareLights();
			return;
		}
		float num = Mathf.Min(Time.deltaTime, 0.25f);
		scareLightsTimer += num;
		if (scareLightsPhase == 1)
		{
			if (scareLightsTimer >= 2f)
			{
				SetScareLights(on: false);
				scareLightsPhase = 2;
				scareLightsTimer = 0f;
				return;
			}
			scareLightsFlickerTimer -= num;
			if (scareLightsFlickerTimer <= 0f)
			{
				scareLightsFlickerTimer = UnityEngine.Random.Range(0.05f, 0.25f);
				scareLightsFlickerOn = !scareLightsFlickerOn;
				SetScareLights(scareLightsFlickerOn);
			}
		}
		else if (scareLightsTimer >= scareLightsOffDuration)
		{
			RestoreScareLights();
		}
	}

	// A light whose state no longer matches what the scare set was changed by the player (or lost power): leave it alone.
	private void DropPlayerToggledScareLights()
	{
		for (int i = scareLightModules.Count - 1; i >= 0; i--)
		{
			ModuleLight moduleLight = scareLightModules[i];
			if (moduleLight == null || moduleLight.part == null || moduleLight.isOn != scareLightsExpectedOn)
			{
				scareLightModules.RemoveAt(i);
			}
		}
	}

	private void SetScareLights(bool on)
	{
		scareLightsExpectedOn = on;
		for (int i = 0; i < scareLightModules.Count; i++)
		{
			ModuleLight moduleLight = scareLightModules[i];
			if (moduleLight == null || moduleLight.part == null)
			{
				continue;
			}
			try
			{
				if (moduleLight.isOn != on)
				{
					moduleLight.SetLightState(on);
				}
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.Log("[EntityTracker] Scare light toggle failed: " + ex.Message);
			}
		}
	}

	// Turns back on exactly the lights that were on when the scare began, and clears the scare.
	private void RestoreScareLights()
	{
		if (scareLightsPhase != 0)
		{
			for (int i = 0; i < scareLightModules.Count; i++)
			{
				ModuleLight moduleLight = scareLightModules[i];
				if (moduleLight != null && moduleLight.part != null && !moduleLight.isOn && moduleLight.part.vessel != null)   // also parts decoupled during the scare: no light stays dark for good
				{
					try
					{
						moduleLight.SetLightState(true);
					}
					catch (Exception ex)
					{
						UnityEngine.Debug.Log("[EntityTracker] Scare light restore failed: " + ex.Message);
					}
				}
			}
		}
		scareLightModules.Clear();
		scareLightsVessel = null;
		scareLightsPhase = 0;
		scareLightsTimer = 0f;
	}

	// ---- 2. Radio glitch ----

	private void StartRadioScare()
	{
		string text = ScareRadioLines[UnityEngine.Random.Range(0, ScareRadioLines.Length)];
		ScreenMessages.PostScreenMessage(GarbleScareText(text, 0.45f), 2.5f, ScreenMessageStyle.UPPER_CENTER, new Color(0.7f, 1f, 0.7f));
		StartCoroutine(ScareDelayedMessage(1.6f, GarbleScareText(text, 0.12f), 4f, new Color(0.7f, 1f, 0.7f)));
		PlayScareSound();
	}

	private static string GarbleScareText(string text, float amount)
	{
		const string glyphs = "#%&@?/|*=~0X";
		char[] array = text.ToCharArray();
		for (int i = 0; i < array.Length; i++)
		{
			if (char.IsLetter(array[i]) && UnityEngine.Random.value < amount)
			{
				array[i] = glyphs[UnityEngine.Random.Range(0, glyphs.Length)];
			}
		}
		return "[SIGNAL] " + new string(array);
	}

	private IEnumerator ScareDelayedMessage(float delay, string message, float duration, Color color)
	{
		float waited = 0f;
		while (waited < delay)
		{
			yield return null;
			if (Time.timeScale > 0f)
			{
				waited += Time.deltaTime;
			}
		}
		if (HighLogic.LoadedSceneIsFlight && !scareSceneChangePending && scriptedScares)
		{
			ScreenMessages.PostScreenMessage(message, duration, ScreenMessageStyle.UPPER_CENTER, color);
		}
	}

	private void PlayScareSound()
	{
		if (DreadSuppressesRandomCue())
		{
			return;   // Dread on: the synth's static plays instead (see DreadOnScare)
		}
		// the original's stinger recordings are gone; without Dread the radio scare is text only
	}

	private void UpdateScareAudio()
	{
		if (scareAudioObject == null)
		{
			return;
		}
		scareAudioStopTimer -= Time.unscaledDeltaTime;
		AudioSource component = scareAudioObject.GetComponent<AudioSource>();
		if (scareAudioStopTimer <= 0f || component == null || !component.isPlaying && Time.timeScale > 0f)
		{
			DestroyScareAudio();
		}
		else if (scareAudioStopTimer < 1f)
		{
			component.volume = 0.45f * Mathf.Clamp01(scareAudioStopTimer);
		}
	}

	private void DestroyScareAudio()
	{
		if (scareAudioObject != null)
		{
			UnityEngine.Object.Destroy(scareAudioObject);
		}
		scareAudioObject = null;
	}

	// ---- 3. Sensor ghost (fallback: false proximity warning; the map contacts cannot be triggered on demand) ----

	private void StartSensorScare()
	{
		int num = UnityEngine.Random.Range(25, 90);
		string format = ScareSensorLines[UnityEngine.Random.Range(0, ScareSensorLines.Length)];
		Color color = new Color(1f, 0.35f, 0.25f);
		ScreenMessages.PostScreenMessage(string.Format(format, num), 3.5f, ScreenMessageStyle.UPPER_CENTER, color);
		StartCoroutine(ScareDelayedMessage(4f, "Contact lost.", 3f, color));
	}

	// ---- 4. Visibility ----

	// Generalised from IsRakeVisibleToPlayer: is the point upOffset above the visual's origin inside the main camera's view, with nothing but the vessel and the visual in the way.
	private bool IsVisualVisibleToPlayer(Vessel vessel, GameObject visual, CelestialBody body, float upOffset)
	{
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (camera == null || visual == null || body == null)
		{
			return false;
		}
		Vector3 normalized = ((Vector3)(visual.transform.position - body.position)).normalized;
		Vector3 vector = visual.transform.position + normalized * upOffset;
		Vector3 vector2 = camera.WorldToViewportPoint(vector);
		if (vector2.z <= 0f || vector2.x < 0f || vector2.x > 1f || vector2.y < 0f || vector2.y > 1f)
		{
			return false;
		}
		Vector3 vector3 = vector - camera.transform.position;
		float magnitude = vector3.magnitude;
		if (magnitude < 0.01f)
		{
			return true;
		}
		RaycastHit[] array = Physics.RaycastAll(camera.transform.position, vector3 / magnitude, magnitude, -1, QueryTriggerInteraction.Ignore);
		RaycastHit[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			RaycastHit raycastHit = array2[i];
			Transform transform = ((raycastHit.collider != null) ? raycastHit.collider.transform : null);
			if (!(transform == null) && (!(vessel != null) || !transform.IsChildOf(vessel.transform)) && !transform.IsChildOf(visual.transform))
			{
				return false;
			}
		}
		return true;
	}

	// ---- Debug buttons (called from the toolbar panel when debugTools is on) ----

	private void DrawScareDebugButtons()
	{
		if (!scriptedScares)
		{
			GUILayout.Label("Off (scriptedScares = false).");
			return;
		}
		string block = FlightGlobals.ActiveVessel == null ? "no craft" : null;
		GUILayout.BeginHorizontal();
		if (DebugButton("Lights fail", block ?? (scareLightsFail ? null : "off")))
		{
			if (!StartScare(ScareKind.Lights, forced: true))
			{
				ScreenMessages.PostScreenMessage("No lit lights, or a scare is already running.", 3f, ScreenMessageStyle.UPPER_CENTER);
			}
		}
		if (DebugButton("Radio glitch", block ?? (scareRadioGlitch ? null : "off")))
		{
			StartScare(ScareKind.Radio, forced: true);
		}
		if (DebugButton("False proximity warning", block ?? (scareSensorGhost ? null : "off")))
		{
			StartScare(ScareKind.Sensor, forced: true);
		}
		GUILayout.EndHorizontal();
	}
}
