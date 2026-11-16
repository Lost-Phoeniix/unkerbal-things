using System;
using UnityEngine;

// Ground monster approach AI, hover leaps, Mimic large-vessel strike and Rake returns.
public partial class EntityTracker
{
	private enum BehaviorPhase
	{
		Stalk,
		Circle,
		Fakeout,
		Charge
	}

	// One instance per ground monster. Tied to the monster's GameObject, so a new spawn starts fresh.
	private sealed class GroundBehaviorState
	{
		public UnityEngine.Object owner;

		public float lastCall = -100f;

		// Approach state machine.
		public bool started;
		public BehaviorPhase phase;
		public float engageTime;
		public float engageLimit;
		public float phaseTimer;
		public float orbitRadius;
		public float orbitDirection = 1f;
		public int fakeouts;
		public float losTimer;
		public Vector3d moveDirection;
		public Vector3d stalkDirection;

		// Hover leaps.
		public float hoverLastCall = -100f;
		public bool leaping;
		public bool apexChecked;
		public float leapProgress;
		public float leapDuration = 1f;
		public float leapPeak;
		public float leapCooldown;
		public float lift;
		public bool catchPending;
		public float catchTime;

		public void ResetApproach()
		{
			started = false;
			phase = BehaviorPhase.Stalk;
			engageTime = 0f;
			fakeouts = 0;
			losTimer = 0f;
			moveDirection = Vector3d.zero;
			stalkDirection = Vector3d.zero;
		}

		public void ResetAll()
		{
			ResetApproach();
			hoverLastCall = -100f;
			leaping = false;
			apexChecked = false;
			leapProgress = 0f;
			leapCooldown = 0f;
			lift = 0f;
			catchPending = false;
		}
	}

	private const float BehaviorChargeRange = 60f;
	private const float BehaviorHoverTrackHeight = 300f;
	private const float BehaviorEngageRange = 400f;
	private const float BehaviorStalkNearSpeed = 0.55f;
	private const float BehaviorCircleSpeed = 0.35f;
	private const float BehaviorFakeoutSpeed = 0.5f;
	private const float BehaviorHoverCatchRadius = 12f;

	// Settings (see settings.cfg). Static so static helpers can read them.
	private static bool smartEntityAi = true;
	private static bool hoverAttacks = true;
	private static float hoverAttackMaxHeight = 60f;
	private static bool mimicKillsLargeVessels = true;
	private static bool rakeReturnsAfterWatched = true;
	private static float rakeMaxReturns = 2f;

	private readonly GroundBehaviorState behaviorMimic = new GroundBehaviorState();
	private readonly GroundBehaviorState behaviorBulk = new GroundBehaviorState();
	private readonly GroundBehaviorState behaviorLaythe = new GroundBehaviorState();
	private readonly GroundBehaviorState behaviorRake = new GroundBehaviorState();

	// Rake return bookkeeping.
	private bool rakeReturnPending;
	private bool rakeSpawningReturn;
	private int rakeReturnsDone;
	private float rakeReturnTimer;
	private float rakeReturnWaited;
	private CelestialBody rakeReturnBody;
	private Vector3d rakeReturnBearing;

	private static void LoadBehaviorSettings(ConfigNode node)
	{
		if (node != null && node.HasValue("smartEntityAi"))
		{
			bool.TryParse(node.GetValue("smartEntityAi"), out smartEntityAi);
		}
		if (node != null && node.HasValue("hoverAttacks"))
		{
			bool.TryParse(node.GetValue("hoverAttacks"), out hoverAttacks);
		}
		if (node != null && node.HasValue("mimicKillsLargeVessels"))
		{
			bool.TryParse(node.GetValue("mimicKillsLargeVessels"), out mimicKillsLargeVessels);
		}
		if (node != null && node.HasValue("rakeReturnsAfterWatched"))
		{
			bool.TryParse(node.GetValue("rakeReturnsAfterWatched"), out rakeReturnsAfterWatched);
		}
		ReadFloatSetting(node, "hoverAttackMaxHeight", ref hoverAttackMaxHeight);
		ReadFloatSetting(node, "rakeMaxReturns", ref rakeMaxReturns);
		hoverAttackMaxHeight = Mathf.Clamp(hoverAttackMaxHeight, 5f, 300f);
		rakeMaxReturns = Mathf.Clamp(rakeMaxReturns, 0f, 10f);
	}

