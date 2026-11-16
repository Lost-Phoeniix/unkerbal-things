using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnkerbalThings.Dread;

// Game side of the worm swarm's hunt. The brain is Dread/WormHunt.cs.
// The swarm is blind and Moho has no air: it feels only what travels through the ground. This file turns what the craft does
// into ground signals (touchdowns, engines low over the ground, RCS/wheels/drills while landed, impacts, explosions, the seismic
// signal generator) and reports landed craft near the heap; then moves the creature as the brain says (a crawling heap or the
// serpent, MohoWormGpuSkin.TravelMode), starts the ambush when it wants to strike (EntityTracker.WormAttack.cs) and keeps the
// strike locked on. Spawn ~5 km away (hidden, no height rule), despawn beyond 10 km.
// Only with the gpu worm body and mohoWormAmbush; mohoWormHunt = false restores the old hunt (Dread/DreadHunt).
public partial class EntityTracker
{
	private static bool mohoWormHunt = true;
	private static float mohoWormSpawnMetres = 5000f;
	private static float mohoWormDespawnMetres = 10000f;
	private static float mohoWormSpawnMaxHeight = 5000f;   // m above the ground under the craft (not above sea level)
	private static bool mohoWormAlwaysSpawn = true;        // Moho is dangerous: the swarm is always there (no chance roll)
	private float wormSpawnCheck;
	internal string WormSpawnState = "";
	private static readonly WormHuntSettings wormHuntSettings = new WormHuntSettings();

	private WormHunt wormBrain;
	private HuntFrame wormFrame;
	private Guid wormTargetId;
	private bool wormHaveTarget;
	private float wormSeismicPulseAt = -1000f;
	private string wormLastSignalVessel = "";
	// science loop (WormSciencePart.cs); the plane (HuntFrame) stays fixed for the creature's life: it never gets farther than
	// mohoWormDespawnMetres from the craft, and the projection is exact in distance from its origin
	private static float wormScanMetres = 50f, wormScanSeconds = 30f, wormPhotoMetres = 300f, wormEnergyMetres = 150f;
	private static float wormSpotterMaxAltitude = 5000f, wormStudySeconds = 180f;
	// the swarm messes with electronics: science readouts get approximate and garbled between these distances (WormReadout)
	private static bool wormReadoutNoise = true;
	private static float wormReadoutNoiseNear = 100f, wormReadoutNoiseFar = 2500f;
	// how long each timed experiment runs (s); the scan's is wormScanSeconds
	private static float wormPhotoSeconds = 10f, wormEnergySeconds = 15f, wormSeismicSeconds = 30f, wormStudyRunSeconds = 15f;

	internal static EntityTracker Instance { get { return instance; } }
	internal WormHunt WormBrain { get { return WormHuntOn ? wormBrain : null; } }

