using System;
using System.Collections.Generic;
using KrakensKreations.Dread;
using UnityEngine;

// The ground monsters (Mimic, Bulk, Rake, Laythe land creature) hunt from what they know.
// The brain is DreadHunt (pure, tested offline). This file is the game side: the senses (noise events from the active vessel, line of
// sight), the map from the planet to the brain's flat plane, the terrain the brain checks its steps against, body facing, the head look
// and the read-only state for the director, the footsteps, the debug panel and the test csv.
// Master switch huntAi. false: SmartGroundApproach (Stalk / Circle / Fake-out / Charge), the dark rule and the old facing, as before.
public partial class EntityTracker
{
	// ---- settings (see settings.cfg) ----
	private static bool huntAi = true;
	private static bool huntHeadLook = true;
	private static readonly HuntSettings huntSettings = new HuntSettings();
	private static float huntTurnSmallScale = 0.3f;   // turn radius factor of the Mimic, Rake and Laythe creature against the Moho creature

	private static void LoadHuntSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "huntAi", ref huntAi);
		ReadBoolScareSetting(node, "huntHeadLook", ref huntHeadLook);
		ReadFloatSetting(node, "huntGiveUpSeconds", ref huntSettings.GiveUpSeconds);
		ReadFloatSetting(node, "huntHearThrottleMetres", ref huntSettings.HearThrottle);
		ReadFloatSetting(node, "huntHearRcsMetres", ref huntSettings.HearRcs);
		ReadFloatSetting(node, "huntHearLightsMetres", ref huntSettings.HearLights);
		ReadFloatSetting(node, "huntHearImpactMetres", ref huntSettings.HearImpact);
		ReadFloatSetting(node, "huntHearWheelsMetres", ref huntSettings.HearWheels);
		ReadFloatSetting(node, "huntHearDecoupleMetres", ref huntSettings.HearDecouple);
		ReadFloatSetting(node, "huntSightMetres", ref huntSettings.SightMetres);
		ReadFloatSetting(node, "huntNoticeMetres", ref huntSettings.NoticeMetres);
		ReadFloatSetting(node, "huntTurnDegPerSecond", ref huntSettings.TurnDegPerSecond);
		ReadBoolScareSetting(node, "huntNaturalTurn", ref huntSettings.NaturalTurn);
		ReadFloatSetting(node, "huntTurnRadiusMetres", ref huntSettings.TurnRadiusMetres);
		ReadFloatSetting(node, "huntTurnRadiusPerSpeed", ref huntSettings.TurnRadiusPerSpeed);
		ReadFloatSetting(node, "huntTurnYawAccelDegPerSecond2", ref huntSettings.TurnYawAccelDegPerSecond2);
		ReadFloatSetting(node, "huntTurnCreepSpeed", ref huntSettings.TurnCreepSpeed);
		ReadFloatSetting(node, "huntTurnEaseFloor", ref huntSettings.TurnEaseFloor);
		ReadFloatSetting(node, "huntTurnSmallBodyScale", ref huntTurnSmallScale);
		huntTurnSmallScale = Mathf.Clamp(huntTurnSmallScale, 0.05f, 2f);
		ReadFloatSetting(node, "huntMaxSlope", ref huntSettings.MaxGradient);
		huntSettings.MaxGradient = Mathf.Clamp(huntSettings.MaxGradient, 0.3f, 3f);
		huntSettings.Clamp();
	}

	// ---------------------------------------------------------------- the map from the planet to the plane

	// Azimuthal equidistant projection around the point where the monster was first seen by the brain: distances from that point are exact,
	// and the hunts stay within a few kilometres of it.
	private sealed class HuntFrame
	{
		private readonly CelestialBody body;
		private readonly Vector3d o, e1, e2;
		private readonly double radius;

		public HuntFrame(CelestialBody body, Vector3d worldPosition)
		{
			this.body = body;
			Vector3d rel = worldPosition - body.position;
			radius = Math.Max(rel.magnitude, 1.0);
			o = rel / radius;
			Vector3d north = Vector3d.Exclude(o, (Vector3d)body.transform.up);
			if (north.sqrMagnitude < 1e-9)
			{
				north = Vector3d.Exclude(o, Vector3d.right);
			}
			e1 = north.normalized;
			e2 = Vector3d.Cross(o, e1).normalized;
		}

		public void ToPlane(Vector3d worldPosition, out double x, out double y)
		{
			Vector3d rel = worldPosition - body.position;
			double r = rel.magnitude;
			x = 0.0;
			y = 0.0;
			if (r < 1.0)
			{
				return;
			}
			Vector3d u = rel / r;
			double c = Vector3d.Dot(u, o);
			Vector3d perp = u - o * c;
			double pm = perp.magnitude;
			if (pm < 1e-12)
			{
				return;
			}
			double theta = Math.Atan2(pm, c);
			Vector3d d = perp / pm;
			x = radius * theta * Vector3d.Dot(d, e1);
			y = radius * theta * Vector3d.Dot(d, e2);
		}

		public Vector3d FromPlane(double x, double y)
		{
			double rr = Math.Sqrt(x * x + y * y);
			if (rr < 1e-9)
			{
				return body.position + o * radius;
			}
			double theta = rr / radius;
			Vector3d d = (e1 * x + e2 * y) / rr;
			Vector3d u = o * Math.Cos(theta) + d * Math.Sin(theta);
			return body.position + u * radius;
		}
	}

	// What the brain checks its steps against: the same terrain the monsters are projected onto (ProjectToGround), and the sea.
	private sealed class HuntTerrain : IHuntTerrain
	{
		private readonly HuntFrame frame;
		private readonly CelestialBody body;

		public HuntTerrain(HuntFrame frame, CelestialBody body)
		{
			this.frame = frame;
			this.body = body;
		}

		public bool Walkable(double x, double y)
		{
			if (!body.ocean)
			{
				return true;
			}
			return IsLandAt(frame.FromPlane(x, y), body);
		}

		public double Height(double x, double y)
		{
			Vector3d p = frame.FromPlane(x, y);
			return body.TerrainAltitude(body.GetLatitude(p), body.GetLongitude(p));
		}
	}

	// ---------------------------------------------------------------- per-monster state

	internal struct HuntInfo
	{
		public bool Valid;
		public string Name;
		public HuntState State;
		public double BeliefDistance;     // monster to where it believes the craft is, -1 when it has no belief
		public double BeliefError;        // error radius of that belief
		public double TrueDistance;       // monster to the real craft (read-only, for the director, the steps and the debug panel; the brain never sees it)
		public double BeliefOffset;       // believed place to the real craft
		public float Speed;
		public bool Listening;
		public bool Sees;
		public float FaceErrorDeg;        // body forward against the direction of travel, 0 when not moving. With the Moho creature: the creature's own body (BodyForward), what is drawn
		public float FaceErrorObjectDeg;  // the same for the tracker's root object (its transform.rotation), which the creature's body does not follow exactly: the old csv number
		public float HeadYawDeg;
		public bool HasHead;
		public int Heard;
		public int Stalls;
		public int Waypoints;
		public float SearchSeconds;

		public bool Hunting { get { return State == HuntState.Hunt || State == HuntState.Attack; } }
		public bool Searching { get { return State == HuntState.Investigate || State == HuntState.Search; } }
	}

	private sealed class HuntMonster
	{
		public readonly int id;
		public readonly string name;
		public UnityEngine.Object owner;
		public CelestialBody body;
		public HuntFrame frame;
		public HuntTerrain terrain;
		public DreadHunt hunt;
		public Quaternion correction = Quaternion.identity;
		public float lastTick = -100f;
		public Vector3d lastPos;
		public bool hasLastPos;
		public Vector3d travelAcc;
		public float losTimer;
		public bool losCached;
		public int heard;
		public HuntInfo info;
		public HuntHeadLook head;
		public bool headSearched;
		public Vector3d lastFacing;

		public HuntMonster(int id, string name)
		{
			this.id = id;
			this.name = name;
		}
	}

	private readonly HuntMonster huntMimic = new HuntMonster(0, "mimic");
	private readonly HuntMonster huntBulk = new HuntMonster(1, "bulk");
	private readonly HuntMonster huntRake = new HuntMonster(2, "rake");
	private readonly HuntMonster huntLaythe = new HuntMonster(3, "laythe");

	private static readonly Quaternion HuntBulkCorrection = Quaternion.Euler(0f, 180f, 0f);

	private static string HuntStatusText(DreadHunt h)
	{
		switch (h.State)
		{
		case HuntState.Investigate:
			return "INVESTIGATING A NOISE";
		case HuntState.Search:
			return h.Listening ? "LISTENING" : "SEARCHING";
		case HuntState.Hunt:
			return "HUNTING";
		case HuntState.Attack:
			return "ATTACKING";
		case HuntState.GiveUp:
			return "LOST TRACK";
		default:
			return "WAITING";
		}
	}

	// ---------------------------------------------------------------- senses

	private struct HuntNoiseEvent
	{
		public HuntNoise kind;
		public float loudness;
		public Vector3d position;
	}

	private readonly List<HuntNoiseEvent> huntEvents = new List<HuntNoiseEvent>();
	private readonly List<HuntNoiseEvent> huntPendingEvents = new List<HuntNoiseEvent>();
	private int huntNoiseFrame = -1;
	private Guid huntNoiseVessel;
	private float huntThrottlePing, huntRcsPing, huntLightsPing, huntWheelsPing, huntEngineScan;
	private bool huntHadVessel, huntWasLanded;
	private float huntLastDescent;
	private bool huntEnginesOn;
	private bool huntHooked;
	private bool huntNoiseFaulted;
	private float huntEngineLoud;

	private void HuntEnsureHooks()
	{
		if (huntHooked)
		{
			return;
		}
		huntHooked = true;
		try
		{
			GameEvents.onPartDeCoupleComplete.Add(OnHuntDecouple);
		}
		catch (Exception ex)
		{
			Debug.Log("[KK Hunt] decouple noise unavailable: " + ex.Message);
		}
	}

	private void DestroyHunt()
	{
		if (!huntHooked)
		{
			return;
		}
		huntHooked = false;
		try
		{
			GameEvents.onPartDeCoupleComplete.Remove(OnHuntDecouple);
		}
		catch (Exception)
		{
		}
	}

	private void OnHuntDecouple(Part part)
	{
		try
		{
			Vessel active = FlightGlobals.ActiveVessel;
			if (part == null || active == null)
			{
				return;
			}
			Vessel pv = part.vessel;
			if (pv != null && pv != active && (pv.GetWorldPos3D() - active.GetWorldPos3D()).sqrMagnitude > 200.0 * 200.0)
			{
				return;
			}
			HuntNoiseEvent e = new HuntNoiseEvent();
			e.kind = HuntNoise.Decouple;
			e.loudness = 0.9f;
			e.position = active.GetWorldPos3D();
			huntPendingEvents.Add(e);
		}
		catch (Exception)
		{
		}
	}

	// For the in-game test: a noise that the craft "made" at a place, delivered through the same path as the real ones.
	internal void HuntInjectNoise(HuntNoise kind, float loudness, Vector3d position)
	{
		HuntNoiseEvent e = new HuntNoiseEvent();
		e.kind = kind;
		e.loudness = loudness;
		e.position = position;
		huntPendingEvents.Add(e);
	}

	private void HuntAddEvent(HuntNoise kind, float loudness, Vessel vessel)
	{
		HuntNoiseEvent e = new HuntNoiseEvent();
		e.kind = kind;
		e.loudness = Mathf.Clamp01(loudness);
		e.position = vessel.GetWorldPos3D();
		huntEvents.Add(e);
	}

	// Once per frame: what the active vessel does that a monster could hear (or, for the lights, see).
	private void HuntUpdateNoise(Vessel vessel)
	{
		if (Time.frameCount == huntNoiseFrame)
		{
			return;
		}
		huntNoiseFrame = Time.frameCount;
		huntEvents.Clear();
		for (int i = 0; i < huntPendingEvents.Count; i++)
		{
			huntEvents.Add(huntPendingEvents[i]);
		}
		huntPendingEvents.Clear();
		if (vessel == null || Time.timeScale <= 0f)
		{
			return;
		}
		float dt = Mathf.Min(Time.deltaTime, 0.25f);
		if (!huntHadVessel || huntNoiseVessel != vessel.id)
		{
			huntHadVessel = true;
			huntNoiseVessel = vessel.id;
			huntWasLanded = vessel.Landed;
			huntLastDescent = 0f;
			huntEnginesOn = false;
			huntEngineScan = 0f;
		}
		bool landed = vessel.Landed;
		if (landed && !huntWasLanded)
		{
			HuntAddEvent(HuntNoise.Impact, 0.45f + Mathf.Clamp01(huntLastDescent / 12f) * 0.55f, vessel);
		}
		huntWasLanded = landed;
		huntLastDescent = Mathf.Max(0f, (float)(-vessel.verticalSpeed));
		FlightCtrlState ctrl = vessel.ctrlState;
		float throttle = ctrl != null ? ctrl.mainThrottle : 0f;

		huntEngineScan -= dt;
		if (huntEngineScan <= 0f)
		{
			huntEngineScan = 2f;
			huntEnginesOn = false;
			List<ModuleEngines> engines = vessel.FindPartModulesImplementing<ModuleEngines>();
			for (int i = 0; i < engines.Count; i++)
			{
				ModuleEngines engine = engines[i];
				if (engine != null && engine.EngineIgnited && !engine.flameout && engine.isOperational)
				{
					huntEnginesOn = true;
					break;
				}
			}
		}
		huntThrottlePing -= dt;
		if (huntThrottlePing <= 0f)
		{
			huntThrottlePing = 1f;
			if (huntEnginesOn && throttle > 0.02f)
			{
				// an engine on the ground is carried through it; in the air much less
				HuntAddEvent(HuntNoise.Throttle, (0.4f + 0.6f * throttle) * (landed ? 1f : 0.6f), vessel);
			}
		}
		huntRcsPing -= dt;
		if (huntRcsPing <= 0f)
		{
			huntRcsPing = 1f;
			if (ctrl != null && vessel.ActionGroups[KSPActionGroup.RCS] && (Mathf.Abs(ctrl.pitch) > 0.05f || Mathf.Abs(ctrl.yaw) > 0.05f || Mathf.Abs(ctrl.roll) > 0.05f || Mathf.Abs(ctrl.X) > 0.05f || Mathf.Abs(ctrl.Y) > 0.05f || Mathf.Abs(ctrl.Z) > 0.05f))
			{
				HuntAddEvent(HuntNoise.Rcs, 0.35f, vessel);
			}
		}
		huntLightsPing -= dt;
		if (huntLightsPing <= 0f)
		{
			huntLightsPing = 2f;
			if (vessel.ActionGroups[KSPActionGroup.Light])
			{
				HuntAddEvent(HuntNoise.Lights, 0.5f, vessel);
			}
		}
		huntWheelsPing -= dt;
		if (huntWheelsPing <= 0f)
		{
			huntWheelsPing = 1f;
			double speed = vessel.srf_velocity.magnitude;
			if (landed && ctrl != null && speed > 0.8 && (Mathf.Abs(ctrl.wheelThrottle) > 0.05f || Mathf.Abs(ctrl.wheelSteer) > 0.05f))
			{
				HuntAddEvent(HuntNoise.Wheels, Mathf.Clamp(0.25f + (float)speed / 25f, 0.25f, 0.8f), vessel);
			}
		}
	}

	private void HuntBind(HuntMonster m, GameObject obj, Vector3d monsterPos, CelestialBody body, Quaternion correction)
	{
		if (ReferenceEquals(m.owner, obj) && m.body == body && m.hunt != null)
		{
			return;
		}
		m.owner = obj;
		m.body = body;
		m.correction = correction;
		m.frame = new HuntFrame(body, monsterPos);
		m.terrain = new HuntTerrain(m.frame, body);
		m.hunt = new DreadHunt(huntSettings, m.terrain, Environment.TickCount * 31 + m.id * 977);
		m.hunt.Reset();
		m.hunt.TurnScale = (m.id == 1) ? 1f : huntTurnSmallScale;
		m.hasLastPos = false;
		m.travelAcc = Vector3d.zero;
		m.heard = 0;
		m.losTimer = 0f;
		m.losCached = false;
		m.head = null;
		m.headSearched = false;
		m.lastTick = -100f;
	}

	private static Vector3d HuntTangent(Vector3d v, Vector3d up)
	{
		return v - up * Vector3d.Dot(v, up);
	}

	// The body's forward as the monster's own facing code defines it (before the model correction), flattened onto the ground.
	private static Vector3d HuntBodyForward(HuntMonster m, GameObject obj, Vector3d up)
	{
		Vector3 f = (obj.transform.rotation * Quaternion.Inverse(m.correction)) * Vector3.forward;
		Vector3d t = HuntTangent((Vector3d)f, up);
		return t.sqrMagnitude > 1e-9 ? t.normalized : Vector3d.zero;
	}

	private double HuntHeadingFromWorld(HuntMonster m, Vector3d monsterPos, Vector3d worldDir, double px, double py)
	{
		double tx, ty;
		m.frame.ToPlane(monsterPos + worldDir * 10.0, out tx, out ty);
		return Math.Atan2(ty - py, tx - px);
	}

	// Called once per frame at the top of each ground monster's Update, with the real position. Feeds the senses and returns whether the
	// monster is aware of the craft (heard it or sees it). Nothing here lets the brain read the craft's position except through Hear/Sight.
	private bool HuntSense(HuntMonster m, GameObject obj, Vector3d monsterPos, CelestialBody body, Vessel vessel, Quaternion correction)
	{
		if (!huntAi || obj == null || body == null || vessel == null)
		{
			return false;
		}
		HuntEnsureHooks();
		HuntBind(m, obj, monsterPos, body, correction);
		DreadHunt h = m.hunt;
		double px, py;
		m.frame.ToPlane(monsterPos, out px, out py);
		Vector3d up = (monsterPos - body.position).normalized;
		if (!h.Aware && !(ReferenceEquals(m, huntBulk) && bulkTurnHold))
		{
			// not walking: its heading is however the body stands
			Vector3d bf = HuntBodyForward(m, obj, up);
			if (bf.sqrMagnitude > 0.5)
			{
				h.Heading = HuntHeadingFromWorld(m, monsterPos, bf, px, py);
			}
		}
		h.X = px;
		h.Y = py;
		Vector3d craftPos = vessel.GetWorldPos3D();
		double cx, cy;
		m.frame.ToPlane(craftPos, out cx, out cy);
		float now = Time.time;
		float dt = Mathf.Clamp(now - m.lastTick, 0f, 0.25f);
		m.lastTick = now;
		double dist = (craftPos - monsterPos).magnitude;
		double losRange = Math.Max(huntSettings.SightMetres, huntSettings.HearLights) * 1.05;
		if (dist <= losRange)
		{
			m.losTimer -= dt;
			if (m.losTimer <= 0f)
			{
				m.losTimer = 0.35f;
				m.losCached = HasGroundLineOfSight(monsterPos, craftPos, body);
			}
		}
		else
		{
			m.losCached = false;
			m.losTimer = 0f;
		}
		h.Linger = DreadWantsRelease();
		if (Time.timeScale > 0f)
		{
			h.Sight(m.losCached, cx, cy);
			try
			{
				HuntUpdateNoise(vessel);
			}
			catch (Exception ex)
			{
				if (!huntNoiseFaulted)
				{
					huntNoiseFaulted = true;
					Debug.Log("[KK Hunt] noise sensing failed (monsters keep running on sight only): " + ex);
				}
				huntEvents.Clear();
			}
			for (int i = 0; i < huntEvents.Count; i++)
			{
				HuntNoiseEvent e = huntEvents[i];
				if (e.kind == HuntNoise.Lights && !m.losCached)
				{
					continue;
				}
				double ex, ey;
				m.frame.ToPlane(e.position, out ex, out ey);
				if (h.Hear(e.kind, e.loudness, ex, ey))
				{
					m.heard++;
				}
			}
		}
		HuntUpdateInfo(m, obj, monsterPos, up, craftPos, dist, dt);
		return h.Aware;
	}

	private void HuntUpdateInfo(HuntMonster m, GameObject obj, Vector3d monsterPos, Vector3d up, Vector3d craftPos, double dist, float dt)
	{
		DreadHunt h = m.hunt;
		HuntInfo info = new HuntInfo();
		info.Valid = true;
		info.Name = m.name;
		info.State = h.State;
		info.TrueDistance = dist;
		info.BeliefDistance = -1.0;
		info.BeliefError = h.BeliefError;
		info.BeliefOffset = -1.0;
		if (h.HasBelief)
		{
			double px, py;
			m.frame.ToPlane(monsterPos, out px, out py);
			info.BeliefDistance = Math.Sqrt((h.BeliefX - px) * (h.BeliefX - px) + (h.BeliefY - py) * (h.BeliefY - py));
			Vector3d bw = m.frame.FromPlane(h.BeliefX, h.BeliefY);
			info.BeliefOffset = (bw - craftPos).magnitude;
		}
		info.Speed = h.Speed;
		info.Listening = h.Listening;
		info.Sees = h.HasSight;
		info.Heard = m.heard;
		info.Stalls = h.Stalls;
		info.Waypoints = h.WaypointsDone;
		info.SearchSeconds = h.SearchSeconds;
		info.HasHead = m.head != null && m.head.HasHead;
		info.HeadYawDeg = m.head != null ? m.head.Yaw : 0f;
		// facing against travel: what the body shows against how it really moved over the last third of a second
		float face = 0f, faceObj = 0f;
		if (m.hasLastPos && dt > 0f)
		{
			Vector3d disp = HuntTangent(monsterPos - m.lastPos, up);
			if (disp.magnitude > 400.0 * dt)
			{
				// a jump (the test's teleport, a floating-origin shift), not travel: it must not leave a stale direction behind
				m.travelAcc = Vector3d.zero;
				disp = Vector3d.zero;
			}
			m.travelAcc = m.travelAcc * Math.Exp(-dt / 0.15) + disp;
			double speedEst = m.travelAcc.magnitude / 0.15;
			if (speedEst > 1.5)
			{
				Vector3d bf = HuntBodyForward(m, obj, up);
				if (bf.sqrMagnitude > 0.5)
				{
					faceObj = Vector3.Angle((Vector3)bf, (Vector3)m.travelAcc.normalized);
					face = faceObj;
				}
				if (ReferenceEquals(m, huntBulk) && bulkMoho != null)
				{
					// the drawn body is the creature's, not the root object
					Vector3d mf = HuntTangent((Vector3d)bulkMoho.BodyForward, up);
					if (mf.sqrMagnitude > 0.5)
					{
						face = Vector3.Angle((Vector3)mf.normalized, (Vector3)m.travelAcc.normalized);
					}
				}
			}
		}
		m.lastPos = monsterPos;
		m.hasLastPos = true;
		info.FaceErrorDeg = face;
		info.FaceErrorObjectDeg = faceObj;
		m.info = info;
	}

	private HuntMonster HuntFor(int id)
	{
		switch (id)
		{
		case 0:
			return huntMimic;
		case 1:
			return huntBulk;
		case 2:
			return huntRake;
		default:
			return huntLaythe;
		}
	}

	// Read-only: the state of the hunt of the nearest awake or waiting ground monster, for the debug panel and the test csv.
	internal HuntInfo GetHuntInfo(int id)
	{
		HuntMonster m = HuntFor(id);
		return m != null && m.hunt != null && m.info.Valid ? m.info : new HuntInfo();
	}

	internal HuntInfo GetNearestHuntInfo()
	{
		HuntInfo best = new HuntInfo();
		for (int i = 0; i < 4; i++)
		{
			HuntMonster m = HuntFor(i);
			if (!HuntActive(i) || m.hunt == null || !m.info.Valid)
			{
				continue;
			}
			if (!best.Valid || m.info.TrueDistance < best.TrueDistance)
			{
				best = m.info;
			}
		}
		return best;
	}

	private bool HuntActive(int id)
	{
		switch (id)
		{
		case 0:
			return groundEntityActive;
		case 1:
			return bulkEntityActive;
		case 2:
			return rakeEntityActive;
		default:
			return laytheCreatureActive;
		}
	}

	private void DrawHuntDebugLabels()
	{
		if (!huntAi)
		{
			return;
		}
		for (int i = 0; i < 4; i++)
		{
			HuntMonster m = HuntFor(i);
			if (!HuntActive(i) || m.hunt == null || !m.info.Valid)
			{
				continue;
			}
			HuntInfo f = m.info;
			GUILayout.Label("HUNT " + f.Name + ": " + f.State + (f.Listening ? " (listening)" : "") + (f.Sees ? " sees" : "") + "  believes " + (f.BeliefDistance >= 0.0 ? f.BeliefDistance.ToString("F0") + " m +-" + f.BeliefError.ToString("F0") : "nothing") + "  true " + f.TrueDistance.ToString("F0") + " m  " + f.Speed.ToString("F1") + " m/s  heard " + f.Heard + "  face " + f.FaceErrorDeg.ToString("F0") + " deg" + (f.HasHead ? "  head " + f.HeadYawDeg.ToString("F0") : ""));
		}
	}

	// ---------------------------------------------------------------- movement

	// Replaces SmartGroundApproach when huntAi is on. Returns true when it handled the monster this frame.
	private bool HuntGroundApproach(HuntMonster m, GroundBehaviorState behavior, UnityEngine.Object owner, ref Vector3d position, Vessel vessel, CelestialBody body, float baseSpeed, out string status)
	{
		status = null;
		if (!huntAi || m.hunt == null || body == null || vessel == null || Time.timeScale <= 0f)
		{
			return false;
		}
		DreadHunt h = m.hunt;
		float dt = BehaviorStep();
		bool moho = ReferenceEquals(m, huntBulk) && bulkMoho != null;
		if (moho)
		{
			MohoPrepare(m);
		}
		h.Update(dt, baseSpeed);
		Vector3d moved = m.frame.FromPlane(h.X, h.Y);
		position = ProjectToGround(moved, body);
		status = HuntStatusText(h);
		if (moho && mohoLeaps)
		{
			string leapStatus = MohoLeapStep(m, ref position, vessel, body, dt);
			if (leapStatus != null)
			{
				status = leapStatus;
			}
		}
		else if (h.HasSight && vessel.heightFromTerrain >= 10f && HoverGroundAttack(behavior, owner, ref position, vessel, body, 0f, out var hoverStatus))
		{
			status = hoverStatus;
		}
		return true;
	}

	// The brain's heading as a world direction on the ground at the monster. False while it has nothing to show (waiting, lost track).
	private bool HuntFacing(HuntMonster m, Vector3d position, CelestialBody body, out Vector3d direction)
	{
		direction = Vector3d.zero;
		if (!huntAi || m.hunt == null || body == null || !m.hunt.Aware)
		{
			return false;
		}
		return HuntHeadingDirection(m, position, body, m.hunt.Heading, out direction);
	}

	// A plane heading as a world direction on the ground at the position.
	private bool HuntHeadingDirection(HuntMonster m, Vector3d position, CelestialBody body, double heading, out Vector3d direction)
	{
		direction = Vector3d.zero;
		if (m.hunt == null || m.frame == null || body == null)
		{
			return false;
		}
		double px, py;
		m.frame.ToPlane(position, out px, out py);
		Vector3d target = m.frame.FromPlane(px + Math.Cos(heading) * 10.0, py + Math.Sin(heading) * 10.0);
		Vector3d up = (position - body.position).normalized;
		Vector3d t = HuntTangent(target - position, up);
		if (t.sqrMagnitude < 1e-6)
		{
			return false;
		}
		direction = t.normalized;
		m.lastFacing = direction;
		return true;
	}

	// Walking animation and sound follow real movement, not the word "chasing": a monster that stops to listen stands.
	private bool HuntIsMoving(HuntMonster m)
	{
		return !huntAi || m.hunt == null || m.hunt.Speed > 0.4f;
	}

	// ---------------------------------------------------------------- head look

	// Only the head turns toward the craft, and only while the monster sees it. The body never does.
	private void HuntUpdateHead(HuntMonster m, GameObject obj, Vector3d position, CelestialBody body, Vessel vessel)
	{
		if (!huntAi || !huntHeadLook || obj == null || m.hunt == null || body == null)
		{
			return;
		}
		if (!m.headSearched)
		{
			m.headSearched = true;
			m.head = obj.GetComponent<HuntHeadLook>();
			if (m.head == null)
			{
				m.head = obj.AddComponent<HuntHeadLook>();
			}
			m.head.Find();
			Debug.Log("[KK Hunt] " + m.name + " head: " + (m.head.HasHead ? m.head.HeadName : "none, the body keeps its travel direction"));
		}
		if (!m.head.HasHead)
		{
			return;
		}
		Vector3 up = (Vector3)(position - body.position).normalized;
		m.head.Up = up;
		float target = 0f;
		Vector3d facing = m.lastFacing;
		if (m.hunt.HasSight && vessel != null && facing.sqrMagnitude > 0.5)
		{
			Vector3d toCraft = HuntTangent(vessel.GetWorldPos3D() - position, (Vector3d)up);
			if (toCraft.sqrMagnitude > 1.0)
			{
				target = Mathf.Clamp(Vector3.SignedAngle((Vector3)facing, (Vector3)toCraft.normalized, up), -75f, 75f);
			}
		}
		m.head.TargetYaw = target;
	}

	// Per monster object; runs after the animation scripts so that the head turn lands on top of their pose.
	[DefaultExecutionOrder(1000)]
	internal sealed class HuntHeadLook : MonoBehaviour
	{
		private Transform head;
		public bool HasHead { get { return head != null; } }
		public string HeadName { get { return head != null ? head.name : "none"; } }
		public float TargetYaw;
		public float Yaw { get; private set; }
		public Vector3 Up = Vector3.up;

		public void Find()
		{
			Transform best = null;
			Transform[] all = GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < all.Length; i++)
			{
				string n = all[i].name.ToLowerInvariant();
				if (n.Contains("head") && !n.Contains("light") && !n.Contains("ahead") && (best == null || n.Length < best.name.Length))
				{
					best = all[i];
				}
			}
			head = best;
		}

		private void LateUpdate()
		{
			if (head == null)
			{
				return;
			}
			float dt = Time.timeScale > 0f ? Time.deltaTime : 0f;
			Yaw = Mathf.MoveTowards(Yaw, TargetYaw, 200f * dt);
			if (Mathf.Abs(Yaw) > 0.05f)
			{
				head.rotation = Quaternion.AngleAxis(Yaw, Up) * head.rotation;
			}
		}
	}
}