	// Extra catch distance for a target hovering 2-10 m up, which the ground monsters' own chase branch cannot get closer to than its height.
	private static float GetVerticalCatchAllowance(Vessel vessel)
	{
		if (!hoverAttacks || vessel == null)
		{
			return 0f;
		}
		return Mathf.Clamp(vessel.heightFromTerrain - 2f, 0f, 8f);
	}

	// Highest vessel height above terrain that a ground monster will still consider a target.
	private static float GetGroundTargetHeightLimit()
	{
		return hoverAttacks ? BehaviorHoverTrackHeight : 10f;
	}

	private static float BehaviorStep()
	{
		return Mathf.Clamp(TimeWarp.deltaTime, 0f, 0.1f);
	}

	private static void BindBehavior(GroundBehaviorState s, UnityEngine.Object owner)
	{
		if (!ReferenceEquals(s.owner, owner))
		{
			s.ResetAll();
			s.owner = owner;
		}
	}

	private static Vector3d RotateAboutUp(Vector3d direction, Vector3d up, float degrees)
	{
		return (Vector3d)(Quaternion.AngleAxis(degrees, (Vector3)up) * (Vector3)direction);
	}

	private static void StartBehaviorPhase(GroundBehaviorState s, BehaviorPhase phase)
	{
		s.phase = phase;
		switch (phase)
		{
		case BehaviorPhase.Circle:
			s.phaseTimer = UnityEngine.Random.Range(3f, 7f);
			s.orbitDirection = ((UnityEngine.Random.value < 0.5f) ? (-1f) : 1f);
			break;
		case BehaviorPhase.Fakeout:
			s.phaseTimer = UnityEngine.Random.Range(1.5f, 3f);
			s.fakeouts++;
			break;
		}
	}

	// Choose what to do once the monster has closed to its orbit distance.
	private static void ChooseBehaviorPhase(GroundBehaviorState s, bool afterCircle)
	{
		float value = UnityEngine.Random.value;
		bool canFake = s.fakeouts < 2;
		if (afterCircle)
		{
			StartBehaviorPhase(s, (canFake && value < 0.3f) ? BehaviorPhase.Fakeout : BehaviorPhase.Charge);
		}
		else if (canFake && value < 0.2f)
		{
			StartBehaviorPhase(s, BehaviorPhase.Fakeout);
		}
		else if (value < 0.75f)
		{
			StartBehaviorPhase(s, BehaviorPhase.Circle);
		}
		else
		{
			StartBehaviorPhase(s, BehaviorPhase.Charge);
		}
		// Dread release window: a monster that has not committed lingers (circles, drifting outward) instead of charging.
		if (DreadWantsRelease() && s.phase == BehaviorPhase.Charge)
		{
			StartBehaviorPhase(s, BehaviorPhase.Circle);
		}
	}

	// Prefers a heading (within 60 degrees of straight) from which terrain hides the monster from the target.
	private static Vector3d ChooseHiddenHeading(Vector3d position, Vector3d direct, Vector3d up, Vector3d targetPosition, CelestialBody body, double range)
	{
		float[] array = new float[5] { 0f, 30f, -30f, 60f, -60f };
		if (UnityEngine.Random.value < 0.5f)
		{
			array[1] = -30f;
			array[2] = 30f;
			array[3] = -60f;
			array[4] = 60f;
		}
		double num = Math.Min(120.0, range * 0.5);
		for (int i = 0; i < array.Length; i++)
		{
			Vector3d vector3d = ((array[i] == 0f) ? direct : RotateAboutUp(direct, up, array[i]));
			Vector3d entityPosition = ProjectToGround(position + vector3d * num, body);
			if (!HasGroundLineOfSight(entityPosition, targetPosition, body))
			{
				return vector3d;
			}
		}
		return direct;
	}

