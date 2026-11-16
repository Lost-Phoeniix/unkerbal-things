using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnkerbalThings.Dread;
using UnityEngine;

// In-game scenes of the leviathan, run with underwater/ksptest/run-ksp-test-tv.ps1 (True Volumetrics, Scatterer, EVE). The flag's "scenes" line picks them.
// uw_light: how dark Laythe's ocean really is at each depth, by day and by night, measured on the final picture.
public partial class EntityTracker
{
	private sealed partial class TestDriver
	{
		private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		// the deepest water near the wanted longitude, so the craft is far above the floor
		private static void FindDeepWater(CelestialBody body, double wantLon, out double lat, out double lon, out double floor)
		{
			lat = 0.0;
			lon = wantLon;
			floor = 0.0;
			for (double la = -40.0; la <= 40.0; la += 4.0)
			{
				for (double lo = -30.0; lo <= 30.0; lo += 4.0)
				{
					double h = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(la, wantLon + lo)) - body.Radius;
					if (h < floor)
					{
						floor = h;
						lat = la;
						lon = wantLon + lo;
					}
				}
			}
		}

		private IEnumerator UwGoTo(CelestialBody body, double lat, double lon, double depth)
		{
			holding = false;
			uwHoldOn = false;
			uwWantVel = Vector3d.zero;
			if (FlightGlobals.ActiveVessel.mainBody != body || Math.Abs(FlightGlobals.ActiveVessel.latitude - lat) + Math.Abs(FlightGlobals.ActiveVessel.longitude - lon) > 0.01)
			{
				FlightGlobals.fetch.SetVesselPosition(FlightGlobals.Bodies.IndexOf(body), lat, lon, 20.0, Vector3.zero, true, false);
				yield return WaitFor(() => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.mainBody == body, 180f, "arrival at " + body.bodyName);
				// caught at once: a free fall onto the water can break the craft before the test has begun
				uwRampAlt = FlightGlobals.ActiveVessel.altitude;
				uwTargetAlt = uwRampAlt;
				uwIntegral = 0.0;
				uwHoldOn = true;
				yield return new WaitForSecondsRealtime(4f);
			}
			UwHold(lat, lon, depth);
			yield return UwReach();
		}

		// The craft stays awake and physical, as a real submarine: a controller holds it at the wanted depth with forces on all parts.
		// (Frozen on rails the water went black below 30 m and the craft counted as crashed through the terrain.)
		private bool uwHoldOn;
		// the held craft is driven at this velocity through the water (world, level); zero: it is held still
		private Vector3d uwWantVel;
		private double uwTargetAlt, uwRampAlt, uwIntegral;

		private void UwHold(double lat, double lon, double depth)
		{
			Vessel v = FlightGlobals.ActiveVessel;
			if (!uwHoldOn)
			{
				uwRampAlt = v.altitude;
				uwIntegral = 0.0;
			}
			uwTargetAlt = -depth;
			uwHoldOn = true;
		}

		private IEnumerator UwReach()
		{
			float end = Time.realtimeSinceStartup + 240f;
			float nextLog = 0f;
			while (Time.realtimeSinceStartup < end && Math.Abs(FlightGlobals.ActiveVessel.altitude - uwTargetAlt) > 3.0)
			{
				Vessel w = FlightGlobals.ActiveVessel;
				if (Time.realtimeSinceStartup >= nextLog)
				{
					nextLog = Time.realtimeSinceStartup + 10f;
					Debug.Log("[UT Test] descending: altitude " + w.altitude.ToString("F1", Inv) + " ramp " + uwRampAlt.ToString("F1", Inv) + " integral " + uwIntegral.ToString("F1", Inv) + " packed " + w.packed + " situation " + w.situation + " speed " + w.srfSpeed.ToString("F1", Inv) + " vertical " + w.verticalSpeed.ToString("F1", Inv) + " parts " + w.parts.Count + " warp " + TimeWarp.CurrentRate + " timeScale " + Time.timeScale);
				}
				if (w.packed)
				{
					w.GoOffRails();
				}
				yield return null;
			}
			Debug.Log("[UT Test] depth " + FlightGlobals.ActiveVessel.altitude.ToString("F1", Inv) + " wanted " + uwTargetAlt.ToString("F0", Inv) + " speed " + FlightGlobals.ActiveVessel.srfSpeed.ToString("F1", Inv));
			yield return new WaitForSecondsRealtime(1.5f);
		}

