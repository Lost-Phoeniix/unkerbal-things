using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnkerbalThings.Dread;
using UnityEngine;

// TUFX sweep: the worm swarm and the imager feed under every TUFX profile in a test copy of the game.
// At each moment of interest the game is frozen (Time.timeScale 0), so every profile
// is shot on the very same frame; then each profile is set through TUFX's own ChangeProfileForScene. "NONE" is the game
// without TUFX (every PostProcessLayer off, HDR off). All judging happens here, on a 480x300 copy of each frame; only the
// numbers (tufx-<scene>.csv), the flags (tufx-flags.txt) and small BMPs are written.
// Scenes: tufx_worm (rest, approach + Dread pass, spire, heap, heap close, then a short frame-time check), tufx_worm_dusk
// (the same at a low sun), tufx_feed (outside fade, imager feed, feed turned, feed near).
public partial class EntityTracker
{
	private sealed partial class TestDriver
	{
		private const int TufxW = 480, TufxH = 300;

		private sealed class TufxShot
		{
			public float[] L;
			public Color32[] Px;
			public float Mean, Std, White, Black, Sharp;
		}

		private object tufxLoader;
		private MethodInfo tufxChange;
		private object tufxFlightScene;
		private string tufxStartProfile;
		private int tufxStartAa;
		private TufxShot tufxLast;
		private StringBuilder tufxRows, tufxFlags;
		private string tufxScene;
		private int tufxFlagCount, tufxNoEffect;

		private IEnumerator TufxScene(string id, EntityTracker tracker, Vessel v)
		{
			tufxScene = id;
			tufxRows = new StringBuilder("scene,state,profile,hdr,mean,std,white,black,sharp,d_mean_vs_none,diff_vs_none,worm_area,worm_signal,spill,spill_noise,black_excess,dread_ratio,flags\n");
			tufxFlags = new StringBuilder();
			tufxFlagCount = 0;
			tufxNoEffect = 0;
			if (!TufxBind())
			{
				File.WriteAllText(Path.Combine(outDir, "tufx-flags.txt"), "HARNESS: TUFX is not loaded or its API changed (TexturesUnlimitedFXLoader.ChangeProfileForScene)\n");
				yield break;
			}
			List<string> profiles = TufxProfiles();
			Debug.Log("[UT Test] tufx: " + profiles.Count + " profiles, flight profile at start " + tufxStartProfile);
			if (id.StartsWith("tufx_feed", StringComparison.Ordinal)) yield return TufxFeed(id, tracker, v, profiles);
			else yield return TufxWorm(id, tracker, v, profiles);
			Time.timeScale = 1f;
			TufxSet(tufxStartProfile);
			File.WriteAllText(Path.Combine(outDir, "tufx-" + id + ".csv"), tufxRows.ToString());
			File.AppendAllText(Path.Combine(outDir, "tufx-flags.txt"), tufxFlags.ToString());
			File.AppendAllText(Path.Combine(outDir, "tufx-summary.txt"), id + ": " + profiles.Count + " profiles, " + tufxFlagCount + " flags, " + tufxNoEffect + " profile shots identical to NONE\n");
		}

		// ---------------------------------------------------------------- worm scenes

