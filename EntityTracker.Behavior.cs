using System;
using UnityEngine;

// Hover leaps of the Moho creature (a target off the ground is attacked by leaps). The earlier approach dance
// (Stalk / Circle / Fake-out / Charge, smartEntityAi) was removed; the hunt (EntityTracker.Hunt.cs) replaced it.
public partial class EntityTracker
{
	// One instance per ground monster. Tied to the monster's GameObject, so a new spawn starts fresh.
	private sealed class GroundBehaviorState
	{
		public UnityEngine.Object owner;

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

		public void ResetAll()
		{
			hoverLastCall = -100f;
			leaping = false;
			apexChecked = false;
			leapProgress = 0f;
			leapCooldown = 0f;
			lift = 0f;
			catchPending = false;
		}
	}

	private const float BehaviorHoverTrackHeight = 300f;
	private const float BehaviorHoverCatchRadius = 12f;

	// Settings (see settings.cfg). Static so static helpers can read them.
	private static bool hoverAttacks = true;
	private static float hoverAttackMaxHeight = 60f;
	private readonly GroundBehaviorState behaviorBulk = new GroundBehaviorState();

	private static void LoadBehaviorSettings(ConfigNode node)
	{
		if (node != null && node.HasValue("hoverAttacks"))
		{
			bool.TryParse(node.GetValue("hoverAttacks"), out hoverAttacks);
		}
		ReadFloatSetting(node, "hoverAttackMaxHeight", ref hoverAttackMaxHeight);
		hoverAttackMaxHeight = Mathf.Clamp(hoverAttackMaxHeight, 5f, 300f);
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

}
