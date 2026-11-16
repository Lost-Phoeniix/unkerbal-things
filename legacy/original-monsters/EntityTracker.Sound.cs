using System;
using System.IO;
using KrakensKreations.Dread;
using UnityEngine;

// The recorded soundscape: decides nothing itself, it tells DreadSoundLogic the state of the game and hands the result to the mixer.
// Master switch dreadSoundscape. false, or clips missing: DreadSoundOwns stays false and the synth keeps doing everything as before.
public partial class EntityTracker
{
	private static bool dreadSoundscape = true;
	private static float dreadSoundscapeVolume = 1f;
	private static float dreadSoundBedDb = 0f;
	private static float dreadSoundEventDb = 0f;
	private static bool dreadSoundWhales = true;
	private static float dreadSoundMusicFadeSeconds = 25f;
	private static float dreadSoundWaterDepth = 100f;
	private static bool dreadSoundHauntedBed = false;

	private DreadSoundLogic dreadSoundLogic;
	private DreadSoundBank dreadVacBank, dreadWaterBank;
	private float dreadVacIdle, dreadWaterIdle;
	private DreadClip[][] dreadSoundKinds;
	private int dreadSoundKindsSet;
	private bool dreadSoundOwning;
	private HuntMonster dreadSoundNearestHunt;
	private Vector3d dreadSoundNearestPosition;
	private const float DreadBankKeepSeconds = 120f;

	// test only
	internal Action<DreadSoundKind, float> DreadSoundCueObserver;
	internal DreadSoundLogic DreadSoundLogicForTest { get { return dreadSoundLogic; } }

	private bool DreadSoundOwns { get { return dreadSoundOwning; } }

	// Factor for the game's music from the soundscape (1 = untouched); ApplyDreadAudio multiplies it into the director's duck. Not tied to owning: leaving a place lets the music come back slowly instead of at once.
	private float DreadSoundMusicGain { get { return dreadSoundscape && dreadAudio && dreadSoundLogic != null ? dreadSoundLogic.MusicGain : 1f; } }