		private void UwFixed()
		{
			if (!uwHoldOn || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.packed)
			{
				return;
			}
			Vessel v = FlightGlobals.ActiveVessel;
			CheatOptions.NoCrashDamage = true;
			CheatOptions.UnbreakableJoints = true;
			float dt = Time.fixedDeltaTime;
			// through the surface slowly, a hard entry breaks the craft; the ramp waits for the craft
			double rate = v.altitude > -15.0 ? 1.5 : 8.0;
			if (Math.Abs(uwRampAlt - v.altitude) < 12.0)
			{
				uwRampAlt = uwRampAlt < uwTargetAlt ? Math.Min(uwTargetAlt, uwRampAlt + rate * dt) : Math.Max(uwTargetAlt, uwRampAlt - rate * dt);
			}
			Vector3d up = v.upAxis;
			double error = uwRampAlt - v.altitude;
			uwIntegral = Math.Max(-40.0, Math.Min(40.0, uwIntegral + error * 0.3 * dt));
			Vector3d vel = v.srf_velocity;
			Vector3d flat = Vector3d.Exclude(up, vel);
			Vector3d a = up * (1.0 * error + uwIntegral - 2.5 * Vector3d.Dot(vel, up)) - (flat - Vector3d.Exclude(up, uwWantVel)) * (uwWantVel.sqrMagnitude > 0.0 ? 4.0 : 1.5) - FlightGlobals.getGeeForceAtPosition(v.GetWorldPos3D());
			Vector3 acc = a;
			foreach (Part p in v.parts)
			{
				if (p.Rigidbody != null)
				{
					p.Rigidbody.AddForce(acc, ForceMode.Acceleration);
				}
			}
		}