	// Shared approach state machine. Returns true when it moved the monster (Stalk, Circle, Fakeout);
	// returns false during Charge so the caller's original straight-line chase runs.
	private bool SmartGroundApproach(GroundBehaviorState s, UnityEngine.Object owner, ref Vector3d position, Vector3d toTarget, CelestialBody body, float baseSpeed, out string status)
	{
		status = null;
		if (body == null || Time.timeScale <= 0f)
		{
			return false;
		}
		BindBehavior(s, owner);
		if (Time.time - s.lastCall > 30f && !(s.phase == BehaviorPhase.Charge && Time.time - s.hoverLastCall < 35f))
		{
			s.ResetApproach();
		}
		s.lastCall = Time.time;
		float num = BehaviorStep();
		if (!s.started)
		{
			s.started = true;
			s.phase = BehaviorPhase.Stalk;
			s.engageTime = 0f;
			s.engageLimit = UnityEngine.Random.Range(40f, 58f);
			s.orbitRadius = UnityEngine.Random.Range(80f, 200f);
			s.fakeouts = 0;
		}
		double magnitude = toTarget.magnitude;
		bool inEngageRange = magnitude <= (double)Math.Max(BehaviorEngageRange, s.orbitRadius);
		if (inEngageRange && !DreadWantsRelease())
		{
			s.engageTime += num;
		}
		float speedFactor = 1f;
		Vector3d normalized = (position - body.position).normalized;
		Vector3d vector3d = toTarget - normalized * Vector3d.Dot(toTarget, normalized);
		double magnitude2 = vector3d.magnitude;
		if (magnitude2 < 1.0)
		{
			return false;
		}
		Vector3d vector3d2 = vector3d / magnitude2;
		if (s.phase != BehaviorPhase.Charge && ((inEngageRange && s.engageTime >= s.engageLimit) || magnitude <= (double)BehaviorChargeRange))
		{
			s.phase = BehaviorPhase.Charge;
		}
		Vector3d vector3d3;
		switch (s.phase)
		{
		case BehaviorPhase.Stalk:
			if (magnitude <= (double)(s.orbitRadius + 10f))
			{
				ChooseBehaviorPhase(s, afterCircle: false);
				return SmartGroundApproachHold(s, ref position, vector3d2, body, baseSpeed, num, out status);
			}
			s.losTimer -= num;
			if (s.losTimer <= 0f || s.stalkDirection.sqrMagnitude < 0.5)
			{
				s.losTimer = UnityEngine.Random.Range(0.4f, 0.5f);
				s.stalkDirection = ((magnitude < 1500.0) ? ChooseHiddenHeading(position, vector3d2, normalized, position + toTarget, body, magnitude) : vector3d2);
			}
			if (s.moveDirection.sqrMagnitude < 0.5)
			{
				s.moveDirection = s.stalkDirection;
			}
			s.moveDirection = Vector3d.Lerp(s.moveDirection, s.stalkDirection, 1.0 - Math.Exp(-3f * num));
			vector3d3 = s.moveDirection.normalized;
			status = "STALKING";
			speedFactor = ((inEngageRange) ? BehaviorStalkNearSpeed : 1f);
			break;
		case BehaviorPhase.Circle:
		{
			s.phaseTimer -= num;
			if (DreadWantsRelease())
			{
				s.orbitRadius = Mathf.Min(s.orbitRadius + 12f * num, 320f);
			}
			if (s.phaseTimer <= 0f)
			{
				ChooseBehaviorPhase(s, afterCircle: true);
				return SmartGroundApproachHold(s, ref position, vector3d2, body, baseSpeed, num, out status);
			}
			Vector3d vector3d4 = Vector3d.Cross(normalized, vector3d2) * s.orbitDirection;
			double num2 = Math.Max(-1.0, Math.Min(1.0, (magnitude - (double)s.orbitRadius) / (double)s.orbitRadius));
			Vector3d vector3d5 = vector3d4 + vector3d2 * (num2 * 0.9);
			vector3d3 = ((vector3d5.sqrMagnitude > 0.0001) ? vector3d5.normalized : vector3d4);
			status = "CIRCLING";
			speedFactor = BehaviorCircleSpeed;
			break;
		}
		case BehaviorPhase.Fakeout:
			s.phaseTimer -= num;
			if (s.phaseTimer <= 0f)
			{
				s.phase = BehaviorPhase.Stalk;
				s.orbitRadius = UnityEngine.Random.Range(60f, 90f);
				s.stalkDirection = Vector3d.zero;
				s.moveDirection = Vector3d.zero;
			}
			vector3d3 = -vector3d2;
			status = "FALLING BACK";
			speedFactor = BehaviorFakeoutSpeed;
			break;
		default:
			return false;
		}
		position += vector3d3 * baseSpeed * speedFactor * num;
		position = ProjectToGround(position, body);
		return true;
	}

	// A phase was just chosen this frame: take a first step in it (or fall through to Charge).
	private bool SmartGroundApproachHold(GroundBehaviorState s, ref Vector3d position, Vector3d directDirection, CelestialBody body, float baseSpeed, float step, out string status)
	{
		status = null;
		if (s.phase == BehaviorPhase.Charge)
		{
			return false;
		}
		Vector3d vector3d = Vector3d.zero;
		float num = 1f;
		if (s.phase == BehaviorPhase.Fakeout)
		{
			vector3d = -directDirection;
			status = "FALLING BACK";
			num = BehaviorFakeoutSpeed;
		}
		else
		{
			Vector3d normalized = (position - body.position).normalized;
			vector3d = Vector3d.Cross(normalized, directDirection) * s.orbitDirection;
			status = "CIRCLING";
			num = BehaviorCircleSpeed;
		}
		position += vector3d * baseSpeed * num * step;
		position = ProjectToGround(position, body);
		return true;
	}

