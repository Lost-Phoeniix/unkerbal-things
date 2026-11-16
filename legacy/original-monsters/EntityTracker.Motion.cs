using System;
using UnityEngine;

// Visual-only motion polish for the monsters (turn rate, lean, speed-driven gait, slope alignment, Mimic frame rate).
// Nothing here touches a logical position: the hooks run after an Update method has set the object's rotation and read the
// logical (ground) position only to derive velocity. smoothEntityMotion=false makes every hook return at once.
public partial class EntityTracker
{
	private enum MotionKind
	{
		Mimic,
		Bulk,
		Rake,
		Laythe,
		Sea,
		Bloop
	}

	private sealed class MotionState
	{
		public UnityEngine.Object owner;

		public float lastCall = -100f;

		public Vector3d lastRel;

		// Smoothed body-relative velocity, acceleration and speed (m/s), all in the tangent-ish world frame.
		public Vector3 vel;

		public Vector3 accel;

		public float speed;

		// Rotation bookkeeping: what the Update method wanted, the rate-limited version, and what we wrote.
		public Quaternion target = Quaternion.identity;

		public Quaternion smoothed = Quaternion.identity;

		public Quaternion applied = Quaternion.identity;

		public bool hasApplied;

		// Gait.
		public float gaitPhase;

		public float gaitAmount;

		// Slope alignment (tangent offset of the up vector, smoothed).
		public Vector3 slopeOffset;

		public Vector3 slopeTargetOffset;

		public float slopeTimer;

		public float swayPhase;
	}

	private const float MotionSlopeBlend = 0.6f;
	private const float MotionSlopeMaxTan = 0.47f;
	private const float MotionSlopeSampleInterval = 0.25f;
	private const float MotionSlopeSampleDistance = 4f;
	private const float MotionVelocityTau = 0.25f;
	private const float MotionAccelTau = 0.2f;
	private const float MotionMaxGaitHz = 6f;

	// Full leg-cycle length in metres, per monster (one cycle = both feet once).
	private const float MotionBulkStride = 13f;
	private const float MotionRakeStride = 16f;
	private const float MotionLaytheStride = 11f;

	// Settings (see settings.cfg). Static so static helpers can read it.
	private static bool smoothEntityMotion = true;

	private readonly MotionState motionMimic = new MotionState();
	private readonly MotionState motionBulk = new MotionState();
	private readonly MotionState motionRake = new MotionState();
	private readonly MotionState motionLaythe = new MotionState();
	private readonly MotionState motionSea = new MotionState();
	private readonly MotionState motionBloop = new MotionState();

	private static void LoadMotionSettings(ConfigNode node)
	{
		if (node != null && node.HasValue("smoothEntityMotion"))
		{
			bool.TryParse(node.GetValue("smoothEntityMotion"), out smoothEntityMotion);
		}
	}

	private static float MotionTurnRate(MotionKind kind)
	{
		switch (kind)
		{
		case MotionKind.Mimic:
			return 540f;
		case MotionKind.Rake:
			return 420f;
		case MotionKind.Laythe:
			return 300f;
		case MotionKind.Bulk:
			return 220f;
		case MotionKind.Sea:
			return 110f;
		default:
			return 30f;
		}
	}

	// For Bulk, whose Update builds its rotation incrementally from the current one: hand back the unmodified target.
	private Quaternion MotionBaseRotation(MotionState s, GameObject obj)
	{
		if (smoothEntityMotion && obj != null && ReferenceEquals(s.owner, obj) && s.hasApplied && Quaternion.Angle(obj.transform.rotation, s.applied) < 0.05f)
		{
			return s.target;
		}
		return obj.transform.rotation;
	}

