using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using KrakensKreations.Dread;
using UnityEngine;

// Unattended in-game footstep test, after work/rv/bench/RVBench. Does nothing unless GameData/Kraken's Kreations/kktest.flag exists.
// Builds its own sandbox save (saves/kk_test), runs the scenes, writes results to outDir, quits. Started by an external test script.
[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class KKTestBoot : MonoBehaviour
{
	private void Awake()
	{
		string flag = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "kktest.flag");
		if (File.Exists(flag))
		{
			EntityTracker.StartTestDriver(flag);
		}
		Destroy(this);
	}
}

public partial class EntityTracker
{
	private static bool dreadStepTestNotLanded;

	internal static void StartTestDriver(string flagPath)
	{
		GameObject go = new GameObject("KK_TestDriver");
		DontDestroyOnLoad(go);
		go.AddComponent<TestDriver>().flagPath = flagPath;
	}

	private sealed partial class TestDriver : MonoBehaviour
	{
		private const string SaveFolder = "kk_test";
		private const double MohoLat = 10.0, MohoLon = 30.0;
		internal string flagPath;
		private string outDir;
		private StringBuilder csv = new StringBuilder("scene,t,distance,speed,walking,chasing,landed,vacuum,gain,cutoff,pitch,step,peak,voicePlaying,status,menace,screenAttached,grade,huntState,beliefDist,beliefErr,trueDist,beliefOffset,huntSpeed,listening,sees,faceErrDeg,faceErrObjectDeg,heard,stalls,waypoints,headYaw,hasHead\n");
		private bool holding, holdLanded, holdSplashed;
		private float holdHeight = 32f;
		private bool sprintLoaded;
		private double holdLat, holdLon, holdAlt;
		private int steps;
		private int flightReadyCount;
		private bool failed;

		private void Start()
		{
			GameEvents.onFlightReady.Add(() => flightReadyCount++);
			StartCoroutine(Guard(Run()));
		}

		// a coroutine exception must still end the run with a FAILED file
		private IEnumerator Guard(IEnumerator body)
		{
			while (true)
			{
				object current;
				try
				{
					if (!body.MoveNext())
					{
						yield break;
					}
					current = body.Current;
				}
				catch (Exception ex)
				{
					Fail(ex.ToString());
					yield break;
				}
				if (current is IEnumerator nested)
				{
					yield return Guard(nested);
					if (failed)
					{
						yield break;
					}
				}
				else
				{
					yield return current;
				}
			}
		}

		private void Fail(string message)
		{
			failed = true;
			Debug.LogError("[KK Test] " + message);
			if (outDir != null)
			{
				File.WriteAllText(Path.Combine(outDir, "FAILED"), message);
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
			}
			Application.Quit();
		}

