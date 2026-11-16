using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KrakensKreations.Dread;
using UnityEngine;

// Wires the Dread presentation (director, screen shader, synth, instruments) to the real game state.
// Master switch dreadEnabled. false: UpdateDread returns at once and every hook below behaves as before this unit.
// Any exception in the Dread update is caught, logged once, and disables Dread; monster logic never depends on it.
public partial class EntityTracker
{
	// ---- settings (see settings.cfg) ----
	private static bool dreadEnabled = true;
	private static bool dreadScreen = true;
	private static float dreadScreenStrength = 1f;
	private static string dreadScreenMethod = "onRenderImage";
	private static bool dreadGrade = true;
	private static bool dreadVignette = true;
	private static bool dreadGrain = true;
	private static bool dreadSignal = true;
	private static bool dreadFeedFreeze = true;
	private static bool dreadAudio = true;
	private static float dreadAudioVolume = 1f;
	private static bool dreadDrone = true;
	private static bool dreadHullSounds = true;
	private static bool dreadCreature = true;
	private static bool dreadStatic = true;
	private static bool dreadMusicDuck = true;
	private static float dreadMusicDuckDb = 9f;
	private static bool dreadListenerLowpass = true;
	private static float dreadLowpassHz = 2500f;
	private static bool dreadInstruments = true;
	private static bool dreadPortraitStatic = true;
	private static bool dreadInstrumentJitter = true;
	private static bool dreadIvaLights = true;
	private static bool dreadEvents = true;
	private static bool dreadFalseAlarms = true;
	private static bool dreadMonsterRelease = true;
	private static bool dreadMonsterNoise = true;
	private static float dreadDarkLoseSeconds = 12f;
	private static bool dreadTufxSafe = true;
	private static bool dreadRandomHorrorCue = false;
	private static bool dreadLimitScreams = true;
	private static float dreadScreamMaxSeconds = 8f;
	private static float dreadScreamCooldownSeconds = 240f;
	private static int dreadSeed = 0;
	private static bool dreadFootsteps = true;
	private static float dreadFootstepsFarMetres = 2500f;
	private static float dreadFootstepsNearMetres = 30f;

	// ---- hints read by the monsters (static: the helpers that read them are static). Never touched by anything that can throw. ----
	private static bool dreadReleaseHint;
	private static float dreadDarkSeconds;

	// ---- runtime ----
	private DreadDirector dreadDirector;
	private readonly DreadScreenDriver dreadScreenDriver = new DreadScreenDriver();
	private DreadCamera dreadCamera;
	private GameObject dreadHostObject;
	private DreadAudioHost dreadAudioHost;
	private DreadInstrumentsHost dreadInstrumentsHost;
	private readonly DreadMusicDuck dreadMusicDuckState = new DreadMusicDuck();
	private readonly DreadListenerLowpass dreadLowpassState = new DreadListenerLowpass();
	private readonly DreadScreamGate groundScreamGate = new DreadScreamGate();
	private readonly DreadScreamGate laytheScreamGate = new DreadScreamGate();
	private bool dreadFailed;
	private bool dreadStarted;
	private bool dreadHooked;
	private bool dreadSceneChanging;
	private int dreadFrame;
	private float dreadLosTimer;
	private bool dreadLosCached;
	private float dreadCleanTimer;
	private bool dreadWasChasing;
	private bool dreadCreatureDone;
	private float dreadLastCreatureTime = -1000f;
	private float dreadLastCueTime = -1000f;
	private float dreadStaticPulse;
	private float dreadStaticPulseLevel;
	private bool dreadCameraNoticeLogged;
	private DreadOutput dreadLast;
	private double dreadLastDistance = -1.0;

	// Debug overrides (toolbar, debugTools only).
	private float dreadDebugMenace = -1f;
	internal string dreadDebugCameraName;   // test only: forces the camera the pass attaches to
	private DreadEventKind dreadForceKind;
	private float dreadForceRemaining;

	// TUFX glitch restore.
	private string dreadTufxSavedProfile;
	private float dreadTufxRestoreTimer;

	// Footsteps.
	private DreadStepLogic dreadStepLogic;
	private int dreadStepWalker = -1;
	private Vector3d dreadStepLastRelative;
	private float dreadStepSpeed;

	private struct DreadCandidate
	{
		public bool valid;
		public double distance;
		public bool chasing;
		public CelestialBody losBody;
		public Vector3d position;
		public HuntMonster hunt;
	}

	// ------------------------------------------------------------------ settings