		private IEnumerator TufxWorm(string id, EntityTracker tracker, Vessel v, List<string> profiles)
		{
			viewLockSettings.Enabled = false;   // the view lock blacks out the outside view near the swarm
			dreadScreen = false;                // the worm as TUFX shows it; the Dread pass gets its own check in "approach"
			tracker.WormAmbushLog = "";
			tracker.SpawnBulkEntity();
			yield return null;
			if (!tracker.bulkEntityActive) throw new InvalidOperationException(id + ": monster did not spawn: " + tracker.bulkEntityStatus);
			tracker.PlaceTestWalker(v, 350.0);
			MohoWormGpuSkin gpu = tracker.bulkWormSwarm as MohoWormGpuSkin;
			if (gpu == null) { tufxFlags.Append("HARNESS ").Append(id).Append(": no gpu worm body (").Append(tracker.bulkWormSwarm != null ? tracker.bulkWormSwarm.BodyName : "none").Append(")\n"); yield break; }
			holdUpright = true;   // a toppled craft turns the flight camera's frame upside down
			Hold();
			Transform craftT = v.transform;
			float t = 0f, noiseAt = 1.5f, killedAt = -1f, riseAt = -1f;
			bool seismic = false, released = false, camOnCraft = false;
			bool doneRest = false, doneApproach = false, doneSpire = false, doneHeap = false, dreadPrimed = false;
			while (t < 150f)
			{
				yield return null;
				t += Time.unscaledDeltaTime;
				if (!tracker.bulkEntityActive) { tufxFlags.Append("HARNESS ").Append(id).Append(": creature gone at t=").Append(t.ToString("F1", CultureInfo.InvariantCulture)).Append('\n'); break; }
				if (!doneRest && t > 1.0f)
				{
					doneRest = true;
					TufxAim(gpu, 45f, 0.35f, 0.9f);
					yield return new WaitForSecondsRealtime(0.6f);
					yield return TufxSweep("rest", tracker, gpu, profiles, false);
				}
				if (!seismic && t > 1.5f && tracker.WormBrain != null) { seismic = true; tracker.WormSeismicPulse(v); }
				if (t > noiseAt && t < 30f) { noiseAt += 1f; tracker.HuntInjectNoise(t < 2.6f ? HuntNoise.Impact : HuntNoise.Throttle, 0.9f, v.GetWorldPos3D()); }
				string phase = tracker.WormAmbushPhaseName;
				Vector3d cpos = tracker.bulkEntityBody.position + tracker.bulkEntityRelativePosition;
				bool alive = v != null && v.state != Vessel.State.DEAD && v.parts != null && v.parts.Count > 0;
				float dist = alive ? (float)(v.GetWorldPos3D() - cpos).magnitude : -1f;
				float speed = tracker.bulkMoho != null ? tracker.bulkMoho.CurrentSpeed : 0f;
				// the approach, with the Dread pass primed at menace 0.7 for 2.5 s first (it keeps that level while frozen)
				if (!doneApproach && phase == "None" && dist > 0f && dist < 260f && !dreadPrimed)
				{
					dreadPrimed = true;
					tracker.dreadDebugMenace = 0.7f;
					dreadScreen = true;
				}
				if (!doneApproach && dreadPrimed && phase == "None" && dist > 0f && dist < 230f && t > 4f)
				{
					doneApproach = true;
					TufxAim(gpu, 40f, 0.25f, 2.2f);
					yield return new WaitForSecondsRealtime(0.4f);
					yield return TufxSweep("approach", tracker, gpu, profiles, true);
					tracker.dreadDebugMenace = -1f;
					dreadScreen = false;
				}
				if (!camOnCraft && phase != "None")
				{
					camOnCraft = true;
					if (!doneApproach) { doneApproach = true; tufxFlags.Append("HARNESS ").Append(id).Append(": the approach state was never reached (dist ").Append(dist.ToString("F0", CultureInfo.InvariantCulture)).Append(" m)\n"); tracker.dreadDebugMenace = -1f; dreadScreen = false; }
					FlightCamera.fetch.SetTargetTransform(craftT);
					FlightCamera.fetch.SetDistanceImmediate(70f);
					FlightCamera.fetch.camPitch = 0.2f;
				}
				if (phase == "Rise" && riseAt < 0f) riseAt = t;
				if (!doneSpire && riseAt >= 0f && t > riseAt + 0.6f)
				{
					doneSpire = true;
					yield return TufxSweep("spire", tracker, gpu, profiles, false);
				}
				if (!released && tracker.WormAmbushGrabbed) { released = true; holding = false; }
				if (killedAt < 0f && (tracker.WormAmbushKilled || (!alive && phase != "None"))) killedAt = t;
				// the kill blast (fireball, dust, flung worms); the fed heap afterwards buries itself, so nothing is left to see
				if (killedAt >= 0f && !doneHeap && t > killedAt + 0.2f && tracker.bulkEntityObject != null)
				{
					doneHeap = true;
					TufxAim(gpu, 80f, 0.25f, 0.9f);
					yield return null;
					yield return null;
					yield return TufxSweep("kill_blast", tracker, gpu, profiles, false);
					break;
				}
			}
			if (!doneSpire) tufxFlags.Append("HARNESS ").Append(id).Append(": spire state never reached\n");
			if (!doneHeap) tufxFlags.Append("HARNESS ").Append(id).Append(": kill_blast state never reached (craft destroyed: ").Append(killedAt >= 0f).Append(")\n");
			if (!id.EndsWith("_dusk", StringComparison.Ordinal) && tracker.bulkEntityObject != null) yield return TufxPerf(tracker, gpu, profiles);
			holdUpright = false;
			if (tufxLook != null) { UnityEngine.Object.Destroy(tufxLook); tufxLook = null; }
			tracker.DespawnBulkEntity();
		}