	// Hover attacks: handles a chasing monster whose target is off the ground. Returns true when handled.
	private bool HoverGroundAttack(GroundBehaviorState s, UnityEngine.Object owner, ref Vector3d position, Vessel vessel, CelestialBody body, float baseSpeed, out string status)
	{
		status = null;
		if (!hoverAttacks || vessel == null || body == null || Time.timeScale <= 0f || vessel.heightFromTerrain > BehaviorHoverTrackHeight)
		{
			return false;
		}
		BindBehavior(s, owner);
		float num = BehaviorStep();
		s.hoverLastCall = Time.time;
		Vector3d worldPos3D = vessel.GetWorldPos3D();
		Vector3d normalized = (position - body.position).normalized;
		Vector3d vector3d = worldPos3D - position;
		double num2 = Vector3d.Dot(vector3d, normalized);
		Vector3d vector3d2 = vector3d - normalized * num2;
		double magnitude = vector3d2.magnitude;
		bool flag = vessel.heightFromTerrain <= hoverAttackMaxHeight;
		// Run beneath the vessel (at base speed).
		if (magnitude > 1.0)
		{
			double num3 = Math.Min(magnitude, (double)(baseSpeed * num));
			position += vector3d2 / magnitude * num3;
			position = ProjectToGround(position, body);
			normalized = (position - body.position).normalized;
		}
		if (s.leaping)
		{
			s.leapProgress += num / s.leapDuration;
			float num4 = Mathf.Clamp01(s.leapProgress);
			s.lift = 4f * s.leapPeak * num4 * (1f - num4);
			if (!s.apexChecked && num4 >= 0.5f)
			{
				s.apexChecked = true;
				s.lift = s.leapPeak;
				Vector3d vector3d3 = position + normalized * (double)s.leapPeak;
				if ((worldPos3D - vector3d3).magnitude <= (double)BehaviorHoverCatchRadius)
				{
					s.catchPending = true;
					s.catchTime = Time.time;
					s.leaping = false;
					status = "POUNCING";
					return true;
				}
			}
			if (s.leapProgress >= 1f)
			{
				s.leaping = false;
				s.lift = 0f;
				s.leapCooldown = UnityEngine.Random.Range(3f, 6f);
			}
			status = "LEAPING";
			return true;
		}
		if (s.lift > 0f && !s.catchPending)
		{
			s.lift = Mathf.Max(0f, s.lift - s.leapPeak / 0.3f * num);
		}
		s.leapCooldown -= num;
		if (!flag)
		{
			status = "WAITING BENEATH";
			return true;
		}
		if (s.leapCooldown <= 0f && magnitude <= 20.0 && num2 > 4.0)
		{
			s.leaping = true;
			s.apexChecked = false;
			s.leapProgress = 0f;
			s.leapPeak = Mathf.Clamp((float)num2, 4f, hoverAttackMaxHeight + 15f);
			float num5 = Mathf.Sqrt(Mathf.Clamp((float)body.GeeASL, 0.3f, 2f));
			s.leapDuration = Mathf.Clamp((0.9f + s.leapPeak * 0.012f) / num5, 0.8f, 2.5f);
			status = "LEAPING";
			return true;
		}
		status = ((magnitude <= 20.0) ? "STALKING (BELOW)" : "RUNNING BENEATH");
		return true;
	}

	private static bool TakeHoverCatch(GroundBehaviorState s)
	{
		if (!s.catchPending)
		{
			return false;
		}
		s.catchPending = false;
		s.lift = 0f;
		return Time.time - s.catchTime < 1f;	}

	private static float GetLeapLift(GroundBehaviorState s)
	{
		if (s.lift <= 0f)
		{
			return 0f;
		}
		if (Time.time - s.hoverLastCall > 0.25f)
		{
			// No longer driven (for example the Rake was frozen by being watched): fall back along the arc over ~0.3 s.
			s.leaping = false;
			s.lift = Mathf.Max(0f, s.lift - Mathf.Max(s.leapPeak, 1f) / 0.3f * Time.deltaTime);
		}
		return s.lift;
	}

	private static bool HoverFacing(GroundBehaviorState s)
	{
		return hoverAttacks && Time.time - s.hoverLastCall < 0.5f;
	}