	private void ApplyEntityMotion(MotionState s, MotionKind kind, GameObject obj, CelestialBody body, Vector3d logicalPos, GroundBehaviorState behavior, float refSpeed)
	{
		if (!smoothEntityMotion || s == null || obj == null || body == null)
		{
			return;
		}
		Transform tr = obj.transform;
		bool sameOwner = ReferenceEquals(s.owner, obj);
		float dt = TimeWarp.deltaTime;
		if (Time.timeScale <= 0f || dt <= 0f)
		{
			if (sameOwner && s.hasApplied)
			{
				tr.rotation = s.applied;
			}
			return;
		}
		refSpeed = Mathf.Max(refSpeed, 1f);
		float step = Mathf.Min(dt, 0.1f);
		Vector3d rel = logicalPos - body.position;
		Vector3 up = (Vector3)rel.normalized;
		Quaternion raw = tr.rotation;
		bool fresh = !sameOwner || !s.hasApplied || Time.time - s.lastCall > 0.5f || dt > 0.25f;
		bool valid = false;
		Vector3 rawVel = Vector3.zero;
		float dist = 0f;
		if (fresh)
		{
			s.owner = obj;
			s.vel = Vector3.zero;
			s.accel = Vector3.zero;
			s.speed = 0f;
			s.target = raw;
			s.smoothed = raw;
			s.gaitAmount = 0f;
			s.slopeOffset = Vector3.zero;
			s.slopeTargetOffset = Vector3.zero;
			s.slopeTimer = 0f;
		}
		else
		{
			if (Quaternion.Angle(raw, s.applied) < 0.05f)
			{
				raw = s.target;
			}
			Vector3d delta = rel - s.lastRel;
			double rawSpeed = delta.magnitude / (double)dt;
			// Teleports, respawns and floating origin glitches: ignore the frame.
			if (rawSpeed <= (double)(refSpeed * 3f + 25f))
			{
				valid = true;
				rawVel = (Vector3)(delta / (double)dt);
				dist = (float)delta.magnitude;
			}
		}
		s.lastRel = rel;
		s.lastCall = Time.time;
		s.target = raw;

		if (valid)
		{
			Vector3 previous = s.vel;
			s.vel = Vector3.Lerp(s.vel, rawVel, 1f - Mathf.Exp(-step / MotionVelocityTau));
			Vector3 accelRaw = (s.vel - previous) / step;
			s.accel = Vector3.Lerp(s.accel, accelRaw, 1f - Mathf.Exp(-step / MotionAccelTau));
			s.speed = s.vel.magnitude;
			float stride = (kind == MotionKind.Bulk) ? MotionBulkStride : ((kind == MotionKind.Rake) ? MotionRakeStride : MotionLaytheStride);
			float advance = Mathf.Min(dist / stride * ((float)Math.PI * 2f), (float)Math.PI * 2f * MotionMaxGaitHz * step);
			s.gaitPhase = Mathf.Repeat(s.gaitPhase + advance, (float)Math.PI * 2f);
		}
		float amountTarget = Mathf.Clamp01((s.speed - 0.3f) / 1.5f);
		s.gaitAmount = Mathf.MoveTowards(s.gaitAmount, amountTarget, step * 4f);

		// Turn rate (faster while charging).
		float rate = MotionTurnRate(kind);
		if (behavior != null && smartEntityAi && behavior.phase == BehaviorPhase.Charge && Time.time - behavior.lastCall < 1f)
		{
			rate *= 2f;
		}
		Quaternion eased = Quaternion.Slerp(s.smoothed, raw, 1f - Mathf.Exp(-12f * step));
		s.smoothed = Quaternion.RotateTowards(s.smoothed, eased, rate * step);
		Quaternion result = s.smoothed;

		if (kind == MotionKind.Sea || kind == MotionKind.Bloop)
		{
			// Slow yaw sway while moving.
			float freq = (kind == MotionKind.Sea) ? 0.45f : 0.2f;
			float amplitude = (kind == MotionKind.Sea) ? 4f : 2.5f;
			s.swayPhase = Mathf.Repeat(s.swayPhase + step * freq * (float)Math.PI * 2f, (float)Math.PI * 2f);
			float moving = Mathf.Clamp01(s.speed / (0.4f * refSpeed));
			result = Quaternion.AngleAxis(Mathf.Sin(s.swayPhase) * amplitude * moving, up) * result;
		}
		else
		{
			// Lean toward the (smoothed) acceleration, partial slope alignment.
			Vector3 leanOffset = Vector3.zero;
			Vector3 heading = s.vel - up * Vector3.Dot(s.vel, up);
			if (heading.sqrMagnitude > 0.04f)
			{
				heading.Normalize();
				Vector3 right = Vector3.Cross(up, heading);
				float speedScale = Mathf.Clamp01(s.speed / (0.25f * refSpeed));
				float pitchDeg = Mathf.Clamp(Vector3.Dot(s.accel, heading) / refSpeed * 2f, -5f, 5f) * speedScale;
				float rollDeg = Mathf.Clamp(Vector3.Dot(s.accel, right) / refSpeed * 3f, -6f, 6f) * speedScale;
				leanOffset = heading * Mathf.Tan(pitchDeg * ((float)Math.PI / 180f)) + right * Mathf.Tan(rollDeg * ((float)Math.PI / 180f));
			}
			bool leaping = behavior != null && (behavior.leaping || behavior.lift > 0f);
			s.slopeTimer -= step;
			if (leaping)
			{
				s.slopeTargetOffset = Vector3.zero;
			}
			else if (s.slopeTimer <= 0f)
			{
				s.slopeTimer = MotionSlopeSampleInterval;
				s.slopeTargetOffset = SampleTerrainTiltOffset(logicalPos, body, up);
			}
			float slopeTau = leaping ? 0.1f : 0.4f;
			s.slopeOffset = Vector3.Lerp(s.slopeOffset, s.slopeTargetOffset, 1f - Mathf.Exp(-step / slopeTau));
			Vector3 finalUp = up + s.slopeOffset * MotionSlopeBlend + leanOffset;
			if (finalUp.sqrMagnitude > 0.01f)
			{
				result = Quaternion.FromToRotation(up, finalUp.normalized) * result;
			}
		}

		s.applied = result;
		s.hasApplied = true;
		tr.rotation = result;

		switch (kind)
		{
		case MotionKind.Mimic:
			if (groundEntityMimicAnimation != null)
			{
				groundEntityMimicAnimation.SetMotionSpeedFactor(s.speed / refSpeed);
			}
			break;
		case MotionKind.Bulk:
			if (bulkEntityBoneAnimation != null)
			{
				bulkEntityBoneAnimation.SetGait(s.gaitPhase, s.gaitAmount);
			}
			break;
		case MotionKind.Rake:
			if (rakeBoneAnimation != null)
			{
				rakeBoneAnimation.SetGait(s.gaitPhase, s.gaitAmount);
			}
			break;
		case MotionKind.Laythe:
			if (laytheCreatureBoneAnimation != null)
			{
				laytheCreatureBoneAnimation.SetGait(s.gaitPhase, s.gaitAmount);
			}
			break;
		}
	}