	private static void LoadWormHuntSettings(ConfigNode node)
	{
		WormHuntSettings s = wormHuntSettings;
		ReadBoolScareSetting(node, "mohoWormHunt", ref mohoWormHunt);
		ReadFloatSetting(node, "mohoWormSpawnMetres", ref mohoWormSpawnMetres);
		ReadFloatSetting(node, "mohoWormDespawnMetres", ref mohoWormDespawnMetres);
		ReadFloatSetting(node, "mohoWormSpawnMaxHeight", ref mohoWormSpawnMaxHeight);
		ReadFloatSetting(node, "mohoWormAttackDigIn", ref MohoWormGpuSkin.HurryHold);
		MohoWormGpuSkin.HurryHold = Mathf.Clamp(MohoWormGpuSkin.HurryHold, 0f, 8f);
		ReadBoolScareSetting(node, "mohoWormAlwaysSpawn", ref mohoWormAlwaysSpawn);
		ReadFloatSetting(node, "wormDormantSpeed", ref s.DormantSpeed);
		ReadFloatSetting(node, "wormSearchSpeed", ref s.SearchSpeed);
		ReadFloatSetting(node, "wormWanderSpeed", ref s.WanderSpeed);
		ReadFloatSetting(node, "wormDormantLeash", ref s.DormantLeash);
		ReadFloatSetting(node, "wormHeapTrailSoft", ref wormHeapTrailSoft);
		ReadFloatSetting(node, "mohoWormSearchHurry", ref MohoWormGpuSkin.ChaseUrgency);
		ReadFloatSetting(node, "wormHeapRollMetres", ref MohoWormGpuSkin.RollDistance);
		MohoWormGpuSkin.RollDistance = Mathf.Clamp(MohoWormGpuSkin.RollDistance, 0f, 140f);
		wormHeapTrailSoft = Mathf.Clamp(wormHeapTrailSoft, 0f, 150f);
		MohoWormGpuSkin.ChaseUrgency = Mathf.Clamp(MohoWormGpuSkin.ChaseUrgency, 1f, 5f);
		ReadFloatSetting(node, "mohoWormDormantSpread", ref MohoWormGpuSkin.CalmSpread);
		MohoWormGpuSkin.CalmSpread = Mathf.Clamp(MohoWormGpuSkin.CalmSpread, 5f, 150f);
		ReadFloatSetting(node, "wormScrambleSerpentSpeed", ref s.ScrambleSerpentSpeed);
		ReadFloatSetting(node, "wormAlertMinMetres", ref s.AlertMinMetres);
		ReadFloatSetting(node, "wormAlertMaxMetres", ref s.AlertMaxMetres);
		ReadFloatSetting(node, "wormSearchCircleMetres", ref s.SearchCircleMetres);
		ReadFloatSetting(node, "wormSearchGiveUpSeconds", ref s.SearchGiveUpSeconds);
		ReadFloatSetting(node, "wormFeelPresenceMetres", ref s.PresenceMetres);
		ReadFloatSetting(node, "wormFeelTouchdownMetres", ref s.TouchdownMetres);
		ReadFloatSetting(node, "wormTouchdownSoftSpeed", ref s.TouchdownSoftSpeed);
		ReadFloatSetting(node, "wormTouchdownHardSpeed", ref s.TouchdownHardSpeed);
		ReadFloatSetting(node, "wormFeelHoverMetres", ref s.HoverMetres);
		ReadFloatSetting(node, "wormHoverHeight", ref s.HoverHeight);
		ReadFloatSetting(node, "wormFeelRcsMetres", ref s.RcsMetres);
		ReadFloatSetting(node, "wormFeelImpactMetres", ref s.ImpactMetres);
		ReadFloatSetting(node, "wormFeelDrillMetres", ref s.DrillMetres);
		ReadFloatSetting(node, "wormFeelExplosionMetres", ref s.ExplosionMetres);
		ReadFloatSetting(node, "wormFeelWheelsMetres", ref s.WheelsMetres);
		ReadFloatSetting(node, "wormLockEscapeMetres", ref s.LockEscapeMetres);
		ReadFloatSetting(node, "wormPlungeLeadSeconds", ref s.PlungeLeadSeconds);
		ReadFloatSetting(node, "wormScrambleHeapSeconds", ref s.ScrambleHeapSeconds);
		ReadFloatSetting(node, "wormScrambleSerpentSeconds", ref s.ScrambleSerpentSeconds);
		ReadFloatSetting(node, "wormScrambleRadius", ref s.ScrambleRadius);
		ReadFloatSetting(node, "wormWanderSeconds", ref s.WanderSeconds);
		ReadFloatSetting(node, "wormScanMetres", ref wormScanMetres);
		ReadFloatSetting(node, "wormScanSeconds", ref wormScanSeconds);
		ReadFloatSetting(node, "wormPhotoMetres", ref wormPhotoMetres);
		ReadFloatSetting(node, "wormEnergyMetres", ref wormEnergyMetres);
		ReadFloatSetting(node, "wormSpotterMaxAltitude", ref wormSpotterMaxAltitude);
		ReadFloatSetting(node, "wormStudySeconds", ref wormStudySeconds);
		ReadBoolScareSetting(node, "wormReadoutNoise", ref wormReadoutNoise);
		ReadFloatSetting(node, "wormReadoutNoiseNear", ref wormReadoutNoiseNear);
		ReadFloatSetting(node, "wormReadoutNoiseFar", ref wormReadoutNoiseFar);
		wormReadoutNoiseNear = Mathf.Clamp(wormReadoutNoiseNear, 0f, 5000f);
		wormReadoutNoiseFar = Mathf.Clamp(wormReadoutNoiseFar, wormReadoutNoiseNear + 1f, 20000f);
		ReadFloatSetting(node, "wormPhotoSeconds", ref wormPhotoSeconds);
		ReadFloatSetting(node, "wormEnergySeconds", ref wormEnergySeconds);
		ReadFloatSetting(node, "wormSeismicSeconds", ref wormSeismicSeconds);
		ReadFloatSetting(node, "wormStudyRunSeconds", ref wormStudyRunSeconds);
		wormPhotoSeconds = Mathf.Clamp(wormPhotoSeconds, 0f, 600f);
		wormEnergySeconds = Mathf.Clamp(wormEnergySeconds, 0f, 600f);
		wormSeismicSeconds = Mathf.Clamp(wormSeismicSeconds, 0f, 600f);
		wormStudyRunSeconds = Mathf.Clamp(wormStudyRunSeconds, 0f, 600f);
		mohoWormSpawnMetres = Mathf.Clamp(mohoWormSpawnMetres, 500f, 20000f);
		mohoWormDespawnMetres = Mathf.Clamp(mohoWormDespawnMetres, mohoWormSpawnMetres * 1.2f, 50000f);
		mohoWormSpawnMaxHeight = Mathf.Clamp(mohoWormSpawnMaxHeight, 10f, 50000f);
		wormScanMetres = Mathf.Clamp(wormScanMetres, 5f, 1000f);
		wormScanSeconds = Mathf.Clamp(wormScanSeconds, 0f, 600f);
		wormPhotoMetres = Mathf.Clamp(wormPhotoMetres, 10f, 10000f);
		wormEnergyMetres = Mathf.Clamp(wormEnergyMetres, 10f, 10000f);
		wormSpotterMaxAltitude = Mathf.Clamp(wormSpotterMaxAltitude, 100f, 100000f);
		wormStudySeconds = Mathf.Clamp(wormStudySeconds, 10f, 3600f);
		// the serpent's speed is the gpu body's capped sprint
		s.SerpentSpeed = mohoWormSerpentMaxSpeed;
		s.AmbushMetres = mohoAmbushDistance;
		s.Clamp();
	}