	// Mimic over 10 parts: destroy a few random parts, then reuse the standing-down/retreat flow.
	private void MimicMaimLargeVessel(Vessel vessel)
	{
		int num = UnityEngine.Random.Range(3, 6);
		HandleEntityCatch(vessel, num);
		if (!debugImmortality)
		{
			ScreenMessages.PostScreenMessage("The Mimic tears into your vessel and falls back.", 5f, ScreenMessageStyle.UPPER_CENTER);
		}
		groundEntityStandingDown = true;
		groundEntityWithdrawalTimer = 3f;
		groundEntityStatus = string.Concat(groundVariant, ": STANDING STILL");
	}

	// Called when the Rake's retreat ends. Returns true if it left a pending return (the Rake was despawned).
	private bool BeginRakeReturnWait()
	{
		if (!rakeReturnsAfterWatched || rakeReturnsDone >= (int)rakeMaxReturns || rakeEntityBody == null)
		{
			rakeReturnPending = false;
			return false;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody != rakeEntityBody)
		{
			return false;
		}
		Vector3d worldPos3D = activeVessel.GetWorldPos3D();
		Vector3d normalized = (worldPos3D - rakeEntityBody.position).normalized;
		Vector3d vector3d = rakeEntityBody.position + rakeEntityRelativePosition - worldPos3D;
		vector3d -= normalized * Vector3d.Dot(vector3d, normalized);
		rakeReturnBearing = ((vector3d.sqrMagnitude > 1.0) ? vector3d.normalized : Vector3d.zero);
		rakeReturnBody = rakeEntityBody;
		rakeReturnsDone++;
		rakeReturnTimer = UnityEngine.Random.Range(45f, 90f);
		rakeReturnWaited = 0f;
		rakeReturnPending = true;
		DespawnRakeEntity();
		return true;
	}

	private void UpdateRakeReturnWait()
	{
		if (!rakeReturnPending)
		{
			return;
		}
		if (!HighLogic.LoadedSceneIsFlight)
		{
			rakeReturnPending = false;
			return;
		}
		if (Time.timeScale <= 0f)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		if (rakeReturnBody == null || activeVessel.mainBody != rakeReturnBody || rakeEntityActive)
		{
			rakeReturnPending = false;
			return;
		}
		float num = BehaviorStep();
		if (rakeReturnTimer > 0f)
		{
			rakeReturnTimer -= num;
			return;
		}
		if (!IsEligibleForRakeEncounter(activeVessel))
		{
			rakeReturnWaited += num;
			if (rakeReturnWaited > 120f)
			{
				rakeReturnPending = false;
			}
			return;
		}
		rakeReturnPending = false;
		rakeSpawningReturn = true;
		try
		{
			SpawnRakeEntity();
		}
		finally
		{
			rakeSpawningReturn = false;
		}
		if (rakeEntityActive)
		{
			MoveRakeToNewDirection(activeVessel);
		}
	}

	// A fresh Rake encounter (not one of our returns) resets the return counter.
	private void NoteRakeSpawned()
	{
		if (!rakeSpawningReturn)
		{
			rakeReturnsDone = 0;
			rakeReturnPending = false;
		}
	}

	private void MoveRakeToNewDirection(Vessel vessel)
	{
		if (rakeEntityBody == null || rakeEntityObject == null || rakeReturnBearing.sqrMagnitude < 0.5)
		{
			return;
		}
		Vector3d worldPos3D = vessel.GetWorldPos3D();
		Vector3d normalized = (worldPos3D - rakeEntityBody.position).normalized;
		Vector3d vector3d = rakeEntityBody.position + rakeEntityRelativePosition - worldPos3D;
		vector3d -= normalized * Vector3d.Dot(vector3d, normalized);
		if (vector3d.sqrMagnitude < 1.0 || Vector3d.Dot(vector3d.normalized, rakeReturnBearing) <= 0.5)
		{
			return;
		}
		for (int i = 0; i < 8; i++)
		{
			Vector3d vector3d2 = RotateAboutUp(vector3d, normalized, UnityEngine.Random.Range(110f, 250f));
			if (Vector3d.Dot(vector3d2.normalized, rakeReturnBearing) > 0.5)
			{
				continue;
			}
			Vector3d vector3d3 = ProjectToGround(worldPos3D + vector3d2, rakeEntityBody);
			if (IsLandAt(vector3d3, rakeEntityBody))
			{
				rakeEntityRelativePosition = vector3d3 - rakeEntityBody.position;
				rakeEntityObject.transform.position = vector3d3;
				return;
			}
		}
	}
}