		private IEnumerator Run()
		{
			ConfigNode flag = ConfigNode.Load(flagPath);
			outDir = flag.GetValue("outDir");
			Directory.CreateDirectory(outDir);
			Application.runInBackground = true;
			yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.MAINMENU, 1800f, "main menu");
			yield return new WaitForSecondsRealtime(5f);
			string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves", SaveFolder);
			if (!File.Exists(Path.Combine(dir, "kerbin.sfs")))
			{
				yield return BuildSaves(dir, flag.GetValue("craft"));
			}
			if (flag.HasValue("scenes") && flag.GetValue("scenes").StartsWith("uw_"))
			{
				yield return UnderwaterScenes(flag.GetValue("scenes"));
				File.WriteAllText(Path.Combine(outDir, "DONE"), "ok");
				Application.Quit();
				yield break;
			}
			if (Environment.GetEnvironmentVariable("KK_TEST_NATURALTURN") == "0")
			{
				huntSettings.NaturalTurn = false;   // A/B runs against the old turning
			}
			if (Environment.GetEnvironmentVariable("KK_TEST_SCENES") == "dread_screen")
			{
				yield return DreadScreenScene("kerbin");
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				File.WriteAllText(Path.Combine(outDir, "DONE"), "ok");
				Application.Quit();
				yield break;
			}
			if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "sound_set")
			{
				yield return Scene("sound_moho", "kerbin");
				yield return Scene("sound_water", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_sprint_set")
			{
				yield return Scene("moho_sprint", "kerbin");
				yield return Scene("moho_run", "kerbin");
				yield return Scene("moho_leap", "kerbin");
				yield return Scene("moho_vis", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_model_only")
			{
				yield return Scene("moho_model", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_leap_only")
			{
				yield return Scene("moho_leap", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_run_leap_vis")
			{
				yield return Scene("moho_run", "kerbin");
				yield return Scene("moho_leap", "kerbin");
				yield return Scene("moho_vis", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "kerbin_pad_only")
			{
				yield return Scene("kerbin_pad", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_turn")
			{
				yield return Scene("moho_turn", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_creature")
			{
				yield return Scene("moho_creature", "kerbin");
			}
			else
			{
				yield return Scene("moho_landed", "kerbin");
				yield return Scene("moho_hover", "kerbin");
				yield return Scene("moho_decoy", "kerbin");
				yield return Scene("kerbin_pad", "kerbin");
			}
			File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
			File.WriteAllText(Path.Combine(outDir, "DONE"), "ok");
			Application.Quit();
		}

		private IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
		{
			float end = Time.realtimeSinceStartup + seconds;
			while (!condition())
			{
				if (Time.realtimeSinceStartup > end)
				{
					throw new TimeoutException("waiting for " + what);
				}
				yield return null;
			}
		}

		private static bool FlightUp()
		{
			return HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null;
		}

		// A new sandbox game, one stock craft on the pad, then one .sfs per place with that craft moved there.
		private IEnumerator BuildSaves(string dir, string craftRelative)
		{
			string craft = Path.Combine(KSPUtil.ApplicationRootPath, craftRelative);
			if (!File.Exists(craft))
			{
				throw new FileNotFoundException(craft);
			}
			Game game = GamePersistence.CreateNewGame(SaveFolder, Game.Modes.SANDBOX, new GameParameters(), "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
			HighLogic.CurrentGame = game;
			HighLogic.SaveFolder = SaveFolder;
			GamePersistence.SaveGame(game, "persistent", SaveFolder, SaveMode.OVERWRITE);
			game.Start();
			yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER, 600f, "space center");
			yield return new WaitForSecondsRealtime(5f);
			ConfigNode craftNode = ConfigNode.Load(craft);
			VesselCrewManifest manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craftNode);
			FlightDriver.StartWithNewLaunch(craft, "Squad/Flags/default", "LaunchPad", manifest);
			yield return WaitFor(FlightUp, 600f, "flight");
			yield return new WaitForSecondsRealtime(10f);
			Vessel v = FlightGlobals.ActiveVessel;
			GamePersistence.SaveGame("kk_base", SaveFolder, SaveMode.OVERWRITE);
			ConfigNode root = GamePersistence.LoadSFSFile("kk_base", SaveFolder);
			WriteScene(root, dir, "kerbin", v.mainBody, v.latitude, v.longitude, v.altitude);
		}

		private static void WriteScene(ConfigNode root, string dir, string file, CelestialBody body, double lat, double lon, double alt)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			ConfigNode copy = root.CreateCopy();
			ConfigNode game = copy.HasNode("GAME") ? copy.GetNode("GAME") : copy;
			ConfigNode state = game.GetNode("FLIGHTSTATE");
			ConfigNode vessel = state.GetNodes("VESSEL")[int.Parse(state.GetValue("activeVessel"), ci)];
			bool landed = file == "kerbin";
			vessel.SetValue("sit", landed ? "LANDED" : "ORBITING", true);
			vessel.SetValue("landed", landed ? "True" : "False", true);
			vessel.SetValue("splashed", "False", true);
			vessel.SetValue("landedAt", "", true);
			vessel.SetValue("displaylandedAt", "", true);
			vessel.SetValue("lat", lat.ToString("R", ci), true);
			vessel.SetValue("lon", lon.ToString("R", ci), true);
			vessel.SetValue("alt", alt.ToString("R", ci), true);
			vessel.SetValue("hgt", "-1", true);
			ConfigNode orbit = vessel.GetNode("ORBIT");
			orbit.SetValue("REF", FlightGlobals.Bodies.IndexOf(body).ToString(ci), true);
			orbit.SetValue("SMA", (body.Radius + alt).ToString("R", ci), true);
			orbit.SetValue("ECC", "0", true);
			copy.Save(Path.Combine(dir, file + ".sfs"));
		}

		// The craft stays packed and parked, as in RVBench: same place every run, and it cannot fall over.
		private void FixedUpdate() { Hold(); UwFixed(); }

		private void Update() { Hold(); }

		private void Hold()
		{
			if (!holding || !FlightGlobals.ready || FlightGlobals.ActiveVessel == null)
			{
				return;
			}
			Vessel v = FlightGlobals.ActiveVessel;
			OrbitPhysicsManager.HoldVesselUnpack(10);
			if (!v.packed)
			{
				v.GoOnRails();
			}
			v.Landed = holdLanded;
			v.Splashed = holdSplashed;
			v.situation = holdSplashed ? Vessel.Situations.SPLASHED : Vessel.Situations.LANDED;
			// a packed craft does not measure this itself, and the spawn and hover logic read it
			v.heightFromTerrain = holdLanded ? 2f : holdHeight;
			v.latitude = holdLat;
			v.longitude = holdLon;
			v.altitude = holdAlt;
		}

		private IEnumerator Scene(string id, string file)
		{
			Debug.Log("[KK Test] scene " + id);
			// the hover scene stays on the craft already standing on Moho: setting it down a second time crashed it through terrain still loading
			bool sprintId = id == "moho_sprint" || id == "moho_run" || id == "moho_leap" || id == "moho_vis" || id == "moho_model" || id == "moho_turn";
			bool reload = (id != "moho_hover" && id != "moho_decoy" && id != "moho_run" && id != "moho_leap" && id != "moho_vis" && id != "moho_model" && id != "moho_turn") || (sprintId && !sprintLoaded);
			if (sprintId)
			{
				sprintLoaded = true;
			}
			if (reload)
			{
			holding = false;
			Game game = GamePersistence.LoadGame(file, SaveFolder, true, false);
			if (game == null)
			{
				throw new InvalidOperationException("cannot load " + file);
			}
			HighLogic.SaveFolder = SaveFolder;
			HighLogic.CurrentGame = game;
			game.startScene = GameScenes.FLIGHT;
			holdLanded = true;
			// going flight to flight the old scene still looks ready, so wait for the new scene's own event
			int readyBefore = flightReadyCount;
			game.Start();
			yield return WaitFor(() => flightReadyCount > readyBefore && FlightUp(), 600f, "flight " + id);
			}
			Vessel v = FlightGlobals.ActiveVessel;
			string wanted = id == "kerbin_pad" || id == "sound_water" ? "Kerbin" : "Moho";
			if (wanted == "Moho" && reload)
			{
				// a really landed craft, set down the way the cheat menu's Set Position does
				yield return new WaitForSecondsRealtime(5f);
				CelestialBody moho = FlightGlobals.GetBodyByName("Moho");
				// under the sun, so that the view can be judged too
				double sunLon = moho.GetLongitude(Planetarium.fetch.Sun.position);
				FlightGlobals.fetch.SetVesselPosition(FlightGlobals.Bodies.IndexOf(moho), MohoLat, sunLon, 5.0, Vector3.zero, false, true);
				yield return WaitFor(() => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.mainBody == moho && FlightGlobals.ActiveVessel.Landed, 180f, "landing on Moho");
				v = FlightGlobals.ActiveVessel;
				yield return new WaitForSecondsRealtime(5f);
			}
			if (v.mainBody.bodyName != wanted)
			{
				throw new InvalidOperationException(id + ": craft is at " + v.mainBody.bodyName + ", expected " + wanted);
			}
			holdLat = v.latitude;
			holdLon = v.longitude;
			holdAlt = v.altitude;
			if (id == "sound_water")
			{
				holdAlt = -150.0;
			}
			holding = true;
			yield return new WaitForSecondsRealtime(5f);
			dreadStepTestNotLanded = false;
			yield return WaitFor(() => instance != null, 120f, "EntityTracker");
			EntityTracker tracker = instance;
			DreadAudioHost host = null;
			yield return WaitFor(() => { host = tracker.dreadAudioHost; return host != null && host.Steps != null; }, 120f, "audio host");
			int rate = AudioSettings.outputSampleRate;
			float[] record = new float[rate * 300];
			DreadStepVoice voice = host.Steps;
			voice.RecordPosition = 0;
			voice.Record = record;
			if (id == "moho_turn")
			{
				yield return TurnTest.Run(tracker, v, outDir, (sid, st, what) => Note(sid, st, what), (b) => { dreadStepTestNotLanded = b; });
				holdLanded = true;
				holdHeight = 32f;
				voice.Record = null;
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				yield break;
			}
			if (id == "sound_moho" || id == "sound_water")
			{
				voice.Record = null;
				yield return SoundTest.Run(id, tracker, v, outDir);
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				yield break;
			}
			if (id == "moho_sprint" || id == "moho_run" || id == "moho_leap" || id == "moho_vis" || id == "moho_model")
			{
				yield return SprintTest.Run(id, tracker, v, voice, outDir, (sid, st, what) => Note(sid, st, what), (b) =>
				{
					dreadStepTestNotLanded = b;
					if (id == "moho_leap")
					{
						// the craft is held 55 m up for this scene, after the monster has spawned
						holdLanded = !b;
						holdHeight = b ? 55f : 32f;
						Hold();
					}
				});
				holdLanded = true;
				holdHeight = 32f;
				voice.Record = null;
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				yield break;
			}

			double startDistance = id == "moho_landed" ? 2400.0 : (id == "moho_creature" ? 800.0 : (id == "moho_decoy" ? 700.0 : 400.0));
			Hold();
			if (id == "kerbin_pad")
			{
				tracker.SpawnGroundEntity(GroundVariant.Engineer, naturalSpawn: false);
			}
			else
			{
				tracker.SpawnBulkEntity();
			}
			yield return null;
			if (!(id == "kerbin_pad" ? tracker.groundEntityActive : tracker.bulkEntityActive))
			{
				throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus + " / " + tracker.groundEntityStatus + ", height " + v.heightFromTerrain);
			}
			tracker.PlaceTestWalker(v, startDistance);
			// flying is told to the footstep logic only: the parked craft stays put and the monster keeps its target
			dreadStepTestNotLanded = id == "moho_hover";
			float limit = id == "moho_landed" ? 240f : (id == "moho_creature" ? 26f : (id == "moho_decoy" ? 260f : 40f));
			CreatureProbe probe = id == "moho_creature" ? new CreatureProbe() : null;
			if (probe != null && tracker.bulkMoho != null)
			{
				FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
				FlightCamera.fetch.SetDistanceImmediate(230f);
				FlightCamera.fetch.camPitch = 0.1f;
				FlightCamera.fetch.camHdg = 0.9f;
			}
			float gaveAt = -1f;
			// the decoy scene: the noise comes from a place 1500 m beyond the monster, on the side away from the craft (2200 m from the craft), so the monster walks away from the craft to it and has to search there
			Vector3d decoyPos = Vector3d.zero;
			if (id == "moho_decoy")
			{
				Vector3d upv = (v.GetWorldPos3D() - v.mainBody.position).normalized;
				Vector3d northv = Vector3d.Exclude(upv, v.mainBody.transform.up).normalized;
				decoyPos = ProjectToGround(v.GetWorldPos3D() + northv * 2200.0, v.mainBody);
			}
			float t = 0f, sample = 0f, noiseAt = 1.5f;
			int noiseCount = 0;
			bool took = false, back = false, shot = false;
			while (t < limit)
			{
				yield return null;
				float dt = Time.unscaledDeltaTime;
				t += dt;
				// the craft is not silent: a landing impact and a 12 s burn, then nothing. Injected through the same path as the real noises.
				if (t > noiseAt && t < 14f)
				{
					noiseAt += 1f;
					Vector3d noisePos = id == "moho_decoy" ? decoyPos : v.GetWorldPos3D();
					if (noiseCount++ == 0)
					{
						tracker.HuntInjectNoise(HuntNoise.Impact, 1f, noisePos);
					}
					tracker.HuntInjectNoise(HuntNoise.Throttle, 0.9f, noisePos);
				}
				if (id == "moho_landed")
				{
					if (!took && t > 40f) { took = true; dreadStepTestNotLanded = true; Note(id, t, "takeoff"); }
					if (!back && t > 52f) { back = true; dreadStepTestNotLanded = false; Note(id, t, "landed"); }
				}
				DreadStepLogic logic = tracker.dreadStepLogic;
				bool fired = logic != null && logic.StepFired;
				if (fired) steps++;
				if (probe != null)
				{
					probe.Sample(tracker, t, outDir);
				}
				if (!shot && t > 20f)
				{
					shot = true;
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-20s.png"));
				}
				sample += dt;
				if (fired || sample >= 0.25f)
				{
					sample = 0f;
					Row(id, t, tracker, v, logic, voice, fired);
				}
				if (id == "moho_decoy")
				{
					EntityTracker.HuntInfo di = tracker.GetNearestHuntInfo();
					if (di.Valid && di.State == HuntState.GiveUp && gaveAt < 0f) { gaveAt = t; Note(id, t, "gave up"); }
					if (gaveAt >= 0f && t > gaveAt + 8f) { break; }
				}
				if (id == "moho_landed" && back && tracker.bulkEntityActive && tracker.dreadLastDistance >= 0.0 && tracker.dreadLastDistance < 40.0)
				{
					break;
				}
			}
			ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-end.png"));
			if (probe != null)
			{
				probe.Write(outDir);
			}
			// the monster goes away: the steps must fade by themselves
			tracker.DespawnBulkEntity();
			tracker.DespawnGroundEntity();
			Note(id, t, "despawned");
			float after = 0f;
			while (after < 6f)
			{
				yield return null;
				after += Time.unscaledDeltaTime;
				sample += Time.unscaledDeltaTime;
				if (sample >= 0.25f)
				{
					sample = 0f;
					Row(id, t + after, tracker, v, tracker.dreadStepLogic, voice, false);
				}
			}
			voice.Record = null;
			WriteWav(Path.Combine(outDir, id + ".wav"), record, voice.RecordPosition, rate);
			File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
			holding = false;
		}


		// ---- Dread screen scene: forced menace 0 / 0.5 / 1, a screenshot of each, the measured difference, per camera variant ----

		private static string CameraLine(Camera c)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(c.name).Append(" depth ").Append(c.depth.ToString("F1", CultureInfo.InvariantCulture)).Append(" enabled ").Append(c.enabled)
				.Append(" active ").Append(c.gameObject.activeInHierarchy).Append(" target ").Append(c.targetTexture != null ? c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height : "screen")
				.Append(" hdr ").Append(c.allowHDR).Append(" msaa ").Append(c.allowMSAA).Append(" path ").Append(c.actualRenderingPath).Append(" mask ").Append(c.cullingMask.ToString("X"))
				.Append(" clear ").Append(c.clearFlags).Append(" near ").Append(c.nearClipPlane.ToString("G4", CultureInfo.InvariantCulture)).Append(" far ").Append(c.farClipPlane.ToString("G4", CultureInfo.InvariantCulture));
			sb.Append(" cb[");
			foreach (UnityEngine.Rendering.CameraEvent ev in Enum.GetValues(typeof(UnityEngine.Rendering.CameraEvent)))
			{
				UnityEngine.Rendering.CommandBuffer[] cbs = c.GetCommandBuffers(ev);
				for (int i = 0; i < cbs.Length; i++)
				{
					sb.Append(ev).Append(':').Append(cbs[i].name).Append(' ');
				}
			}
			sb.Append("] comps[");
			foreach (Component k in c.GetComponents<Component>())
			{
				if (k != null && !(k is Transform) && !(k is Camera))
				{
					sb.Append(k.GetType().Name).Append(' ');
				}
			}
			return sb.Append(']').ToString();
		}

		private static void LogCameras(string why)
		{
			Debug.Log("[KK Test] cameras (" + why + ") count " + Camera.allCamerasCount + " main " + (Camera.main != null ? Camera.main.name : "none") + " ; " + SystemInfo.graphicsDeviceType + " ; colorspace " + QualitySettings.activeColorSpace + " ; " + Screen.width + "x" + Screen.height);
			FlightCamera fc = FlightCamera.fetch;
			if (fc != null)
			{
				Debug.Log("[KK Test] FlightCamera.mainCamera " + (fc.mainCamera != null ? fc.mainCamera.name : "null") + " ; fc.cameras " + (fc.cameras == null ? "null" : fc.cameras.Length.ToString()));
				if (fc.cameras != null)
				{
					for (int i = 0; i < fc.cameras.Length; i++)
					{
						Debug.Log("[KK Test]   fc.cameras[" + i + "] " + (fc.cameras[i] != null ? fc.cameras[i].name : "null"));
					}
				}
			}
			Camera[] all = Camera.allCameras;
			for (int i = 0; i < all.Length; i++)
			{
				Debug.Log("[KK Test]   " + CameraLine(all[i]));
			}
		}

		private static Texture2D GrabFrame()
		{
			return ScreenCapture.CaptureScreenshotAsTexture();
		}

		// mean absolute per-channel difference, 0..1, over the whole frame
		private static double MeanDiff(Texture2D a, Texture2D b)
		{
			Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
			int n = Math.Min(pa.Length, pb.Length);
			long sum = 0;
			for (int i = 0; i < n; i++)
			{
				sum += Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b);
			}
			return sum / (3.0 * n * 255.0);
		}

		private static double MeanLuma(Texture2D a)
		{
			Color32[] pa = a.GetPixels32();
			double sum = 0;
			for (int i = 0; i < pa.Length; i += 7)
			{
				sum += 0.299 * pa[i].r + 0.587 * pa[i].g + 0.114 * pa[i].b;
			}
			return sum / ((pa.Length + 6) / 7) / 255.0;
		}

		private IEnumerator LookStills(EntityTracker tracker, string pname, StringBuilder report)
		{
			float[] lookLevels = new float[] { 0f, 0.5f, 1f };
			Texture2D look0 = null;
			for (int li = 0; li < lookLevels.Length; li++)
			{
				tracker.dreadDebugMenace = lookLevels[li];
				yield return new WaitForSecondsRealtime(li == 0 ? 3f : 2.5f);
				string ptag = "look-" + pname + "-m" + ((int)(lookLevels[li] * 100f)).ToString("D3");
				ScreenCapture.CaptureScreenshot(Path.Combine(outDir, ptag + ".png"));
				yield return new WaitForEndOfFrame();
				Texture2D s = GrabFrame();
				if (li == 0) { look0 = s; }
				double d = li == 0 ? 0 : MeanDiff(look0, s);
				Debug.Log("[KK Test] " + ptag + " luma " + MeanLuma(s).ToString("F4", CultureInfo.InvariantCulture) + " diffVs0 " + d.ToString("F5", CultureInfo.InvariantCulture) + " method " + dreadScreenMethod + " attached " + (tracker.dreadCamera != null && tracker.dreadCamera.Attached) + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : ""));
				report.AppendLine("look/" + pname + "," + lookLevels[li].ToString("F2", CultureInfo.InvariantCulture) + "," + (tracker.dreadCamera != null && tracker.dreadCamera.Attached ? "1" : "0") + ",," + MeanLuma(s).ToString("F4", CultureInfo.InvariantCulture) + "," + d.ToString("F5", CultureInfo.InvariantCulture) + ",");
				if (li > 0) { UnityEngine.Object.Destroy(s); }
			}
			UnityEngine.Object.Destroy(look0);
			File.WriteAllText(Path.Combine(outDir, "dread_screen.csv"), report.ToString());
		}

		// each event at menace 0.8: a full-size still in the middle of it, then the same event as a clip with the camera turning slowly (so that a frozen picture shows)
		private IEnumerator DreadEvents(EntityTracker tracker, string pname, StringBuilder report)
		{
			DreadEventKind[] kinds = new DreadEventKind[] { DreadEventKind.TearEvent, DreadEventKind.Dropout, DreadEventKind.FeedFreeze };
			float[] seconds = new float[] { 0.5f, 1.5f, 1.2f };
			tracker.dreadDebugMenace = 0.8f;
			yield return new WaitForSecondsRealtime(1.5f);
			for (int k = 0; k < kinds.Length; k++)
			{
				string ename = kinds[k].ToString();
				tracker.DreadForce(kinds[k], seconds[k]);
				yield return new WaitForSecondsRealtime(seconds[k] * 0.45f);
				ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "event-" + pname + "-" + ename + ".png"));
				yield return new WaitForEndOfFrame();
				Debug.Log("[KK Test] event still " + pname + " " + ename + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : ""));
				yield return new WaitForSecondsRealtime(seconds[k] + 1.5f);
				yield return DreadClip(tracker, pname + "-" + ename, kinds[k], seconds[k]);
				yield return new WaitForSecondsRealtime(1f);
			}
			tracker.dreadDebugMenace = 1f;
			yield return new WaitForSecondsRealtime(2f);
			yield return DreadClip(tracker, pname + "-menace1", DreadEventKind.None, 0f);
		}

		private IEnumerator DreadClip(EntityTracker tracker, string name, DreadEventKind kind, float seconds)
		{
			System.Collections.Generic.List<Texture2D> frames = new System.Collections.Generic.List<Texture2D>();
			StringBuilder times = new StringBuilder();
			float start = Time.realtimeSinceStartup;
			float total = 0.5f + Mathf.Max(seconds, 1f) + 1.2f;
			bool fired = false, keep = false;
			while (Time.realtimeSinceStartup - start < total && frames.Count < 150)
			{
				yield return new WaitForEndOfFrame();
				float t = Time.realtimeSinceStartup - start;
				if (!fired && t > 0.5f && kind != DreadEventKind.None) { fired = true; tracker.DreadForce(kind, seconds); }
				FlightCamera.fetch.camHdg += 0.12f * Time.unscaledDeltaTime;
				keep = !keep;
				if (keep)
				{
					frames.Add(GrabFrame());
					times.AppendLine(t.ToString("F4", CultureInfo.InvariantCulture));
				}
			}
			int w = frames.Count > 0 ? frames[0].width / 2 : 0, h = frames.Count > 0 ? frames[0].height / 2 : 0;
			using (FileStream fs = new FileStream(Path.Combine(outDir, "clip-" + name + ".rgb"), FileMode.Create))
			{
				byte[] row = new byte[w * h * 3];
				foreach (Texture2D f in frames)
				{
					Color32[] px = f.GetPixels32();
					int fw = f.width;
					for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
					{
						Color32 c = px[(y * 2) * fw + x * 2];
						int o = ((h - 1 - y) * w + x) * 3;
						row[o] = c.r; row[o + 1] = c.g; row[o + 2] = c.b;
					}
					fs.Write(row, 0, row.Length);
					UnityEngine.Object.Destroy(f);
				}
			}
			File.WriteAllText(Path.Combine(outDir, "clip-" + name + ".txt"), w + " " + h + " " + frames.Count + "\n" + times);
			yield return null;
		}

		private IEnumerator DreadScreenScene(string file)
		{
			Debug.Log("[KK Test] scene dread_screen");
			holding = false;
			Game game = GamePersistence.LoadGame(file, SaveFolder, true, false);
			if (game == null)
			{
				throw new InvalidOperationException("cannot load " + file);
			}
			HighLogic.SaveFolder = SaveFolder;
			HighLogic.CurrentGame = game;
			game.startScene = GameScenes.FLIGHT;
			holdLanded = true;
			int readyBefore = flightReadyCount;
			game.Start();
			yield return WaitFor(() => flightReadyCount > readyBefore && FlightUp(), 600f, "flight dread_screen");
			yield return new WaitForSecondsRealtime(8f);
			Vessel v = FlightGlobals.ActiveVessel;
			holdLat = v.latitude; holdLon = v.longitude; holdAlt = v.altitude;
			holding = true;
			yield return WaitFor(() => instance != null, 120f, "EntityTracker");
			EntityTracker tracker = instance;
			yield return new WaitForSecondsRealtime(3f);
			LogCameras("before");
			StringBuilder report = new StringBuilder("variant,menace,attached,target,meanLuma,diffVs0,noiseFloor0\n");
			// variants: the shipped choice, the other method, and each camera that renders to the screen
			System.Collections.Generic.List<string> variants = new System.Collections.Generic.List<string>();
			variants.Add("default|shipped");
			if (Environment.GetEnvironmentVariable("KK_TEST_SWEEP") == "1") foreach (Camera c in Camera.allCameras)
			{
				if (c != null && c.enabled && c.gameObject.activeInHierarchy && c.targetTexture == null)
				{
					variants.Add(c.name + "|commandBuffer");
					variants.Add(c.name + "|onRenderImage");
				}
			}
			string savedMethod = dreadScreenMethod;
			foreach (string variant in variants)
			{
				string[] vp = variant.Split('|');
				tracker.dreadDebugCameraName = vp[0] == "default" ? null : vp[0];
				if (vp[1] != "shipped") dreadScreenMethod = vp[1];
				Texture2D baseline = null, baseline2 = null;
				double floor = 0;
				float[] levels = new float[] { 0f, 0f, 0.5f, 1f };
				for (int li = 0; li < levels.Length; li++)
				{
					tracker.dreadDebugMenace = levels[li];
					yield return new WaitForSecondsRealtime(li == 0 ? 3.5f : 2.5f);
					string tag = vp[0].Replace(' ', '_') + "-" + vp[1] + "-m" + ((int)(levels[li] * 100f)).ToString("D3") + (li == 1 ? "b" : "");
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "dread-" + tag + ".png"));
					yield return new WaitForEndOfFrame();
					Texture2D shot = GrabFrame();
					if (li == 0) { baseline = shot; }
					else if (li == 1) { baseline2 = shot; floor = MeanDiff(baseline, shot); }
					string target = tracker.dreadCamera != null && tracker.dreadCamera.Target != null ? tracker.dreadCamera.Target.name : "none";
					double diff = li == 0 ? 0 : MeanDiff(baseline, shot);
					report.AppendLine(vp[0] + "/" + vp[1] + "," + levels[li].ToString("F2", CultureInfo.InvariantCulture) + "," + (tracker.dreadCamera != null && tracker.dreadCamera.Attached ? "1" : "0") + "," + target + "," + MeanLuma(shot).ToString("F4", CultureInfo.InvariantCulture) + "," + diff.ToString("F5", CultureInfo.InvariantCulture) + "," + floor.ToString("F5", CultureInfo.InvariantCulture));
					if (li == 3 || (li == 2))
					{
						Debug.Log("[KK Test] " + variant + " m" + levels[li] + " attached " + (tracker.dreadCamera != null && tracker.dreadCamera.Attached) + " target " + target + " diff " + diff.ToString("F5", CultureInfo.InvariantCulture) + " floor " + floor.ToString("F5", CultureInfo.InvariantCulture) + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : "no dread camera"));
						if (li == 3) { LogCameras(variant + " m1"); }
					}
					if (li > 1) { UnityEngine.Object.Destroy(shot); }
				}
				UnityEngine.Object.Destroy(baseline);
				UnityEngine.Object.Destroy(baseline2);
				File.WriteAllText(Path.Combine(outDir, "dread_screen.csv"), report.ToString());
			}
			// ---- look phase: night, then night with a monster in view; shipped method only ----
			tracker.dreadDebugCameraName = null;
			string cfgPath = Environment.GetEnvironmentVariable("KK_TEST_CFG");
			if (!string.IsNullOrEmpty(cfgPath) && File.Exists(cfgPath))
			{
				foreach (string line in File.ReadAllLines(cfgPath))
				{
					string tl = line.Trim();
					if (tl.StartsWith("dreadScreenMethod"))
					{
						dreadScreenMethod = tl.Substring(tl.IndexOf('=') + 1).Trim();
						Debug.Log("[KK Test] shipped dreadScreenMethod from " + cfgPath + " = " + dreadScreenMethod);
					}
				}
			}
			// Kerbin by day: stills at 0 / 0.5 / 1, then the events, each as a still and as a clip (half size, every second frame, real timestamps)
			yield return LookStills(tracker, "dayShipped", report);
			yield return DreadEvents(tracker, "day", report);
			// night
			tracker.dreadDebugMenace = 0f;
			Planetarium.SetUniversalTime(Planetarium.GetUniversalTime() + 10800.0);
			yield return new WaitForSecondsRealtime(6f);
			yield return LookStills(tracker, "night", report);
			yield return DreadEvents(tracker, "night", report);
			// Moho, vacuum and black sky, the monster in view: is it still recognisable?
			tracker.dreadDebugMenace = 0f;
			holding = false;
			// from a fresh load, as the Moho scenes do: a craft held packed on the pad never reports landing after the move
			Game mohoGame = GamePersistence.LoadGame(file, SaveFolder, true, false);
			HighLogic.SaveFolder = SaveFolder;
			HighLogic.CurrentGame = mohoGame;
			mohoGame.startScene = GameScenes.FLIGHT;
			int readyMoho = flightReadyCount;
			mohoGame.Start();
			yield return WaitFor(() => flightReadyCount > readyMoho && FlightUp(), 600f, "flight dread_screen moho");
			yield return new WaitForSecondsRealtime(5f);
			yield return WaitFor(() => instance != null, 120f, "EntityTracker");
			tracker = instance;
			CelestialBody moho = FlightGlobals.GetBodyByName("Moho");
			double sunLon = moho.GetLongitude(Planetarium.fetch.Sun.position);
			FlightGlobals.fetch.SetVesselPosition(FlightGlobals.Bodies.IndexOf(moho), MohoLat, sunLon, 5.0, Vector3.zero, false, true);
			yield return WaitFor(() => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.mainBody == moho && FlightGlobals.ActiveVessel.Landed, 180f, "landing on Moho");
			v = FlightGlobals.ActiveVessel;
			yield return new WaitForSecondsRealtime(5f);
			holdLat = v.latitude; holdLon = v.longitude; holdAlt = v.altitude;
			holding = true;
			yield return new WaitForSecondsRealtime(3f);
			dreadMonsterNoise = false;
			tracker.SpawnBulkEntity();
			yield return null;
			if (tracker.bulkEntityActive)
			{
				tracker.PlaceTestWalker(v, 500.0);
				yield return new WaitForSecondsRealtime(1f);
				FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
				FlightCamera.fetch.SetDistanceImmediate(230f);
				FlightCamera.fetch.camPitch = 0.1f;
				FlightCamera.fetch.camHdg = 0.9f;
				yield return new WaitForSecondsRealtime(2f);
			}
			else
			{
				Debug.Log("[KK Test] dread_screen: no Moho monster: " + tracker.bulkEntityStatus);
			}
			yield return LookStills(tracker, "mohoMonster", report);
			yield return DreadEvents(tracker, "moho", report);
			// under water at Kerbin, craft held below the surface: the look of the water only, without a monster
			FlightCamera.fetch.SetTargetTransform(v.transform);
			tracker.dreadDebugMenace = 0f;
			holding = false;
			CelestialBody kerbin = FlightGlobals.GetBodyByName("Kerbin");
			// open ocean west of the space centre, with the sun over it
			double toNoon = kerbin.GetLongitude(Planetarium.fetch.Sun.position) + 100.0;
			toNoon -= 360.0 * Math.Floor(toNoon / 360.0);
			Planetarium.SetUniversalTime(Planetarium.GetUniversalTime() + toNoon / 360.0 * kerbin.rotationPeriod);
			FlightGlobals.fetch.SetVesselPosition(FlightGlobals.Bodies.IndexOf(kerbin), 0.0, -100.0, 5.0, Vector3.zero, false, true);
			yield return new WaitForSecondsRealtime(12f);
			v = FlightGlobals.ActiveVessel;
			double terrain = v.mainBody.TerrainAltitude(v.latitude, v.longitude, true);
			Debug.Log("[KK Test] underwater place: body " + v.mainBody.bodyName + " terrain " + terrain.ToString("F0", CultureInfo.InvariantCulture));
			holdLat = v.latitude; holdLon = v.longitude; holdAlt = terrain < -40.0 ? -25.0 : v.altitude;
			holding = true;
			yield return new WaitForSecondsRealtime(4f);
			FlightCamera.fetch.SetDistanceImmediate(14f);
			yield return LookStills(tracker, terrain < -40.0 ? "underwater" : "kerbinInland", report);
			File.WriteAllText(Path.Combine(outDir, "dread_screen.csv"), report.ToString());
			tracker.DespawnGroundEntity();
			dreadScreenMethod = savedMethod;
			tracker.dreadDebugCameraName = null;
			tracker.dreadDebugMenace = -1f;
			holding = false;
		}