	/// <summary>Configured for the worm hunt (before a body exists): spawn rules switch with this.</summary>
	private static bool WormHuntConfigured { get { return mohoWormHunt && mohoWormAmbush && mohoWormBody == "gpu"; } }
	/// <summary>The worm hunt drives the creature (the gpu body is up).</summary>
	private bool WormHuntOn { get { return WormHuntConfigured && WormGpuCatches; } }

	/// <summary>Height of the craft above the ground under it (m). One terrain lookup on the planet's own height map: KSP's
	/// heightFromTerrain turns into the height above sea level once the craft is more than ~600 m up, which is wrong over mountains.</summary>
	private static double WormHeightAboveGround(Vessel v)
	{
		CelestialBody b = v.mainBody;
		double ground = b.TerrainAltitude(v.latitude, v.longitude);
		if (b.ocean && ground < 0.0) ground = 0.0;
		return v.altitude - ground;
	}

	private static bool WormHuntEligible(Vessel v)
	{
		if (v == null || v.mainBody == null || v.Splashed || !IsBulkEntityBody(v.mainBody)) return false;
		return WormHeightAboveGround(v) <= mohoWormSpawnMaxHeight;
	}

	// The swarm is always on Moho (mohoWormAlwaysSpawn): every 2 s, while no creature is out and the craft is low enough, it spawns.
	private void UpdateWormAlwaysSpawn()
	{
		if (!WormHuntConfigured || !mohoWormAlwaysSpawn || !HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f) { WormSpawnState = ""; return; }
		wormSpawnCheck -= Time.unscaledDeltaTime;
		if (wormSpawnCheck > 0f) return;
		wormSpawnCheck = 2f;
		Vessel v = FlightGlobals.ActiveVessel;
		if (v == null || v.mainBody == null || !IsBulkEntityBody(v.mainBody)) { WormSpawnState = "not on Moho/Eve"; return; }
		if (bulkEntityActive) { WormSpawnState = "out"; return; }
		if (bloopEntityActive) { WormSpawnState = "leviathan is out"; return; }
		double h = WormHeightAboveGround(v);
		if (v.Splashed || h > mohoWormSpawnMaxHeight) { WormSpawnState = "craft " + (h / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " km above ground: waiting"; return; }
		SpawnBulkEntity();
		WormSpawnState = bulkEntityActive ? "spawned" : "failed: " + bulkEntityStatus;
		Debug.Log("[UT Worm] always-spawn: craft " + h.ToString("F0", CultureInfo.InvariantCulture) + " m above ground -> " + WormSpawnState);
	}

	/// <summary>Spawn place for the worm hunt: hidden if possible; else just out of the camera's view; else anywhere at the distance
	/// (a craft high up sees almost all the ground, and the swarm must always be there).</summary>
	private bool WormFindSpawn(Vessel v, CelestialBody body, out Vector3d ground, out string how)
	{
		float a = mohoWormSpawnMetres * 0.85f, b = mohoWormSpawnMetres * 1.15f;
		how = "hidden";
		if (mohoSpawnHidden && TryFindHiddenSpawnPosition(v, body, a, b, out ground)) return true;
		Vector3d craft = v.GetWorldPos3D();
		Vector3d up = (craft - body.position).normalized;
		Vector3d east = Vector3d.Exclude(up, body.transform.up).normalized;
		Vector3d any = Vector3d.zero;
		bool haveAny = false;
		for (int i = 0; i < 64; i++)
		{
			Vector3d dir = (Vector3d)(Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), (Vector3)up) * (Vector3)east);
			Vector3d g = ProjectToGround(craft + dir * (double)UnityEngine.Random.Range(a, b), body);
			if (!IsLandAt(g, body)) continue;
			if (!haveAny) { any = g; haveAny = true; }
			if (!MohoBodyInView(g, body, 0.35)) { ground = g; how = "out of camera view"; return true; }
		}
		ground = any;
		how = "anywhere";
		return haveAny;
	}

	private void ResetWormHunt()
	{
		wormBrain = null;
		wormFrame = null;
		wormHaveTarget = false;
		wormVesselStates.Clear();
		wormSignals.Clear();
		WormRemoveHooks();
	}

	// ---------------------------------------------------------------- per frame

	// Called from UpdateBulkEntity (after the ambush hook). True: the worm hunt handled this frame.
	private bool UpdateWormHunt()
	{
		if (!WormHuntOn) return false;
		Vessel active = FlightGlobals.ActiveVessel;
		CelestialBody body = bulkEntityBody;
		if (active == null || body == null) return true;
		Vector3d pos = body.position + bulkEntityRelativePosition;
		// despawn only when the active vessel is far away (or elsewhere)
		if (active.mainBody != body || (active.GetWorldPos3D() - pos).magnitude > mohoWormDespawnMetres)
		{
			WormLog("despawn, the active vessel is beyond " + mohoWormDespawnMetres.ToString("F0", CultureInfo.InvariantCulture) + " m");
			DespawnBulkEntity();
			return true;
		}
		float dt = TimeWarp.deltaTime;
		if (Time.timeScale <= 0f || dt <= 0f) return true;
		dt = Mathf.Min(dt, 0.25f);
		MohoWormGpuSkin g = WormGpu;
		if (wormBrain == null || wormFrame == null)
		{
			wormFrame = new HuntFrame(body, pos);
			wormBrain = new WormHunt(wormHuntSettings, Environment.TickCount ^ 0x5bd1e995);
			double hx, hy; wormFrame.ToPlane(pos, out hx, out hy);
			wormBrain.X = hx; wormBrain.Y = hy;
			wormBrain.Heading = UnityEngine.Random.Range(0f, 6.2831853f);
			WormLog("dormant heap " + (active.GetWorldPos3D() - pos).magnitude.ToString("F0", CultureInfo.InvariantCulture) + " m from the craft");
		}
		WormHunt b = wormBrain;
		double x, y;
		wormFrame.ToPlane(pos, out x, out y);
		b.X = x; b.Y = y;

		// senses: only what travels through the ground
		WormSense(body, dt);
		for (int i = 0; i < wormSignals.Count; i++)
		{
			WormSignalEvent e = wormSignals[i];
			double sx, sy;
			wormFrame.ToPlane(e.position, out sx, out sy);
			WormFelt felt = b.Feel(e.kind, sx, sy, e.strength);
			if (felt == WormFelt.Spotted || felt == WormFelt.Alerted)
			{
				wormLastSignalVessel = e.vesselName;
				WormLog(e.kind + " from " + e.vesselName + " at " + b.LastSignalDistance.ToString("F0", CultureInfo.InvariantCulture) + " m -> " + felt);
				if (felt == WormFelt.Spotted) { wormTargetId = e.vesselId; wormHaveTarget = e.vesselId != Guid.Empty; }
			}
		}
		wormSignals.Clear();
		Vessel near = NearestLandedVessel(body, pos);
		if (near != null && b.State != WormState.Attack)
		{
			double cx, cy;
			wormFrame.ToPlane(VesselCentre(near), out cx, out cy);
			if (b.Presence(cx, cy) == WormFelt.Spotted)
			{
				wormTargetId = near.id; wormHaveTarget = true; wormLastSignalVessel = near.vesselName;
				WormLog("landed " + near.vesselName + " within reach -> spotted");
			}
		}

		// locked on: the strike follows the craft (unless it moved far enough sideways)
		Vessel target = WormTarget(body);
		if (b.State == WormState.Attack)
		{
			if (target == null && !b.AmbushRunning)
			{
				WormLog("the target is gone");
				b.AmbushEnded(false);
			}
			else if (target != null)
			{
				double cx, cy;
				wormFrame.ToPlane(VesselCentre(target), out cx, out cy);
				b.TrackCraft(cx, cy);
			}
		}

		// a crawling heap is only as fast as its worms: past wormHeapTrailSoft m of trail it slows, down to a fifth at twice that
		if (g != null && b.Body == WormBodyMode.Heap)
		{
			float want = wormHeapTrailSoft > 0f ? Mathf.Clamp(1f - (g.HeapTrail - wormHeapTrailSoft) / wormHeapTrailSoft, 0.2f, 1f) : 1f;
			b.HeapSpeedScale = Mathf.MoveTowards(b.HeapSpeedScale, want, dt * 0.8f);
		}
		else b.HeapSpeedScale = 1f;
		b.Update(dt);
		UpdateWormWatch();
		if (g != null)
		{
			g.TravelMode = b.Body == WormBodyMode.Serpent ? MohoWormGpuSkin.Travel.Serpent : MohoWormGpuSkin.Travel.Heap;
			g.Hurry = b.Body == WormBodyMode.Serpent || b.State == WormState.Attack;   // the hunt sets off quickly: everyone digs in at once, short dig-in
			g.Calm = b.State == WormState.Dormant || b.State == WormState.Fed;   // a resting heap churns instead of combing one way
			g.Chase = b.State == WormState.Search || b.State == WormState.Wander;   // a searching heap's worms crawl harder to keep up
		}

		if (b.WantsAmbush && target != null)
		{
			if (TryStartWormAmbush(target, pos, true))
			{
				b.AmbushStarted();
				return true;
			}
		}

		// move as the brain says; a heap that must dig in before the serpent sets off holds the creature still
		Vector3d moved = ProjectToGround(wormFrame.FromPlane(b.X, b.Y), body);
		bool held = !WormMayMove(moved);
		if (!held) pos = moved;
		else { b.X = x; b.Y = y; }
		bulkEntityRelativePosition = pos - body.position;
		bulkEntityChasing = b.Aware || b.State == WormState.Attack;   // the dread layer reads this
		bulkEntityStandingDown = false;
		bulkEntityRetreating = false;
		bulkEntityStatus = held ? "BURROWING" : WormStatusText(b);
		Vector3d up = (pos - body.position).normalized;
		Vector3d fwd;
		if (HuntPlaneDirection(wormFrame, pos, up, b.Heading, out fwd)) bulkEntityObject.transform.rotation = Quaternion.LookRotation((Vector3)fwd, (Vector3)up);
		bulkEntityObject.transform.position = (Vector3)pos;
		FeedMoho(pos, up, target, bulkEntityChasing);
		return true;
	}

	// worm hunt notes: the Moho log (debug, test runs) and KSP.log, so a test flight leaves a trail
	private void WormLog(string what)
	{
		MohoNote("worm hunt: " + what);
		Debug.Log("[UT Worm] " + what);
	}

	private static bool HuntPlaneDirection(HuntFrame frame, Vector3d position, Vector3d up, double heading, out Vector3d direction)
	{
		double px, py;
		frame.ToPlane(position, out px, out py);
		Vector3d t = frame.FromPlane(px + Math.Cos(heading) * 10.0, py + Math.Sin(heading) * 10.0) - position;
		t -= up * Vector3d.Dot(t, up);
		direction = t.sqrMagnitude > 1e-6 ? t.normalized : Vector3d.zero;
		return t.sqrMagnitude > 1e-6;
	}

	private static string WormStatusText(WormHunt b)
	{
		switch (b.State)
		{
		case WormState.Dormant: return "DORMANT";
		case WormState.Fed: return "FED (DORMANT)";
		case WormState.Alerted: return "ALERTED";
		case WormState.Search: return "SEARCHING";
		case WormState.Attack: return b.Direct ? "STRIKING" : "HUNTING";
		case WormState.Scramble: return "SCRAMBLING";
		default: return "WANDERING";
		}
	}

	private Vessel WormTarget(CelestialBody body)
	{
		if (!wormHaveTarget) return null;
		List<Vessel> loaded = FlightGlobals.VesselsLoaded;
		for (int i = 0; i < loaded.Count; i++)
		{
			Vessel v = loaded[i];
			if (v != null && v.id == wormTargetId) return (v.state != Vessel.State.DEAD && v.mainBody == body && v.parts != null && v.parts.Count > 0) ? v : null;
		}
		return null;
	}

	private static bool WormCountsAsCraft(Vessel v)
	{
		return v != null && v.loaded && v.parts != null && v.parts.Count > 0 && v.state != Vessel.State.DEAD
			&& v.vesselType != VesselType.Flag && v.vesselType != VesselType.Debris && v.vesselType != VesselType.SpaceObject && v.vesselType != VesselType.DeployedScienceController && v.vesselType != VesselType.DeployedSciencePart;
	}

	// the nearest landed craft (or Kerbal on the ground) on the swarm's body
	private Vessel NearestLandedVessel(CelestialBody body, Vector3d pos)
	{
		Vessel best = null;
		double bestD = double.MaxValue;
		List<Vessel> loaded = FlightGlobals.VesselsLoaded;
		for (int i = 0; i < loaded.Count; i++)
		{
			Vessel v = loaded[i];
			if (!WormCountsAsCraft(v) || v.mainBody != body || !v.Landed) continue;
			double d = (v.GetWorldPos3D() - pos).magnitude;
			if (d < bestD) { bestD = d; best = v; }
		}
		return best;
	}

	// ---------------------------------------------------------------- ground signals

	private struct WormSignalEvent
	{
		public WormSignal kind;
		public Vector3d position;
		public float strength;
		public Guid vesselId;
		public string vesselName;
	}

	private sealed class WormVesselState
	{
		public bool landed;
		public float airTime;
		public float lastSpeed;
		public float scanTimer;
		public float ping;
		public bool enginesOn, drilling;
		public float clearance = float.MaxValue;
	}

	private readonly List<WormSignalEvent> wormSignals = new List<WormSignalEvent>();
	private readonly Dictionary<Guid, WormVesselState> wormVesselStates = new Dictionary<Guid, WormVesselState>();
	private bool wormHooks;
	private float wormIgnoreDeathsUntil;

	private void WormAddSignal(WormSignal kind, Vector3d at, float strength, Vessel v)
	{
		WormSignalEvent e = new WormSignalEvent();
		e.kind = kind; e.position = at; e.strength = strength;
		e.vesselId = v != null ? v.id : Guid.Empty;
		e.vesselName = v != null ? v.vesselName : "?";
		wormSignals.Add(e);
	}

	private void WormEnsureHooks()
	{
		if (wormHooks) return;
		wormHooks = true;
		GameEvents.onPartDie.Add(OnWormPartDie);
	}

	private void WormRemoveHooks()
	{
		if (!wormHooks) return;
		wormHooks = false;
		GameEvents.onPartDie.Remove(OnWormPartDie);
	}

	// an explosion shakes the ground (the swarm's own kills do not count)
	private void OnWormPartDie(Part p)
	{
		try
		{
			if (p == null || p.vessel == null || !bulkEntityActive || bulkEntityBody == null || p.vessel.mainBody != bulkEntityBody) return;
			if (WormAmbushActive || Time.time < wormIgnoreDeathsUntil) return;
			if (p.vessel.isEVA && p.vessel.parts != null && p.vessel.parts.Count <= 1 && !p.vessel.Landed) return;
			WormAddSignal(WormSignal.Explosion, p.transform.position, 0f, p.vessel);
		}
		catch (Exception) { }
	}

	/// <summary>The seismic signal generator fired (WormSciencePart.cs): the swarm spots it at once, wherever it is.</summary>
	internal void WormSeismicPulse(Vessel v)
	{
		if (v == null || !bulkEntityActive || bulkEntityBody == null || v.mainBody != bulkEntityBody) return;
		wormSeismicPulseAt = Time.time;
		WormAddSignal(WormSignal.Seismic, VesselCentre(v), 0f, v);
	}

	// Once per frame: what the loaded craft on the swarm's body do that travels through the ground.
	private void WormSense(CelestialBody body, float dt)
	{
		WormEnsureHooks();
		List<Vessel> loaded = FlightGlobals.VesselsLoaded;
		for (int i = 0; i < loaded.Count; i++)
		{
			Vessel v = loaded[i];
			if (v == null || v.mainBody != body || v.parts == null || v.parts.Count == 0 || v.state == Vessel.State.DEAD || v.vesselType == VesselType.Flag) continue;
			WormVesselState st;
			bool landed = v.Landed;
			float speed = (float)v.srf_velocity.magnitude;
			if (!wormVesselStates.TryGetValue(v.id, out st))
			{
				st = new WormVesselState { landed = landed, lastSpeed = speed };
				wormVesselStates[v.id] = st;
				continue;
			}
			bool craft = WormCountsAsCraft(v);
			// touchdown: a real landing after at least a second in the air. The active craft's landing alerts it anywhere; anything
			// else that comes down (debris, a Kerbal hopping) is an impact.
			if (landed && !st.landed && st.airTime >= 1f)
			{
				if (craft && v == FlightGlobals.ActiveVessel && !v.isEVA) WormAddSignal(WormSignal.Touchdown, VesselCentre(v), st.lastSpeed, v);
				else WormAddSignal(WormSignal.Impact, VesselCentre(v), st.lastSpeed, v);
			}
			st.airTime = landed ? 0f : st.airTime + dt;
			st.landed = landed;
			if (!landed) st.lastSpeed = speed;
			if (!craft || v.isEVA) continue;
			// systems, scanned twice a second
			st.scanTimer -= dt;
			if (st.scanTimer <= 0f)
			{
				st.scanTimer = 0.5f;
				WormScanSystems(v, st);
			}
			st.ping -= dt;
			if (st.ping > 0f) continue;
			st.ping = 1f;
			Vector3d at = VesselCentre(v);
			// engines running less than wormHoverHeight above the actual ground (landed counts as 0 m)
			if (st.enginesOn && (landed || st.clearance < wormHuntSettings.HoverHeight)) WormAddSignal(WormSignal.Hover, at, 0f, v);
			if (!landed) continue;
			FlightCtrlState ctrl = v.ctrlState;
			if (ctrl != null && v.ActionGroups[KSPActionGroup.RCS] && (Mathf.Abs(ctrl.pitch) > 0.05f || Mathf.Abs(ctrl.yaw) > 0.05f || Mathf.Abs(ctrl.roll) > 0.05f || Mathf.Abs(ctrl.X) > 0.05f || Mathf.Abs(ctrl.Y) > 0.05f || Mathf.Abs(ctrl.Z) > 0.05f))
				WormAddSignal(WormSignal.Rcs, at, 0f, v);
			if (ctrl != null && speed > 0.8f && (Mathf.Abs(ctrl.wheelThrottle) > 0.05f || Mathf.Abs(ctrl.wheelSteer) > 0.05f))
				WormAddSignal(WormSignal.Wheels, at, 0f, v);
			if (st.drilling) WormAddSignal(WormSignal.Drill, at, 0f, v);
		}
	}

	private static void WormScanSystems(Vessel v, WormVesselState st)
	{
		st.enginesOn = false;
		st.drilling = false;
		try
		{
			List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
			for (int i = 0; i < engines.Count; i++)
			{
				ModuleEngines e = engines[i];
				if (e != null && e.EngineIgnited && !e.flameout && e.finalThrust > 0.01f) { st.enginesOn = true; break; }
			}
			List<ModuleResourceHarvester> drills = v.FindPartModulesImplementing<ModuleResourceHarvester>();
			for (int i = 0; i < drills.Count; i++)
			{
				if (drills[i] != null && drills[i].IsActivated) { st.drilling = true; break; }
			}
			// height of the lowest part above the ground (the root's terrain height moved down to the lowest part)
			st.clearance = float.MaxValue;
			if (!v.Landed && v.heightFromTerrain >= 0f)
			{
				Vector3d up = (v.GetWorldPos3D() - v.mainBody.position).normalized;
				Vector3 root = v.transform.position;
				float lowest = 0f;
				for (int i = 0; i < v.parts.Count; i++)
				{
					Part p = v.parts[i];
					if (p == null) continue;
					lowest = Mathf.Min(lowest, Vector3.Dot(p.transform.position - root, (Vector3)up));
				}
				st.clearance = Mathf.Max(0f, v.heightFromTerrain + lowest);
			}
		}
		catch (Exception) { }
	}

	// ---------------------------------------------------------------- ambush hooks

	/// <summary>The aim of the strike while the worm hunt is locked on: the craft while it stays near the lock, else the last held point.</summary>
	private bool WormLockedAim(Vessel v, out Vector3d aim)
	{
		aim = Vector3d.zero;
		WormHunt b = WormBrain;
		if (b == null || !b.Locked || wormFrame == null) return false;
		double cx, cy;
		wormFrame.ToPlane(VesselCentre(v), out cx, out cy);
		b.TrackCraft(cx, cy);
		if (!b.LockBroken) return false;   // still locked: the normal aim (the craft, led)
		aim = wormFrame.FromPlane(b.TargetX, b.TargetY);
		return true;
	}

	private void WormHuntAmbushEnded(bool kill)
	{
		wormIgnoreDeathsUntil = Time.time + 4f;
		WormHunt b = WormBrain;
		if (b == null) return;
		b.AmbushEnded(kill);
		wormHaveTarget = false;
		WormLog("ambush " + (kill ? "killed -> fed heap" : "missed -> scramble"));
	}

	// ---------------------------------------------------------------- science queries (WormSciencePart.cs)

	internal static float WormScanMetres { get { return wormScanMetres; } }
	internal static float WormScanSeconds { get { return wormScanSeconds; } }
	internal static float WormPhotoMetres { get { return wormPhotoMetres; } }
	internal static float WormEnergyMetres { get { return wormEnergyMetres; } }
	internal static float WormSpotterMaxAltitude { get { return wormSpotterMaxAltitude; } }
	internal static float WormPhotoSeconds { get { return wormPhotoSeconds; } }
	internal static float WormEnergySeconds { get { return wormEnergySeconds; } }
	internal static float WormSeismicSeconds { get { return wormSeismicSeconds; } }
	internal static float WormStudyRunSeconds { get { return wormStudyRunSeconds; } }
	/// <summary>Interference 0..1 on science readouts at `distance` m from the swarm; 0 when wormReadoutNoise is off.</summary>
	internal static float WormReadoutInterference(double distance) { return wormReadoutNoise ? WormReadout.Interference(distance, wormReadoutNoiseNear, wormReadoutNoiseFar) : 0f; }
	internal static bool WormReadoutNoise { get { return wormReadoutNoise; } }

	/// <summary>The swarm on the vessel's body: its ground point and the straight-line distance from `from`. heap: it lies on the
	/// ground as a heap (not travelling as the serpent, not striking); visible: some of it is above the ground.</summary>
	internal bool WormSwarmFrom(Vessel v, Vector3d from, out Vector3d swarm, out double distance, out bool heap, out bool visible)
	{
		swarm = Vector3d.zero; distance = double.PositiveInfinity; heap = false; visible = false;
		if (v == null || !bulkEntityActive || bulkEntityBody == null || v.mainBody != bulkEntityBody) return false;
		swarm = bulkEntityBody.position + bulkEntityRelativePosition;
		MohoWormGpuSkin g = WormGpu;
		WormHunt b = WormBrain;
		bool serpent = g != null && g.SerpentActive;
		heap = !WormAmbushActive && !serpent && (b == null || b.Body == WormBodyMode.Heap);
		visible = g == null || g.VisibleWorms > 200 || serpent;
		// a heap: measure to the worms on the ground (their centre), which lag the creature's own point while it crawls
		if (heap && g != null && g.VisibleWorms > 200) swarm = (Vector3d)g.PointCentroid;
		distance = (from - swarm).magnitude;
		return true;
	}

	/// <summary>The behaviour study: the seismic signal generator fired a little while ago and the swarm came for it.</summary>
	internal bool WormStudyReady()
	{
		WormHunt b = WormBrain;
		if (b == null || Time.time - wormSeismicPulseAt > wormStudySeconds) return false;
		return b.State == WormState.Attack || b.State == WormState.Scramble || WormAmbushActive;
	}

	internal bool WormSeismicRecent { get { return Time.time - wormSeismicPulseAt <= wormStudySeconds; } }

	// ---------------------------------------------------------------- debug readout (debug only; shows the ranges)

	private float wormWatchUntil = -1f, wormWatchNext;
	private static float wormHeapTrailSoft = 50f;
	private long wormWatchPop0, wormWatchDug0;

	/// <summary>Test (debug tools): the swarm acts as if it felt this craft land far away: it travels as the serpent, comes up and
	/// searches round the craft. For 3 minutes a line goes to KSP.log every 2 s ([UT Worm] watch), so nobody has to watch it.</summary>
	private void WormTestSearch()
	{
		Vessel v = FlightGlobals.ActiveVessel;
		WormHunt b = WormBrain;
		if (v == null || b == null || wormFrame == null) { ScreenMessages.PostScreenMessage("No worm swarm out.", 3f, ScreenMessageStyle.UPPER_CENTER); return; }
		double cx, cy;
		wormFrame.ToPlane(VesselCentre(v), out cx, out cy);
		b.ForceAlert(cx, cy);
		MohoWormGpuSkin g = WormGpu;
		wormWatchPop0 = g != null ? g.PoppedInSight : 0; wormWatchDug0 = g != null ? g.DugIn : 0;
		wormWatchUntil = Time.time + 180f; wormWatchNext = 0f;
		WormLog("TEST search round " + v.vesselName + ": " + b.State + ", " + b.LastSignalDistance.ToString("F0", CultureInfo.InvariantCulture) + " m away; recording 180 s");
	}

	private void UpdateWormWatch()
	{
		if (wormWatchUntil < 0f) return;
		if (Time.time > wormWatchUntil) { wormWatchUntil = -1f; WormLog("watch: done"); return; }
		if (Time.time < wormWatchNext) return;
		wormWatchNext = Time.time + 2f;
		WormHunt b = WormBrain;
		MohoWormGpuSkin g = WormGpu;
		Vessel v = FlightGlobals.ActiveVessel;
		if (b == null || g == null || v == null || bulkEntityBody == null) return;
		CultureInfo c = CultureInfo.InvariantCulture;
		Vector3d pos = bulkEntityBody.position + bulkEntityRelativePosition;
		double lag = ((Vector3d)g.PointCentroid - pos).magnitude;
		WormLog("watch: " + b.State + "/" + b.Body + (g.SerpentActive ? " serpent " + g.Body.State : "") + ", speed " + b.Speed.ToString("F1", c) + " (x" + b.HeapSpeedScale.ToString("F2", c) + ")"
			+ ", craft " + (v.GetWorldPos3D() - pos).magnitude.ToString("F0", c) + " m, above ground " + g.VisibleWorms + ", vanished in sight +" + (g.PoppedInSight - wormWatchPop0)
			+ ", dug in +" + (g.DugIn - wormWatchDug0) + ", farthest worm " + g.MaxPointRadius.ToString("F0", c) + " m, heap centre trails " + lag.ToString("F0", c) + " m");
	}

	private void DrawWormHuntDebug()
	{
		WormHunt b = WormBrain;
		Vessel v = FlightGlobals.ActiveVessel;
		if (b == null || v == null || bulkEntityBody == null) return;
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Search round me (logs 3 min)")) WormTestSearch();
		if (GUILayout.Button("Seismic pulse (it knows where I am)")) WormSeismicPulse(v);
		GUILayout.EndHorizontal();
		CultureInfo c = CultureInfo.InvariantCulture;
		Vector3d pos = bulkEntityBody.position + bulkEntityRelativePosition;
		Vector3d craft = v.GetWorldPos3D();
		Vector3d up = (craft - bulkEntityBody.position).normalized;
		Vector3d d = pos - craft;
		Vector3d north = Vector3d.Exclude(up, bulkEntityBody.transform.up).normalized;
		Vector3d east = Vector3d.Cross(up, north).normalized;
		double bearing = WormScience.Bearing(Vector3d.Dot(d, east), Vector3d.Dot(d, north));
		MohoWormGpuSkin g = WormGpu;
		GUILayout.Label("State: " + b.State + " / " + b.Body + (g != null && g.SerpentActive ? " (serpent " + g.Body.State + (g.Body.Sinking ? ", sinking" : "") + ")" : "") + "  " + b.StateSeconds.ToString("F0", c) + " s  speed " + b.Speed.ToString("F1", c) + " m/s");
		GUILayout.Label("  swarm " + d.magnitude.ToString("F0", c) + " m, bearing " + bearing.ToString("F0", c) + " (" + WormScience.Compass(bearing) + ")");
		if (b.HasBelief && wormFrame != null)
		{
			Vector3d bw = wormFrame.FromPlane(b.BeliefX, b.BeliefY);
			GUILayout.Label("  belief circle: centre " + (bw - craft).magnitude.ToString("F0", c) + " m from you, radius " + b.BeliefRadius.ToString("F0", c) + " m" + (b.State == WormState.Search ? ", inside " + b.SearchInsideSeconds.ToString("F0", c) + " s" : ""));
		}
		if (b.Locked) GUILayout.Label("  locked" + (b.Direct ? " (direct)" : "") + (b.LockBroken ? ", BROKEN (you got " + wormHuntSettings.LockEscapeMetres.ToString("F0", c) + " m away)" : ""));
		if (b.HasFelt) GUILayout.Label("  last felt: " + b.LastSignal + " from " + wormLastSignalVessel + " at " + b.LastSignalDistance.ToString("F0", c) + " m (range " + (double.IsInfinity(b.LastSignalRange) ? "any" : b.LastSignalRange.ToString("F0", c)) + ") -> " + b.LastFelt + ", " + b.LastSignalAge.ToString("F0", c) + " s ago");
		GUILayout.Label("  kills " + b.Kills + ", misses " + b.Misses + (g != null ? ", worms above ground " + g.VisibleWorms + " of " + g.WormTotal + ", vanished in sight " + g.PoppedInSight : ""));
		if (wormWatchUntil > 0f) GUILayout.Label("  recording to KSP.log: " + Mathf.CeilToInt(wormWatchUntil - Time.time) + " s left");
	}
}