		private static void UwClearDialogs()
		{
			PopupDialog.ClearPopUps();
			foreach (PopupDialog d in FindObjectsOfType<PopupDialog>())
			{
				Destroy(d.gameObject);
			}
			foreach (Transform t in FindObjectsOfType<Transform>())
			{
				if (t.name.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Kopernicus", StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponent<RectTransform>() != null)
				{
					Debug.Log("[UT Test] closing dialog " + t.name);
					Destroy(t.gameObject);
				}
			}
		}

		private static void UwCamera(float pitch, float hdg, float distance)
		{
			FlightCamera fc = FlightCamera.fetch;
			fc.SetDistanceImmediate(distance);
			fc.camPitch = pitch;
			fc.camHdg = hdg;
		}

		private static float Luminance(Texture2D t)
		{
			Color32[] px = t.GetPixels32();
			double sum = 0.0;
			int n = 0;
			for (int i = 0; i < px.Length; i += 37)
			{
				sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
				n++;
			}
			return (float)(sum / Math.Max(1, n));
		}

		private IEnumerator UwShot(string name, StringBuilder log, string row)
		{
			UwClearDialogs();
			yield return new WaitForEndOfFrame();
			Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
			float lum = Luminance(t);
			Destroy(t);
			ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
			log.Append(row).Append(',').Append(lum.ToString("F2", Inv)).Append('\n');
			Debug.Log("[UT Test] " + name + " luminance " + lum.ToString("F2", Inv));
		}

		private IEnumerator UwLoadOnKerbin()
		{
			holding = false;
			uwHoldOn = false;
			// only for the arrival and the holding forces: the monster's kill destroys parts directly and does not ask this
			CheatOptions.NoCrashDamage = true;
			CheatOptions.UnbreakableJoints = true;
			Game game = GamePersistence.LoadGame("kerbin", SaveFolder, true, false);
			if (game == null)
			{
				throw new InvalidOperationException("cannot load kerbin");
			}
			HighLogic.SaveFolder = SaveFolder;
			HighLogic.CurrentGame = game;
			game.startScene = GameScenes.FLIGHT;
			int readyBefore = flightReadyCount;
			game.Start();
			yield return WaitFor(() => flightReadyCount > readyBefore && FlightUp(), 900f, "flight");
			yield return new WaitForSecondsRealtime(8f);
			PopupDialog.ClearPopUps();
		}

		private IEnumerator UwLightScene()
		{
			Debug.Log("[UT Test] scene uw_light");
			yield return UwLoadOnKerbin();
			CelestialBody laythe = FlightGlobals.GetBodyByName("Laythe");
			if (laythe == null || laythe.pqsController == null)
			{
				throw new InvalidOperationException("no Laythe");
			}
			StringBuilder log = new StringBuilder("sun,depth,view,floor,altitude,luminance\n");
			double[] depths = { 5, 10, 20, 30, 40, 50, 60, 80, 100, 150, 200, 300, 450, 600 };
			string[] sunName = { "day", "night" };
			for (int s = 0; s < 2; s++)
			{
				double sunLon = laythe.GetLongitude(Planetarium.fetch.Sun.position);
				double lat, lon, floor;
				FindDeepWater(laythe, s == 0 ? sunLon : sunLon + 180.0, out lat, out lon, out floor);
				Debug.Log("[UT Test] uw_light " + sunName[s] + " lat " + lat.ToString("F1", Inv) + " lon " + lon.ToString("F1", Inv) + " floor " + floor.ToString("F0", Inv));
				yield return UwGoTo(laythe, lat, lon, depths[0]);
				foreach (double d in depths)
				{
					if (d > -floor - 20.0)
					{
						continue;
					}
					UwHold(lat, lon, d);
					yield return UwReach();
					UwClearDialogs();
					UwCamera(0.05f, 0.6f, 25f);
					yield return new WaitForSecondsRealtime(2f);
					string head = sunName[s] + "," + d.ToString("F0", Inv) + ",";
					string tail = "," + floor.ToString("F0", Inv) + "," + FlightGlobals.ActiveVessel.altitude.ToString("F1", Inv);
					yield return UwShot("uw-" + sunName[s] + "-" + d.ToString("F0", Inv) + "-side", log, head + "side" + tail);
					UwCamera(-0.9f, 0.6f, 25f);
					yield return new WaitForSecondsRealtime(1.5f);
					yield return UwShot("uw-" + sunName[s] + "-" + d.ToString("F0", Inv) + "-up", log, head + "up" + tail);
				}
				File.WriteAllText(Path.Combine(outDir, "uw-light.csv"), log.ToString());
			}
		}

		private IEnumerator UwAim(Vector3 world)
		{
			FlightCamera fc = FlightCamera.fetch;
			Camera cam = fc.mainCamera;
			Vessel v = FlightGlobals.ActiveVessel;
			Vector3 up = ((Vector3)(v.GetWorldPos3D() - v.mainBody.position)).normalized;
			float sign = 1f, last = 999f;
			for (int i = 0; i < 12; i++)
			{
				yield return null;
				yield return null;
				float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward, up), Vector3.ProjectOnPlane(world - cam.transform.position, up), up);
				if (Mathf.Abs(ang) < 3f)
				{
					break;
				}
				if (Mathf.Abs(ang) > Mathf.Abs(last) + 1f)
				{
					sign = -sign;
				}
				last = ang;
				fc.camHdg += sign * ang * Mathf.Deg2Rad;
			}
			Vector3 to = (world - (Vector3)v.GetWorldPos3D()).normalized;
			fc.camPitch = Mathf.Clamp(-Mathf.Asin(Vector3.Dot(to, up)), -1.2f, 1.2f);
			yield return null;
		}

		private IEnumerator UwArrive(bool day, double depth, StringBuilder log)
		{
			yield return UwLoadOnKerbin();
			CelestialBody laythe = FlightGlobals.GetBodyByName("Laythe");
			double sunLon = laythe.GetLongitude(Planetarium.fetch.Sun.position), lat, lon, floor;
			FindDeepWater(laythe, day ? sunLon : sunLon + 180.0, out lat, out lon, out floor);
			log.Append("# ").Append(day ? "day" : "night").Append(" lat ").Append(lat.ToString("F1", Inv)).Append(" lon ").Append(lon.ToString("F1", Inv)).Append(" floor ").Append(floor.ToString("F0", Inv)).Append('\n');
			yield return UwGoTo(laythe, lat, lon, depth);
			if (FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.state == Vessel.State.DEAD || FlightGlobals.ActiveVessel.parts.Count < 20 || FlightGlobals.ActiveVessel.altitude > -depth + 15.0)
			{
				throw new InvalidOperationException("the craft did not reach " + depth + " m whole: parts " + (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.parts.Count : 0) + " altitude " + (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.altitude.ToString("F1", Inv) : "none"));
			}
			UwClearDialogs();
			UwCamera(0.05f, 0.6f, 22f);
			yield return WaitFor(() => instance != null, 120f, "EntityTracker");
			yield return new WaitForSecondsRealtime(4f);
		}

		// A whole encounter as a player would have it: a loud craft with its lights on, the monster comes, passes and finally kills. The clock of the kill is the result.
		private IEnumerator UwEncounterScene(bool day, string id)
		{
			Debug.Log("[UT Test] scene " + id);
			StringBuilder log = new StringBuilder();
			yield return UwArrive(day, bloopMinDepthMetres + 40.0, log);
			log.Append("t,state,distance,clock,killTime,feints,killOpen,beliefErr,monsterDepth,cue,vesselState\n");
			Vessel v = FlightGlobals.ActiveVessel;
			v.ActionGroups.SetGroup(KSPActionGroup.Light, true);
			EntityTracker tracker = instance;
			tracker.SpawnBloopEntity(debugForce: true);
			BloopBrain brain = tracker.BloopBrainForTest;
			if (brain == null)
			{
				throw new InvalidOperationException(id + ": no brain, status " + tracker.bloopEntityStatus);
			}
			float start = Time.time, nextNoise = 0f, nextRow = 0f, nextShot = 60f, seenTime = -1f;
			BloopCue seen = BloopCue.None;
			int shots = 0;
			while (Time.time - start < 1100f)
			{
				float t = Time.time - start;
				v = FlightGlobals.ActiveVessel;
				bool gone = tracker.BloopBrainForTest == null;
				if (v == null || v.state == Vessel.State.DEAD || v.parts == null || v.parts.Count == 0 || gone)
				{
					log.Append(t.ToString("F1", Inv)).Append(",END,brainGone=").Append(gone).Append(",vessel=").Append(v == null ? "null" : v.state + " parts " + v.parts.Count).Append(",clock=").Append(brain.Clock.ToString("F1", Inv)).Append(",caught=").Append(brain.Caught).Append('\n');
					break;
				}
				if (t >= nextNoise)
				{
					tracker.HuntInjectNoise(HuntNoise.Throttle, 0.7f, v.GetWorldPos3D());
					nextNoise = t + UnityEngine.Random.Range(5f, 12f);
				}
				if (tracker.bloopLastCueTime > seenTime && tracker.bloopLastCue != BloopCue.None)
				{
					seen = tracker.bloopLastCue;
					seenTime = tracker.bloopLastCueTime;
					if (shots < 40)
					{
						shots++;
						yield return UwAim(tracker.bloopEntityObject.transform.position);
						yield return new WaitForSecondsRealtime(0.8f);
						ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-" + t.ToString("000", Inv) + "-" + seen + ".png"));
					}
				}
				if (t >= nextShot && tracker.bloopEntityObject != null)
				{
					nextShot = t + 45f;
					yield return UwAim(tracker.bloopEntityObject.transform.position);
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-" + t.ToString("000", Inv) + "-state-" + brain.State + ".png"));
				}
				if (t >= nextRow && tracker.bloopFrame != null)
				{
					nextRow = t + 1f;
					double dist = (tracker.bloopFrame.ToPlane(v.GetWorldPos3D()) - brain.Position).Length;
					log.Append(t.ToString("F1", Inv)).Append(',').Append(brain.State).Append(',').Append(dist.ToString("F0", Inv)).Append(',').Append(brain.Clock.ToString("F1", Inv)).Append(',').Append(brain.KillTime.ToString("F0", Inv)).Append(',').Append(brain.Feints).Append(',').Append(brain.KillOpen).Append(',').Append(brain.BeliefError.ToString("F0", Inv)).Append(',').Append((-brain.Position.Z).ToString("F0", Inv)).Append(',').Append(seen).Append(',').Append(v.state).Append('\n');
					seen = BloopCue.None;
					File.WriteAllText(Path.Combine(outDir, id + ".csv"), log.ToString());
				}
				yield return null;
			}
			yield return new WaitForSecondsRealtime(6f);
			ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-end.png"));
			yield return new WaitForSecondsRealtime(2f);
			log.Append("# end: vessel ").Append(FlightGlobals.ActiveVessel == null ? "gone" : FlightGlobals.ActiveVessel.state + " parts " + FlightGlobals.ActiveVessel.parts.Count).Append('\n');
			File.WriteAllText(Path.Combine(outDir, id + ".csv"), log.ToString());
		}

		// A craft under way and what the fish does about it: at rest, straight, zigzag, in a circle, turning back, fast. The craft is driven by the holding forces.
		// Then the fish from close by in each of its poses (swim, both turns, jaws, sprint), put in front of the camera.
		private IEnumerator UwMoveScene(bool day, string id)
		{
			Debug.Log("[UT Test] scene " + id);
			StringBuilder log = new StringBuilder();
			yield return UwArrive(day, bloopMinDepthMetres + 40.0, log);
			Vessel v = FlightGlobals.ActiveVessel;
			v.ActionGroups.SetGroup(KSPActionGroup.Light, true);
			EntityTracker tracker = instance;
			tracker.SpawnBloopEntity(debugForce: true);
			BloopBrain brain = tracker.BloopBrainForTest;
			if (brain == null)
			{
				throw new InvalidOperationException(id + ": no brain, status " + tracker.bloopEntityStatus);
			}
			// it starts near, so the minutes go into the figures and not into the approach
			BloopVec craft0 = tracker.bloopFrame.ToPlane(v.GetWorldPos3D());
			brain.Position = craft0 + new BloopVec(260, 60, -35);
			log.Append("t,phase,state,distance,below,craftSpeed,fishSpeed,turnDegPerSec,preySpeed,mouth,inspections,feints,cue,animation\n");
			string[] names = { "rest", "straight10", "zigzag10", "circle12", "back10", "straight16" };
			float[] ends = { 45f, 105f, 165f, 225f, 285f, 345f };
			float start = Time.time, nextNoise = 0f, nextRow = 0f, nextShot = 20f, seenTime = -1f, passShot = -100f;
			BloopCue seen = BloopCue.None;
			int[] nearRows = new int[names.Length], allRows = new int[names.Length];
			while (Time.time - start < ends[ends.Length - 1])
			{
				float t = Time.time - start;
				v = FlightGlobals.ActiveVessel;
				if (v == null || v.state == Vessel.State.DEAD || tracker.BloopBrainForTest == null || tracker.bloopFrame == null)
				{
					log.Append(t.ToString("F1", Inv)).Append(",END,craft or fish gone\n");
					break;
				}
				// the scene is about how it moves: no kill, so every phase and the poses are reached (its rises stay feints)
				brain.KillTime = 1e9;
				int phase = 0;
				while (phase < ends.Length - 1 && t >= ends[phase]) phase++;
				float pt = t - (phase == 0 ? 0f : ends[phase - 1]);
				BloopVec want = new BloopVec(0, 0, 0);
				if (phase == 1) want = new BloopVec(10, 0, 0);
				if (phase == 2) { double a = ((int)(pt / 12f) % 2 == 0 ? 50 : -50) * Math.PI / 180; want = new BloopVec(Math.Cos(a) * 10, Math.Sin(a) * 10, 0); }
				if (phase == 3) { double a = pt * 12.0 / 120.0; want = new BloopVec(Math.Cos(a) * 12, Math.Sin(a) * 12, 0); }
				if (phase == 4) want = new BloopVec((int)(pt / 25f) % 2 == 0 ? 10 : -10, 0, 0);
				if (phase == 5) want = new BloopVec(16, 0, 0);
				uwWantVel = (Vector3d)tracker.bloopFrame.DirToWorld(want);
				if (t >= nextNoise)
				{
					tracker.HuntInjectNoise(HuntNoise.Throttle, 0.7f, v.GetWorldPos3D());
					nextNoise = t + UnityEngine.Random.Range(5f, 12f);
				}
				if (tracker.bloopLastCueTime > seenTime && tracker.bloopLastCue != BloopCue.None)
				{
					seen = tracker.bloopLastCue;
					seenTime = tracker.bloopLastCueTime;
				}
				BloopVec rel = brain.Position - tracker.bloopFrame.ToPlane(v.GetWorldPos3D());
				bool pass = brain.State == BloopState.Inspect && rel.Length < 55 && t - passShot > 4f;
				if ((t >= nextShot || pass) && tracker.bloopEntityObject != null)
				{
					if (pass) passShot = t; else nextShot = t + 15f;
					FlightCamera.fetch.camPitch = Mathf.Clamp((float)-Math.Asin(Math.Max(-1, Math.Min(1, rel.Z / Math.Max(1, rel.Length)))), -1.2f, 1.2f);
					yield return UwAim(tracker.bloopEntityObject.transform.position);
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-" + t.ToString("000", Inv) + "-" + names[phase] + "-" + brain.State + (pass ? "-pass" : "") + ".png"));
				}
				if (t >= nextRow)
				{
					nextRow = t + 0.5f;
					allRows[phase]++;
					if (rel.Length < 150) nearRows[phase]++;
					log.Append(t.ToString("F1", Inv)).Append(',').Append(names[phase]).Append(',').Append(brain.State).Append(',').Append(rel.Length.ToString("F0", Inv)).Append(',').Append((-rel.Z).ToString("F0", Inv)).Append(',')
						.Append(v.srfSpeed.ToString("F1", Inv)).Append(',').Append(brain.Speed.ToString("F1", Inv)).Append(',').Append((brain.TurnRate * 180 / Math.PI).ToString("F1", Inv)).Append(',').Append(brain.PreyVelocity.Length.ToString("F1", Inv)).Append(',')
						.Append(brain.MouthOpen.ToString("F2", Inv)).Append(',').Append(brain.Inspections).Append(',').Append(brain.Feints).Append(',').Append(seen).Append(',').Append(tracker.BloopAnimationForTest.Replace(',', ';')).Append('\n');
					seen = BloopCue.None;
					File.WriteAllText(Path.Combine(outDir, id + ".csv"), log.ToString());
				}
				yield return null;
			}
			uwWantVel = Vector3d.zero;
			StringBuilder sum = new StringBuilder();
			for (int i = 0; i < names.Length; i++)
			{
				sum.Append(names[i]).Append(": within 150 m in ").Append(nearRows[i]).Append(" of ").Append(allRows[i]).Append(" samples\n");
			}
			sum.Append("inspections ").Append(brain.Inspections).Append(", feints ").Append(brain.Feints).Append(", clock ").Append(brain.Clock.ToString("F0", Inv)).Append(" s\n");
			yield return new WaitForSecondsRealtime(6f);

			// the poses from close by: the fish in front of the camera, side on and head on
			if (tracker.BloopBrainForTest != null && FlightGlobals.ActiveVessel != null)
			{
				UwCamera(0.05f, 0.6f, 22f);
				yield return new WaitForSecondsRealtime(1f);
				string[] poseNames = { "swim", "turn-right", "turn-left", "jaws", "sprint-jaws", "turn-right-jaws" };
				Vector3[] poses = { new Vector3(18f, 0f, 0f), new Vector3(18f, 0.6f, 0f), new Vector3(18f, -0.6f, 0f), new Vector3(18f, 0f, 1f), new Vector3(46f, 0f, 1f), new Vector3(18f, 0.6f, 1f) };
				for (int i = 0; i < poses.Length; i++)
				{
					foreach (bool front in new[] { false, true })
					{
						Camera cam = FlightCamera.fetch.mainCamera;
						Vector3 at = cam.transform.position + cam.transform.forward * (front ? 75f : 42f);
						tracker.bloopTestPose = poses[i];
						// side on it swims across the picture, head on it comes at the camera; put back once so the pose has settled when the picture is taken
						Vector3 dir = front ? -cam.transform.forward : cam.transform.right;
						BloopVec h = tracker.bloopFrame.ToPlane(at + dir * 10f) - tracker.bloopFrame.ToPlane(at);
						for (int k = 0; k < 2; k++)
						{
							brain.Position = tracker.bloopFrame.ToPlane(at - dir * (front ? 0f : 12f));
							brain.Heading = new BloopVec(h.X, h.Y, 0).Unit;
							yield return new WaitForSecondsRealtime(0.7f);
						}
						sum.Append("pose ").Append(poseNames[i]).Append(front ? " front: " : " side: ").Append(tracker.BloopAnimationForTest).Append('\n');
						ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-pose-" + poseNames[i] + (front ? "-front" : "-side") + ".png"));
						yield return new WaitForSecondsRealtime(0.4f);
					}
				}
				tracker.bloopTestPose = null;
			}
			File.WriteAllText(Path.Combine(outDir, id + ".txt"), sum.ToString());
			File.WriteAllText(Path.Combine(outDir, id + ".csv"), log.ToString());
			tracker.DespawnBloopEntity();
		}

		// Every reveal once, on demand, with the monster put where the cue needs it: reference pictures, and the model from close by.
		private IEnumerator UwCuesScene(bool day, string id, double cueDepth)
		{
			Debug.Log("[UT Test] scene " + id);
			StringBuilder log = new StringBuilder();
			yield return UwArrive(day, cueDepth, log);
			Vessel v = FlightGlobals.ActiveVessel;
			EntityTracker tracker = instance;
			tracker.SpawnBloopEntity(debugForce: true);
			BloopBrain brain = tracker.BloopBrainForTest;
			if (brain == null)
			{
				throw new InvalidOperationException(id + ": no brain, status " + tracker.bloopEntityStatus);
			}
			BloopVec craft = tracker.bloopFrame.ToPlane(v.GetWorldPos3D());
			v.ActionGroups.SetGroup(KSPActionGroup.Light, true);
			foreach (int metres in new[] { 46, 70, 110 })
			{
				brain.Position = craft + new BloopVec(metres, 25, 10);
				brain.Heading = new BloopVec(0, 1, 0);
				yield return new WaitForSecondsRealtime(1.5f);
				yield return UwAim(tracker.bloopEntityObject.transform.position);
				yield return new WaitForSecondsRealtime(0.5f);
				ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-model-" + metres + "m.png"));
				yield return new WaitForSecondsRealtime(0.5f);
			}
			BloopCue[] all = { BloopCue.WaterDrift, BloopCue.Dimming, BloopCue.Silhouette, BloopCue.Bubbles, BloopCue.Echo, BloopCue.ConeGlimpse, BloopCue.EyeGlint, BloopCue.LureGlow, BloopCue.Silence, BloopCue.Sediment, BloopCue.Scream };
			foreach (BloopCue cue in all)
			{
				BloopVec at = cue == BloopCue.Silhouette || cue == BloopCue.Dimming ? new BloopVec(90, 40, 70) : cue == BloopCue.Bubbles || cue == BloopCue.Scream ? new BloopVec(90, 40, -70) : cue == BloopCue.ConeGlimpse ? new BloopVec(50, 25, 10) : cue == BloopCue.LureGlow ? new BloopVec(170, 60, 5) : new BloopVec(240, 90, -20);
				brain.Position = craft + at;
				tracker.bloopFx.Lure(0f, 0f);
				UwCamera(cue == BloopCue.Silhouette ? -0.85f : 0.05f, 0.6f, 22f);
				yield return new WaitForSecondsRealtime(0.4f);
				yield return UwAim(tracker.bloopEntityObject.transform.position);
				if (cue == BloopCue.Scream) tracker.LeviathanScream();
				tracker.BloopApplyCue(cue, 0.9f, v, craft);
				yield return new WaitForSecondsRealtime(cue == BloopCue.WaterDrift || cue == BloopCue.Bubbles || cue == BloopCue.Sediment ? 4f : cue == BloopCue.LureGlow ? 2.5f : 1.3f);
				ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-cue-" + cue + ".png"));
				yield return new WaitForSecondsRealtime(1f);
				log.Append(cue).Append(" shot, last cue ").Append(tracker.bloopLastCue).Append('\n');
			}
			File.WriteAllText(Path.Combine(outDir, id + ".txt"), log.ToString());
			tracker.DespawnBloopEntity();
		}

		// The leviathan straight in front of the camera at three distances, side on, with and without the craft's lights: can it be seen at all.
		private IEnumerator UwLookScene(bool day, string id)
		{
			Debug.Log("[UT Test] scene " + id);
			StringBuilder log = new StringBuilder();
			yield return UwArrive(day, bloopMinDepthMetres + 40.0, log);
			Vessel v = FlightGlobals.ActiveVessel;
			EntityTracker tracker = instance;
			tracker.SpawnBloopEntity(debugForce: true);
			BloopBrain brain = tracker.BloopBrainForTest;
			if (brain == null)
			{
				throw new InvalidOperationException(id + ": no brain, status " + tracker.bloopEntityStatus);
			}
			UwCamera(0.05f, 0.6f, 22f);
			yield return new WaitForSecondsRealtime(1f);
			foreach (bool lights in new[] { false, true })
			{
				v.ActionGroups.SetGroup(KSPActionGroup.Light, lights);
				foreach (int metres in new[] { 40, 80, 150 })
				{
					Camera cam = FlightCamera.fetch.mainCamera;
					Vector3 at = cam.transform.position + cam.transform.forward * metres;
					brain.Position = tracker.bloopFrame.ToPlane(at);
					BloopVec side = tracker.bloopFrame.ToPlane(at + cam.transform.right * 10f) - brain.Position;
					brain.Heading = new BloopVec(side.X, side.Y, 0).Unit;
					yield return new WaitForSecondsRealtime(0.35f);
					log.Append(tracker.LeviathanDiagnostics()).Append('\n');
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-" + metres + "m" + (lights ? "-lights" : "") + ".png"));
					yield return new WaitForSecondsRealtime(0.5f);
				}
			}
			File.WriteAllText(Path.Combine(outDir, id + ".txt"), log.ToString());
			tracker.DespawnBloopEntity();
		}

		private IEnumerator UnderwaterScenes(string scenes)
		{
			foreach (string raw in scenes.Split(','))
			{
				string id = raw.Trim();
				if (id == "uw_light") yield return UwLightScene();
				if (id == "uw_cues_day") yield return UwCuesScene(true, id, 620.0);
				if (id == "uw_cues_night") yield return UwCuesScene(false, id, 620.0);
				if (id == "uw_hunt_day") yield return UwEncounterScene(true, id);
				if (id == "uw_hunt_night") yield return UwEncounterScene(false, id);
				if (id == "uw_look_day") yield return UwLookScene(true, id);
				if (id == "uw_look_night") yield return UwLookScene(false, id);
				if (id == "uw_move_day") yield return UwMoveScene(true, id);
				if (id == "uw_move_night") yield return UwMoveScene(false, id);
			}
		}
	}
}