	// Tangent offset to add to the radial up vector so it leans toward the terrain normal (zero if it cannot be sampled).
	private static Vector3 SampleTerrainTiltOffset(Vector3d position, CelestialBody body, Vector3 up)
	{
		Vector3 t1 = Vector3.Cross(up, (Vector3)body.transform.up);
		if (t1.sqrMagnitude < 0.0001f)
		{
			t1 = Vector3.Cross(up, Vector3.right);
		}
		t1.Normalize();
		Vector3 t2 = Vector3.Cross(up, t1);
		double d = MotionSlopeSampleDistance;
		Vector3d a = MotionTerrainPoint(position + (Vector3d)t1 * d, body);
		Vector3d b = MotionTerrainPoint(position - (Vector3d)t1 * d, body);
		Vector3d c = MotionTerrainPoint(position + (Vector3d)t2 * d, body);
		Vector3d e = MotionTerrainPoint(position - (Vector3d)t2 * d, body);
		Vector3 normal = Vector3.Cross((Vector3)(a - b), (Vector3)(c - e));
		if (normal.sqrMagnitude < 1E-06f)
		{
			return Vector3.zero;
		}
		normal.Normalize();
		float dot = Vector3.Dot(normal, up);
		if (dot < 0f)
		{
			normal = -normal;
			dot = -dot;
		}
		if (dot < 0.2f)
		{
			return Vector3.zero;
		}
		Vector3 offset = normal / dot - up;
		float limit = MotionSlopeMaxTan;
		if (offset.sqrMagnitude > limit * limit)
		{
			offset = offset.normalized * limit;
		}
		return offset;
	}

	private static Vector3d MotionTerrainPoint(Vector3d worldPosition, CelestialBody body)
	{
		double latitude = body.GetLatitude(worldPosition);
		double longitude = body.GetLongitude(worldPosition);
		return body.GetWorldSurfacePosition(latitude, longitude, body.TerrainAltitude(latitude, longitude));
	}
}
