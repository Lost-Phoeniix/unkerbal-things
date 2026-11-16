using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnkerbalThings.Dread;
using UnityEngine;

// In-game scene of the turn unit (run through TestDriver.Scene as "moho_turn", KK_TEST_ONLY=moho_turn).
// The creature approaches the craft, hunting, with the camera following the CREATURE. Once it is close and fast it is told to leave
// (LeaveBulkEntity, the same path as 'strikes a vessel too large to kill and vanishes'), so that it has to turn round while running, in view.
// Output: turn-moho_turn.csv (every frame), turn-moho_turn.txt (summary), frames turn-NN.png (one every 0.5 s from the order to leave).
public partial class EntityTracker
{
	private static class TurnTest
	{
		static readonly CultureInfo C = CultureInfo.InvariantCulture;

		internal static IEnumerator Run(EntityTracker tracker, Vessel v, string outDir, Action<string, float, string> note, Action<bool> setNotLanded)
		{
			string id = "moho_turn";
			StringBuilder csv = new StringBuilder("t,phase,speedHunt,speedMeasured,bodyHeadingDeg,travelHeadingDeg,bodyVsTravelDeg,objVsTravelDeg,yawRateDeg,leanDeg,chestLeadDeg,trueDist,north,east,status,plantedDrift,footCross\n");
			StringBuilder txt = new StringBuilder();
			float t = 0f;
			Action<string> N = (s) => note(id, t, s);
			tracker.SpawnBulkEntity();
			float spawnWait = 0f;
			while (!tracker.bulkEntityActive && spawnWait < 90f)
			{
				yield return null;
				spawnWait += Time.unscaledDeltaTime;
			}
			if (!tracker.bulkEntityActive)
			{
				throw new InvalidOperationException(id + ": no spawn after 90 s: " + tracker.bulkEntityStatus);
			}
			tracker.PlaceTestWalker(v, 700.0);
			yield return null;
			FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
			FlightCamera.fetch.SetDistanceImmediate(300f);
			FlightCamera.fetch.camPitch = 0.12f;
			FlightCamera.fetch.camHdg = 1.2f;
			N("creature placed 700 m from the craft, camera on the creature at 300 m");
			CelestialBody body = tracker.bulkEntityBody;
			Vector3d up0 = (v.GetWorldPos3D() - body.position).normalized;
			Vector3d north = Vector3d.Exclude(up0, body.transform.up).normalized;
			Vector3d east = Vector3d.Cross(up0, north).normalized;

			float noiseAt = 1.0f;
			int noiseCount = 0;
			string phase = "approach";
			float leaveAt = -1f;
			int shotIdx = 0;
			float nextShot = 0f;
			Vector3 lastPos = Vector3.zero;
			bool havePos = false;
			float maxFaceModel = 0f, maxFaceModelOutside = 0f, maxFaceObj = 0f, minDot = 2f, maxYawAtZero = 0f, maxSpeedJump = 0f, lastSpeed = 0f, maxLean = 0f, maxLead = 0f;
			float minSpeedAfterOrder = 1e9f, turnEndAt = -1f, maxDriftLogic = 0f, lastDist = 0f;
			double maxExtent = 0.0;
			int crossFrames = 0, frames = 0;
			Vector3[] planted = new Vector3[4];
			bool[] was = new bool[4];
			Vector3 startLeavePos = Vector3.zero;
			float speedAtLeave = 0f, distAtLeave = 0f;
			bool leaveIssued = false;
			float limit = 150f;
			while (t < limit && tracker.bulkEntityActive)
			{
				yield return null;
				float dt = Time.unscaledDeltaTime;
				t += dt;
				if (!leaveIssued && t > noiseAt && t < 20f)
				{
					noiseAt += 1f;
					if (noiseCount++ == 0)
					{
						tracker.HuntInjectNoise(HuntNoise.Impact, 1f, v.GetWorldPos3D());
					}
					tracker.HuntInjectNoise(HuntNoise.Throttle, 0.9f, v.GetWorldPos3D());
				}
				EntityTracker.HuntInfo hi = tracker.GetNearestHuntInfo();
				MohoCreature c = tracker.bulkMoho;
				if (c == null || !hi.Valid)
				{
					continue;
				}
				// the order to leave: close (inside 330 m) and fast (60 m/s or more), or after 70 s whatever happens
				if (!leaveIssued && ((hi.TrueDistance < 330.0 && hi.Speed >= 60f) || t > 70f))
				{
					leaveIssued = true;
					leaveAt = t;
					phase = "leave";
					speedAtLeave = hi.Speed;
					distAtLeave = (float)hi.TrueDistance;
					startLeavePos = c.Position;
					N("ORDER TO LEAVE at " + distAtLeave.ToString("F0", C) + " m, speed " + speedAtLeave.ToString("F1", C) + " m/s");
					tracker.LeaveBulkEntity("test: turn scene");
					nextShot = t;
				}
				Vector3 pos = c.Position;
				Vector3 upv = c.Up;
				float measured = 0f, travelDeg = 0f, bodyVsTravel = 0f, objVsTravel = 0f;
				Vector3 bf = Vector3.ProjectOnPlane(c.BodyForward, upv).normalized;
				float bodyDeg = Mathf.Atan2(Vector3.Dot(bf, (Vector3)east), Vector3.Dot(bf, (Vector3)north)) * Mathf.Rad2Deg;
				if (havePos && dt > 0f)
				{
					Vector3 delta = Vector3.ProjectOnPlane(pos - lastPos, upv);
					measured = delta.magnitude / dt;
					if (measured > 1.5f)
					{
						Vector3 tr = delta.normalized;
						travelDeg = Mathf.Atan2(Vector3.Dot(tr, (Vector3)east), Vector3.Dot(tr, (Vector3)north)) * Mathf.Rad2Deg;
						float dot = Vector3.Dot(bf, tr);
						bodyVsTravel = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
						objVsTravel = hi.FaceErrorObjectDeg;
						minDot = Mathf.Min(minDot, dot);
						maxFaceModel = Mathf.Max(maxFaceModel, bodyVsTravel);
						if (measured > 8f && Mathf.Abs(c.YawRateDeg) < 8f)
						{
							maxFaceModelOutside = Mathf.Max(maxFaceModelOutside, bodyVsTravel);
						}
						maxFaceObj = Mathf.Max(maxFaceObj, objVsTravel);
					}
					if (leaveIssued)
					{
						minSpeedAfterOrder = Mathf.Min(minSpeedAfterOrder, measured);
					}
					if (measured < 0.05f)
					{
						maxYawAtZero = Mathf.Max(maxYawAtZero, Mathf.Abs(c.YawRateDeg));
					}
					maxSpeedJump = Mathf.Max(maxSpeedJump, Mathf.Abs(hi.Speed - lastSpeed) / dt);
				}
				lastSpeed = hi.Speed;
				lastPos = pos;
				havePos = true;
				maxLean = Mathf.Max(maxLean, Mathf.Abs(c.LeanDeg));
				maxLead = Mathf.Max(maxLead, Mathf.Abs(c.ChestLeadDeg));
				// logic feet: a planted foot must not move; the two sides must not cross
				float drift = 0f;
				for (int i = 0; i < 4; i++)
				{
					bool pl = c.IsPlanted(i);
					if (pl && was[i])
					{
						drift = Mathf.Max(drift, (c.FootContact(i) - planted[i]).magnitude);
					}
					planted[i] = c.FootContact(i);
					was[i] = pl;
				}
				maxDriftLogic = Mathf.Max(maxDriftLogic, drift);
				Vector3 rb = Vector3.Cross(upv, bf);
				int cross = 0;
				if (Vector3.Dot(c.FootContact(0) - c.FootContact(1), rb) > 0f) cross++;
				if (Vector3.Dot(c.FootContact(2) - c.FootContact(3), rb) > 0f) cross++;
				if (cross > 0 && measured > 3f)
				{
					crossFrames++;
				}
				frames++;
				Vector3d rel = (Vector3d)pos - v.GetWorldPos3D();
				double n = Vector3d.Dot(rel, north), e = Vector3d.Dot(rel, east);
				if (leaveIssued)
				{
					Vector3d d = (Vector3d)(pos - startLeavePos);
					double ex = Vector3d.Dot(d, north), ey = Vector3d.Dot(d, east);
					maxExtent = Math.Max(maxExtent, Math.Sqrt(ex * ex + ey * ey));
				}
				csv.AppendLine(string.Join(",", new string[]
				{
					t.ToString("F3", C), phase, hi.Speed.ToString("F2", C), measured.ToString("F2", C), bodyDeg.ToString("F1", C), travelDeg.ToString("F1", C),
					bodyVsTravel.ToString("F1", C), objVsTravel.ToString("F1", C), c.YawRateDeg.ToString("F2", C), c.LeanDeg.ToString("F2", C), c.ChestLeadDeg.ToString("F2", C),
					hi.TrueDistance.ToString("F1", C), n.ToString("F1", C), e.ToString("F1", C), (tracker.bulkEntityStatus ?? "").Replace(",", ";"), drift.ToString("F5", C), cross.ToString()
				}));
				if (leaveIssued && t >= nextShot && shotIdx < 40)
				{
					shotIdx++;
					nextShot = t + 0.5f;
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "turn-" + shotIdx.ToString("D2") + ".png"));
				}
				// settled on the new line: moving away from the craft, body along travel, no yaw
				bool receding = hi.TrueDistance > lastDist;
				lastDist = (float)hi.TrueDistance;
				if (leaveIssued && turnEndAt < 0f && t > leaveAt + 1f && receding && Mathf.Abs(c.YawRateDeg) < 1.5f && measured > 20f && bodyVsTravel < 5f)
				{
					turnEndAt = t;
					N("settled on the new line " + (turnEndAt - leaveAt).ToString("F1", C) + " s after the order, sweep " + maxExtent.ToString("F0", C) + " m");
				}
				if (leaveIssued && (t > leaveAt + 40f || (turnEndAt > 0f && t > turnEndAt + 4f)))
				{
					break;
				}
			}
			txt.AppendLine("order to leave at t=" + leaveAt.ToString("F1", C) + " s, " + distAtLeave.ToString("F0", C) + " m from the craft, speed " + speedAtLeave.ToString("F1", C) + " m/s");
			txt.AppendLine("frames " + frames + ", settled after " + (turnEndAt > 0 ? (turnEndAt - leaveAt).ToString("F1", C) : "n/a") + " s, widest sweep from the order " + maxExtent.ToString("F0", C) + " m, slowest measured speed after the order " + minSpeedAfterOrder.ToString("F1", C) + " m/s");
			txt.AppendLine("model body (creature.BodyForward) against travel: max " + maxFaceModel.ToString("F1", C) + " deg overall, " + maxFaceModelOutside.ToString("F1", C) + " deg outside turns (yaw under 8 deg/s, speed over 8 m/s); min dot " + minDot.ToString("F3", C));
			txt.AppendLine("tracker root object against travel (the old csv number): max " + maxFaceObj.ToString("F1", C) + " deg");
			txt.AppendLine("yaw rate at measured speed under 0.05 m/s: " + maxYawAtZero.ToString("F2", C) + " deg/s; max hunt speed change " + maxSpeedJump.ToString("F1", C) + " m/s2");
			txt.AppendLine("max lean " + maxLean.ToString("F1", C) + " deg, max chest lead " + maxLead.ToString("F1", C) + " deg; planted foot logic drift max " + maxDriftLogic.ToString("F5", C) + " m; frames with a side pair crossed while moving: " + crossFrames);
			File.WriteAllText(Path.Combine(outDir, "turn-" + id + ".csv"), csv.ToString());
			File.WriteAllText(Path.Combine(outDir, "turn-" + id + ".txt"), txt.ToString());
			N("turn scene summary written");
			setNotLanded(false);
			if (tracker.bulkEntityActive)
			{
				tracker.DespawnBulkEntity();
			}
			yield return null;
		}
	}
}
