using System;
using System.Globalization;
using UnityEngine;
using UnkerbalThings.Dread;

/// <summary>Ground proximity radar: near the swarm the outside view is gone, so a craft needs a way to fly blind.
/// Measures height above the terrain straight down and the range along the flight path (raycasts on the terrain and part layers),
/// beeps faster as impact nears (a generated tone, no sound file), and shows its reading in the part menu and on the camera feed /
/// blacked-out view (EntityTracker.ViewLock.cs). Rules: Dread/ViewLock.cs, ProximityWarn. Only the active vessel's radar sounds.</summary>
public class ModuleUTGroundRadar : PartModule
{
	[KSPField(isPersistant = true)] public bool radarOn = true;
	[KSPField] public float armHeight = 60f;      // m: below this, sinking gives "terrain"
	[KSPField] public float rangeAhead = 400f;    // m: how far ahead along the path it looks
	[KSPField] public float volume = 0.45f;

	[KSPField(guiActive = true, guiName = "Radar")]
	public string radarStatus = "-";

	public double Height { get; private set; } = double.PositiveInfinity;
	public double VerticalSpeed { get; private set; }
	public double TimeToImpact { get; private set; } = double.PositiveInfinity;
	public int WarnLevel { get; private set; }
	public bool Active { get { return radarOn && vessel != null && vessel == FlightGlobals.ActiveVessel && !vessel.Landed && !vessel.Splashed; } }

	AudioSource beep;
	float beepTimer, measureTimer;
	static AudioClip clip;
	const int RayMask = (1 << 15) | (1 << 0);   // terrain/buildings (15) and parts (0); the craft itself is skipped

	public override void OnStart(StartState state)
	{
		UpdateEvents();
		if (!HighLogic.LoadedSceneIsFlight) return;
		beep = gameObject.AddComponent<AudioSource>();
		beep.playOnAwake = false;
		beep.spatialBlend = 0f;
		beep.volume = volume * GameSettings.SHIP_VOLUME;
		beep.clip = Clip();
	}

	static AudioClip Clip()
	{
		if (clip != null) return clip;
		const int rate = 44100;
		int n = rate * 9 / 100;   // 90 ms
		float[] s = new float[n];
		for (int i = 0; i < n; i++)
		{
			float t = i / (float)rate;
			float env = Mathf.Min(1f, i / 220f) * Mathf.Min(1f, (n - i) / 400f);
			s[i] = env * (0.7f * Mathf.Sin(2f * Mathf.PI * 1250f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 2500f * t)) * 0.6f;
		}
		clip = AudioClip.Create("ut_radar_beep", n, 1, rate, false);
		clip.SetData(s, 0);
		return clip;
	}

	[KSPEvent(guiActive = true, guiName = "Radar: on")]
	public void ToggleRadar() { radarOn = !radarOn; UpdateEvents(); }

	[KSPAction("Toggle ground radar")]
	public void ToggleRadarAction(KSPActionParam param) { ToggleRadar(); }

	void UpdateEvents() { Events["ToggleRadar"].guiName = radarOn ? "Radar: on" : "Radar: off"; }

	public void Update()
	{
		if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
		measureTimer -= Time.deltaTime;
		if (measureTimer <= 0f)
		{
			measureTimer = 0.1f;
			Measure();
		}
		if (!Active || Time.timeScale <= 0f) { WarnLevel = Active ? WarnLevel : 0; return; }
		float every = ProximityWarn.BeepInterval(WarnLevel, TimeToImpact);
		if (every <= 0f) { beepTimer = 0f; return; }
		beepTimer -= Time.unscaledDeltaTime;
		if (beepTimer > 0f) return;
		beepTimer = every;
		if (beep != null && beep.clip != null) { beep.volume = volume * GameSettings.SHIP_VOLUME; beep.PlayOneShot(beep.clip, 1f); }   // never restarts a beep mid-sample (click)
	}

	void Measure()
	{
		CultureInfo c = CultureInfo.InvariantCulture;
		if (!radarOn) { radarStatus = "off"; WarnLevel = 0; return; }
		if (vessel.Landed || vessel.Splashed) { radarStatus = "on the ground"; WarnLevel = 0; Height = 0; TimeToImpact = double.PositiveInfinity; return; }
		Vector3 from = part.transform.position;
		Vector3 down = (vessel.mainBody.position - (Vector3d)from).normalized;
		double h = vessel.heightFromTerrain >= 0f ? vessel.heightFromTerrain : double.PositiveInfinity;
		RaycastHit hit;
		if (Cast(from, down, 5000f, out hit)) h = Math.Min(h, hit.distance);
		Vector3 vel = (Vector3)vessel.srf_velocity;
		float speed = vel.magnitude;
		double ahead = double.PositiveInfinity;
		if (speed > 0.5f && Cast(from, vel / speed, rangeAhead, out hit)) ahead = hit.distance;
		Height = h;
		VerticalSpeed = vessel.verticalSpeed;
		TimeToImpact = ProximityWarn.TimeToImpact(h, VerticalSpeed, ahead, speed);
		WarnLevel = ProximityWarn.Level(h, VerticalSpeed, TimeToImpact, armHeight);
		string alt = double.IsInfinity(h) ? "---" : h.ToString(h < 100 ? "F1" : "F0", c) + " m";
		string tti = double.IsInfinity(TimeToImpact) || TimeToImpact > 99 ? "" : "  impact " + TimeToImpact.ToString("F0", c) + " s";
		string call = ProximityWarn.Callout(WarnLevel);
		radarStatus = alt + "  " + VerticalSpeed.ToString("+0.0;-0.0", c) + " m/s" + tti + (call.Length > 0 ? "  " + call : "");
	}

	bool Cast(Vector3 from, Vector3 dir, float range, out RaycastHit best)
	{
		best = new RaycastHit();
		RaycastHit[] hits = Physics.RaycastAll(from, dir, range, RayMask, QueryTriggerInteraction.Ignore);
		float bestD = float.MaxValue;
		bool any = false;
		for (int i = 0; i < hits.Length; i++)
		{
			Part p = hits[i].collider != null ? hits[i].collider.GetComponentInParent<Part>() : null;
			if (p != null && p.vessel == vessel) continue;   // our own craft
			if (hits[i].distance < bestD) { bestD = hits[i].distance; best = hits[i]; any = true; }
		}
		return any;
	}

	/// <summary>Short line for the feed overlay.</summary>
	public string HudLine()
	{
		if (!radarOn || vessel == null || vessel.Landed || vessel.Splashed) return null;
		CultureInfo c = CultureInfo.InvariantCulture;
		string alt = double.IsInfinity(Height) ? "----" : Height.ToString(Height < 100 ? "0.0" : "0", c);
		return "RAD ALT " + alt + "  V/S " + VerticalSpeed.ToString("+0.0;-0.0", c);
	}
}