		// moho_creature scene: screenshots with the camera on the creature, and the planted-foot drift measured on the live mesh.
		private sealed class CreatureProbe
		{
			private readonly Vector3[] start = new Vector3[4];
			private readonly bool[] was = new bool[4];
			private readonly float[] drift = new float[4];
			private int shots, frames, footfalls;
			private float nextShot = 3f, maxSpeed, hmax;
			private bool hooked;
			private Vector3 headLast;
			private float headTurnMax;
			private string note = "";
			private string shader = "?";

			internal void Sample(EntityTracker tracker, float t, string outDir)
			{
				MohoCreature c = tracker.bulkMoho;
				if (c == null)
				{
					note = "bulkMoho is null";
					return;
				}
				if (!hooked)
				{
					hooked = true;
					c.FootFall += (i, p, s) => footfalls++;
				}
				MohoSkinProcedural skin = c.Skin as MohoSkinProcedural;
				if (skin == null || skin.Object == null)
				{
					return;
				}
				shader = skin.ShaderName;
				frames++;
				for (int i = 0; i < 4; i++)
				{
					bool pl = c.IsPlanted(i);
					Vector3 tip = skin.TipWorld(i);
					if (pl && !was[i])
					{
						start[i] = tip;
						drift[i] = Mathf.Max(drift[i], 0f);
					}
					if (pl)
					{
						drift[i] = Mathf.Max(drift[i], (tip - start[i]).magnitude);
					}
					was[i] = pl;
				}
				maxSpeed = Mathf.Max(maxSpeed, c.CurrentSpeed);
				hmax = Mathf.Max(hmax, c.HeadPosition.magnitude > 0f ? Vector3.Dot(c.HeadPosition - c.Position, c.Up) : 0f);
				if (t >= nextShot && shots < 4)
				{
					shots++;
					nextShot += 4f;
					FlightCamera.fetch.camHdg += 1.6f;
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "creature-" + shots + ".png"));
				}
			}

			internal void Write(string outDir)
			{
				CultureInfo ci = CultureInfo.InvariantCulture;
				float worst = Mathf.Max(Mathf.Max(drift[0], drift[1]), Mathf.Max(drift[2], drift[3]));
				File.WriteAllText(Path.Combine(outDir, "creature.txt"), "shader " + shader + "\nframes " + frames + "\nfootfalls " + footfalls + "\nmax planted fingertip drift m (live mesh) " + worst.ToString("F5", ci) + "\nmax speed " + maxSpeed.ToString("F1", ci) + "\nmax head height above ground m " + hmax.ToString("F1", ci) + "\n" + note + "\n");
			}
		}

		private void Note(string id, float t, string what)
		{
			csv.AppendLine(id + "," + t.ToString("F2", CultureInfo.InvariantCulture) + ",,,,,,,,,,,,,NOTE " + what);
		}

		private void Row(string id, float t, EntityTracker tracker, Vessel v, DreadStepLogic logic, DreadStepVoice voice, bool fired)
		{
			CultureInfo c = CultureInfo.InvariantCulture;
			DreadAudioHost host = tracker.dreadAudioHost;
			string status = (tracker.bulkEntityStatus + " / " + tracker.groundEntityStatus + " / h " + v.heightFromTerrain.ToString("F1", c)).Replace(",", ";");
			EntityTracker.HuntInfo hi = tracker.GetNearestHuntInfo();
			csv.AppendLine(string.Join(",", new string[]
			{
				id, t.ToString("F2", c), tracker.dreadLastDistance.ToString("F1", c), tracker.dreadStepSpeed.ToString("F2", c), tracker.dreadStepWalker >= 0 ? "1" : "0",
				tracker.bulkEntityChasing || tracker.groundEntityChasing ? "1" : "0", v.Landed && !dreadStepTestNotLanded ? "1" : "0", v.mainBody.atmosphere ? "0" : "1",
				logic != null ? logic.Gain.ToString("F4", c) : "", logic != null ? logic.CutoffHz.ToString("F0", c) : "", logic != null ? logic.Pitch.ToString("F3", c) : "",
				fired ? "1" : "0", voice.TakePeak().ToString("F4", c), host != null && host.SourcePlaying ? "1" : "0", status,
				tracker.dreadLast.Menace.ToString("F3", c), tracker.dreadCamera != null && tracker.dreadCamera.Attached ? "1" : "0", tracker.dreadLast.Grade.ToString("F3", c),
				hi.Valid ? hi.State.ToString() : "", hi.Valid ? hi.BeliefDistance.ToString("F1", c) : "", hi.Valid ? hi.BeliefError.ToString("F1", c) : "", hi.Valid ? hi.TrueDistance.ToString("F1", c) : "", hi.Valid ? hi.BeliefOffset.ToString("F1", c) : "",
				hi.Valid ? hi.Speed.ToString("F2", c) : "", hi.Listening ? "1" : "0", hi.Sees ? "1" : "0",  hi.Valid ? hi.FaceErrorDeg.ToString("F1", c) : "", hi.Valid ? hi.FaceErrorObjectDeg.ToString("F1", c) : "", hi.Valid ? hi.Heard.ToString() : "", hi.Valid ? hi.Stalls.ToString() : "", hi.Valid ? hi.Waypoints.ToString() : "", hi.Valid ? hi.HeadYawDeg.ToString("F1", c) : "", hi.HasHead ? "1" : "0"
			}));
		}

		private static void WriteWav(string path, float[] mono, int frames, int rate)
		{
			using (BinaryWriter w = new BinaryWriter(File.Create(path)))
			{
				w.Write(Encoding.ASCII.GetBytes("RIFF"));
				w.Write(36 + frames * 2);
				w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
				w.Write(16);
				w.Write((short)1);
				w.Write((short)1);
				w.Write(rate);
				w.Write(rate * 2);
				w.Write((short)2);
				w.Write((short)16);
				w.Write(Encoding.ASCII.GetBytes("data"));
				w.Write(frames * 2);
				for (int i = 0; i < frames; i++)
				{
					w.Write((short)Mathf.Clamp(mono[i] * 32767f, -32768f, 32767f));
				}
			}
		}
	}

	private void PlaceTestWalkerDir(Vessel vessel, double metres, Vector3d direction)
	{
		CelestialBody body = vessel.mainBody;
		Vector3d from = vessel.GetWorldPos3D();
		Vector3d ground = ProjectToGround(from + direction * metres, body);
		if (groundEntityActive && groundEntityBody == body)
		{
			groundEntityRelativePosition = ground - body.position;
			if (groundEntityObject != null)
			{
				groundEntityObject.transform.position = ground;
			}
		}
	}

	private void PlaceTestWalker(Vessel vessel, double metres)
	{
		CelestialBody body = vessel.mainBody;
		Vector3d from = vessel.GetWorldPos3D();
		Vector3d up = (from - body.position).normalized;
		Vector3d north = Vector3d.Exclude(up, body.transform.up).normalized;
		Vector3d ground = ProjectToGround(from + north * metres, body);
		if (bulkEntityActive && bulkEntityBody == body)
		{
			bulkEntityRelativePosition = ground - body.position;
			if (bulkEntityObject != null)
			{
				bulkEntityObject.transform.position = ground;
			}
		}
		if (groundEntityActive && groundEntityBody == body)
		{
			groundEntityRelativePosition = ground - body.position;
			if (groundEntityObject != null)
			{
				groundEntityObject.transform.position = ground;
			}
		}
	}
}