		// ---------------------------------------------------------------- imager feed scene

		private IEnumerator TufxFeed(string id, EntityTracker tracker, Vessel v, List<string> profiles)
		{
			viewLockSettings.Enabled = true;
			dreadScreen = true;
			holdUpright = true;
			Hold();
			ModuleUTImagerFeed imager = FindImager(v);
			if (imager == null) { tufxFlags.Append("HARNESS ").Append(id).Append(": no imager on the craft (run with -Craft 'UT Imager Test.craft')\n"); yield break; }
			tracker.WormAmbushLog = "";
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
			tracker.WormTestSearch();
			bool doneFade = false, doneFeed = false, donePan = false, doneNear = false;
			float feedAt = -1f, panAt = -1f;
			for (float t = 0f; t < 260f; t += 0.5f)
			{
				yield return new WaitForSecondsRealtime(0.5f);
				ViewLock vl = tracker.viewLock;
				if (vl == null) continue;
				double d = tracker.viewSwarmDistance;
				if (!doneFade && vl.Mode == ViewMode.Outside && vl.OutsideFade >= 0.35f && vl.OutsideFade <= 0.7f)
				{
					doneFade = true;
					yield return TufxSweep("outside_fade", tracker, null, profiles, false);
				}
				if (tracker.viewCamTaken && feedAt < 0f) feedAt = t;
				if (!doneFeed && feedAt >= 0f && t - feedAt >= 1.5f)
				{
					doneFeed = true;
					yield return TufxSweep("feed", tracker, null, profiles, false);
					panAt = t;
					imager.Steer(40f, -4f, 0f);
				}
				if (!donePan && panAt >= 0f && t - panAt >= 1.5f && tracker.viewCamTaken)
				{
					donePan = true;
					yield return TufxSweep("feed_turned", tracker, null, profiles, false);
				}
				if (!doneNear && donePan && tracker.viewCamTaken && d <= 500.0 && !tracker.WormAmbushActive)
				{
					doneNear = true;
					yield return TufxSweep("feed_near", tracker, null, profiles, false);
				}
				if (tracker.WormAmbushActive || (doneNear && donePan)) break;
			}
			if (!doneFade) tufxFlags.Append("HARNESS ").Append(id).Append(": outside_fade state never reached\n");
			if (!doneFeed) tufxFlags.Append("HARNESS ").Append(id).Append(": the feed never came on\n");
			if (!doneNear) tufxFlags.Append("HARNESS ").Append(id).Append(": feed_near never reached (strike came first or the swarm stayed away)\n");
			// the feed fix (viewFeedTufxOff): TUFX must come back once the feed ends, with the player's profile
			string fixProfile = profiles.Count > 1 ? profiles[profiles.Count / 2] : null;
			if (fixProfile != null) TufxSet(fixProfile);
			viewLockSettings.Enabled = false;
			yield return new WaitForSecondsRealtime(1.5f);
			Camera main = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
			Behaviour mainLayer = main != null ? main.GetComponent("PostProcessLayer") as Behaviour : null;
			string back = "feed fix: after the feed (" + fixProfile + ") camera taken " + tracker.viewCamTaken + ", TUFX layer on the flight camera " + (mainLayer == null ? "missing" : mainLayer.enabled ? "on" : "OFF") + ", hdr " + (main != null && main.allowHDR) + " (profile hdr " + TufxHdr(fixProfile) + ")";
			File.AppendAllText(Path.Combine(outDir, "tufx-summary.txt"), back + "\n");
			if (mainLayer == null || !mainLayer.enabled || tracker.viewCamTaken || (main != null && main.allowHDR != TufxHdr(fixProfile))) { tufxFlagCount++; tufxFlags.Append(id).Append(": FEED FIX did not restore TUFX: ").Append(back).Append('\n'); }
			holdUpright = false;
			tracker.DespawnBulkEntity();
		}

