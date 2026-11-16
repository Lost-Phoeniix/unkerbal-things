using UnkerbalThings.Dread;
using UnityEngine;

// The Moho worm in the recorded soundscape.
// Moho has no air: everything the craft hears of the swarm comes through the ground or through the hull itself.
//   travelling under ground -> a rumble bed whose loudness and brightness follow the real distance (landed craft only)
//   ambush dive and silence  -> everything of ours goes quiet
//   eruption                 -> the ground bursting under the craft (landed) or debris on the hull (flying)
//   the swarm holds the craft-> bodies pressing, the structure giving, repeated
//   kill                     -> the structure failing and tanks venting
// dreadSoundWorm = false restores the old soundscape (no worm sounds); dreadFootstepsWorm = true brings back the old footsteps.
public partial class EntityTracker
{
	private static bool dreadSoundWorm = true;
	private static bool dreadFootstepsWorm = false;

	private static void LoadWormSoundSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "dreadSoundWorm", ref dreadSoundWorm);
		ReadBoolScareSetting(node, "dreadFootstepsWorm", ref dreadFootstepsWorm);
	}

	/// <summary>The gpu worm body is the creature on this body: footsteps would be a lie about it.</summary>
	private bool WormBodyHasNoFeet { get { return !dreadFootstepsWorm && WormGpu != null; } }

	private void DreadSoundWormInput(Vessel v, ref DreadSoundInput i)
	{
		i.WormRumbleMetres = -1f;
		MohoWormGpuSkin g = WormGpu;
		if (!dreadSoundWorm || g == null || v == null || !bulkEntityActive || bulkEntityBody == null || v.mainBody != bulkEntityBody) return;
		WormAmbushPhase p = wormAmbushPhase;
		i.WormSilence = p == WormAmbushPhase.Dive || p == WormAmbushPhase.Silent;
		i.WormGrip = wormGrabbed && (p == WormAmbushPhase.Rise || p == WormAmbushPhase.Ball || p == WormAmbushPhase.Drop || p == WormAmbushPhase.Collapse);
		if (WormAmbushActive || !g.SerpentActive || g.Body.State != WormSerpent.Mode.Travel) return;
		WormHunt b = WormBrain;
		float speed = b != null ? b.Speed : mohoWormSerpentMaxSpeed * 0.5f;
		if (speed < 1f) return;
		i.WormRumbleMetres = (float)(v.GetWorldPos3D() - (bulkEntityBody.position + bulkEntityRelativePosition)).magnitude;
		i.WormRumbleSpeed = Mathf.Clamp01(speed / Mathf.Max(mohoWormSerpentMaxSpeed, 1f));
	}

	private void DreadSoundWormErupt(float strength)
	{
		if (dreadSoundWorm && dreadSoundOwning && dreadSoundLogic != null) dreadSoundLogic.WormErupt(strength);
	}

	// The craft dies in this frame and the dread layer suspends with it, so the kill goes straight to the mixer
	// (not through the logic's cue queue) and the mixer keeps playing out for a few seconds (DreadSoundSuspend).
	private float wormKillTailUntil = -1f;
	private const float WormKillTailSeconds = 8f;
	private bool WormKillTailRunning { get { return Time.time < wormKillTailUntil; } }

	private void DreadSoundWormKill()
	{
		if (!dreadSoundWorm || !dreadSoundOwning || dreadSoundLogic == null) return;
		DreadSoundMixer mixer = dreadAudioHost != null ? dreadAudioHost.Mixer : null;
		DreadClip[] clips = dreadSoundKinds != null ? dreadSoundKinds[(int)DreadSoundKind.Kill] : null;
		if (mixer == null || clips == null || clips.Length == 0) return;
		float gain = Mathf.Pow(10f, dreadSoundEventDb / 20f);
		DreadClip clip = clips[dreadSoundLogic.Pick(DreadSoundKind.Kill, clips.Length)];
		mixer.VoiceLevel = 1f;   // the ambush silence may have left the voices faded down
		mixer.Play(clip, gain, 1f, 0f, 0f);
		// play the whole recording out (plus a second of room), never cut at a fixed time
		float len = clip != null && clip.Rate > 0 ? clip.Frames / (float)clip.Rate : 0f;
		wormKillTailUntil = Time.time + Mathf.Max(WormKillTailSeconds, len + 1f);
		if (DreadSoundCueObserver != null) DreadSoundCueObserver(DreadSoundKind.Kill, gain);
	}
}