	private static void LoadDreadSoundSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "dreadSoundscape", ref dreadSoundscape);
		ReadFloatSetting(node, "dreadSoundscapeVolume", ref dreadSoundscapeVolume);
		ReadFloatSetting(node, "dreadSoundBedDb", ref dreadSoundBedDb);
		ReadFloatSetting(node, "dreadSoundEventDb", ref dreadSoundEventDb);
		ReadBoolScareSetting(node, "dreadSoundWhales", ref dreadSoundWhales);
		ReadFloatSetting(node, "dreadSoundMusicFadeSeconds", ref dreadSoundMusicFadeSeconds);
		ReadFloatSetting(node, "dreadSoundWaterDepth", ref dreadSoundWaterDepth);
		ReadBoolScareSetting(node, "dreadSoundHauntedBed", ref dreadSoundHauntedBed);
		dreadSoundMusicFadeSeconds = Mathf.Clamp(dreadSoundMusicFadeSeconds, 0f, 300f);
		dreadSoundWaterDepth = Mathf.Clamp(dreadSoundWaterDepth, 3f, 5000f);
		dreadSoundscapeVolume = Mathf.Clamp(dreadSoundscapeVolume, 0f, 2f);
		dreadSoundBedDb = Mathf.Clamp(dreadSoundBedDb, -30f, 20f);
		dreadSoundEventDb = Mathf.Clamp(dreadSoundEventDb, -30f, 20f);
	}

	private static DreadSoundEnv DreadSoundEnvOf(Vessel vessel, bool notLanded)
	{
		CelestialBody body = vessel.mainBody;
		if (body.ocean && (vessel.Splashed || vessel.altitude < 0.0))
		{
			return vessel.altitude < -2.0 ? DreadSoundEnv.Underwater : DreadSoundEnv.Surface;
		}
		if (!body.atmosphere || vessel.altitude > body.atmosphereDepth)
		{
			return vessel.Landed && !notLanded ? DreadSoundEnv.VacuumLanded : DreadSoundEnv.VacuumFlying;
		}
		return DreadSoundEnv.None;
	}

	private static string DreadSoundFolder { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "DreadSounds"); } }

	// Runs before ApplyDreadAudio, so the synth hears in the same frame that the soundscape has taken over.
	private void ApplyDreadSoundscape(Vessel vessel, in DreadOutput o, in DreadInput input, float dt)
	{
		DreadSoundMixer mixer = dreadAudioHost != null ? dreadAudioHost.Mixer : null;
		if (mixer == null || !dreadAudio || !dreadSoundscape)
		{
			dreadSoundOwning = false;
			if (mixer != null)
			{
				mixer.MasterLevel = 0f;
			}
			return;
		}
		if (dreadSoundLogic == null)
		{
			dreadSoundLogic = new DreadSoundLogic(dreadSeed != 0 ? dreadSeed : Environment.TickCount);
		}
		DreadSoundLogic logic = dreadSoundLogic;
		logic.BedTrimDb = dreadSoundBedDb;
		logic.EventTrimDb = dreadSoundEventDb;
		logic.Whales = dreadSoundWhales;
		logic.MusicFadeSeconds = dreadSoundMusicFadeSeconds;
		logic.WaterSceneDepth = dreadSoundWaterDepth;
		logic.HauntedBed = dreadSoundHauntedBed;

		DreadSoundInput i = new DreadSoundInput();
		i.Dt = dt;
		i.Env = DreadSoundEnvOf(vessel, dreadStepTestNotLanded);
		i.DepthMetres = vessel.altitude < 0.0 ? (float)(-vessel.altitude) : 0f;
		i.Menace = o.Menace;
		i.Distance = (float)dreadLastDistance;
		i.Chasing = input.Chasing;
		i.Searching = input.Searching;
		i.Listening = huntAi && dreadSoundNearestHunt != null && dreadSoundNearestHunt.hunt != null && dreadSoundNearestHunt.hunt.Listening;
		i.Haunted = vessel.mainBody.bodyName == "Moho";
		i.VesselTons = vessel.GetTotalMass();
		i.HullSilence = o.Active == DreadEventKind.HullSilence;
		i.Attack = input.AttackRunning;
		i.Suspended = TimeWarp.CurrentRate > 1.001f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH;
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
		if (cam != null && dreadLastDistance > 0.0)
		{
			i.Bearing = Vector3.Dot(cam.transform.right, ((Vector3)(dreadSoundNearestPosition - (Vector3d)cam.transform.position)).normalized);
		}
		logic.Update(in i);

		DreadSoundBank bank = DreadSoundBankFor(logic.WantedSet, dt, mixer);
		dreadSoundOwning = logic.OwnsSound && bank != null;
		mixer.MasterLevel = dreadAudioVolume * dreadSoundscapeVolume;
		mixer.Bed0 = logic.VacBedA;
		mixer.Bed1 = logic.VacBedB;
		mixer.Bed2 = logic.WaterBed;
		mixer.Bed3 = logic.PressureBed;
		mixer.WaterCutoffHz = logic.WaterCutoffHz;

		if (dreadSoundOwning)
		{
			switch (o.Fired)
			{
			case DreadEventKind.CreakEvent:
				logic.DirectorCreak();
				break;
			case DreadEventKind.ThumpEvent:
				logic.DirectorThump();
				break;
			}
		}
		logic.Drain();
		for (int c = 0; c < logic.CueCount; c++)
		{
			DreadSoundCue cue = logic.Cues[c];
			DreadClip[] clips = dreadSoundOwning && dreadSoundKinds != null ? dreadSoundKinds[(int)cue.Kind] : null;
			if (clips == null || clips.Length == 0)
			{
				continue;
			}
			mixer.Play(clips[logic.Pick(cue.Kind, clips.Length)], cue.Gain, cue.Pitch, cue.Pan, cue.CutoffHz);
			if (DreadSoundCueObserver != null)
			{
				DreadSoundCueObserver(cue.Kind, cue.Gain);
			}
		}
	}

	// A set is loaded when first wanted and let go after the craft has left its place for a while.
	private DreadSoundBank DreadSoundBankFor(int wanted, float dt, DreadSoundMixer mixer)
	{
		dreadVacIdle = wanted == 1 ? 0f : dreadVacIdle + dt;
		dreadWaterIdle = wanted == 2 ? 0f : dreadWaterIdle + dt;
		if (wanted == 1 && dreadVacBank == null)
		{
			dreadVacBank = DreadSoundBank.Begin(DreadSoundFolder, "vac");
		}
		if (wanted == 2 && dreadWaterBank == null)
		{
			dreadWaterBank = DreadSoundBank.Begin(DreadSoundFolder, "uw");
		}
		if (dreadVacBank != null && dreadVacIdle > DreadBankKeepSeconds)
		{
			dreadVacBank = null;
			mixer.SetBed(0, null);
			mixer.SetBed(1, null);
		}
		if (dreadWaterBank != null && dreadWaterIdle > DreadBankKeepSeconds)
		{
			dreadWaterBank = null;
			mixer.SetBed(2, null);
			mixer.SetBed(3, null);
		}
		DreadSoundBank bank = wanted == 1 ? dreadVacBank : (wanted == 2 ? dreadWaterBank : null);
		if (bank == null || !bank.Ready)
		{
			return null;
		}
		if (dreadSoundKindsSet != wanted)
		{
			dreadSoundKindsSet = wanted;
			DreadClip[][] kinds = new DreadClip[(int)DreadSoundKind.Count][];
			for (int k = 0; k < kinds.Length; k++)
			{
				kinds[k] = bank.Clips(DreadSoundLogic.KindNames[k]);
			}
			dreadSoundKinds = kinds;
			Debug.Log("[KK Dread] sound set " + (wanted == 1 ? "vac" : "uw") + ": " + bank.Total + " clips");
			DreadClip[] a = bank.Clips("bed-a"), b = bank.Clips("bed-b");
			int first = wanted == 1 ? 0 : 2;
			mixer.SetBed(first, a.Length > 0 ? a[0] : null);
			mixer.SetBed(first + 1, b.Length > 0 ? b[0] : null);
		}
		return bank.Total > 0 ? bank : null;
	}

	private void DreadSoundOnScare(ScareKind kind)
	{
		if (!dreadSoundOwning || dreadSoundLogic == null)
		{
			return;
		}
		switch (kind)
		{
		case ScareKind.Lights:
			dreadSoundLogic.DirectorCreak();
			break;
		case ScareKind.Sensor:
			dreadSoundLogic.DirectorThump();
			break;
		}
	}

	private void DreadSoundOnLanding(float scale)
	{
		if (dreadSoundOwning && dreadSoundLogic != null)
		{
			dreadSoundLogic.Landing(scale);
		}
	}

	private void DreadSoundOnWarning(int stage, float strength)
	{
		if (dreadSoundOwning && dreadSoundLogic != null)
		{
			dreadSoundLogic.Warning(stage, strength);
		}
	}

	/// <summary>For scares elsewhere in the mod: play one kind of sound now, if the place allows it (nothing wet in a vacuum, nothing of the ground below water). strength 0..1.</summary>
	internal void DreadSoundCue(DreadSoundKind kind, float strength)
	{
		if (dreadSoundOwning && dreadSoundLogic != null)
		{
			dreadSoundLogic.Scare(kind, strength);
		}
	}

	private void DreadSoundSuspend()
	{
		dreadSoundOwning = false;
		dreadSoundLogic = null;
		dreadSoundNearestHunt = null;
		dreadSoundKindsSet = 0;
		DreadSoundMixer mixer = dreadAudioHost != null ? dreadAudioHost.Mixer : null;
		if (mixer != null)
		{
			mixer.MasterLevel = 0f;
			for (int b = 0; b < DreadSoundMixer.BedSlots; b++)
			{
				mixer.SetBedGain(b, 0f);
			}
		}
	}
}
