using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnkerbalThings.Dread;
using UnityEngine;

// In-game scenes of the sprint unit (run by KKTestBoot via TestDriver.Scene). Scenes: moho_sprint (approach from 2.7 km: warnings, sprint, contact),
// moho_run (long sight so that running leaps happen), moho_leap (craft held 55 m up: the catch leap, flight-mode warnings), moho_vis (spawn and
// despawn visibility). Output: sprint-<scene>.csv, sprint-<scene>.txt, screenshots, notes in steps.csv through the note callback.
public partial class EntityTracker
{
	private static class SprintTest
	{
		static readonly CultureInfo C = CultureInfo.InvariantCulture;

		// Independent of the game code: Unity's own camera projection, and the old coarse terrain line of sight with the head at 60 m.
		internal static bool Exposed(EntityTracker t, out bool inView, out bool los)
		{
			inView = false;
			los = false;
			Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
			Vessel v = FlightGlobals.ActiveVessel;
			if (cam == null || v == null || t.bulkEntityBody == null)
			{
				return true;
			}
			CelestialBody body = t.bulkEntityBody;
			Vector3d ground = body.position + t.bulkEntityRelativePosition;
			Vector3 up = ((Vector3)(ground - body.position)).normalized;
			for (int h = 0; h <= 5 && !inView; h++)
			{
				Vector3 p = (Vector3)ground + up * (13f * h);
				Vector3 vp = cam.WorldToViewportPoint(p);
				if (vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f)
				{
					inView = true;
				}
			}
			Vector3d headBase = ground + (Vector3d)up * 25.0;   // the function adds 35 m: a 60 m eye
			los = HasGroundLineOfSight(headBase, v.GetWorldPos3D(), body) || HasGroundLineOfSight(headBase, (Vector3d)cam.transform.position, body);
			return inView && los;
		}