		// ---------------------------------------------------------------- one frozen moment, every profile

		private IEnumerator TufxSweep(string state, EntityTracker tracker, MohoWormGpuSkin gpu, List<string> profiles, bool dreadPass)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			Time.timeScale = 0f;
			yield return null;
			List<Renderer> worm = new List<Renderer>();
			if (gpu != null && gpu.Core != null && gpu.Core.View != null)
			{
				Transform frame = gpu.Core.View.transform.parent != null ? gpu.Core.View.transform.parent : gpu.Core.View.transform;
				foreach (Renderer r in frame.GetComponentsInChildren<Renderer>(false)) if (r.enabled) worm.Add(r);
			}
			bool wormPair = worm.Count > 0;
			TufxShot a0 = null, b0 = null, d0 = null;
			bool[] mask0 = null, mask0Wide = null;
			int area0 = 0;
			float signal0 = 0f, spill0 = 0f;
			for (int pi = 0; pi < profiles.Count; pi++)
			{
				string prof = profiles[pi];
				TufxSet(prof);
				if (dreadPass) dreadScreen = false;
				yield return TufxSettle();
				yield return TufxGrab(); TufxShot a = tufxLast;
				TufxShot b = null, b2 = null, d = null;
				if (wormPair)
				{
					SetRenderers(worm, false);
					yield return TufxSettle();
					yield return TufxGrab(); b = tufxLast;
					yield return null;
					yield return TufxGrab(); b2 = tufxLast;
					SetRenderers(worm, true);
				}
				if (dreadPass)
				{
					dreadScreen = true;
					yield return TufxSettle();
					yield return TufxGrab(); d = tufxLast;
					dreadScreen = false;
				}
				List<string> flags = new List<string>();
				float wormArea = 0f, signal = float.NaN, spill = float.NaN, spillNoise = float.NaN, blackExcess = float.NaN, dreadRatio = float.NaN, dMean = 0f, diffNone = 0f;
				if (pi == 0)
				{
					a0 = a; b0 = b; d0 = d;
					if (wormPair)
					{
						mask0 = new bool[a.L.Length];
						for (int i = 0; i < mask0.Length; i++) if (Math.Abs(a.L[i] - b.L[i]) > 0.03f) { mask0[i] = true; area0++; }
						mask0Wide = Dilate(mask0, 4);
						signal0 = MaskedDiff(a, b, mask0, true);
						spill0 = Spill(a, b, mask0Wide);
						if (area0 < a.L.Length / 500) tufxFlags.Append("HARNESS ").Append(tufxScene).Append('/').Append(state).Append(": worm hardly in view without TUFX (").Append(area0).Append(" px of 144000), worm checks skipped\n");
					}
					wormArea = area0 / (float)a.L.Length;
				}
				else
				{
					dMean = a.Mean - a0.Mean;
					diffNone = MeanAbs(a, a0);
					if (diffNone < 0.002f) tufxNoEffect++;
					if (wormPair && area0 >= a.L.Length / 500)
					{
						signal = signal0 > 1e-4f ? MaskedDiff(a, b, mask0, true) / signal0 : float.NaN;
						spill = Spill(a, b, mask0Wide);
						spillNoise = Spill(b2, b, mask0Wide);
						// NaN-like: pixels that were clearly lit without TUFX and are pure black now, with the worm minus without it
						// (auto exposure and dark grading change the whole frame, so they cancel out here)
						blackExcess = NewBlack(a, a0) - NewBlack(b, b0);
						if (signal < 0.3f) flags.Add("worm nearly invisible (contrast " + Pct(signal) + " of no-TUFX)");
						if (signal > 3.5f) flags.Add("worm far too strong (contrast " + Pct(signal) + " of no-TUFX)");
						if (spill - spillNoise - spill0 > 0.05f) flags.Add("worm changes " + Pct(spill - spillNoise - spill0) + " of the screen outside its outline");
						if (blackExcess > 0.01f) flags.Add("worm adds black areas (" + Pct(blackExcess) + " of the frame, NaN in post-processing?)");
					}
					if (a.Std < 0.015f && a0.Std >= 0.03f) flags.Add("blank picture");
					if (a.White > 0.5f && a.White > a0.White + 0.3f) flags.Add("blown out (" + Pct(a.White) + " white)");
					if (a.Black > a0.Black + 0.4f) flags.Add("crushed to black (" + Pct(a.Black) + " black)");
					if (Math.Abs(dMean) > 0.3f) flags.Add("brightness off by " + dMean.ToString("+0.00;-0.00", ci));
					if (a0.Sharp > 0.004f && a.Sharp / a0.Sharp < 0.35f) flags.Add("picture blurred (detail " + Pct(a.Sharp / a0.Sharp) + " of no-TUFX)");
					if (dreadPass && d != null && d0 != null)
					{
						float e0 = MeanAbs(d0, a0), e = MeanAbs(d, a);
						dreadRatio = e0 > 1e-4f ? e / e0 : float.NaN;
						if (dreadRatio < 0.25f) flags.Add("Dread screen effect almost gone (" + Pct(dreadRatio) + " of no-TUFX)");
						if (dreadRatio > 4f) flags.Add("Dread screen effect far stronger (" + Pct(dreadRatio) + " of no-TUFX)");
						if (d.Std < 0.015f && d0.Std >= 0.03f) flags.Add("blank picture with the Dread pass");
						if (d.White > 0.5f && d.White > d0.White + 0.3f) flags.Add("blown out with the Dread pass");
					}
				}
				string slug = pi.ToString("00", ci) + "-" + System.Text.RegularExpressions.Regex.Replace(prof, "[^A-Za-z0-9]+", "_");
				string baseName = "tufx-" + tufxScene + "-" + state + "-" + slug;
				WriteBmp(Path.Combine(outDir, baseName + ".bmp"), a.Px, TufxW, TufxH);
				if (pi == 0 || flags.Count > 0)
				{
					if (b != null) WriteBmp(Path.Combine(outDir, baseName + "-noworm.bmp"), b.Px, TufxW, TufxH);
					if (d != null) WriteBmp(Path.Combine(outDir, baseName + "-dread.bmp"), d.Px, TufxW, TufxH);
				}
				if (flags.Count > 0)
				{
					tufxFlagCount++;
					tufxFlags.Append(tufxScene).Append(" / ").Append(state).Append(" / ").Append(prof).Append(TufxHdr(prof) ? " (HDR)" : "").Append(": ").Append(string.Join("; ", flags.ToArray())).Append("  [").Append(baseName).Append(".bmp]\n");
				}
				tufxRows.Append(tufxScene).Append(',').Append(state).Append(",\"").Append(prof).Append("\",").Append(TufxHdr(prof) ? 1 : 0).Append(',')
					.Append(F(a.Mean)).Append(',').Append(F(a.Std)).Append(',').Append(F(a.White)).Append(',').Append(F(a.Black)).Append(',').Append(F(a.Sharp)).Append(',')
					.Append(F(dMean)).Append(',').Append(F(diffNone)).Append(',').Append(F(wormArea)).Append(',').Append(F(signal)).Append(',').Append(F(spill)).Append(',')
					.Append(F(spillNoise)).Append(',').Append(F(blackExcess)).Append(',').Append(F(dreadRatio)).Append(",\"").Append(string.Join("; ", flags.ToArray())).Append("\"\n");
			}
			TufxSet(tufxStartProfile);
			Time.timeScale = 1f;
			File.WriteAllText(Path.Combine(outDir, "tufx-" + tufxScene + ".csv"), tufxRows.ToString());
			Debug.Log("[UT Test] tufx " + tufxScene + "/" + state + " done, flags so far " + tufxFlagCount);
		}

