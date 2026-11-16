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
		Sensor,
		Glimpse
	}

	private static bool scriptedScares = true;
	private static bool scareLightsFail = true;
	private static bool scareRadioGlitch = true;
	private static bool scareSensorGhost = true;
	private static bool scareGlimpse = true;
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

	// Glimpse scare.
	private GameObject scareGlimpseObject;
	private CelestialBody scareGlimpseBody;
	private Vessel scareGlimpseVessel;
	private Vector3d scareGlimpseRelative;
	private Vector3d scareGlimpseAlign;
	private float scareGlimpseUpOffset;
	private float scareGlimpseAge;
	private float scareGlimpseLook;
	private float scareGlimpseCheckTimer;

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
		ReadBoolScareSetting(node, "scareGlimpse", ref scareGlimpse);
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
		return (tracking && !arrived) || (groundEntityActive && groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating) || (bulkEntityActive && bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating) || (laytheCreatureActive && laytheCreatureChasing) || (seaEntityActive && seaEntityChasing) || (bloopEntityActive && bloopEntityChasing) || (rakeEntityActive && rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating) || (eyeEntityActive && eyeEntityChasing);
	}

	private bool IsAnyRealMonsterActive()
	{
		return groundEntityActive || bulkEntityActive || laytheCreatureActive || seaEntityActive || bloopEntityActive || rakeEntityActive || eyeEntityActive || grabberEntityActive || dresSomethingActive;
	}

	private bool IsRealEncounterBlocking()
	{
		return attackSequenceList.Count > 0 || grabberEntityActive || dresSomethingActive;
	}

	private bool IsScareBusy()
	{
		return scareLightsPhase != 0 || scareGlimpseObject != null;
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
		UpdateGlimpseScare();
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
			if (scareGlimpse && CanStartGlimpse(activeVessel))
			{
				list.Add(ScareKind.Glimpse);
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
		case ScareKind.Glimpse:
			started = StartGlimpseScare(activeVessel);
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
		DestroyScareGlimpse();
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
				if (moduleLight != null && moduleLight.part != null && !moduleLight.isOn && moduleLight.part.vessel == scareLightsVessel)
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
			return;   // Dread on: the old stinger is replaced by the synth's static (see DreadOnScare)
		}
		AudioClip audioClip = null;
		if (randomHorrorLoaded && randomHorrorAudio != null && !randomHorrorAudio.isPlaying)
		{
			audioClip = randomHorrorAudio.clip;
		}
		if (audioClip == null)
		{
			audioClip = normalNearbyClip;
		}
		if (audioClip == null)
		{
			return;
		}
		DestroyScareAudio();
		scareAudioObject = new GameObject("EntityTracker_ScareAudio");
		AudioSource audioSource = scareAudioObject.AddComponent<AudioSource>();
		audioSource.playOnAwake = false;
		audioSource.loop = false;
		audioSource.spatialBlend = 0f;
		audioSource.volume = 0.45f;
		audioSource.clip = audioClip;
		audioSource.Play();
		scareAudioStopTimer = Mathf.Min(audioClip.length, 6f);
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

	// ---- 4. Glimpse ----

	private bool CanStartGlimpse(Vessel vessel)
	{
		if (vessel == null || vessel.mainBody == null || scareGlimpseObject != null || vessel.Splashed || MapView.MapIsEnabled || IsRealChaseActive() || IsRealEncounterBlocking() || IsAnyRealMonsterActive())
		{
			return false;
		}
		if (!vessel.Landed && !(vessel.heightFromTerrain >= 0f && vessel.heightFromTerrain < 100f))
		{
			return false;
		}
		return GetGlimpseKinds(vessel.mainBody).Count > 0;
	}

	// 0 Mimic, 1 Bulk, 2 Laythe creature, 3 Rake.
	private static List<int> GetGlimpseKinds(CelestialBody body)
	{
		List<int> list = new List<int>();
		if (body == null)
		{
			return list;
		}
		if (string.Equals(body.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(0);
			return list;
		}
		if (string.Equals(body.bodyName, "Mun", StringComparison.OrdinalIgnoreCase))
		{
			return list;
		}
		if (IsBulkEntityBody(body))
		{
			list.Add(1);
		}
		if (string.Equals(body.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(2);
		}
		if (body.pqsController != null)
		{
			list.Add(3);
		}
		return list;
	}

	private static GameObject CreateImportedScareVisual(string model, Vector3 scale, bool strip, bool lit)
	{
		GameObject gameObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/" + model) : null);
		if (gameObject == null)
		{
			return null;
		}
		gameObject.SetActive(value: true);
		gameObject.transform.localScale = scale;
		Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Renderer[] componentsInChildren2 = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
		}
		Collider[] componentsInChildren3 = gameObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren4 = gameObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		if (strip)
		{
			StripImportedEntityControllers(gameObject);
		}
		DisableImportedRootMotion(gameObject);
		if (lit)
		{
			ConfigureLitEntityMaterials(gameObject);
		}
		return gameObject;
	}

	private bool StartGlimpseScare(Vessel vessel)
	{
		if (!CanStartGlimpse(vessel))
		{
			return false;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : null);
		if (camera == null)
		{
			return false;
		}
		CelestialBody mainBody = vessel.mainBody;
		Vector3d worldPos3D = vessel.GetWorldPos3D();
		Vector3 normalized = ((Vector3)(worldPos3D - mainBody.position)).normalized;
		Vector3 vector = -camera.transform.forward;
		vector = Vector3.ProjectOnPlane(vector, normalized);
		if (vector.sqrMagnitude < 0.01f)
		{
			vector = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, normalized);
		}
		vector.Normalize();
		bool flag = false;
		Vector3d vector3d = Vector3d.zero;
		for (int i = 0; i < 16; i++)
		{
			Vector3 vector2 = Quaternion.AngleAxis(UnityEngine.Random.Range(-55f, 55f), normalized) * vector;
			Vector3d worldPosition = worldPos3D + (Vector3d)(vector2 * UnityEngine.Random.Range(150f, 400f));
			Vector3d vector3d2 = ProjectToGround(worldPosition, mainBody);
			double magnitude = (vector3d2 - worldPos3D).magnitude;
			if (magnitude < 140.0 || magnitude > 420.0 || !IsLandAt(vector3d2, mainBody))
			{
				continue;
			}
			Vector3 vector3 = camera.WorldToViewportPoint((Vector3)vector3d2 + ((Vector3)(vector3d2 - mainBody.position)).normalized * 4f);
			if (vector3.z > 0f && vector3.x > -0.1f && vector3.x < 1.1f && vector3.y > -0.1f && vector3.y < 1.1f)
			{
				continue;
			}
			if (!HasGroundLineOfSight(vector3d2, worldPos3D, mainBody))
			{
				continue;
			}
			vector3d = vector3d2;
			flag = true;
			break;
		}
		if (!flag)
		{
			return false;
		}
		List<int> glimpseKinds = GetGlimpseKinds(mainBody);
		int num = glimpseKinds[UnityEngine.Random.Range(0, glimpseKinds.Count)];
		GameObject gameObject;
		float num2 = 4f;
		switch (num)
		{
		case 0:
			gameObject = CreateGroundEntityVisual(GroundVariant.Engineer);
			break;
		case 1:
			gameObject = CreateBulkEntityVisual();
			num2 = 5f;
			break;
		case 2:
			gameObject = CreateImportedScareVisual("the_smile", LaytheCreatureVisualScaleVector, strip: true, lit: true);
			break;
		default:
			gameObject = CreateImportedScareVisual("the_rake", Vector3.one * 1.2f, strip: false, lit: false);
			num2 = 7.66f;
			break;
		}
		if (gameObject == null)
		{
			return false;
		}
		gameObject.name = "EntityTracker_ScareGlimpse";
		Vector3 normalized2 = ((Vector3)(vector3d - mainBody.position)).normalized;
		Vector3d vector3d3 = worldPos3D - vector3d;
		vector3d3 -= (Vector3d)normalized2 * Vector3d.Dot(vector3d3, (Vector3d)normalized2);
		Vector3 vector4 = ((vector3d3.sqrMagnitude > 0.01) ? ((Vector3)vector3d3.normalized) : Vector3.ProjectOnPlane(vector, normalized2).normalized);
		Vector3d vector3d4 = vector3d;
		gameObject.transform.position = vector3d;
		switch (num)
		{
		case 0:
			gameObject.transform.LookAt((Vector3)vector3d + vector4, normalized2);
			break;
		case 1:
			gameObject.transform.rotation = Quaternion.LookRotation(vector4, normalized2) * Quaternion.Euler(0f, 180f, 0f);
			AlignVisualFeetToGround(gameObject, vector3d, normalized2, 0.03f);
			break;
		case 2:
			gameObject.transform.rotation = Quaternion.LookRotation(vector4, normalized2) * LaytheCreatureModelAxisCorrection;
			gameObject.transform.position = (Vector3)vector3d + normalized2 * 0.02f;
			AlignVisualFeetToGround(gameObject, vector3d, normalized2, 0.02f);
			break;
		default:
			gameObject.transform.rotation = Quaternion.LookRotation(vector4, normalized2) * Quaternion.Euler(0f, 180f, 0f) * RakeModelAxisCorrection;
			gameObject.transform.position = (Vector3)vector3d - normalized2 * 6.56f;
			break;
		}
		scareGlimpseObject = gameObject;
		scareGlimpseBody = mainBody;
		scareGlimpseVessel = vessel;
		scareGlimpseRelative = vector3d4 - mainBody.position;
		scareGlimpseAlign = (Vector3d)gameObject.transform.position - vector3d4;
		scareGlimpseUpOffset = num2;
		scareGlimpseAge = 0f;
		scareGlimpseLook = 0f;
		scareGlimpseCheckTimer = 0f;
		return true;
	}

	private void UpdateGlimpseScare()
	{
		if (scareGlimpseObject == null)
		{
			if (scareGlimpseBody != null || scareGlimpseVessel != null)
			{
				DestroyScareGlimpse();
			}
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!HighLogic.LoadedSceneIsFlight || scareSceneChangePending || activeVessel == null || activeVessel != scareGlimpseVessel || activeVessel.mainBody != scareGlimpseBody || scareGlimpseBody == null || TimeWarp.CurrentRate > 1.001f || IsRealChaseActive() || IsRealEncounterBlocking() || IsAnyRealMonsterActive())
		{
			DestroyScareGlimpse();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			return;
		}
		scareGlimpseObject.transform.position = (Vector3)(scareGlimpseBody.position + scareGlimpseRelative + scareGlimpseAlign);
		float num = Mathf.Min(Time.deltaTime, 0.25f);
		scareGlimpseAge += num;
		if (scareGlimpseAge >= 30f)
		{
			DestroyScareGlimpse();
			return;
		}
		scareGlimpseCheckTimer += num;
		if (scareGlimpseCheckTimer < 0.1f)
		{
			return;
		}
		float checkStep = scareGlimpseCheckTimer;
		scareGlimpseCheckTimer = 0f;
		if (!MapView.MapIsEnabled && IsVisualVisibleToPlayer(activeVessel, scareGlimpseObject, scareGlimpseBody, scareGlimpseUpOffset))
		{
			scareGlimpseLook += checkStep;
			if (scareGlimpseLook >= 0.7f)
			{
				DestroyScareGlimpse();
			}
		}
	}

	private void DestroyScareGlimpse()
	{
		if (scareGlimpseObject != null)
		{
			UnityEngine.Object.Destroy(scareGlimpseObject);
		}
		scareGlimpseObject = null;
		scareGlimpseBody = null;
		scareGlimpseVessel = null;
		scareGlimpseRelative = Vector3d.zero;
		scareGlimpseAlign = Vector3d.zero;
		scareGlimpseAge = 0f;
		scareGlimpseLook = 0f;
	}

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
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		if (scareLightsFail && GUILayout.Button("Scare: lights fail"))
		{
			if (!StartScare(ScareKind.Lights, forced: true))
			{
				ScreenMessages.PostScreenMessage("No lit lights, or a scare is already running.", 3f, ScreenMessageStyle.UPPER_CENTER);
			}
		}
		if (scareRadioGlitch && GUILayout.Button("Scare: radio glitch"))
		{
			StartScare(ScareKind.Radio, forced: true);
		}
		if (scareSensorGhost && GUILayout.Button("Scare: false proximity warning"))
		{
			StartScare(ScareKind.Sensor, forced: true);
		}
		if (scareGlimpse && GUILayout.Button("Scare: glimpse"))
		{
			if (!StartScare(ScareKind.Glimpse, forced: true))
			{
				ScreenMessages.PostScreenMessage("Glimpse needs: landed or below 100 m, a suitable body, no chase.", 3f, ScreenMessageStyle.UPPER_CENTER);
			}
		}
	}
}
