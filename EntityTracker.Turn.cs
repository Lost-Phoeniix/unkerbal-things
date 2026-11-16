using UnkerbalThings.Dread;
using System;
using UnityEngine;

// The Moho creature's running away (RETREATING / RUNNING OFF) under the same heading rules as its hunt.
// The old retreat moved the position straight away from the craft at full speed on the first frame and set the body to face that way at once,
// so the position went one way while the body was still turning: the creature ran backwards. Now the creature has a heading and a speed
// (DreadHunt.RetreatStep -> HeadingMotion), "away from the craft" only steers the heading, and the position moves along the heading.
// huntNaturalTurn=false restores the old retreat.
public partial class EntityTracker
{
	// away: the direction away from the craft, tangent to the ground at the position.
	private void MohoRetreatMove(ref Vector3d position, Vector3d away, Vessel vessel)
	{
		DreadHunt h = huntBulk.hunt;
		float dt = Mathf.Clamp(TimeWarp.deltaTime, 0f, 0.25f);
		if (!bulkTurnHold)
		{
			bulkTurnHold = true;
			// it was standing still (stood down): it starts from rest, not from the speed the brain last had. Otherwise it keeps its speed.
			if (mohoSpeedSmooth < 1f)
			{
				h.Speed = 0f;
			}
		}
		double px, py;
		huntBulk.frame.ToPlane(position, out px, out py);
		h.X = px;
		h.Y = py;
		bool move = away.sqrMagnitude > 1.0;
		double goal = h.Heading;
		if (move)
		{
			double gx, gy;
			huntBulk.frame.ToPlane(position + away.normalized * 1000.0, out gx, out gy);
			goal = Math.Atan2(gy - py, gx - px);
		}
		float retreatSpeed = 42f;
		float wanted = retreatSpeed;
		float accel = huntSettings.Accel * 42f;
		if (bulkLeaving)
		{
			// runs, up to the sprint speed, and does not stop for the terrain
			wanted = Mathf.Max(retreatSpeed, MohoSprintSpeedNow);
			accel = Mathf.Max(mohoSprintAccel, 5f);
		}
		h.RetreatStep(dt, goal, wanted, accel, huntSettings.Brake * 42f, move);
		bulkLeaveSpeed = h.Speed;
		position = ProjectToGround(huntBulk.frame.FromPlane(h.X, h.Y), bulkEntityBody);
		bulkEntityRelativePosition = position - bulkEntityBody.position;
	}
}