		// The camera stays on the craft (the player's view) and is turned toward the monster.
		static IEnumerator AimCamera(EntityTracker t, float distance)
		{
			FlightCamera fc = FlightCamera.fetch;
			fc.SetTargetVessel(FlightGlobals.ActiveVessel);
			fc.SetDistanceImmediate(distance);
			fc.camPitch = 0.08f;
			float sign = 1f;
			float last = 999f;
			for (int k = 0; k < 8; k++)
			{
				yield return null;
				yield return null;
				Camera cam = fc.mainCamera;
				Vessel v = FlightGlobals.ActiveVessel;
				if (cam == null || v == null || t.bulkEntityBody == null)
				{
					yield break;
				}
				Vector3 up = ((Vector3)(v.GetWorldPos3D() - t.bulkEntityBody.position)).normalized;
				Vector3 toC = Vector3.ProjectOnPlane((Vector3)((t.bulkEntityBody.position + t.bulkEntityRelativePosition) - v.GetWorldPos3D()), up);
				float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward, up), toC, up);
				if (Mathf.Abs(ang) < 3f)
				{
					yield break;
				}
				if (Mathf.Abs(ang) > Mathf.Abs(last) + 1f)
				{
					sign = -sign;
				}
				last = ang;
				fc.camHdg += sign * ang * Mathf.Deg2Rad;
			}
		}

		static void Shot(string outDir, string name)
		{
			ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name));
		}

		static IEnumerator WaitSeconds(float s)
		{
			float t = 0f;
			while (t < s)
			{
				yield return null;
				t += Time.unscaledDeltaTime;
			}
		}

		internal static IEnumerator Run(string id, EntityTracker tracker, Vessel v, DreadStepVoice voice, string outDir, Action<string, float, string> note, Action<bool> setNotLanded)
		{
			StringBuilder csv = new StringBuilder("t,state,trueDist,huntSpeed,measuredSpeed,gallop,airborne,lift,footfalls,steps,status,warn1,warn2,warn3,leaps,maxLeapLift,gain\n");
			StringBuilder txt = new StringBuilder();
			int logIndex = 0;
			float t = 0f;
			Action<string> N = (s) => note(id, t, s);

			// ---- spawn through the real spawn rule (not for the hidden-spawn scene: that one does its own)
			if (id == "moho_model")
			{
				yield return ModelScene(tracker, v, outDir, N, txt, () => t);
				File.WriteAllText(Path.Combine(outDir, "sprint-" + id + ".txt"), txt.ToString());
				yield break;
			}
			if (id == "moho_vis")
			{
				yield return VisScene(tracker, v, outDir, N, txt, () => t);
				File.WriteAllText(Path.Combine(outDir, "sprint-" + id + ".txt"), txt.ToString());
				yield break;
			}
			float startDistance = id == "moho_sprint" ? 2700f : (id == "moho_run" ? 2300f : 1400f);
			float limit = id == "moho_sprint" ? 135f : (id == "moho_run" ? 120f : 140f);
			float oldNotice = huntSettings.NoticeMetres, oldSight = huntSettings.SightMetres, oldGap = mohoLeapSettings.RunMeanGapSeconds;
			if (id == "moho_run")
			{
				huntSettings.NoticeMetres = 2500f;
				huntSettings.SightMetres = 4000f;
				mohoLeapSettings.RunMeanGapSeconds = 3f;
			}
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
			bool inV, los;
			bool exposedAtSpawn = Exposed(tracker, out inV, out los);
			N("spawn after " + spawnWait.ToString("F1", C) + " s wait; independent check inFrustum=" + inV + " lineOfSight=" + los + " exposed=" + exposedAtSpawn + " (game says exposed=" + tracker.IsBulkExposedNow() + ")");
			tracker.PlaceTestWalker(v, startDistance);
			N("test teleport to " + startDistance.ToString("F0", C) + " m (the monster is placed, not spawned)");
			if (id != "moho_sprint" && id != "moho_run")
			{
				setNotLanded(true);
			}
			yield return AimCamera(tracker, 30f);
			int steps = 0;
			float tNotice = -1f, tFast = -1f, tContact = -1f, nextShot = 0f, leapShotAt = 0f, speedMax = 0f, distAtNotice = -1f, distAtFast = -1f;
			int leapShots = 0, stepsInAir = 0, airFrames = 0;
			float noiseAt = 1.5f;
			int noiseCount = 0;
			int thumpBefore = 0, thumpAfter = 0, landSeen = 0, shakeSeen = 0;
			float maxShake = 0f;
			float lastLandCount = 0f;
			int thumpEvents0 = CountThumps(tracker);
			while (t < limit)
			{
				yield return null;
				float dt = Time.unscaledDeltaTime;
				t += dt;
				if (t > noiseAt && t < 14f)
				{
					noiseAt += 1f;
					if (noiseCount++ == 0)
					{
						tracker.HuntInjectNoise(HuntNoise.Impact, 1f, v.GetWorldPos3D());
					}
					tracker.HuntInjectNoise(HuntNoise.Throttle, 0.9f, v.GetWorldPos3D());
				}
				while (logIndex < tracker.MohoLog.Count)
				{
					N(tracker.MohoLog[logIndex++]);
				}
				EntityTracker.HuntInfo hi = tracker.GetNearestHuntInfo();
				MohoCreature c = tracker.bulkMoho;
				bool airborne = tracker.mohoLeap.Active;
				DreadStepLogic logic = tracker.dreadStepLogic;
				bool fired = logic != null && logic.StepFired;
				if (fired)
				{
					steps++;
					if (airborne)
					{
						stepsInAir++;
					}
				}
				if (airborne)
				{
					airFrames++;
				}
				if (hi.Valid && hi.State == HuntState.Hunt && tNotice < 0f)
				{
					tNotice = t;
					distAtNotice = (float)hi.TrueDistance;
					N("HUNT begins (sees the craft) at " + distAtNotice.ToString("F0", C) + " m");
				}
				if (hi.Valid)
				{
					speedMax = Mathf.Max(speedMax, hi.Speed);
				}
				if (hi.Valid && hi.Speed >= 100f && tFast < 0f)
				{
					tFast = t;
					distAtFast = (float)hi.TrueDistance;
					N("hunt speed reached 100 m/s at " + distAtFast.ToString("F0", C) + " m");
				}
				// the thump on landing: a synth event of kind thump since the last landing count
				if (tracker.MohoLandCount != (int)lastLandCount)
				{
					lastLandCount = tracker.MohoLandCount;
					landSeen++;
					thumpAfter = CountThumps(tracker);
					maxShake = Mathf.Max(maxShake, tracker.attackCameraAmplitude);
					N("landing " + landSeen + ": synth thump events so far " + (thumpAfter - thumpEvents0) + ", camera shake amplitude " + tracker.attackCameraAmplitude.ToString("F2", C) + " deg, last reach " + tracker.MohoLastLandScale.ToString("F2", C) + " at " + tracker.MohoLastLandDistance.ToString("F0", C) + " m");
				}
				if (tracker.attackSequenceList.Count > 0 && tContact < 0f)
				{
					tContact = t;
					N("contact: attack sequence began, monster " + (hi.Valid ? hi.TrueDistance.ToString("F1", C) : "?") + " m from the craft, speed " + (hi.Valid ? hi.Speed.ToString("F1", C) : "?"));
				}
				if (t >= nextShot && hi.Valid && hi.State == HuntState.Hunt && leapShots < 40)
				{
					nextShot = t + 1.0f;
					if (!airborne)
					{
						Shot(outDir, id + "-run-" + t.ToString("F0", C) + "s.png");
					}
				}
				if (airborne && t >= leapShotAt && leapShots < 12)
				{
					leapShotAt = t + 1.2f;
					leapShots++;
					Shot(outDir, id + "-leap-" + leapShots + ".png");
				}
				csv.AppendLine(string.Join(",", new string[]
				{
					t.ToString("F2", C), hi.Valid ? hi.State.ToString() : "", hi.Valid ? hi.TrueDistance.ToString("F1", C) : "", hi.Valid ? hi.Speed.ToString("F1", C) : "", tracker.dreadStepSpeed.ToString("F1", C),
					c != null && c.Gallop ? "1" : "0", airborne ? "1" : "0", tracker.mohoLeapLift.ToString("F1", C), c != null ? c.FootFallCount.ToString() : "", fired ? "1" : "0",
					(tracker.bulkEntityStatus ?? "").Replace(",", ";"), tracker.MohoWarnCount[1].ToString(), tracker.MohoWarnCount[2].ToString(), tracker.MohoWarnCount[3].ToString(),
					tracker.MohoLeapCount.ToString(), tracker.MohoMaxLeapLift.ToString("F1", C), logic != null ? logic.Gain.ToString("F4", C) : ""
				}));
				if (tContact < 0f && (tracker.bulkLeaving || tracker.bulkEntityRetreating))
				{
					tContact = t;
					limit = Mathf.Max(limit, t + 75f);
					N("contact: the monster reached the craft (" + (hi.Valid ? hi.TrueDistance.ToString("F1", C) : "?") + " m) and starts to leave; hunt speed " + (hi.Valid ? hi.Speed.ToString("F1", C) : "?"));
				}
				bool finished = !tracker.bulkEntityActive;
				if (finished)
				{
					break;
				}
				if (id == "moho_leap" && tracker.MohoLandCount >= 3)
				{
					break;
				}
				if (id == "moho_run" && tracker.MohoLandCount >= 2 && t > 40f && tNotice > 0f && t > tNotice + 25f)
				{
					break;
				}
			}
			Shot(outDir, id + "-end.png");
			while (logIndex < tracker.MohoLog.Count)
			{
				N(tracker.MohoLog[logIndex++]);
			}
			txt.AppendLine("scene " + id + ", " + t.ToString("F1", C) + " s");
			txt.AppendLine("hunt begins (sight) at t=" + tNotice.ToString("F2", C) + " s, " + distAtNotice.ToString("F0", C) + " m");
			txt.AppendLine("hunt speed first >= 100 m/s at t=" + tFast.ToString("F2", C) + " s, " + distAtFast.ToString("F0", C) + " m");
			txt.AppendLine("contact (attack sequence) at t=" + tContact.ToString("F2", C) + " s; sight-to-contact " + (tContact > 0f && tNotice > 0f ? (tContact - tNotice).ToString("F2", C) + " s" : "n/a"));
			txt.AppendLine("max hunt speed " + speedMax.ToString("F1", C) + " m/s");
			txt.AppendLine("footfalls " + (tracker.bulkMoho != null ? tracker.bulkMoho.FootFallCount : 0) + ", steps fired " + steps + ", steps fired while airborne " + stepsInAir + " (airborne frames " + airFrames + ")");
			txt.AppendLine("leaps " + tracker.MohoLeapCount + ", landings " + tracker.MohoLandCount + ", refused plans " + tracker.MohoLeapRefused + ", highest body lift above ground measured " + tracker.MohoMaxLeapLift.ToString("F1", C) + " m");
			txt.AppendLine("warnings fired: stage1 " + tracker.MohoWarnCount[1] + " stage2 " + tracker.MohoWarnCount[2] + " stage3 " + tracker.MohoWarnCount[3]);
			txt.AppendLine("max camera shake amplitude seen after a landing (deg) " + maxShake.ToString("F2", C));
			foreach (string s in tracker.MohoLog)
			{
				txt.AppendLine("log " + s);
			}
			File.WriteAllText(Path.Combine(outDir, "sprint-" + id + ".csv"), csv.ToString());
			File.WriteAllText(Path.Combine(outDir, "sprint-" + id + ".txt"), txt.ToString());
			setNotLanded(false);
			huntSettings.NoticeMetres = oldNotice;
			huntSettings.SightMetres = oldSight;
			mohoLeapSettings.RunMeanGapSeconds = oldGap;
			tracker.MohoLog.Clear();
			tracker.MohoLeapCount = 0;
			tracker.MohoLandCount = 0;
			tracker.MohoMaxLeapLift = 0f;
			tracker.MohoLeapRefused = 0;
			for (int i = 0; i < 4; i++)
			{
				tracker.MohoWarnCount[i] = 0;
			}
			if (tracker.bulkEntityActive)
			{
				txt.AppendLine("the monster was still active at the end of the scene (forced despawn, exposed=" + tracker.IsBulkExposedNow() + ")");
				tracker.DespawnBulkEntity();
			}
			yield return WaitSeconds(3f);
		}

		static int CountThumps(EntityTracker tracker)
		{
			DreadSynth s = tracker.dreadAudioHost != null ? tracker.dreadAudioHost.Synth : null;
			if (s == null)
			{
				return 0;
			}
			int n = 0;
			int count = s.EventCount;
			for (int i = 0; i < count; i++)
			{
				if (s.GetEventKind(i) == DreadSynth.EvThump)
				{
					n++;
				}
			}
			return n;
		}

		static IEnumerator ModelScene(EntityTracker tracker, Vessel v, string outDir, Action<string> N, StringBuilder txt, Func<float> clock)
		{
			tracker.SpawnBulkEntity();
			float w = 0f;
			while (!tracker.bulkEntityActive && w < 90f)
			{
				yield return null;
				w += Time.unscaledDeltaTime;
			}
			if (!tracker.bulkEntityActive)
			{
				throw new InvalidOperationException("moho_model: no spawn: " + tracker.bulkEntityStatus);
			}
			tracker.PlaceTestWalker(v, 900.0);
			MohoBoneSkin model = tracker.bulkMoho != null ? tracker.bulkMoho.Skin as MohoBoneSkin : null;
			txt.AppendLine("skin: " + (model != null ? "bone model, " + model.VertexCount + " vertices, " + model.TriangleCount + " triangles, shader " + model.ShaderName : "PROCEDURAL (the bundle did not load)"));
			FlightCamera fc = FlightCamera.fetch;
			fc.SetTargetTransform(tracker.bulkEntityObject.transform);
			fc.SetDistanceImmediate(300f);
			fc.camPitch = 0.1f;
			fc.camHdg = 0.9f;
			yield return WaitSeconds(3f);
			// at rest, from three sides and the stump close up
			for (int k = 0; k < 3; k++)
			{
				fc.camHdg = 0.9f + k * 2.1f;
				yield return WaitSeconds(1.2f);
				Shot(outDir, "model-rest-" + (k + 1) + ".png");
			}
			fc.SetDistanceImmediate(110f);
			fc.camPitch = 0.45f;
			yield return WaitSeconds(1.2f);
			Shot(outDir, "model-rest-close.png");
			fc.SetDistanceImmediate(300f);
			fc.camPitch = 0.1f;
			fc.camHdg = 0.9f;
			// frame cost: the same view with the skinned mesh drawn and not drawn, 4 s each, twice
			if (model != null && model.Renderer != null)
			{
				double[] sum = new double[2];
				int[] n = new int[2];
				int oldVsync = QualitySettings.vSyncCount, oldFps = Application.targetFrameRate;
				QualitySettings.vSyncCount = 0;
				Application.targetFrameRate = -1;
				for (int round = 0; round < 4; round++)
				{
					bool on = round % 2 == 0;
					model.Renderer.enabled = on;
					yield return WaitSeconds(1f);
					float t = 0f;
					while (t < 4f)
					{
						yield return null;
						float d = Time.unscaledDeltaTime;
						t += d;
						sum[on ? 1 : 0] += d;
						n[on ? 1 : 0]++;
					}
				}
				model.Renderer.enabled = true;
				QualitySettings.vSyncCount = oldVsync;
				Application.targetFrameRate = oldFps;
				double msOff = sum[0] / Math.Max(1, n[0]) * 1000.0, msOn = sum[1] / Math.Max(1, n[1]) * 1000.0;
				txt.AppendLine("frame time with the model drawn " + msOn.ToString("F2", C) + " ms (" + n[1] + " frames), not drawn " + msOff.ToString("F2", C) + " ms (" + n[0] + " frames); resolution " + Screen.width + "x" + Screen.height + ", vsync " + QualitySettings.vSyncCount + ", target fps " + Application.targetFrameRate);
			}
			// the gallop: it hunts the craft from 380 m, the camera stays on it
			tracker.PlaceTestWalker(v, 380.0);
			tracker.HuntInjectNoise(HuntNoise.Impact, 1f, v.GetWorldPos3D());
			float tt = 0f, nextShot = 0f;
			int shots = 0;
			float maxSpeed = 0f;
			fc.SetDistanceImmediate(240f);
			fc.camPitch = 0.12f;
			while (tracker.bulkEntityActive && tt < 60f && !tracker.bulkLeaving)
			{
				yield return null;
				tt += Time.unscaledDeltaTime;
				tracker.HuntInjectNoise(HuntNoise.Throttle, 0.9f, v.GetWorldPos3D());
				EntityTracker.HuntInfo hi = tracker.GetNearestHuntInfo();
				if (hi.Valid)
				{
					maxSpeed = Mathf.Max(maxSpeed, hi.Speed);
				}
				if (hi.Valid && hi.Speed > 60f && tt >= nextShot && shots < 14)
				{
					nextShot = tt + 0.35f;
					shots++;
					fc.camHdg += 0.25f;
					Shot(outDir, "model-gallop-" + shots + ".png");
				}
			}
			txt.AppendLine("gallop shots " + shots + ", max hunt speed " + maxSpeed.ToString("F1", C));
			fc.SetTargetVessel(FlightGlobals.ActiveVessel);
			yield return null;
			tracker.DespawnBulkEntity();
			yield return WaitSeconds(2f);
		}

		// Spawn visibility over many spawns, and a despawn of a creature that is in view.
		static IEnumerator VisScene(EntityTracker tracker, Vessel v, string outDir, Action<string> N, StringBuilder txt, Func<float> clock)
		{
			int spawns = 0, exposed = 0, delayed = 0, inViewCount = 0, losCount = 0;
			List<string> lines = new List<string>();
			// the camera looks around between spawns, so that the frustum differs
			FlightCamera.fetch.SetTargetVessel(FlightGlobals.ActiveVessel);
			FlightCamera.fetch.SetDistanceImmediate(30f);
			for (int k = 0; k < 24; k++)
			{
				FlightCamera.fetch.camHdg = k * 0.7f;
				FlightCamera.fetch.camPitch = (k % 3 - 1) * 0.3f;
				yield return WaitSeconds(0.3f);
				int pendingBefore = tracker.MohoSpawnDelayed;
				tracker.SpawnBulkEntity();
				if (!tracker.bulkEntityActive)
				{
					delayed++;
					lines.Add("spawn " + k + ": delayed (" + tracker.bulkEntityStatus + ")");
					yield return WaitSeconds(3f);
					continue;
				}
				bool inV, los;
				bool ex = Exposed(tracker, out inV, out los);
				bool gameEx = tracker.IsBulkExposedNow();
				spawns++;
				if (ex || gameEx)
				{
					exposed++;
				}
				if (inV)
				{
					inViewCount++;
				}
				if (los)
				{
					losCount++;
				}
				CelestialBody body = tracker.bulkEntityBody;
				double dist = ((body.position + tracker.bulkEntityRelativePosition) - v.GetWorldPos3D()).magnitude;
				lines.Add("spawn " + k + ": " + dist.ToString("F0", C) + " m, independent inFrustum=" + inV + " lineOfSight=" + los + " exposed=" + ex + "; game exposed=" + gameEx);
				tracker.DespawnBulkEntity();
				yield return null;
			}
			txt.AppendLine("spawns " + spawns + ", delayed " + delayed + ", exposed at the moment of spawn (independent or game test) " + exposed + ", of which in view alone " + inViewCount + ", with line of sight alone " + losCount);
			foreach (string l in lines)
			{
				txt.AppendLine(l);
				N(l);
			}
			// despawn: the creature is placed in plain view 500 m away, then told to leave (the path of 'strikes a vessel too large to kill and vanishes')
			FlightCamera.fetch.camPitch = 0.1f;
			tracker.SpawnBulkEntity();
			float w = 0f;
			while (!tracker.bulkEntityActive && w < 60f)
			{
				yield return null;
				w += Time.unscaledDeltaTime;
			}
			if (!tracker.bulkEntityActive)
			{
				txt.AppendLine("leave test: no spawn");
				yield break;
			}
			tracker.PlaceTestWalker(v, 500.0);
			yield return AimCamera(tracker, 30f);
			yield return WaitSeconds(1f);
			bool i1, l1;
			bool visibleBefore = Exposed(tracker, out i1, out l1);
			txt.AppendLine("leave test: at 500 m, independent inFrustum=" + i1 + " lineOfSight=" + l1 + " visible=" + visibleBefore);
			Shot(outDir, "moho_vis-leave-0.png");
			int despawnsBefore = tracker.MohoDespawnCount, exposedBefore = tracker.MohoDespawnExposed;
			tracker.LeaveBulkEntity("test: struck a vessel too large to kill");
			float t = 0f;
			bool lastVisible = visibleBefore;
			bool lastIn = i1, lastLos = l1;
			float maxDist = 0f;
			int shotIdx = 0;
			while (tracker.bulkEntityActive && t < 90f)
			{
				yield return null;
				t += Time.unscaledDeltaTime;
				if (tracker.bulkEntityActive)
				{
					bool a, b;
					lastVisible = Exposed(tracker, out a, out b);
					lastIn = a;
					lastLos = b;
					maxDist = Mathf.Max(maxDist, (float)((tracker.bulkEntityBody.position + tracker.bulkEntityRelativePosition) - v.GetWorldPos3D()).magnitude);
					if (t > shotIdx * 1.5f && shotIdx < 6)
					{
						shotIdx++;
						Shot(outDir, "moho_vis-leave-" + shotIdx + ".png");
					}
				}
			}
			txt.AppendLine("leave test: despawned=" + !tracker.bulkEntityActive + " after " + t.ToString("F1", C) + " s; last frame before despawn: independent inFrustum=" + lastIn + " lineOfSight=" + lastLos + " visible=" + lastVisible + "; distance reached " + maxDist.ToString("F0", C) + " m; game despawn count " + (tracker.MohoDespawnCount - despawnsBefore) + ", exposed at despawn " + (tracker.MohoDespawnExposed - exposedBefore));
			N("leave test: despawned after " + t.ToString("F1", C) + " s, last frame visible=" + lastVisible + ", game exposed at despawn " + (tracker.MohoDespawnExposed - exposedBefore));
		}
	}
}