	private static void LoadDreadSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "dreadEnabled", ref dreadEnabled);
		ReadBoolScareSetting(node, "dreadScreen", ref dreadScreen);
		ReadFloatSetting(node, "dreadScreenStrength", ref dreadScreenStrength);
		ReadStringDreadSetting(node, "dreadScreenMethod", ref dreadScreenMethod);
		ReadBoolScareSetting(node, "dreadGrade", ref dreadGrade);
		ReadBoolScareSetting(node, "dreadVignette", ref dreadVignette);
		ReadBoolScareSetting(node, "dreadGrain", ref dreadGrain);
		ReadBoolScareSetting(node, "dreadSignal", ref dreadSignal);
		ReadBoolScareSetting(node, "dreadFeedFreeze", ref dreadFeedFreeze);
		ReadBoolScareSetting(node, "dreadAudio", ref dreadAudio);
		ReadFloatSetting(node, "dreadAudioVolume", ref dreadAudioVolume);
		ReadBoolScareSetting(node, "dreadDrone", ref dreadDrone);
		ReadBoolScareSetting(node, "dreadHullSounds", ref dreadHullSounds);
		ReadBoolScareSetting(node, "dreadCreature", ref dreadCreature);
		ReadBoolScareSetting(node, "dreadStatic", ref dreadStatic);
		ReadBoolScareSetting(node, "dreadMusicDuck", ref dreadMusicDuck);
		ReadFloatSetting(node, "dreadMusicDuckDb", ref dreadMusicDuckDb);
		ReadBoolScareSetting(node, "dreadListenerLowpass", ref dreadListenerLowpass);
		ReadFloatSetting(node, "dreadLowpassHz", ref dreadLowpassHz);
		ReadBoolScareSetting(node, "dreadInstruments", ref dreadInstruments);
		ReadBoolScareSetting(node, "dreadPortraitStatic", ref dreadPortraitStatic);
		ReadBoolScareSetting(node, "dreadInstrumentJitter", ref dreadInstrumentJitter);
		ReadBoolScareSetting(node, "dreadIvaLights", ref dreadIvaLights);
		ReadBoolScareSetting(node, "dreadEvents", ref dreadEvents);
		ReadBoolScareSetting(node, "dreadFalseAlarms", ref dreadFalseAlarms);
		ReadBoolScareSetting(node, "dreadMonsterRelease", ref dreadMonsterRelease);
		ReadBoolScareSetting(node, "dreadMonsterNoise", ref dreadMonsterNoise);
		ReadFloatSetting(node, "dreadDarkLoseSeconds", ref dreadDarkLoseSeconds);
		ReadBoolScareSetting(node, "dreadTufxSafe", ref dreadTufxSafe);
		ReadBoolScareSetting(node, "dreadRandomHorrorCue", ref dreadRandomHorrorCue);
		ReadBoolScareSetting(node, "dreadLimitScreams", ref dreadLimitScreams);
		ReadFloatSetting(node, "dreadScreamMaxSeconds", ref dreadScreamMaxSeconds);
		ReadFloatSetting(node, "dreadScreamCooldownSeconds", ref dreadScreamCooldownSeconds);
		float seed = dreadSeed;
		ReadFloatSetting(node, "dreadSeed", ref seed);
		dreadSeed = (int)seed;
		ReadBoolScareSetting(node, "dreadFootsteps", ref dreadFootsteps);
		ReadFloatSetting(node, "dreadFootstepsFarMetres", ref dreadFootstepsFarMetres);
		ReadFloatSetting(node, "dreadFootstepsNearMetres", ref dreadFootstepsNearMetres);
		LoadDreadSoundSettings(node);
		dreadFootstepsNearMetres = Mathf.Clamp(dreadFootstepsNearMetres, 1f, 1000f);
		dreadFootstepsFarMetres = Mathf.Clamp(dreadFootstepsFarMetres, dreadFootstepsNearMetres * 2f, 20000f);
		dreadScreenStrength = Mathf.Clamp01(dreadScreenStrength);
		dreadAudioVolume = Mathf.Clamp(dreadAudioVolume, 0f, 2f);
		dreadMusicDuckDb = Mathf.Clamp(dreadMusicDuckDb, 0f, 24f);
		dreadLowpassHz = Mathf.Clamp(dreadLowpassHz, 500f, 20000f);
		dreadDarkLoseSeconds = Mathf.Clamp(dreadDarkLoseSeconds, 3f, 300f);
		dreadScreamMaxSeconds = Mathf.Clamp(dreadScreamMaxSeconds, 0.5f, 120f);
		dreadScreamCooldownSeconds = Mathf.Clamp(dreadScreamCooldownSeconds, 0f, 3600f);
		if (dreadScreenMethod == null)
		{
			dreadScreenMethod = "onRenderImage";
		}
	}

	private static void ReadStringDreadSetting(ConfigNode node, string name, ref string value)
	{
		if (node != null && node.HasValue(name))
		{
			value = node.GetValue(name);
		}
	}

	// ------------------------------------------------------------------ monster-facing hooks (cannot throw)

	// True while the director asks monsters to back off (release window). Soft hint only.
	private static bool DreadWantsRelease()
	{
		return dreadReleaseHint;
	}

	// True when a non-charging ground monster should lose a target that has been dark (engines off, lights off) long enough.
	private static bool DreadDarkLosesTarget(GroundBehaviorState behavior, double distance)
	{
		if (huntAi || !dreadMonsterNoise || dreadDarkSeconds < dreadDarkLoseSeconds)
		{
			return false;
		}
		if (behavior == null || behavior.phase == BehaviorPhase.Charge || behavior.leaping || behavior.lift > 0.01f)
		{
			return false;
		}
		return distance > 150.0;
	}

	private static bool DreadSuppressesRandomCue()
	{
		return dreadEnabled && !dreadRandomHorrorCue;
	}

	private bool DreadAllowScream(DreadScreenGateKind kind, bool wanted)
	{
		if (!dreadEnabled || !dreadLimitScreams)
		{
			return wanted;
		}
		DreadScreamGate gate = (kind == DreadScreenGateKind.Ground) ? groundScreamGate : laytheScreamGate;
		return gate.Allow(wanted, Time.unscaledDeltaTime, dreadScreamMaxSeconds, dreadScreamCooldownSeconds) && wanted;
	}

	private enum DreadScreenGateKind
	{
		Ground,
		Laythe
	}

	// ------------------------------------------------------------------ per-frame entry

	private void UpdateDread()
	{
		if (dreadFailed)
		{
			return;
		}
		try
		{
			UpdateDreadTufxRestore();
			if (!dreadEnabled)
			{
				if (dreadStarted)
				{
					ShutdownDread(destroyHost: false);
				}
				return;
			}
			UpdateDreadCore();
		}
		catch (Exception ex)
		{
			dreadFailed = true;
			Debug.Log("[KK Dread] disabled after an exception (monsters keep running): " + ex);
			try
			{
				ShutdownDread(destroyHost: true);
			}
			catch (Exception)
			{
			}
		}
	}

	private void UpdateDreadCore()
	{
		EnsureDreadHooks();
		bool flight = HighLogic.LoadedSceneIsFlight;
		Vessel vessel = flight ? FlightGlobals.ActiveVessel : null;
		if (!flight || vessel == null || !vessel.loaded || dreadSceneChanging)
		{
			DreadSuspend();
			return;
		}
		if (!dreadStarted)
		{
			dreadStarted = true;
			DreadCamera.EnsureShader(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Shaders", "kk-dread.bundle"));
		}
		if (dreadDirector == null)
		{
			int seed = dreadSeed != 0 ? dreadSeed : (Environment.TickCount ^ 0x5bd1e995);
			dreadDirector = new DreadDirector(seed);
			dreadScreenDriver.Reset();
		}
		EnsureDreadObjects();
		bool paused = Time.timeScale <= 0f;
		if (paused)
		{
			DreadSynth pausedSynth = dreadAudioHost != null ? dreadAudioHost.Synth : null;
			if (pausedSynth != null)
			{
				pausedSynth.MasterLevel = 0f;
			}
			if (dreadAudioHost != null && dreadAudioHost.Steps != null)
			{
				dreadAudioHost.Steps.MasterLevel = 0f;
			}
			if (dreadAudioHost != null && dreadAudioHost.Mixer != null)
			{
				dreadAudioHost.Mixer.MasterLevel = 0f;
			}
			return;
		}
		float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
		dreadFrame++;
		DreadInput input = BuildDreadInput(vessel, dt);
		DreadSettings ds = dreadDirector.Settings;
		ds.EventsEnabled = dreadEvents;
		ds.FalseAlarmsEnabled = dreadFalseAlarms;
		dreadDirector.Update(in input);
		DreadOutput o = dreadDirector.Out;
		ApplyDreadDebugOverride(ref o, dt, input.InIVA);
		dreadLast = o;
		ApplyDreadMonsters(in o, dt);
		ApplyDreadScreen(in o, dt, input.InIVA);
		ApplyDreadSoundscape(vessel, in o, in input, dt);
		ApplyDreadAudio(in o, dt, in input);
		ApplyDreadFootsteps(vessel, dt);
		MohoWarningsStep(vessel, dt);
		ApplyDreadInstruments(in o, in input);
		if ((dreadAudioHost != null && dreadAudioHost.Faulted) || (dreadInstrumentsHost != null && dreadInstrumentsHost.Faulted))
		{
			throw new InvalidOperationException("a Dread component faulted");
		}
	}

	// ------------------------------------------------------------------ inputs from real state

	private void DreadConsider(ref DreadCandidate best, Vector3d vesselPos, Vector3d position, bool chasing, CelestialBody losBody, HuntMonster hunt = null)
	{
		double d = (position - vesselPos).magnitude;
		if (double.IsNaN(d) || double.IsInfinity(d))
		{
			return;
		}
		if (!best.valid || d < best.distance)
		{
			best.valid = true;
			best.distance = d;
			best.chasing = chasing;
			best.losBody = losBody;
			best.position = position;
			best.hunt = hunt;
		}
	}

	private DreadInput BuildDreadInput(Vessel vessel, float dt)
	{
		DreadInput input = new DreadInput();
		input.Dt = dt;
		Vector3d vesselPos = vessel.GetWorldPos3D();
		DreadCandidate best = new DreadCandidate();
		CelestialBody here = vessel.mainBody;
		if (groundEntityActive && groundEntityBody != null && groundEntityBody == here && (groundVariant == GroundVariant.Pilot || groundVariant == GroundVariant.Engineer))
		{
			DreadConsider(ref best, vesselPos, groundEntityBody.position + groundEntityRelativePosition, groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating, groundEntityBody, huntMimic);
		}
		if (bulkEntityActive && bulkEntityBody != null && bulkEntityBody == here)
		{
			DreadConsider(ref best, vesselPos, bulkEntityBody.position + bulkEntityRelativePosition, bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating, bulkEntityBody, huntBulk);
		}
		if (laytheCreatureActive && laytheCreatureBody != null && laytheCreatureBody == here)
		{
			DreadConsider(ref best, vesselPos, laytheCreatureBody.position + laytheCreatureRelativePosition, laytheCreatureChasing, laytheCreatureBody, huntLaythe);
		}
		if (rakeEntityActive && rakeEntityBody != null && rakeEntityBody == here)
		{
			DreadConsider(ref best, vesselPos, rakeEntityBody.position + rakeEntityRelativePosition, rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating, rakeEntityBody, huntRake);
		}
		if (seaEntityActive && seaEntityBody != null && seaEntityBody == here)
		{
			DreadConsider(ref best, vesselPos, seaEntityBody.position + seaEntityRelativePosition, seaEntityChasing, null);
		}
		if (bloopEntityActive && bloopEntityBody != null && bloopEntityBody == here)
		{
			DreadConsider(ref best, vesselPos, bloopEntityBody.position + bloopEntityRelativePosition, bloopEntityChasing, null);
		}
		if (eyeEntityActive)
		{
			DreadConsider(ref best, vesselPos, eyeEntityPosition, eyeEntityChasing, null);
		}
		if (grabberEntityActive && grabberHasVisualPosition)
		{
			DreadConsider(ref best, vesselPos, grabberVisualPosition, !grabberEntityFleeing, null);
		}
		if (dresSomethingActive && dresSomethingBody != null && dresSomethingBody == here)
		{
			DreadConsider(ref best, vesselPos, dresSomethingBody.position + dresSomethingRelativePosition, false, null);
		}
		if (tracking && !arrived)
		{
			DreadConsider(ref best, vesselPos, entityPosition, true, null);
		}
		if (scareGlimpseObject != null)
		{
			DreadConsider(ref best, vesselPos, (Vector3d)scareGlimpseObject.transform.position, false, null);
		}
		bool los = false;
		if (best.valid)
		{
			dreadLosTimer -= dt;
			if (best.losBody != null && best.distance < 3000.0)
			{
				if (dreadLosTimer <= 0f)
				{
					dreadLosTimer = 0.4f;
					dreadLosCached = HasGroundLineOfSight(best.position, vesselPos, best.losBody);
				}
				los = dreadLosCached;
			}
			else
			{
				dreadLosTimer = 0f;
				los = best.distance < 5000.0;
			}
		}
		dreadLastDistance = best.valid ? best.distance : -1.0;
		dreadSoundNearestHunt = best.valid ? best.hunt : null;
		dreadSoundNearestPosition = best.position;
		input.NearestMonsterDistance = best.valid ? (float)best.distance : -1f;
		input.Chasing = best.valid && best.chasing;
		input.HuntState = -1;
		input.BelievedDistance = -1f;
		if (huntAi && best.valid && best.hunt != null && best.hunt.hunt != null && best.hunt.info.Valid)
		{
			// aware is not the same as in sight: only a monster that sees the craft is a chase; one that is walking to a noise or sweeping an area is a search
			input.HuntState = (int)best.hunt.info.State;
			input.BelievedDistance = (float)best.hunt.info.BeliefDistance;
			input.Searching = best.chasing && best.hunt.info.Searching;
			input.Chasing = best.chasing && best.hunt.info.Hunting;
		}
		input.LineOfSight = los;
		input.AttackRunning = attackSequenceList.Count > 0;
		input.HeightFromTerrain = vessel.heightFromTerrain >= 0f ? vessel.heightFromTerrain : (float)vessel.radarAltitude;
		input.DescentRate = (float)(-vessel.verticalSpeed);
		input.Docking = IsDreadDocking(vessel);
		input.Throttle = vessel.ctrlState != null ? vessel.ctrlState.mainThrottle : 0f;
		input.LightsOn = vessel.ActionGroups[KSPActionGroup.Light] && scareLightsPhase == 0;
		input.InIVA = IsDreadIva();
		bool rails = TimeWarp.CurrentRate > 1.001f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH;
		input.SafeScene = !best.valid || vessel.state == Vessel.State.DEAD || rails;
		return input;
	}

	private static bool IsDreadIva()
	{
		if (CameraManager.Instance == null)
		{
			return false;
		}
		CameraManager.CameraMode mode = CameraManager.Instance.currentCameraMode;
		return mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal;
	}

	private static bool IsDreadDocking(Vessel vessel)
	{
		if (FlightGlobals.fetch == null)
		{
			return false;
		}
		ITargetable target = FlightGlobals.fetch.VesselTarget;
		if (target == null)
		{
			return false;
		}
		Vessel targetVessel = target.GetVessel();
		Transform t = target.GetTransform();
		if (targetVessel == null || targetVessel == vessel || t == null)
		{
			return false;
		}
		return (t.position - vessel.transform.position).sqrMagnitude < 150f * 150f;
	}

	// ------------------------------------------------------------------ outputs

	private void ApplyDreadMonsters(in DreadOutput o, float dt)
	{
		dreadReleaseHint = dreadMonsterRelease && o.RequestRelease;
		if (!dreadMonsterNoise)
		{
			dreadDarkSeconds = 0f;
		}
		else if (o.Noise < 0.25f)
		{
			dreadDarkSeconds += dt;
		}
		else if (o.Noise > 0.35f)
		{
			dreadDarkSeconds = 0f;
		}
	}

	private Camera GetDreadTargetCamera(bool iva)
	{
		if (!string.IsNullOrEmpty(dreadDebugCameraName))
		{
			foreach (Camera c in Camera.allCameras)
			{
				if (c != null && c.name == dreadDebugCameraName)
				{
					return c;
				}
			}
		}
		if (iva)
		{
			InternalCamera ic = InternalCamera.Instance;
			return ic != null ? ic.GetComponentInChildren<Camera>() : null;
		}
		FlightCamera fc = FlightCamera.fetch;
		if (fc == null || fc.mainCamera == null)
		{
			return null;
		}
		Camera[] all = fc.cameras;
		if (all != null && all.Length > 1 && all[1] != null && all[1].gameObject.activeInHierarchy)
		{
			if (!dreadCameraNoticeLogged)
			{
				dreadCameraNoticeLogged = true;
				Debug.Log("[KK Dread] screen effect off: split near/far cameras (not the unified D3D11 camera)");
			}
			return null;
		}
		return fc.mainCamera;
	}

	private void ApplyDreadScreen(in DreadOutput o, float dt, bool iva)
	{
		if (!dreadScreen || !DreadCamera.EnsureShader(null))
		{
			if (dreadCamera != null)
			{
				dreadCamera.Detach();
			}
			return;
		}
		CameraManager.CameraMode mode = CameraManager.Instance != null ? CameraManager.Instance.currentCameraMode : CameraManager.CameraMode.Flight;
		if (mode == CameraManager.CameraMode.Map || MapView.MapIsEnabled)
		{
			if (dreadCamera != null)
			{
				dreadCamera.Detach();
			}
			return;
		}
		DreadScreenToggles toggles = new DreadScreenToggles();
		toggles.Grade = dreadGrade;
		toggles.Vignette = dreadVignette;
		toggles.Grain = dreadGrain;
		toggles.Signal = dreadSignal;
		toggles.Freeze = dreadFeedFreeze;
		dreadScreenDriver.Update(dt, in o, iva, toggles, dreadScreenStrength, dreadFrame);
		DreadScreenParams p = dreadScreenDriver.Params;
		if (p.IsClean(0.0005f) && !dreadScreenDriver.CaptureFrame)
		{
			dreadCleanTimer += dt;
			if (dreadCamera != null && dreadCamera.Attached && dreadCleanTimer > 2f)
			{
				dreadCamera.Detach();
			}
			if (dreadCamera == null || !dreadCamera.Attached)
			{
				return;
			}
		}
		else
		{
			dreadCleanTimer = 0f;
		}
		if (dreadCamera == null)
		{
			dreadCamera = new DreadCamera();
		}
		dreadCamera.SetMethod(!string.Equals(dreadScreenMethod, "onRenderImage", StringComparison.OrdinalIgnoreCase));
		Camera target = GetDreadTargetCamera(iva);
		dreadCamera.SetCamera(target);
		dreadCamera.Apply(in p, dreadScreenDriver.CaptureFrame);
	}

	private void EnsureDreadObjects()
	{
		bool wantAudio = dreadAudio;
		bool wantInstruments = dreadInstruments;
		if ((wantAudio || wantInstruments) && dreadHostObject == null)
		{
			dreadHostObject = new GameObject("KK_DreadHost");
			UnityEngine.Object.DontDestroyOnLoad(dreadHostObject);
		}
		if (wantAudio && dreadAudioHost == null && dreadHostObject != null)
		{
			dreadAudioHost = dreadHostObject.AddComponent<DreadAudioHost>();
			dreadAudioHost.Init(dreadSeed != 0 ? dreadSeed : Environment.TickCount, Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "DreadSounds", "step.wav"));
		}
		if (wantInstruments && dreadInstrumentsHost == null && dreadHostObject != null)
		{
			dreadInstrumentsHost = dreadHostObject.AddComponent<DreadInstrumentsHost>();
		}
	}

	private void ApplyDreadAudio(in DreadOutput o, float dt, in DreadInput input)
	{
		if (dreadMusicDuck || DreadSoundMusicGain < 1f)
		{
			dreadMusicDuckState.Update((dreadMusicDuck ? DreadMapping.DuckGain(o.MusicDuck, dreadMusicDuckDb) : 1f) * DreadSoundMusicGain);
		}
		else
		{
			dreadMusicDuckState.Release();
		}
		if (dreadListenerLowpass)
		{
			dreadLowpassState.Update(dt, DreadMapping.LowpassCutoffHz(o.Lowpass, dreadLowpassHz));
		}
		else
		{
			dreadLowpassState.Release();
		}
		if (!dreadAudio || dreadAudioHost == null)
		{
			return;
		}
		DreadSynth s = dreadAudioHost.Synth;
		if (s == null)
		{
			return;
		}
		dreadAudioHost.SetSourceVolume(GameSettings.AMBIENCE_VOLUME);
		s.MasterLevel = dreadAudioVolume;
		// where the recorded soundscape runs it replaces all of these: what cannot touch the craft is not heard
		s.DroneLevel = dreadDrone && !DreadSoundOwns ? o.DroneLevel : 0f;
		s.CreakLevel = dreadHullSounds && !DreadSoundOwns ? 1f : 0f;
		s.ThumpLevel = dreadHullSounds && !DreadSoundOwns ? dreadThumpScale : 0f;
		s.CreatureLevel = dreadCreature && !DreadSoundOwns ? 1f : 0f;
		// the riser (a rising sweep) is gone for good: it was unbearable to listen to
		s.RiserLevel = 0f;
		s.Menace = o.Menace;
		s.Thrust = input.Throttle;
		float staticLevel = DreadMapping.SignalCurve(o.SignalNoise) / DreadMapping.SignalPeak;
		if (o.Active == DreadEventKind.TearEvent)
		{
			staticLevel = Mathf.Max(staticLevel, 0.5f);
		}
		else if (o.Active == DreadEventKind.Dropout)
		{
			staticLevel = Mathf.Max(staticLevel, 0.65f);
		}
		if (dreadStaticPulse > 0f)
		{
			dreadStaticPulse -= dt;
			staticLevel = Mathf.Max(staticLevel, dreadStaticPulseLevel);
		}
		s.StaticLevel = dreadStatic && !DreadSoundOwns ? Mathf.Clamp01(staticLevel) : 0f;

		switch (o.Fired)
		{
		case DreadEventKind.CreakEvent:
			s.TriggerCreak();
			break;
		case DreadEventKind.ThumpEvent:
			s.TriggerThump();
			break;
		case DreadEventKind.HullSilence:
			s.TriggerHullSilence(o.FiredDuration);
			break;
		}

		// A creature voice at most once per chase and rarely.
		bool chasing = input.Chasing;
		if (chasing && !dreadWasChasing)
		{
			s.NewChase();
			dreadCreatureDone = false;
		}
		dreadWasChasing = chasing;
		if (chasing && !dreadCreatureDone && o.Menace > 0.7f && o.Active == DreadEventKind.None && Time.time - dreadLastCreatureTime > 150f)
		{
			dreadCreatureDone = true;
			if (UnityEngine.Random.value < 0.6f)
			{
				dreadLastCreatureTime = Time.time;
				s.TriggerCreature();
			}
		}
	}

	// Nearest of the monsters that walk on the ground. Index only tells them apart, so a change of nearest monster is not read as speed.
	private bool FindDreadWalker(Vector3d vesselPos, CelestialBody here, out int index, out Vector3d position, out bool chasing, out bool walking)
	{
		index = -1;
		position = Vector3d.zero;
		chasing = false;
		walking = false;
		double bestDistance = double.MaxValue;
		if (groundEntityActive && groundEntityBody == here && (groundVariant == GroundVariant.Pilot || groundVariant == GroundVariant.Engineer))
		{
			ConsiderDreadWalker(0, groundEntityBody.position + groundEntityRelativePosition, vesselPos, groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating, behaviorMimic, ref bestDistance, ref index, ref position, ref chasing, ref walking);
		}
		if (bulkEntityActive && bulkEntityBody == here)
		{
			ConsiderDreadWalker(1, bulkEntityBody.position + bulkEntityRelativePosition, vesselPos, bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating, behaviorBulk, ref bestDistance, ref index, ref position, ref chasing, ref walking);
		}
		if (rakeEntityActive && rakeEntityBody == here)
		{
			ConsiderDreadWalker(2, rakeEntityBody.position + rakeEntityRelativePosition, vesselPos, rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating, behaviorRake, ref bestDistance, ref index, ref position, ref chasing, ref walking);
		}
		return index >= 0;
	}

	private static void ConsiderDreadWalker(int id, Vector3d pos, Vector3d vesselPos, bool isChasing, GroundBehaviorState behavior, ref double bestDistance, ref int index, ref Vector3d position, ref bool chasing, ref bool walking)
	{
		double d = (pos - vesselPos).magnitude;
		if (double.IsNaN(d) || double.IsInfinity(d) || d >= bestDistance)
		{
			return;
		}
		bestDistance = d;
		index = id;
		position = pos;
		chasing = isChasing;
		walking = !behavior.leaping && behavior.lift <= 0f;
	}

	// Steps can only reach the craft through the ground, so they need landed and no air. The logic fades them; nothing here cuts.
	private void ApplyDreadFootsteps(Vessel vessel, float dt)
	{
		DreadStepVoice voice = dreadAudioHost != null ? dreadAudioHost.Steps : null;
		if (voice == null)
		{
			return;
		}
		if (dreadStepLogic == null)
		{
			dreadStepLogic = new DreadStepLogic(dreadSeed != 0 ? dreadSeed : Environment.TickCount);
		}
		dreadStepLogic.FarMetres = dreadFootstepsFarMetres;
		dreadStepLogic.NearMetres = dreadFootstepsNearMetres;
		Vector3d vesselPos = vessel.GetWorldPos3D();
		DreadStepInput input = new DreadStepInput();
		input.Dt = dt;
		input.Distance = -1f;
		input.Landed = vessel.Landed && !dreadStepTestNotLanded;
		input.Vacuum = !vessel.mainBody.atmosphere;
		int walker;
		Vector3d position;
		if (dreadFootsteps && FindDreadWalker(vesselPos, vessel.mainBody, out walker, out position, out input.Chasing, out input.Walking))
		{
			Vector3d relative = position - vesselPos;
			HuntMonster huntWalker = HuntFor(walker);
			input.Listening = huntAi && huntWalker.hunt != null && huntWalker.hunt.Listening;
			if (walker == 1 && bulkMoho != null && mohoStepsFromFeet)
			{
				// the creature has feet: a step is a footfall, not a timer
				input.UseFootfalls = true;
				input.Footfalls = mohoFootPending;
				if (mohoLeap.Active || mohoLeapLift > 0f)
				{
					input.Walking = false;
				}
			}
			mohoFootPending = 0;
			float step = Time.deltaTime;
			if (walker == dreadStepWalker && step > 0f)
			{
				// what the craft itself did this frame is taken out, or driving past a standing monster would sound like it walking
				float raw = (float)((relative - dreadStepLastRelative + (Vector3d)vessel.srf_velocity * step).magnitude / step);
				dreadStepSpeed = Mathf.Min(raw, 300f);
			}
			dreadStepWalker = walker;
			dreadStepLastRelative = relative;
			input.Distance = (float)relative.magnitude;
			input.Speed = dreadStepSpeed;
		}
		else
		{
			dreadStepWalker = -1;
		}
		dreadStepLogic.Update(in input);
		voice.MasterLevel = dreadAudio ? dreadAudioVolume : 0f;
		voice.Gain = dreadStepLogic.Gain;
		voice.CutoffHz = dreadStepLogic.CutoffHz;
		voice.Pitch = dreadStepLogic.Pitch;
		if (dreadStepLogic.StepFired)
		{
			for (int k = 0; k < Mathf.Max(1, dreadStepLogic.StepCount); k++)
			{
				voice.TriggerStep();
			}
		}
	}

	private void ApplyDreadInstruments(in DreadOutput o, in DreadInput input)
	{
		if (dreadInstrumentsHost == null)
		{
			return;
		}
		dreadInstrumentsHost.EnablePortraits = dreadInstruments && dreadPortraitStatic;
		dreadInstrumentsHost.EnableJitter = dreadInstruments && dreadInstrumentJitter;
		dreadInstrumentsHost.EnableIvaLights = dreadInstruments && dreadIvaLights;
		dreadInstrumentsHost.InIva = input.InIVA;
		dreadInstrumentsHost.PortraitStatic = o.Active == DreadEventKind.PortraitStatic || o.DropoutActive;
		dreadInstrumentsHost.JitterAmount = Mathf.Max(o.InstrumentJitter, dreadWarnJitter);
		dreadInstrumentsHost.IvaDip = o.IvaLightDip;
	}

	// ------------------------------------------------------------------ scares feed the audio side instead of playing their own

	// Called from StartScare after a scare started. Cues go to the synth, at most one every 8 s and never over a director event.
	private void DreadOnScare(ScareKind kind)
	{
		if (!dreadEnabled || dreadFailed)
		{
			return;
		}
		try
		{
			if (dreadAudioHost == null || dreadDirector == null || dreadAudioHost.Synth == null)
			{
				return;
			}
			if (Time.time - dreadLastCueTime < 8f || dreadDirector.Out.Active != DreadEventKind.None)
			{
				return;
			}
			dreadLastCueTime = Time.time;
			DreadSoundOnScare(kind);
			DreadSynth s = dreadAudioHost.Synth;
			switch (kind)
			{
			case ScareKind.Lights:
				s.TriggerCreak();
				break;
			case ScareKind.Radio:
				dreadStaticPulse = 3f;
				dreadStaticPulseLevel = 0.5f;
				break;
			case ScareKind.Sensor:
				dreadStaticPulse = 1.2f;
				dreadStaticPulseLevel = 0.4f;
				s.TriggerThump();
				break;
			case ScareKind.Glimpse:
				s.TriggerHullSilence(1.2f);
				break;
			}
		}
		catch (Exception)
		{
		}
	}

	// ------------------------------------------------------------------ lifecycle

	private void EnsureDreadHooks()
	{
		if (dreadHooked)
		{
			return;
		}
		dreadHooked = true;
		GameEvents.onGameSceneLoadRequested.Add(OnDreadSceneLoadRequested);
		GameEvents.onLevelWasLoadedGUIReady.Add(OnDreadLevelLoaded);
		GameEvents.onFlightReady.Add(OnDreadFlightReady);
		GameEvents.OnCameraChange.Add(OnDreadCameraChange);
	}

	private void OnDreadSceneLoadRequested(GameScenes scene)
	{
		dreadSceneChanging = true;
		dreadTufxRestoreTimer = 0f;
		dreadTufxSavedProfile = null;
		try
		{
			DreadSuspend();
		}
		catch (Exception)
		{
		}
	}

	private void OnDreadLevelLoaded(GameScenes scene)
	{
		dreadSceneChanging = false;
	}

	private void OnDreadFlightReady()
	{
		dreadSceneChanging = false;
	}

	private void OnDreadCameraChange(CameraManager.CameraMode mode)
	{
		try
		{
			if (dreadCamera != null)
			{
				dreadCamera.Detach();
			}
		}
		catch (Exception)
		{
		}
	}

	// Everything the effects changed goes back (screen, music, listener, instruments) and the monsters stop hearing hints.
	private void DreadSuspend()
	{
		dreadReleaseHint = false;
		dreadDarkSeconds = 0f;
		if (dreadCamera != null)
		{
			dreadCamera.Detach();
		}
		dreadMusicDuckState.Release();
		dreadLowpassState.Release();
		if (dreadInstrumentsHost != null)
		{
			dreadInstrumentsHost.PortraitStatic = false;
			dreadInstrumentsHost.JitterAmount = 0f;
			dreadInstrumentsHost.IvaDip = 0f;
			dreadInstrumentsHost.RestoreAll();
		}
		if (dreadAudioHost != null && dreadAudioHost.Synth != null)
		{
			dreadAudioHost.Synth.MasterLevel = 0f;
			dreadAudioHost.Synth.DroneLevel = 0f;
			dreadAudioHost.Synth.StaticLevel = 0f;
		}
		if (dreadAudioHost != null && dreadAudioHost.Steps != null)
		{
			dreadAudioHost.Steps.Gain = 0f;
		}
		DreadSoundSuspend();
		dreadStepLogic = null;
		dreadStepWalker = -1;
		dreadDirector = null;
		dreadWasChasing = false;
		dreadStaticPulse = 0f;
		dreadLastDistance = -1.0;
	}

	private void ShutdownDread(bool destroyHost)
	{
		DreadSuspend();
		if (dreadCamera != null)
		{
			dreadCamera.Dispose();
			dreadCamera = null;
		}
		if (destroyHost && dreadHostObject != null)
		{
			UnityEngine.Object.Destroy(dreadHostObject);
			dreadHostObject = null;
			dreadAudioHost = null;
			dreadInstrumentsHost = null;
		}
		if (dreadHooked)
		{
			dreadHooked = false;
			GameEvents.onGameSceneLoadRequested.Remove(OnDreadSceneLoadRequested);
			GameEvents.onLevelWasLoadedGUIReady.Remove(OnDreadLevelLoaded);
			GameEvents.onFlightReady.Remove(OnDreadFlightReady);
			GameEvents.OnCameraChange.Remove(OnDreadCameraChange);
		}
		dreadStarted = false;
	}

	// Called from OnDestroy.
	private void DestroyDread()
	{
		try
		{
			ShutdownDread(destroyHost: true);
			RestoreDreadTufx();
		}
		catch (Exception)
		{
		}
	}

	// ------------------------------------------------------------------ TUFX glitch: save and restore, or skip

	// Replaces TryActivateTufxGlitch at the arrival glitch. dreadEnabled false: the original call, unchanged.
	private void TriggerArrivalGlitch()
	{
		if (!dreadEnabled || !dreadTufxSafe)
		{
			TryActivateTufxGlitch();
			return;
		}
		try
		{
			if (dreadCamera != null && dreadCamera.Attached)
			{
				return;   // the Dread pass is active and carries its own tear and dropout
			}
			string previous = ReadTufxProfileName();
			if (string.IsNullOrEmpty(previous))
			{
				return;   // cannot be restored, so do not switch
			}
			dreadTufxSavedProfile = previous;
			dreadTufxRestoreTimer = 0.6f;
			TryActivateTufxGlitch();
		}
		catch (Exception)
		{
			RestoreDreadTufx();
		}
	}

	private void UpdateDreadTufxRestore()
	{
		if (dreadTufxSavedProfile == null)
		{
			return;
		}
		dreadTufxRestoreTimer -= Time.unscaledDeltaTime;
		if (dreadTufxRestoreTimer <= 0f)
		{
			RestoreDreadTufx();
		}
	}

	private void RestoreDreadTufx()
	{
		string name = dreadTufxSavedProfile;
		dreadTufxSavedProfile = null;
		if (string.IsNullOrEmpty(name))
		{
			return;
		}
		try
		{
			object loader = GetTufxLoaderInstance(out Type type);
			if (loader == null)
			{
				return;
			}
			MethodInfo method = type.GetMethod("SetCurrentProfile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (method != null)
			{
				method.Invoke(loader, new object[1] { name });
			}
		}
		catch (Exception)
		{
		}
	}

	private static object GetTufxLoaderInstance(out Type type)
	{
		type = null;
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type t = assembly.GetType("TUFX.TexturesUnlimitedFXLoader", throwOnError: false);
			if (t == null)
			{
				continue;
			}
			FieldInfo field = t.GetField("INSTANCE", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			object obj = field != null ? field.GetValue(null) : null;
			if (obj != null)
			{
				type = t;
				return obj;
			}
		}
		return null;
	}

	// Best effort: the current profile name from a member called CurrentProfileName / currentProfile / CurrentProfile.
	private static string ReadTufxProfileName()
	{
		object loader = GetTufxLoaderInstance(out Type type);
		if (loader == null)
		{
			return null;
		}
		string[] names = new string[] { "CurrentProfileName", "currentProfileName", "currentProfile", "CurrentProfile" };
		BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		foreach (string name in names)
		{
			object value = null;
			PropertyInfo property = type.GetProperty(name, flags);
			FieldInfo field = type.GetField(name, flags);
			if (property != null && property.GetIndexParameters().Length == 0)
			{
				value = property.GetValue(loader, null);
			}
			else if (field != null)
			{
				value = field.GetValue(loader);
			}
			if (value == null)
			{
				continue;
			}
			string s = value as string;
			if (!string.IsNullOrEmpty(s))
			{
				return s;
			}
			foreach (string inner in new string[] { "ProfileName", "profileName", "name" })
			{
				PropertyInfo ip = value.GetType().GetProperty(inner, flags);
				FieldInfo iff = value.GetType().GetField(inner, flags);
				object iv = ip != null ? ip.GetValue(value, null) : (iff != null ? iff.GetValue(value) : null);
				string si = iv as string;
				if (!string.IsNullOrEmpty(si))
				{
					return si;
				}
			}
		}
		return null;
	}

	// ------------------------------------------------------------------ debug (toolbar, debugTools only)

	private void ApplyDreadDebugOverride(ref DreadOutput o, float dt, bool iva)
	{
		if (dreadDebugMenace >= 0f)
		{
			float m = dreadDebugMenace;
			o.Menace = m;
			o.Grade = m;
			o.Vignette = m;
			o.Grain = m;
			o.SignalNoise = iva ? 0f : m * 0.9f;
			o.DroneLevel = m;
			o.MusicDuck = m;
			o.Lowpass = m * m;
			o.IvaLightDip = iva ? m : 0f;
		}
		if (dreadForceKind != DreadEventKind.None)
		{
			dreadForceRemaining -= dt;
			if (dreadForceRemaining <= 0f)
			{
				dreadForceKind = DreadEventKind.None;
			}
			else
			{
				o.Active = dreadForceKind;
				o.ActiveRemaining = dreadForceRemaining;
				o.DropoutActive = dreadForceKind == DreadEventKind.Dropout;
				o.FeedFrozen = dreadForceKind == DreadEventKind.FeedFreeze;
			}
		}
	}

	private void DreadForce(DreadEventKind kind, float seconds)
	{
		dreadForceKind = kind;
		dreadForceRemaining = seconds;
		DreadSynth s = dreadAudioHost != null ? dreadAudioHost.Synth : null;
		if (s == null)
		{
			return;
		}
		switch (kind)
		{
		case DreadEventKind.CreakEvent:
			s.TriggerCreak();
			break;
		case DreadEventKind.ThumpEvent:
			s.TriggerThump();
			break;
		case DreadEventKind.HullSilence:
			s.TriggerHullSilence(seconds);
			break;
		}
	}

	private void DrawDreadDebugButtons()
	{
		if (!dreadEnabled)
		{
			return;
		}
		if (dreadFailed)
		{
			GUILayout.Label("Dread: DISABLED after an exception (see log)");
			return;
		}
		if (!dreadStarted)
		{
			return;
		}
		DreadOutput o = dreadLast;
		GUILayout.Label("Dread menace " + o.Menace.ToString("F2") + "  noise " + o.Noise.ToString("F2") + (o.RequestRelease ? "  RELEASE" : "") + (dreadDarkSeconds > 0.5f ? "  dark " + dreadDarkSeconds.ToString("F0") + " s" : ""));
		GUILayout.Label("Dread screen: " + (DreadCamera.Failure != null ? "off (" + DreadCamera.Failure + ")" : (dreadCamera != null && dreadCamera.Attached ? "on" : "idle (clean)")) + "  nearest " + (dreadLastDistance >= 0.0 ? dreadLastDistance.ToString("F0") + " m" : "none"));
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Dread m 0")) { dreadDebugMenace = 0f; }
		if (GUILayout.Button("0.3")) { dreadDebugMenace = 0.3f; }
		if (GUILayout.Button("0.6")) { dreadDebugMenace = 0.6f; }
		if (GUILayout.Button("1.0")) { dreadDebugMenace = 1f; }
		if (GUILayout.Button("auto")) { dreadDebugMenace = -1f; }
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Tear")) { DreadForce(DreadEventKind.TearEvent, 0.4f); }
		if (GUILayout.Button("Dropout")) { DreadForce(DreadEventKind.Dropout, 1.2f); }
		if (GUILayout.Button("Freeze")) { DreadForce(DreadEventKind.FeedFreeze, 0.9f); }
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Creak")) { DreadForce(DreadEventKind.CreakEvent, 0.5f); }
		if (GUILayout.Button("Thump")) { DreadForce(DreadEventKind.ThumpEvent, 0.5f); }
		if (GUILayout.Button("Silence")) { DreadForce(DreadEventKind.HullSilence, 3f); }
		if (GUILayout.Button("Portraits")) { DreadForce(DreadEventKind.PortraitStatic, 2.5f); }
		GUILayout.EndHorizontal();
	}
}
