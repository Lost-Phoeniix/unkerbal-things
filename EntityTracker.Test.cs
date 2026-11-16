using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnkerbalThings.Dread;
using UnityEngine;

// Unattended in-game footstep test, after work/rv/bench/RVBench. Does nothing unless GameData/UnkerbalThings/kktest.flag exists.
// Builds its own sandbox save (saves/kk_test), runs the scenes, writes results to outDir, quits. Started by an external test script.
[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class KKTestBoot : MonoBehaviour
{
	private void Awake()
	{
		string flag = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "UnkerbalThings", "kktest.flag");
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
		// kk_test for the stock Kerbal 1; any other test craft (run-ksp-test.ps1 -Craft) gets its own folder, since the saves are built once
		private static string SaveFolder = "kk_test";
		private const double MohoLat = 10.0, MohoLon = 30.0;
		internal string flagPath;
		private string outDir;
		private StringBuilder csv = new StringBuilder("scene,t,distance,speed,walking,chasing,landed,vacuum,gain,cutoff,pitch,step,peak,voicePlaying,status,menace,screenAttached,grade,huntState,beliefDist,beliefErr,trueDist,beliefOffset,huntSpeed,listening,sees,faceErrDeg,faceErrObjectDeg,heard,stalls,waypoints,headYaw,hasHead\n");
		private bool holding, holdLanded, holdSplashed;
		private float holdHeight = 32f;
		private float holdFlyAlt;   // > 0: the craft is really held this high above the terrain (on rails as "landed", which keeps it exactly at lat/lon/alt)
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
			Debug.LogError("[UT Test] " + message);
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
			if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "parts_show")
			{
				yield return PartsShow();   // EntityTracker.TestParts.cs: the science part models as the game loaded them, at the main menu
				if (!failed)
				{
					File.WriteAllText(Path.Combine(outDir, "DONE"), "ok");
					Application.Quit();
				}
				yield break;
			}
			string craftFlag = flag.HasValue("craft") ? flag.GetValue("craft") : "";
			SaveFolder = craftFlag.Length == 0 || craftFlag.EndsWith("Kerbal 1.craft", StringComparison.Ordinal) ? "kk_test"
				: "kk_test_" + System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(craftFlag), "[^A-Za-z0-9]", "_");
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
				yield return Scene("moho_creature", "kerbin");
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
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_ambush")
			{
				yield return Scene("moho_ambush", "kerbin");
				yield return Scene("moho_ambush_hover", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_ambush_all")
			{
				yield return Scene("moho_ambush", "kerbin");
				yield return Scene("moho_ambush_hover", "kerbin");
				yield return Scene("moho_ambush_fly_slow", "kerbin");
				yield return Scene("moho_ambush_fly_toward", "kerbin");
				yield return Scene("moho_ambush_fly_away", "kerbin");
				yield return Scene("moho_ambush_high", "kerbin");
				yield return Scene("moho_ambush_flee", "kerbin");
			}
			else if ((Environment.GetEnvironmentVariable("KK_TEST_ONLY") ?? "").StartsWith("scene:", StringComparison.Ordinal))
			{
				// one scene by id, e.g. KK_TEST_ONLY=scene:moho_ambush_flee (one KSP launch per scene avoids the harness's flaky reloads)
				yield return Scene(Environment.GetEnvironmentVariable("KK_TEST_ONLY").Substring(6), "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_ambush_moving")
			{
				yield return Scene("moho_ambush_fly_toward", "kerbin");
				yield return Scene("moho_ambush_fly_away", "kerbin");
				yield return Scene("moho_ambush_high", "kerbin");
				yield return Scene("moho_ambush_flee", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_ambush_air")
			{
				yield return Scene("moho_ambush_hover", "kerbin");
				yield return Scene("moho_ambush_fly_slow", "kerbin");
				yield return Scene("moho_ambush_fly_toward", "kerbin");
				yield return Scene("moho_ambush_fly_away", "kerbin");
				yield return Scene("moho_ambush_high", "kerbin");
				yield return Scene("moho_ambush_flee", "kerbin");
			}
			else if (Environment.GetEnvironmentVariable("KK_TEST_ONLY") == "moho_ambush_ground")
			{
				yield return Scene("moho_ambush", "kerbin");
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
			v.heightFromTerrain = holdFlyAlt > 0f ? holdFlyAlt : (holdLanded ? 2f : holdHeight);
			v.latitude = holdLat;
			v.longitude = holdLon;
			v.altitude = holdAlt;
			if (holdUpright && v.mainBody != null)
			{
				// the craft's long axis along the local vertical (a tall craft set down by Set Position topples otherwise)
				Vector3 up = (v.transform.position - v.mainBody.position).normalized;
				// only when it has drifted: setting it every frame made the craft (and every camera on it) jitter
				if (Vector3.Angle(v.transform.up, up) > 0.5f)
					v.SetRotation(Quaternion.FromToRotation(v.transform.up, up) * v.transform.rotation);
			}
		}

		private IEnumerator Scene(string id, string file)
		{
			Debug.Log("[UT Test] scene " + id);
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
				// "_dusk" scenes (TUFX sweep): 78 deg from the sub-solar point, the sun about 12 deg above the horizon
				if (id.EndsWith("_dusk", StringComparison.Ordinal)) sunLon += 78.0;
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
			if (id == "moho_worm_search")
			{
				voice.Record = null;
				yield return WormSearchScene(id, tracker, v);
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				holding = false;
				holdFlyAlt = 0f;
				holdLanded = true;
				holdHeight = 32f;
				yield break;
			}
			if (id == "moho_view")
			{
				voice.Record = null;
				yield return ViewLockScene(id, tracker, v);
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				holding = false;
				holdFlyAlt = 0f;
				holdLanded = true;
				holdHeight = 32f;
				yield break;
			}
			if (id.StartsWith("tufx_", StringComparison.Ordinal))
			{
				voice.Record = null;
				yield return TufxScene(id, tracker, v);   // EntityTracker.TestTufx.cs
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				holding = false;
				holdFlyAlt = 0f;
				holdLanded = true;
				holdHeight = 32f;
				yield break;
			}
			if (id.StartsWith("moho_ambush", StringComparison.Ordinal))
			{
				voice.Record = null;
				yield return AmbushScene(id, tracker, v);
				File.WriteAllText(Path.Combine(outDir, "steps.csv"), csv.ToString());
				holding = false;
				holdFlyAlt = 0f;
				holdLanded = true;
				holdHeight = 32f;
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

			double startDistance = id == "moho_landed" ? 2400.0 : (id == "moho_creature" ? 1200.0 : (id == "moho_decoy" ? 700.0 : 400.0));
			Hold();
			// kerbin_pad: no creature on Kerbin any more (the Mimic is in legacy/), the scene measures the quiet baseline
			if (id != "kerbin_pad")
			{
				tracker.SpawnBulkEntity();
			}
			yield return null;
			if (id != "kerbin_pad" && !tracker.bulkEntityActive)
			{
				throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus + ", height " + v.heightFromTerrain);
			}
			tracker.PlaceTestWalker(v, startDistance);
			// flying is told to the footstep logic only: the parked craft stays put and the monster keeps its target
			dreadStepTestNotLanded = id == "moho_hover";
			float limit = id == "moho_landed" ? 240f : (id == "moho_creature" ? 32f : (id == "moho_decoy" ? 260f : 40f));
			CreatureProbe probe = id == "moho_creature" ? new CreatureProbe() : null;
			if (probe != null && tracker.bulkMoho != null)
			{
				FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
				FlightCamera.fetch.SetDistanceImmediate(22f);
				FlightCamera.fetch.camPitch = 0.88f;
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
			float t = 0f, sample = 0f, noiseAt = id == "moho_creature" ? 7f : 1.5f;   // moho_creature: a quiet start, so the heap is seen at rest
			int noiseCount = 0;
			bool took = false, back = false, shot = false, released = false;
			while (t < limit)
			{
				yield return null;
				float dt = Time.unscaledDeltaTime;
				t += dt;
				// the craft is not silent: a landing impact and a 12 s burn, then nothing. Injected through the same path as the real noises.
				if (t > noiseAt && t < (id == "moho_creature" ? 19.5f : 14f))
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
					// test only: the tracker's own "standing still" state holds the creature for the rest shots, then it is let go to hunt
					if (t < 6.5f) { tracker.bulkEntityStandingDown = true; tracker.bulkEntityRetreating = false; tracker.bulkWithdrawalTimer = 5f; }
					else if (!released) { released = true; tracker.bulkEntityStandingDown = false; tracker.bulkEntityRetreating = false; tracker.bulkWithdrawalTimer = 0f; Note(id, t, "creature released"); }
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


		// ---- Worm ambush scenes (gpu worm body): the creature starts 350 m away, hears the craft, approaches partly burrowed,
		// burrows about 20 m out, stays silent, erupts under the craft. moho_ambush: landed craft; moho_ambush_hover: held 12 m up.
		// Writes ambush.txt (phase log, swarm counts, cost), ambush.csv, and a screenshot of every phase.
		private static int CountOf(string s, string what)
		{
			int n = 0, i = 0;
			while (s != null && (i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
			return n;
		}

		// The worm hunt's search: the craft hovers 30 m up (held on rails, so it also counts as landed:
		// a search that comes within 100 m strikes, which is recorded too); the swarm 2.5 km off is told it felt the craft land
		// (EntityTracker.WormTestSearch): serpent run, sink, then the rolling heap searching at 8 m/s. One row a second for 200 s.
		private IEnumerator WormSearchScene(string id, EntityTracker tracker, Vessel v)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			StringBuilder sum = new StringBuilder("scene " + id + "\n");
			StringBuilder rows = new StringBuilder("t,state,body,serpent,speed,scale,craft_m,visible,vanished,dug_in,farthest_m,trail_m,ambush\n");
			tracker.WormAmbushLog = "";
			// flicker check without any swarm on the planet (compare with burst-outside, taken with the swarm spawned)
			yield return new WaitForSecondsRealtime(3f);
			yield return Burst(id + "-burst-noswarm", 12, () => { FlightCamera.fetch.camHdg += 45f * Mathf.Deg2Rad * Time.unscaledDeltaTime; });
			tracker.SpawnBulkEntity();
			yield return null;
			if (!tracker.bulkEntityActive) throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus);
			tracker.PlaceTestWalker(v, 2500.0);
			holdLanded = true; holdFlyAlt = 30f; holdHeight = 30f;
			holdAlt = v.mainBody.TerrainAltitude(holdLat, holdLon) + 30f;
			dreadStepTestNotLanded = true;
			Hold();
			holdLanded = true; holdFlyAlt = 30f; holdHeight = 30f;
			holdAlt = v.mainBody.TerrainAltitude(holdLat, holdLon) + 30f;
			dreadStepTestNotLanded = true;
			Hold();
			MohoWormGpuSkin g = tracker.bulkWormSwarm as MohoWormGpuSkin;
			if (g == null) { File.WriteAllText(Path.Combine(outDir, id + "-search.txt"), "FAIL: no gpu worm body (" + (tracker.bulkWormSwarm != null ? tracker.bulkWormSwarm.BodyName : "none") + ")\n"); yield break; }
			yield return WaitFor(() => tracker.WormBrain != null, 60f, "worm brain");
			yield return new WaitForSecondsRealtime(5f);   // the heap settles
			tracker.WormTestSearch();
			long pop0 = g.PoppedInSight, dug0 = g.DugIn;
			float searchFrom = -1f, maxTrail = 0f, minScale = 1f, speedSum = 0f, trailSum = 0f, maxFar = 0f;
			int searchRows = 0;
			string strike = "none";
			bool shot5 = false, shot30 = false, shot60 = false;
			for (int t = 1; t <= 200; t++)
			{
				yield return new WaitForSecondsRealtime(1f);
				WormHunt b = tracker.WormBrain;
				Vector3d pos = tracker.bulkEntityBody.position + tracker.bulkEntityRelativePosition;
				Vessel av = FlightGlobals.ActiveVessel;
				double craftM = av != null ? (av.GetWorldPos3D() - pos).magnitude : -1.0;
				float trail = g.HeapTrail;
				string state = b != null ? b.State.ToString() : "-";
				rows.Append(t).Append(',').Append(state).Append(',').Append(b != null ? b.Body.ToString() : "-").Append(',')
					.Append(g.SerpentActive ? g.Body.State.ToString() : "-").Append(',')
					.Append(b != null ? b.Speed.ToString("F2", ci) : "").Append(',').Append(b != null ? b.HeapSpeedScale.ToString("F2", ci) : "").Append(',')
					.Append(craftM.ToString("F0", ci)).Append(',').Append(g.VisibleWorms).Append(',').Append(g.PoppedInSight - pop0).Append(',').Append(g.DugIn - dug0).Append(',')
					.Append(g.MaxPointRadius.ToString("F0", ci)).Append(',').Append(trail.ToString("F1", ci)).Append(',').Append(tracker.WormAmbushPhaseName).Append('\n');
				if (tracker.WormAmbushActive && strike == "none") strike = "started at t=" + t + " s";
				if (b != null && b.State == WormState.Search && !g.SerpentActive && !tracker.WormAmbushActive)
				{
					if (searchFrom < 0f) searchFrom = t;
					float since = t - searchFrom;
					if (since >= 6f)   // after the heap has come up
					{
						maxTrail = Mathf.Max(maxTrail, trail); minScale = Mathf.Min(minScale, b.HeapSpeedScale);
						speedSum += b.Speed; trailSum += trail; maxFar = Mathf.Max(maxFar, g.MaxPointRadius); searchRows++;
					}
					if (!shot5 && since >= 5f) { shot5 = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-search05s.png")); }
					if (!shot30 && since >= 30f) { shot30 = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-search30s.png")); }
					if (!shot60 && since >= 60f) { shot60 = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-search60s.png")); }
				}
				if (strike != "none" && !tracker.WormAmbushActive && t > 20) { strike += ", ended at t=" + t + " s"; break; }
			}
			sum.Append("search began at t=").Append(searchFrom.ToString("F0", ci)).Append(" s, rows measured ").Append(searchRows).Append('\n');
			if (searchRows > 0)
				sum.Append("search: mean speed ").Append((speedSum / searchRows).ToString("F2", ci)).Append(" m/s, mean trail ").Append((trailSum / searchRows).ToString("F1", ci))
					.Append(" m, worst trail ").Append(maxTrail.ToString("F1", ci)).Append(" m, slowest factor x").Append(minScale.ToString("F2", ci)).Append(", farthest worm ").Append(maxFar.ToString("F0", ci)).Append(" m\n");
			sum.Append("vanished in sight ").Append(g.PoppedInSight - pop0).Append(", dug in ").Append(g.DugIn - dug0).Append('\n');
			sum.Append("strike ").Append(strike).Append('\n');
			sum.Append("ambush log:\n").Append(tracker.WormAmbushLog);
			File.WriteAllText(Path.Combine(outDir, id + "-search.csv"), rows.ToString());
			File.WriteAllText(Path.Combine(outDir, id + "-search.txt"), sum.ToString());
			Debug.Log("[UT Test] " + id + ": " + sum.ToString().Replace('\n', ' '));
		}

		// The view near the swarm (EntityTracker.ViewLock.cs): the craft (test craft with a Swarm Imager and a ground radar)
		// stands on Moho; the swarm starts about 1.6 km off and is told it felt the craft land, so it comes in and strikes. Logs the view
		// state twice a second (view.csv, view.txt) and takes screenshots: outside, fading, the feed coming on, the head panning
		// (colour fringes), the feed at 500 m and 200 m, the blackout during the eruption, and after.
		private bool holdUpright;

		/// <summary>`frames` consecutive frames as screenshots (one per rendered frame), calling `each` before every frame.</summary>
		private IEnumerator Burst(string name, int frames, Action each)
		{
			// also logs, per frame, how far the craft moved relative to the planet and what the terrain thinks its target speed is:
			// a held craft that jumps between frames makes KSP's terrain drop and restore detail (patches toggling)
			StringBuilder log = new StringBuilder("frame,dt,craft_move_m,craft_alt,pqs_target_speed,pqs_max_level\n");
			Vessel v0 = FlightGlobals.ActiveVessel;
			Vector3d prev = v0 != null ? v0.mainBody.transform.InverseTransformPoint(v0.transform.position) : Vector3d.zero;
			CultureInfo ci = CultureInfo.InvariantCulture;
			for (int i = 0; i < frames; i++)
			{
				if (each != null) each();
				ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + "-" + i.ToString("00") + ".png"));
				yield return null;
				Vessel v = FlightGlobals.ActiveVessel;
				if (v == null || v.mainBody == null) continue;
				Vector3d here = v.mainBody.transform.InverseTransformPoint(v.transform.position);
				PQS pqs = v.mainBody.pqsController;
				log.Append(i).Append(',').Append(Time.unscaledDeltaTime.ToString("F4", ci)).Append(',').Append((here - prev).magnitude.ToString("F4", ci)).Append(',')
					.Append(FlightGlobals.getAltitudeAtPos(v.transform.position, v.mainBody).ToString("F2", ci)).Append(',')
					.Append(pqs != null ? pqs.targetSpeed.ToString("F2", ci) : "").Append(',').Append(pqs != null ? pqs.maxLevelAtCurrentTgtSpeed.ToString(ci) : "").Append('\n');
				prev = here;
			}
			File.WriteAllText(Path.Combine(outDir, name + ".csv"), log.ToString());
		}

		private IEnumerator ViewLockScene(string id, EntityTracker tracker, Vessel v)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			// the craft upright (now, so the camera never looks into the ground) and, once the creature has
			// spawned (it only spawns for a landed craft), 30 m above the ground so the camera on top sees the approach
			holdUpright = true;
			Hold();
			StringBuilder rows = new StringBuilder("t,dist,mode,locked,fade,cam_taken,wear,blackout,cam_mode,phase,head_az,head_el,fps\n");
			StringBuilder sum = new StringBuilder("scene " + id + "\n");
			ModuleUTImagerFeed imager = FindImager(v);
			sum.Append("imager on craft: ").Append(imager != null ? "yes (" + imager.part.partInfo.title + ")" : "NO").Append('\n');
			ModuleUTGroundRadar radar = null;
			foreach (Part p in v.parts) { radar = p.FindModuleImplementing<ModuleUTGroundRadar>(); if (radar != null) break; }
			sum.Append("radar on craft: ").Append(radar != null ? "yes" : "NO").Append('\n');
			sum.Append("dread screen shader: ").Append(DreadCamera.Failure ?? "ok").Append('\n');
			tracker.WormAmbushLog = "";
			// flicker check without any swarm on the planet (compare with burst-outside, taken with the swarm spawned)
			yield return new WaitForSecondsRealtime(3f);
			yield return Burst(id + "-burst-noswarm", 12, () => { FlightCamera.fetch.camHdg += 45f * Mathf.Deg2Rad * Time.unscaledDeltaTime; });
			tracker.SpawnBulkEntity();
			yield return null;
			if (!tracker.bulkEntityActive) throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus);
			tracker.PlaceTestWalker(v, 2500.0);
			holdLanded = true; holdFlyAlt = 30f; holdHeight = 30f;
			holdAlt = v.mainBody.TerrainAltitude(holdLat, holdLon) + 30f;
			dreadStepTestNotLanded = true;
			Hold();
			yield return WaitFor(() => tracker.WormBrain != null, 60f, "worm brain");
			yield return new WaitForSecondsRealtime(3f);
			ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-outside.png"));
			// flicker check (terrain flickers in polygons while the camera turns): 12 consecutive frames while the
			// ordinary flight camera turns at 45 deg/s, before anything of the view lock is active
			yield return Burst(id + "-burst-outside", 12, () => { FlightCamera.fetch.camHdg += 45f * Mathf.Deg2Rad * Time.unscaledDeltaTime; });
			tracker.WormTestSearch();
			bool shotBehind = false, shotDown = false;
			bool shotFade = false, shotFeed = false, shotPan = false, shot500 = false, shot200 = false, shotBlack = false, shotAfter = false;
			float panAt = -1f, endAt = -1f, feedAt = -1f, blackAt = -1f;
			string firstFeed = "never", strike = "none";
			for (float t = 0f; t < 260f; t += 0.5f)
			{
				yield return new WaitForSecondsRealtime(0.5f);
				ViewLock vl = tracker.viewLock;
				double d = tracker.viewSwarmDistance;
				string mode = vl != null ? vl.Mode.ToString() : "-";
				string camMode = CameraManager.Instance != null ? CameraManager.Instance.currentCameraMode.ToString() : "-";
				rows.Append(t.ToString("F1", ci)).Append(',').Append(double.IsInfinity(d) ? "inf" : d.ToString("F0", ci)).Append(',').Append(mode).Append(',')
					.Append(vl != null && vl.Locked ? 1 : 0).Append(',').Append(vl != null ? vl.OutsideFade.ToString("F2", ci) : "").Append(',')
					.Append(tracker.viewCamTaken ? 1 : 0).Append(',').Append(tracker.viewFeedWear.ToString("F2", ci)).Append(',')
					.Append(vl != null ? vl.Blackout.ToString("F2", ci) : "").Append(',').Append(camMode).Append(',').Append(tracker.WormAmbushPhaseName).Append(',')
					.Append(imager != null ? imager.Pan.Value.ToString("F1", ci) : "").Append(',').Append(imager != null ? imager.Tilt.Value.ToString("F1", ci) : "").Append(',')
					.Append((1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime)).ToString("F0", ci)).Append('\n');
				if (vl == null) continue;
				if (!shotFade && vl.Mode == ViewMode.Outside && vl.OutsideFade >= 0.35f && vl.OutsideFade <= 0.7f)
				{ shotFade = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-fade.png")); }
				if (tracker.viewCamTaken && feedAt < 0f) { feedAt = t; firstFeed = "t=" + t.ToString("F1", ci) + " s at " + d.ToString("F0", ci) + " m"; }
				if (!shotFeed && feedAt >= 0f && t - feedAt >= 1f) { shotFeed = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed.png")); }
				if (shotFeed && panAt < 0f && imager != null)
				{
					// turn the head toward the swarm side and keep it turning: the picture moves, the colour fields split
					panAt = t;
					imager.Steer(40f, -4f, 0f);
				}
				if (!shotPan && panAt >= 0f && t - panAt >= 1.5f)
				{
					shotPan = true;
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed-panning.png"));
					yield return null;
					// the same flicker check through the feed while the head keeps turning
					ModuleUTImagerFeed im = imager;
					yield return Burst(id + "-burst-feed", 12, () => { if (im != null) im.Steer(2f, 0f, 0f); });
				}
				// the head turned right round (where the sensor mast used to stand), then aimed fully down: no part of the imager in view
				if (shotPan && !shotBehind && imager != null)
				{
					if (Mathf.Abs(imager.Pan.Wrapped) < 170f) imager.Steer(25f, 0f, 0f);
					else { shotBehind = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed-behind.png")); }
				}
				if (shotBehind && !shotDown && imager != null)
				{
					if (imager.Tilt.Value > imager.Tilt.Min + 0.5f) imager.Steer(0f, -25f, 0f);
					else { shotDown = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed-down.png")); }
				}
				if (!shot500 && tracker.viewCamTaken && d <= 500.0 && !tracker.WormAmbushActive) { shot500 = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed-500m.png")); }
				if (!shot200 && tracker.viewCamTaken && d <= 200.0 && !tracker.WormAmbushActive) { shot200 = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-feed-200m.png")); }
				if (tracker.WormAmbushActive && strike == "none") strike = "started at t=" + t.ToString("F1", ci) + " s";
				if (!shotBlack && vl.Blackout >= 0.95f) { shotBlack = true; blackAt = t; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-blackout.png")); }
				if (strike != "none" && !tracker.WormAmbushActive && endAt < 0f) { endAt = t; strike += ", ended at t=" + t.ToString("F1", ci) + " s"; }
				if (!shotAfter && endAt >= 0f && t - endAt >= 4f) { shotAfter = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-after.png")); break; }
			}
			sum.Append("feed came on: ").Append(firstFeed).Append('\n');
			sum.Append("strike: ").Append(strike).Append('\n');
			sum.Append("blackout reached: ").Append(blackAt >= 0f ? "t=" + blackAt.ToString("F1", ci) + " s" : "never").Append('\n');
			sum.Append("shots: fade ").Append(shotFade).Append(", feed ").Append(shotFeed).Append(", panning ").Append(shotPan).Append(", 500m ").Append(shot500)
				.Append(", behind ").Append(shotBehind).Append(", down ").Append(shotDown).Append(", 200m ").Append(shot200).Append(", blackout ").Append(shotBlack).Append(", after ").Append(shotAfter).Append('\n');
			sum.Append("ambush log:\n").Append(tracker.WormAmbushLog);
			holdUpright = false;
			File.WriteAllText(Path.Combine(outDir, id + "-view.csv"), rows.ToString());
			File.WriteAllText(Path.Combine(outDir, id + "-view.txt"), sum.ToString());
			Debug.Log("[UT Test] " + id + ": " + sum.ToString().Replace('\n', ' '));
		}

		private IEnumerator AmbushScene(string id, EntityTracker tracker, Vessel v)
		{
			// airborne scenes: height above the terrain, speed (m/s) and heading (0 = toward the creature, which starts north of the craft)
			float flyAlt = 0f, flySpeed = 0f, flyHeading = 0f;
			bool expectMiss = false;
			if (id == "moho_ambush_hover") flyAlt = 12f;
			else if (id == "moho_ambush_fly_slow") { flyAlt = 15f; flySpeed = 8f; flyHeading = 90f; }
			else if (id == "moho_ambush_fly_toward") { flyAlt = 20f; flySpeed = 35f; flyHeading = 0f; }
			else if (id == "moho_ambush_fly_away") { flyAlt = 10f; flySpeed = 25f; flyHeading = 180f; }
			else if (id == "moho_ambush_high") { flyAlt = 60f; expectMiss = true; }
			else if (id == "moho_ambush_flee") { flyAlt = 10f; flySpeed = 90f; flyHeading = 90f; }   // catchable since the hidden creature creeps at 200 m/s
			bool hover = flyAlt > 0f;
			CultureInfo ci = CultureInfo.InvariantCulture;
			tracker.WormAmbushLog = "";
			// the creature only spawns for a landed craft; the hover scene lifts the craft 12 m afterwards
			tracker.SpawnBulkEntity();
			yield return null;
			if (!tracker.bulkEntityActive) throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus);
			tracker.PlaceTestWalker(v, 350.0);
			if (hover)
			{
				// really in the air: held on rails as "landed" (KSP then keeps it exactly at lat/lon/alt), flyAlt m above the terrain
				holdLanded = true;
				holdFlyAlt = flyAlt;
				holdHeight = flyAlt;
				holdAlt = v.mainBody.TerrainAltitude(holdLat, holdLon) + flyAlt;
				dreadStepTestNotLanded = true;
				Hold();
			}
			MohoWormGpuSkin gpu = tracker.bulkWormSwarm as MohoWormGpuSkin;
			StringBuilder rows = new StringBuilder("t,status,phase,dist,speed,visible,hidden,leaping,spire,spire_top,craft_alive,apply_ms,terrain_ms,sim_ms,ground_gap_median,ground_off,ground_sunk\n");
			StringBuilder sum = new StringBuilder();
			sum.Append("scene ").Append(id).Append("\nbody ").Append(tracker.bulkWormSwarm != null ? tracker.bulkWormSwarm.BodyName : "none").Append("\ngpu ").Append(SystemInfo.graphicsDeviceName).Append(" / ").Append(SystemInfo.graphicsDeviceType).Append('\n');
			if (gpu == null) { sum.Append("FAIL: no gpu worm body\n"); File.WriteAllText(Path.Combine(outDir, id + "-ambush.txt"), sum.ToString()); tracker.DespawnBulkEntity(); yield break; }
			MohoWormGpuSkin.Diagnostics = true;
			float sumGap = 0f, maxGapOff = 0f, maxSunk = 0f; int gapN = 0;
			FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
			FlightCamera.fetch.SetDistanceImmediate(45f);
			FlightCamera.fetch.camPitch = 0.35f;
			float t = 0f, sample = 0f, noiseAt = 1.5f, killedAt = -1f, firstDive = -1f, maxSpeed = 0f, sumApply = 0f, maxApply = 0f, sumTerrain = 0f, maxTerrain = 0f, sumSim = 0f;
			int frames = 0, maxVisibleSilent = 0, maxLeaping = 0, maxSpire = 0;
			float maxTop = 0f, approachShotAt = -1f;
			string lastPhase = "None";
			bool camOnCraft = false, released = false;
			float riseAt = -1f;
			bool spireShot = false;
			bool pileShot = false, pileCam = false;
			float[] pileMarks = { 2f, 5f };   // 5 s after the kill is enough (was +5/+15/+35)
			bool[] pileDone = new bool[3];
			Transform craftT = v.transform;
			double flyLat0 = holdLat, flyLon0 = holdLon;
			float firstMissAt = -1f;
			bool flyShot = false, flyCam = false, flyBack = false, seismicSent = false;
			// sound audit: which footsteps, staged warnings and soundscape cues does the worm body make?
			StringBuilder soundLog = new StringBuilder();
			int soundSteps = 0;
			int[] soundKinds = new int[(int)DreadSoundKind.Count];
			int[] warn0 = (int[])tracker.MohoWarnCount.Clone();
			float soundT = 0f, rumbleSample = 0f;
			// what the soundscape mixer really puts out (its left channel), for listening and level checks
			int mixRate = AudioSettings.outputSampleRate;
			DreadSoundMixer mixRec = tracker.dreadAudioHost != null ? tracker.dreadAudioHost.Mixer : null;
			float[] mixBuf = new float[mixRate * 2 * 160];
			if (mixRec != null) { mixRec.RecordPosition = 0; mixRec.Record = mixBuf; }
			tracker.DreadSoundCueObserver = (k, g) =>
			{
				soundKinds[(int)k]++;
				if (soundLog.Length < 6000) soundLog.Append(soundT.ToString("F1", CultureInfo.InvariantCulture)).Append(" cue ").Append(k).Append(" gain ").Append(g.ToString("F3", CultureInfo.InvariantCulture)).Append(" status ").Append(tracker.bulkEntityStatus).Append('\n');
			};
			while (t < 150f)
			{
				yield return null;
				float dt = Time.unscaledDeltaTime;
				t += dt;
				soundT = t;
				rumbleSample += dt;
				if (rumbleSample >= 0.5f && tracker.dreadSoundLogic != null && soundLog.Length < 12000)
				{
					rumbleSample = 0f;
					DreadSoundLogic sl = tracker.dreadSoundLogic;
					soundLog.Append(t.ToString("F1", CultureInfo.InvariantCulture)).Append(" rumble ").Append((20f * Mathf.Log10(Mathf.Max(sl.RumbleBed, 1e-9f))).ToString("F1", CultureInfo.InvariantCulture))
						.Append(" dB cut ").Append(sl.RumbleCutoffHz.ToString("F0", CultureInfo.InvariantCulture)).Append(" Hz events ").Append(sl.EventGain.ToString("F2", CultureInfo.InvariantCulture))
						.Append(" bedA ").Append(sl.VacBedA.ToString("F4", CultureInfo.InvariantCulture)).Append(" phase ").Append(tracker.wormAmbushPhase).Append(" status ").Append(tracker.bulkEntityStatus).Append('\n');
				}
				if (tracker.dreadStepLogic != null && tracker.dreadStepLogic.StepFired)
				{
					soundSteps++;
					if (soundLog.Length < 6000) soundLog.Append(t.ToString("F1", CultureInfo.InvariantCulture)).Append(" step x").Append(Mathf.Max(1, tracker.dreadStepLogic.StepCount)).Append(" gain ").Append(tracker.dreadStepLogic.Gain.ToString("F3", CultureInfo.InvariantCulture)).Append(" status ").Append(tracker.bulkEntityStatus).Append('\n');
				}
				// the worm hunt only reacts to ground signals (the old noise above is for the walker): a seismic pulse from the
				// craft is spotted at any range, so the heap attacks as in the game
				if (!seismicSent && t > 1.5f && tracker.WormBrain != null && v != null)
				{
					seismicSent = true;
					tracker.WormSeismicPulse(v);
				}
				if (t > noiseAt && t < 30f)
				{
					noiseAt += 1f;
					tracker.HuntInjectNoise(t < 2.6f ? HuntNoise.Impact : HuntNoise.Throttle, 0.9f, v != null ? v.GetWorldPos3D() : Vector3d.zero);
				}
				// scripted flight: the held craft moves along a straight line over the terrain at its height
				if (hover && holding && flySpeed > 0f && t > 2f)
				{
					CelestialBody fb = v.mainBody;
					Vector3d s0 = fb.GetWorldSurfacePosition(flyLat0, flyLon0, 0.0);
					Vector3d upv = (s0 - fb.position).normalized;
					Vector3d northv = Vector3d.Exclude(upv, fb.transform.up).normalized;
					Vector3d eastv = Vector3d.Cross(upv, northv).normalized;
					double h = flyHeading * Math.PI / 180.0;
					Vector3d p = s0 + (northv * Math.Cos(h) + eastv * Math.Sin(h)) * (flySpeed * (t - 2f));
					holdLat = fb.GetLatitude(p);
					holdLon = fb.GetLongitude(p);
					holdAlt = fb.TerrainAltitude(holdLat, holdLon) + flyAlt;
					holdHeight = flyAlt;
				}
				// proof that the craft is really in the air: a side view of it at 4.6 s, then the camera goes back to the creature
				if (hover && !flyShot && t > 4f && craftT != null)
				{
					if (!flyCam) { flyCam = true; FlightCamera.fetch.SetTargetTransform(craftT); FlightCamera.fetch.SetDistanceImmediate(45f); FlightCamera.fetch.camPitch = 0.05f; }
					if (t > 4.6f)
					{
						flyShot = true;
						ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-flying.png"));
						Vector3d g0 = ProjectToGround(v.GetWorldPos3D(), v.mainBody);
						sum.Append("craft height above the terrain at 4.6 s m ").Append(((v.GetWorldPos3D() - g0).magnitude).ToString("F1", ci)).Append(", packed ").Append(v.packed).Append('\n');
					}
				}
				if (flyShot && !flyBack && t > 5.2f && tracker.bulkEntityObject != null && tracker.WormAmbushPhaseName == "None")
				{
					flyBack = true;
					FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
					FlightCamera.fetch.SetDistanceImmediate(45f);
					FlightCamera.fetch.camPitch = 0.35f;
				}
				// outcome: count the ambush ends; a scene that expects a miss ends 8 s after the first one
				string logNow = tracker.WormAmbushLog;
				int kills = CountOf(logNow, "end: kill"), misses = CountOf(logNow, "end: miss");
				if (misses > 0 && firstMissAt < 0f) firstMissAt = t;
				if (expectMiss && firstMissAt >= 0f && t > firstMissAt + 8f) break;
				if (!tracker.bulkEntityActive) { sum.Append("creature gone at ").Append(t.ToString("F1", ci)).Append('\n'); break; }
				string phase = tracker.WormAmbushPhaseName;
				bool alive = v != null && v.state != Vessel.State.DEAD && v.parts != null && v.parts.Count > 0;
				Vector3d cpos = tracker.bulkEntityBody.position + tracker.bulkEntityRelativePosition;
				float dist = alive ? (float)(v.GetWorldPos3D() - cpos).magnitude : -1f;
				float speed = tracker.bulkMoho != null ? tracker.bulkMoho.CurrentSpeed : 0f;
				frames++;
				sumApply += gpu.LastApplyMilliseconds; maxApply = Mathf.Max(maxApply, gpu.LastApplyMilliseconds);
				sumTerrain += gpu.LastTerrainMilliseconds; maxTerrain = Mathf.Max(maxTerrain, gpu.LastTerrainMilliseconds);
				sumSim += gpu.LastSimulationMilliseconds;
				if (phase == "None") maxSpeed = Mathf.Max(maxSpeed, speed);
				maxLeaping = Mathf.Max(maxLeaping, gpu.LeapingWorms);
				if (phase == "Silent") maxVisibleSilent = Mathf.Max(maxVisibleSilent, gpu.VisibleWorms);
				if (phase == "Rise" || phase == "Ball") { maxSpire = Mathf.Max(maxSpire, gpu.SpireWorms); maxTop = Mathf.Max(maxTop, gpu.SpireTop); }
				// the approach, seen from beside the creature when it is near and fast
				if (approachShotAt < 0f && phase == "None" && dist > 0f && dist < 160f && speed > 3f)
				{
					approachShotAt = t;
					ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-approach.png"));
				}
				if (phase != "None" && firstDive < 0f) firstDive = t;
				if (!camOnCraft && phase != "None" && craftT != null)
				{
					camOnCraft = true;
					FlightCamera.fetch.SetTargetTransform(craftT);
					FlightCamera.fetch.SetDistanceImmediate(hover ? 85f : 70f);
					FlightCamera.fetch.camPitch = 0.2f;
				}
				if (phase == "Rise" && riseAt < 0f) riseAt = t;
				if (riseAt >= 0f && !spireShot && t > riseAt + 0.6f) { spireShot = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-spire.png")); }
				if (phase != lastPhase)
				{
					Note(id, t, "phase " + phase + " (status " + tracker.bulkEntityStatus + ")");
					if (phase != "None") ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-" + phase.ToLowerInvariant() + ".png"));
					lastPhase = phase;
				}
				// seized: the hold lets go, so the craft is a real vessel in the ball
				if (!released && tracker.WormAmbushGrabbed) { released = true; holding = false; }
				if (killedAt < 0f && (tracker.WormAmbushKilled || (!alive && firstDive >= 0f))) { killedAt = t; }
				if (killedAt >= 0f && !pileShot && t > killedAt + 2.5f) { pileShot = true; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-pile.png")); }
				// the fed swarm must stay as a dormant heap: watch it for 35 s, the camera on the heap
				if (killedAt >= 0f && !pileCam && t > killedAt + 0.5f && tracker.bulkEntityObject != null)
				{
					pileCam = true;
					FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform);
					FlightCamera.fetch.SetDistanceImmediate(70f);
					FlightCamera.fetch.camPitch = 0.3f;
				}
				for (int k = 0; k < pileMarks.Length; k++)
				{
					if (killedAt >= 0f && !pileDone[k] && t > killedAt + pileMarks[k])
					{
						pileDone[k] = true;
						ScreenCapture.CaptureScreenshot(Path.Combine(outDir, id + "-heap-" + pileMarks[k].ToString("00", ci) + "s.png"));
						sum.Append("heap at +").Append(pileMarks[k].ToString("F0", ci)).Append(" s: creature ").Append(tracker.bulkEntityActive ? "present" : "GONE")
							.Append(", status ").Append(tracker.bulkEntityStatus).Append(", fed ").Append(tracker.WormSated)
							.Append(", visible worms ").Append(gpu.VisibleWorms).Append(", hidden ").Append(gpu.HiddenWorms)
							.Append(", pile height m ").Append(gpu.PileHeight.ToString("F1", ci)).Append('\n');
					}
				}
				if (killedAt >= 0f && t > killedAt + 5.5f) { break; }
				sample += dt;
				if (sample >= 0.25f)
				{
					sample = 0f;
					if (gpu.GroundGapSamples >= 8)
					{
						sumGap += gpu.GroundGapMedian; gapN++;
						maxGapOff = Mathf.Max(maxGapOff, gpu.GroundOffFraction); maxSunk = Mathf.Max(maxSunk, gpu.GroundSunkFraction);
					}
					rows.AppendLine(string.Join(",", new string[] { t.ToString("F2", ci), tracker.bulkEntityStatus, phase, dist.ToString("F1", ci), speed.ToString("F1", ci), gpu.VisibleWorms.ToString(ci), gpu.HiddenWorms.ToString(ci), gpu.LeapingWorms.ToString(ci), gpu.SpireWorms.ToString(ci), gpu.SpireTop.ToString("F1", ci), alive ? "1" : "0", gpu.LastApplyMilliseconds.ToString("F2", ci), gpu.LastTerrainMilliseconds.ToString("F2", ci), gpu.LastSimulationMilliseconds.ToString("F2", ci), gpu.GroundGapMedian.ToString("F2", ci), gpu.GroundOffFraction.ToString("F3", ci), gpu.GroundSunkFraction.ToString("F3", ci) }));
				}
			}
			float n = Mathf.Max(1, frames);
			{ string lg = tracker.WormAmbushLog; sum.Append("flight: ").Append(flyAlt.ToString("F0", ci)).Append(" m up, ").Append(flySpeed.ToString("F0", ci)).Append(" m/s, heading ").Append(flyHeading.ToString("F0", ci)).Append(expectMiss ? ", expected: miss" : ", expected: kill").Append("\noutcome: kills ").Append(CountOf(lg, "end: kill")).Append(", misses ").Append(CountOf(lg, "end: miss")).Append(", craft destroyed ").Append(killedAt >= 0f).Append('\n'); }
			sum.Append("first dive at s ").Append(firstDive.ToString("F1", ci)).Append("\ncraft destroyed at s ").Append(killedAt.ToString("F1", ci))
				.Append("\nmax creature speed before the ambush m/s ").Append(maxSpeed.ToString("F1", ci))
				.Append("\nsound: footsteps fired ").Append(soundSteps)
				.Append(", warnings stage1/2/3 ").Append(tracker.MohoWarnCount[1] - warn0[1]).Append('/').Append(tracker.MohoWarnCount[2] - warn0[2]).Append('/').Append(tracker.MohoWarnCount[3] - warn0[3])
				.Append(", soundscape cues");
			for (int k = 0; k < soundKinds.Length; k++) if (soundKinds[k] > 0) sum.Append(' ').Append((DreadSoundKind)k).Append('=').Append(soundKinds[k]);
			sum.Append("\nmax leaping worms ").Append(maxLeaping)
				.Append("\nmax visible worms during the silence ").Append(maxVisibleSilent)
				.Append("\nspire worms ").Append(maxSpire).Append("\nspire top m ").Append(maxTop.ToString("F1", ci))
				.Append("\navg apply ms ").Append((sumApply / n).ToString("F3", ci)).Append("\nmax apply ms ").Append(maxApply.ToString("F3", ci))
				.Append("\navg terrain ms ").Append((sumTerrain / n).ToString("F3", ci)).Append("\nmax terrain ms ").Append(maxTerrain.ToString("F3", ci))
				.Append("\navg sim dispatch ms ").Append((sumSim / n).ToString("F3", ci))
				.Append("\nterrain samples ").Append(gpu.TerrainSamples).Append("\nterrain samples on the collider ").Append(gpu.RaySamples).Append("\nframe rebases ").Append(gpu.Rebases)
				.Append("\nprovisional ground cells ").Append(gpu.ProvisionalCells)
				.Append("\navg ground gap m (resting worms vs collider, median) ").Append((gapN > 0 ? sumGap / gapN : float.NaN).ToString("F2", ci))
				.Append("\nmax share resting points >0.3 m off the ground ").Append(maxGapOff.ToString("F3", ci))
				.Append("\nmax share points sunk >0.3 m under the ground ").Append(maxSunk.ToString("F3", ci))
				.Append("\nframes ").Append(frames).Append("\nseconds ").Append(t.ToString("F1", ci))
				.Append("\n--- ambush log\n").Append(tracker.WormAmbushLog);
			File.WriteAllText(Path.Combine(outDir, id + "-ambush.txt"), sum.ToString());
			File.WriteAllText(Path.Combine(outDir, id + "-ambush.csv"), rows.ToString());
			File.WriteAllText(Path.Combine(outDir, id + "-sound.txt"), soundLog.ToString());
			tracker.DreadSoundCueObserver = null;
			if (mixRec != null)
			{
				mixRec.Record = null;
				int mixFrames = mixRec.RecordPosition / 2; float[] left = new float[mixFrames];
				for (int k = 0; k < mixFrames; k++) left[k] = mixBuf[2 * k];
				WriteWav(Path.Combine(outDir, id + "-mix.wav"), left, mixFrames, mixRate);
			}
			tracker.DespawnBulkEntity();
			Note(id, t, "despawned");
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
			Debug.Log("[UT Test] cameras (" + why + ") count " + Camera.allCamerasCount + " main " + (Camera.main != null ? Camera.main.name : "none") + " ; " + SystemInfo.graphicsDeviceType + " ; colorspace " + QualitySettings.activeColorSpace + " ; " + Screen.width + "x" + Screen.height);
			FlightCamera fc = FlightCamera.fetch;
			if (fc != null)
			{
				Debug.Log("[UT Test] FlightCamera.mainCamera " + (fc.mainCamera != null ? fc.mainCamera.name : "null") + " ; fc.cameras " + (fc.cameras == null ? "null" : fc.cameras.Length.ToString()));
				if (fc.cameras != null)
				{
					for (int i = 0; i < fc.cameras.Length; i++)
					{
						Debug.Log("[UT Test]   fc.cameras[" + i + "] " + (fc.cameras[i] != null ? fc.cameras[i].name : "null"));
					}
				}
			}
			Camera[] all = Camera.allCameras;
			for (int i = 0; i < all.Length; i++)
			{
				Debug.Log("[UT Test]   " + CameraLine(all[i]));
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
				Debug.Log("[UT Test] " + ptag + " luma " + MeanLuma(s).ToString("F4", CultureInfo.InvariantCulture) + " diffVs0 " + d.ToString("F5", CultureInfo.InvariantCulture) + " method " + dreadScreenMethod + " attached " + (tracker.dreadCamera != null && tracker.dreadCamera.Attached) + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : ""));
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
				Debug.Log("[UT Test] event still " + pname + " " + ename + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : ""));
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
			Debug.Log("[UT Test] scene dread_screen");
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
						Debug.Log("[UT Test] " + variant + " m" + levels[li] + " attached " + (tracker.dreadCamera != null && tracker.dreadCamera.Attached) + " target " + target + " diff " + diff.ToString("F5", CultureInfo.InvariantCulture) + " floor " + floor.ToString("F5", CultureInfo.InvariantCulture) + " :: " + (tracker.dreadCamera != null ? tracker.dreadCamera.DescribeUniforms() : "no dread camera"));
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
						Debug.Log("[UT Test] shipped dreadScreenMethod from " + cfgPath + " = " + dreadScreenMethod);
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
				FlightCamera.fetch.SetDistanceImmediate(45f);
				FlightCamera.fetch.camPitch = 0.62f;
				FlightCamera.fetch.camHdg = 0.9f;
				yield return new WaitForSecondsRealtime(2f);
			}
			else
			{
				Debug.Log("[UT Test] dread_screen: no Moho monster: " + tracker.bulkEntityStatus);
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
			Debug.Log("[UT Test] underwater place: body " + v.mainBody.bodyName + " terrain " + terrain.ToString("F0", CultureInfo.InvariantCulture));
			holdLat = v.latitude; holdLon = v.longitude; holdAlt = terrain < -40.0 ? -25.0 : v.altitude;
			holding = true;
			yield return new WaitForSecondsRealtime(4f);
			FlightCamera.fetch.SetDistanceImmediate(14f);
			yield return LookStills(tracker, terrain < -40.0 ? "underwater" : "kerbinInland", report);
			File.WriteAllText(Path.Combine(outDir, "dread_screen.csv"), report.ToString());
			dreadScreenMethod = savedMethod;
			tracker.dreadDebugCameraName = null;
			tracker.dreadDebugMenace = -1f;
			holding = false;
		}


		// moho_creature scene: the worm swarm's cost and bounds every frame, two shots at rest (18 m), two while chasing (22 m), 12-frame sequences,
		// and for every shot the heap's rectangle on screen (shots.csv), so evaluate.py can judge the pixels inside it.
		private sealed class CreatureProbe
		{
			private int shots, frames, pointCount, triangleCount, spilledMax, workerSteps;
			private float nextShot = 2.2f, maxSpeed, maxApplyMs, maxApplyAfterWarmup, sumApplyMs;
			private float maxSimMs, maxTerrainMs, maxMeshMs, maxUploadMs, sumSimMs, sumTerrainMs, sumMeshMs, sumUploadMs;
			private float maxCentroidOffset, maxPointRadius, maxSegmentLength, cameraDistance, maxOverlap, sumOverlap;
			private Vector3 renderBounds;
			private bool rendererEnabled, threaded, rect20, restCamera;
			private int restSeq, chaseSeq, tumbles;
			private float nextSeq = 3.6f, lastT, movingTime, sumRearDrag, maxPileHeight;
			private string shader = "?", note = "", bodyName = "?";
			private int wormTotal, aspectN;
			private float sumCoherence, sumAspect;
			private readonly StringBuilder swarmSamples = new StringBuilder("t,camera_distance,max_point_radius,centroid_offset,max_segment_length,spilled_worms,overlap_fraction,speed,apply_ms,worker_ms\n");
			private readonly StringBuilder shotRects = new StringBuilder("file,kind,speed,x0,y0,x1,y1,width,height\n");

			internal void Sample(EntityTracker tracker, float t, string outDir)
			{
				MohoCreature c = tracker.bulkMoho;
				IWormSwarmSkin swarm = c != null ? c.Skin as IWormSwarmSkin : null;
				if (swarm == null) { note = "worm heap skin unavailable"; return; }
				frames++;
				pointCount = swarm.PointCount;
				bodyName = swarm.BodyName;
				wormTotal = swarm.WormTotal;
				sumCoherence += swarm.HeadingCoherence;
				if (swarm.HeapAspect > 0f) { sumAspect += swarm.HeapAspect; aspectN++; }
				triangleCount = swarm.RenderedTriangles;
				renderBounds = swarm.RenderBounds.size;
				rendererEnabled = swarm.RendererEnabled;
				shader = swarm.ShaderName;
				threaded = swarm.Threaded;
				workerSteps = swarm.WorkerSteps;
				Vector3 anchor = swarm.Anchor;
				float centroid = (swarm.PointCentroid - anchor).magnitude;
				maxCentroidOffset = Mathf.Max(maxCentroidOffset, centroid);
				maxPointRadius = Mathf.Max(maxPointRadius, swarm.MaxPointRadius);
				maxSegmentLength = Mathf.Max(maxSegmentLength, swarm.MaxSegmentLength);
				spilledMax = Mathf.Max(spilledMax, swarm.SpilledWorms);
				maxOverlap = Mathf.Max(maxOverlap, swarm.OverlapFraction);
				sumOverlap += swarm.OverlapFraction;
				maxSpeed = Mathf.Max(maxSpeed, c.CurrentSpeed);
				maxApplyMs = Mathf.Max(maxApplyMs, swarm.LastApplyMilliseconds);
				if (t > 2f) maxApplyAfterWarmup = Mathf.Max(maxApplyAfterWarmup, swarm.LastApplyMilliseconds);
				sumApplyMs += swarm.LastApplyMilliseconds;
				maxSimMs = Mathf.Max(maxSimMs, swarm.LastSimulationMilliseconds);
				maxMeshMs = Mathf.Max(maxMeshMs, swarm.LastMeshMilliseconds);
				maxTerrainMs = Mathf.Max(maxTerrainMs, swarm.LastTerrainMilliseconds);
				maxUploadMs = Mathf.Max(maxUploadMs, swarm.LastUploadMilliseconds);
				sumSimMs += swarm.LastSimulationMilliseconds; sumMeshMs += swarm.LastMeshMilliseconds;
				sumTerrainMs += swarm.LastTerrainMilliseconds; sumUploadMs += swarm.LastUploadMilliseconds;
				Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
				if (camera != null) cameraDistance = Vector3.Distance(camera.transform.position, anchor);
				// rest shots in the quiet start (the craft's noises begin at 7 s), at 14 m; the camera is placed a frame ahead
				if (!restCamera && camera != null) { restCamera = true; FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform); FlightCamera.fetch.SetDistanceImmediate(20f); FlightCamera.fetch.camPitch = 0.5f; }
				float dtp = t - lastT; lastT = t;
				tumbles = swarm.TumbleCount;
				maxPileHeight = Mathf.Max(maxPileHeight, swarm.PileHeight);
				if (c.CurrentSpeed > 5f) { movingTime += dtp; sumRearDrag += swarm.RearDragFraction * dtp; }
				// motion sequences for rest.gif and chase.gif: 12 frames, 0.12 s apart
				if (camera != null && t >= nextSeq)
				{
					if (restSeq < 12 && c.CurrentSpeed < 2f && t < 6.4f) { restSeq++; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "seq-rest-" + restSeq.ToString("00") + ".png")); nextSeq = t + 0.12f; }
					else if (shots >= 3 && chaseSeq < 12 && c.CurrentSpeed > 15f) { chaseSeq++; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "seq-chase-" + chaseSeq.ToString("00") + ".png")); nextSeq = t + 0.12f; }
				}
				if (t >= nextShot && shots < 4 && camera != null)
				{
					bool rest = shots < 2;
					bool ready = rest ? (c.CurrentSpeed < 2f || t > 6.5f) : c.CurrentSpeed > 20f;
					if (ready)
					{
						shots++;
						string name = "creature-" + shots + ".png";
						ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name));
						Rect(camera, swarm, name, rest ? (c.CurrentSpeed < 2f ? "rest" : "rest-moving") : "chase", c.CurrentSpeed);
						nextShot = t + (rest ? 2.5f : 3f);
						if (shots == 2) { FlightCamera.fetch.SetTargetTransform(tracker.bulkEntityObject.transform); FlightCamera.fetch.SetDistanceImmediate(22f); FlightCamera.fetch.camPitch = 0.6f; }
					}
					else nextShot = t + 0.25f;
				}
				if (!rect20 && t > 20f && camera != null) { rect20 = true; Rect(camera, swarm, "moho_creature-20s.png", "20s", c.CurrentSpeed); }
				if ((frames % 30) == 0)
				{
					CultureInfo ci = CultureInfo.InvariantCulture;
					swarmSamples.AppendLine(string.Join(",", new string[] { t.ToString("F2", ci), cameraDistance.ToString("F1", ci), swarm.MaxPointRadius.ToString("F2", ci), centroid.ToString("F2", ci), swarm.MaxSegmentLength.ToString("F2", ci), swarm.SpilledWorms.ToString(ci), swarm.OverlapFraction.ToString("F3", ci), c.CurrentSpeed.ToString("F1", ci), swarm.LastApplyMilliseconds.ToString("F3", ci), (swarm.LastSimulationMilliseconds + swarm.LastMeshMilliseconds).ToString("F3", ci) }));
				}
			}

			// the heap's box (5 m wide, 2.6 m high, longer when stretched) projected to screen pixels, top-left origin like the PNG
			private void Rect(Camera camera, IWormSwarmSkin swarm, string file, string kind, float speed)
			{
				float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
				Quaternion q = swarm.Frame;
				for (int i = 0; i < 8; i++)
				{
					Vector3 local = new Vector3((i & 1) == 0 ? -2.8f : 2.8f, (i & 2) == 0 ? 0f : 2.6f, (i & 4) == 0 ? -3.4f : 3.4f);
					Vector3 sp = camera.WorldToScreenPoint(swarm.Anchor + q * local);
					if (sp.z <= 0f) continue;
					x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
					y0 = Mathf.Min(y0, Screen.height - sp.y); y1 = Mathf.Max(y1, Screen.height - sp.y);
				}
				CultureInfo ci = CultureInfo.InvariantCulture;
				shotRects.AppendLine(file + "," + kind + "," + speed.ToString("F1", ci) + "," + x0.ToString("F0", ci) + "," + y0.ToString("F0", ci) + "," + x1.ToString("F0", ci) + "," + y1.ToString("F0", ci) + "," + Screen.width + "," + Screen.height);
			}

			internal void Write(string outDir)
			{
				CultureInfo ci = CultureInfo.InvariantCulture;
				float n = Mathf.Max(1, frames);
				string stats = "body " + bodyName + "\nworms " + wormTotal + "\npoints " + pointCount + "\ntriangles " + triangleCount
					+ "\nrenderer enabled " + rendererEnabled + "\nshader " + shader + "\nworker thread " + threaded + "\nworker steps " + workerSteps + "\nframes " + frames
					+ "\navg apply ms " + (sumApplyMs / n).ToString("F3", ci)
					+ "\nmax apply ms warmup excluded " + maxApplyAfterWarmup.ToString("F3", ci)
					+ "\nmax apply ms all frames " + maxApplyMs.ToString("F3", ci)
					+ "\nmain thread terrain/upload avg ms " + (sumTerrainMs / n).ToString("F3", ci) + "/" + (sumUploadMs / n).ToString("F3", ci)
					+ "\nmain thread terrain/upload max ms " + maxTerrainMs.ToString("F3", ci) + "/" + maxUploadMs.ToString("F3", ci)
					+ "\nworker sim/mesh avg ms " + (sumSimMs / n).ToString("F3", ci) + "/" + (sumMeshMs / n).ToString("F3", ci)
					+ "\nworker sim/mesh max ms " + maxSimMs.ToString("F3", ci) + "/" + maxMeshMs.ToString("F3", ci)
					+ "\nmax point radius from anchor m " + maxPointRadius.ToString("F2", ci)
					+ "\nmax swarm centroid offset from anchor m " + maxCentroidOffset.ToString("F2", ci)
					+ "\nmax spilled worms " + spilledMax
					+ "\nmax segment length m " + maxSegmentLength.ToString("F2", ci)
					+ "\navg overlap fraction " + (sumOverlap / n).ToString("F3", ci)
					+ "\nmax overlap fraction " + maxOverlap.ToString("F3", ci)
					+ "\nmax speed " + maxSpeed.ToString("F1", ci)
					+ "\nmax pile height m " + maxPileHeight.ToString("F2", ci)
					+ "\navg heading coherence " + (sumCoherence / n).ToString("F3", ci)
					+ "\navg heap aspect " + (aspectN > 0 ? sumAspect / aspectN : 0f).ToString("F2", ci)
					+ "\nsim steps per second " + (lastT > 0f ? workerSteps / lastT : 0f).ToString("F1", ci)
					+ "\nmoving time s " + movingTime.ToString("F1", ci)
					+ "\ntumbles total " + tumbles
					+ "\ntumble rate per s " + (movingTime > 0f ? tumbles / movingTime : 0f).ToString("F2", ci)
					+ "\navg rear drag fraction " + (movingTime > 0f ? sumRearDrag / movingTime : 0f).ToString("F3", ci)
					+ "\nrenderer bounds " + renderBounds.ToString("F2")
					+ "\ncamera to anchor m " + cameraDistance.ToString("F1", ci) + "\n";
				File.WriteAllText(Path.Combine(outDir, "creature.txt"), stats + note + "\n");
				if (swarmSamples.Length > 0) File.WriteAllText(Path.Combine(outDir, "swarm-samples.csv"), swarmSamples.ToString());
				File.WriteAllText(Path.Combine(outDir, "shots.csv"), shotRects.ToString());
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
			string status = (tracker.bulkEntityStatus + " / h " + v.heightFromTerrain.ToString("F1", c)).Replace(",", ";");
			EntityTracker.HuntInfo hi = tracker.GetNearestHuntInfo();
			csv.AppendLine(string.Join(",", new string[]
			{
				id, t.ToString("F2", c), tracker.dreadLastDistance.ToString("F1", c), tracker.dreadStepSpeed.ToString("F2", c), tracker.dreadStepWalker >= 0 ? "1" : "0",
				tracker.bulkEntityChasing ? "1" : "0", v.Landed && !dreadStepTestNotLanded ? "1" : "0", v.mainBody.atmosphere ? "0" : "1",
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

	private void PlaceTestWalker(Vessel vessel, double metres)
	{
		CelestialBody body = vessel.mainBody;
		Vector3d from = vessel.GetWorldPos3D();
		Vector3d up = (from - body.position).normalized;
		Vector3d north = Vector3d.Exclude(up, body.transform.up).normalized;
		Vector3d ground = ProjectToGround(from + north * metres, body);
		if (bulkEntityActive && bulkEntityBody == body)
		{
			Vector3 shift = (Vector3)(ground - bulkEntityObject.transform.position);
			if (bulkWormSwarm != null) bulkWormSwarm.Reposition(shift);
			bulkEntityRelativePosition = ground - body.position;
			if (bulkEntityObject != null)
			{
				bulkEntityObject.transform.position = ground;
			}
		}
	}
}