		// ---------------------------------------------------------------- frame time, a few profiles (limited on purpose)

		private IEnumerator TufxPerf(EntityTracker tracker, MohoWormGpuSkin gpu, List<string> profiles)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			TufxAim(gpu, 45f, 0.35f, 0.9f);
			List<string> pick = new List<string> { "NONE", "Default-Flight", "FoxLukewarm - Experimental Features", "UT-Stress-Heavy", "NONE" };
			StringBuilder sb = new StringBuilder("profile,frames,mean_ms,p95_ms,worm_apply_ms\n");
			float noneMean = -1f;
			foreach (string p in pick)
			{
				if (p != "NONE" && !profiles.Contains(p)) continue;
				TufxSet(p);
				yield return new WaitForSecondsRealtime(1.5f);
				List<float> ms = new List<float>();
				float apply = 0f, start = Time.realtimeSinceStartup;
				while (Time.realtimeSinceStartup - start < 4f)
				{
					yield return null;
					ms.Add(Time.unscaledDeltaTime * 1000f);
					apply += gpu.LastApplyMilliseconds;
				}
				ms.Sort();
				float mean = 0f;
				foreach (float x in ms) mean += x;
				mean /= Math.Max(1, ms.Count);
				float p95 = ms.Count > 0 ? ms[Math.Min(ms.Count - 1, (int)(ms.Count * 0.95f))] : 0f;
				sb.Append('"').Append(p).Append("\",").Append(ms.Count).Append(',').Append(F(mean)).Append(',').Append(F(p95)).Append(',').Append(F(apply / Math.Max(1, ms.Count))).Append('\n');
				if (p == "NONE" && noneMean < 0f) noneMean = mean;
				else if (noneMean > 0f && p != "NONE" && mean > noneMean + 10f)
				{
					tufxFlagCount++;
					tufxFlags.Append(tufxScene).Append(" / perf / ").Append(p).Append(": frame time ").Append(mean.ToString("F1", ci)).Append(" ms vs ").Append(noneMean.ToString("F1", ci)).Append(" ms without TUFX\n");
				}
			}
			File.WriteAllText(Path.Combine(outDir, "tufx-perf.csv"), sb.ToString());
			TufxSet(tufxStartProfile);
		}

		// ---------------------------------------------------------------- TUFX access (reflection: TUFX is optional)

		private bool TufxBind()
		{
			tufxLoader = GetTufxLoaderInstance(out Type t);
			if (tufxLoader == null) return false;
			tufxChange = t.GetMethod("ChangeProfileForScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			Type sceneType = t.Assembly.GetType("TUFX.TUFXScene", false);
			if (tufxChange == null || sceneType == null) return false;
			tufxFlightScene = Enum.Parse(sceneType, "Flight");
			PropertyInfo cur = t.GetProperty("CurrentProfileName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			tufxStartProfile = cur != null ? cur.GetValue(tufxLoader, null) as string : null;
			if (string.IsNullOrEmpty(tufxStartProfile)) tufxStartProfile = "Default-Flight";
			tufxStartAa = QualitySettings.antiAliasing;
			return true;
		}

		private IDictionary TufxProfileTable()
		{
			PropertyInfo p = tufxLoader.GetType().GetProperty("Profiles", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return p != null ? p.GetValue(tufxLoader, null) as IDictionary : null;
		}

		// NONE first; the scene defaults of other scenes (main menu, KSC, editor, tracking, IVA) are left out
		private List<string> TufxProfiles()
		{
			List<string> names = new List<string>();
			IDictionary d = TufxProfileTable();
			if (d != null) foreach (object k in d.Keys) names.Add(k as string);
			names.RemoveAll(n => n == null || (n.StartsWith("Default-", StringComparison.Ordinal) && n != "Default-Flight" && n != "Default-Empty"));
			names.Sort(StringComparer.Ordinal);
			string only = Environment.GetEnvironmentVariable("KK_TUFX_PROFILES");
			if (!string.IsNullOrEmpty(only))
			{
				List<string> keep = new List<string>(only.Split(','));
				names.RemoveAll(n => !keep.Contains(n));
			}
			int max;
			if (int.TryParse(Environment.GetEnvironmentVariable("KK_TUFX_MAX") ?? "", out max) && max > 0 && names.Count > max) names.RemoveRange(max, names.Count - max);
			names.Insert(0, "NONE");
			return names;
		}

		private bool TufxHdr(string name)
		{
			if (name == "NONE") return false;
			IDictionary d = TufxProfileTable();
			object prof = d != null && d.Contains(name) ? d[name] : null;
			if (prof == null) return false;
			BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			PropertyInfo p = prof.GetType().GetProperty("HDREnabled", f);
			if (p != null) return p.GetValue(prof, null) is bool b && b;
			FieldInfo fi = prof.GetType().GetField("HDREnabled", f);
			return fi != null && fi.GetValue(prof) is bool b2 && b2;
		}

		// TUFX turns MSAA off for HDR + bloom and never back; every profile starts from the game's own setting, as for a player
		private void TufxSet(string name)
		{
			QualitySettings.antiAliasing = tufxStartAa;
			bool none = name == "NONE";
			try { tufxChange.Invoke(tufxLoader, new object[] { none ? "Default-Empty" : name, tufxFlightScene }); }
			catch (Exception ex) { Debug.Log("[UT Test] tufx: profile " + name + " failed: " + (ex.InnerException ?? ex).Message); }
			foreach (Camera c in TufxCameras())
			{
				Behaviour layer = c.GetComponent("PostProcessLayer") as Behaviour;
				if (layer == null) continue;
				layer.enabled = !none;
				if (none) c.allowHDR = false;
				else
				{
					MethodInfo reset = layer.GetType().GetMethod("ResetHistory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (reset != null) try { reset.Invoke(layer, null); } catch (Exception) { }
				}
			}
		}

		private static List<Camera> TufxCameras()
		{
			List<Camera> list = new List<Camera>(Camera.allCameras);
			if (InternalCamera.Instance != null) { Camera ic = InternalCamera.Instance.GetComponent<Camera>(); if (ic != null && !list.Contains(ic)) list.Add(ic); }
			if (ScaledCamera.Instance != null)
			{
				if (ScaledCamera.Instance.cam != null && !list.Contains(ScaledCamera.Instance.cam)) list.Add(ScaledCamera.Instance.cam);
				if (ScaledCamera.Instance.galaxyCamera != null && !list.Contains(ScaledCamera.Instance.galaxyCamera)) list.Add(ScaledCamera.Instance.galaxyCamera);
			}
			return list;
		}

		private IEnumerator TufxSettle()
		{
			for (int i = 0; i < 4; i++) yield return null;
			yield return new WaitForSecondsRealtime(0.2f);
		}

		private IEnumerator TufxGrab()
		{
			yield return new WaitForEndOfFrame();
			Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
			tufxLast = Reduce(tex);
			UnityEngine.Object.Destroy(tex);
		}

		private static void SetRenderers(List<Renderer> list, bool on)
		{
			foreach (Renderer r in list) if (r != null) r.enabled = on;
		}

		// ---------------------------------------------------------------- image numbers

		private static TufxShot Reduce(Texture2D tex)
		{
			Color32[] src = tex.GetPixels32();
			int w = tex.width, h = tex.height;
			TufxShot s = new TufxShot { L = new float[TufxW * TufxH], Px = new Color32[TufxW * TufxH] };
			for (int y = 0; y < TufxH; y++)
			{
				int y0 = y * h / TufxH, y1 = Math.Max(y0 + 1, (y + 1) * h / TufxH);
				for (int x = 0; x < TufxW; x++)
				{
					int x0 = x * w / TufxW, x1 = Math.Max(x0 + 1, (x + 1) * w / TufxW);
					int r = 0, g = 0, b = 0, n = 0;
					for (int yy = y0; yy < y1; yy++)
					{
						int row = yy * w;
						for (int xx = x0; xx < x1; xx++) { Color32 c = src[row + xx]; r += c.r; g += c.g; b += c.b; n++; }
					}
					int i = y * TufxW + x;
					s.Px[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
					s.L[i] = (0.299f * r + 0.587f * g + 0.114f * b) / (n * 255f);
				}
			}
			double sum = 0, sq = 0; int white = 0, black = 0;
			for (int i = 0; i < s.L.Length; i++)
			{
				float l = s.L[i];
				sum += l; sq += l * l;
				if (l > 0.98f) white++;
				if (l < 0.01f) black++;
			}
			int count = s.L.Length;
			s.Mean = (float)(sum / count);
			s.Std = (float)Math.Sqrt(Math.Max(0.0, sq / count - s.Mean * (double)s.Mean));
			s.White = white / (float)count;
			s.Black = black / (float)count;
			double lap = 0;
			for (int y = 1; y < TufxH - 1; y++)
				for (int x = 1; x < TufxW - 1; x++)
				{
					int i = y * TufxW + x;
					lap += Math.Abs(4f * s.L[i] - s.L[i - 1] - s.L[i + 1] - s.L[i - TufxW] - s.L[i + TufxW]);
				}
			s.Sharp = (float)(lap / ((TufxW - 2) * (TufxH - 2)));
			return s;
		}

		private static float MeanAbs(TufxShot a, TufxShot b)
		{
			double s = 0;
			for (int i = 0; i < a.L.Length; i++) s += Math.Abs(a.L[i] - b.L[i]);
			return (float)(s / a.L.Length);
		}

		private static float NewBlack(TufxShot s, TufxShot none)
		{
			int n = 0;
			for (int i = 0; i < s.L.Length; i++) if (s.L[i] < 0.01f && none.L[i] > 0.12f) n++;
			return n / (float)s.L.Length;
		}

		// the camera on the worms themselves (the creature object is not where the heap lies after a strike)
		private GameObject tufxLook;
		private void TufxAim(MohoWormGpuSkin gpu, float distance, float pitch, float heading)
		{
			if (tufxLook == null) tufxLook = new GameObject("UT TUFX look");
			tufxLook.transform.position = gpu.PointCentroid;
			FlightCamera.fetch.SetTargetTransform(tufxLook.transform);
			FlightCamera.fetch.SetDistanceImmediate(distance);
			FlightCamera.fetch.camPitch = pitch;
			FlightCamera.fetch.camHdg = heading;
		}

		private static float MaskedDiff(TufxShot a, TufxShot b, bool[] mask, bool inside)
		{
			double s = 0; int n = 0;
			for (int i = 0; i < a.L.Length; i++) if (mask[i] == inside) { s += Math.Abs(a.L[i] - b.L[i]); n++; }
			return n > 0 ? (float)(s / n) : 0f;
		}

		// share of the frame outside the (widened) worm outline that changes visibly when the worm is hidden
		private static float Spill(TufxShot a, TufxShot b, bool[] wide)
		{
			int n = 0;
			for (int i = 0; i < a.L.Length; i++) if (!wide[i] && Math.Abs(a.L[i] - b.L[i]) > 0.12f) n++;
			return n / (float)a.L.Length;
		}

		private static bool[] Dilate(bool[] m, int r)
		{
			bool[] h = new bool[m.Length], o = new bool[m.Length];
			for (int y = 0; y < TufxH; y++)
				for (int x = 0; x < TufxW; x++)
				{
					if (!m[y * TufxW + x]) continue;
					for (int k = Math.Max(0, x - r); k <= Math.Min(TufxW - 1, x + r); k++) h[y * TufxW + k] = true;
				}
			for (int y = 0; y < TufxH; y++)
				for (int x = 0; x < TufxW; x++)
				{
					if (!h[y * TufxW + x]) continue;
					for (int k = Math.Max(0, y - r); k <= Math.Min(TufxH - 1, y + r); k++) o[k * TufxW + x] = true;
				}
			return o;
		}

		private static string F(float x) { return float.IsNaN(x) ? "" : x.ToString("F4", CultureInfo.InvariantCulture); }
		private static string Pct(float x) { return float.IsNaN(x) ? "?" : (x * 100f).ToString("F0", CultureInfo.InvariantCulture) + " %"; }
	}
}
