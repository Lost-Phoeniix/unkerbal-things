using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// In-game check of the worm science part models (KK_TEST_ONLY=parts_show). Runs at the main menu, needs no save and no flight:
// takes each part's model as the game loaded it (PartLoader), renders it alone with its own camera into parts-sheet.bmp
// (one column per part: front, back, and for a part with the clip "thump" the struck pose) and writes parts.txt
// (bounds in part space, shader, textures, colliders, clips). Radial parts must lie in z >= 0 (the hull is at -Z), stack parts in y >= 0.
public partial class EntityTracker
{
	private sealed partial class TestDriver
	{
		private static readonly string[] SciencePartNames = { "utWormSpotter", "utWormGeophone", "utSeismicGenerator", "utWormScanner", "utWormImager" };

		private IEnumerator PartsShow()
		{
			const int T = 512;
			CultureInfo ci = CultureInfo.InvariantCulture;
			StringBuilder txt = new StringBuilder();
			Vector3 stage = new Vector3(0f, 9000f, 0f);
			GameObject camGo = new GameObject("UT parts camera");
			Camera cam = camGo.AddComponent<Camera>();
			cam.enabled = false;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0.20f, 0.22f, 0.25f, 1f);
			cam.fieldOfView = 30f;
			cam.cullingMask = 1 << 0;
			GameObject keyGo = new GameObject("UT parts key light");
			Light key = keyGo.AddComponent<Light>();
			key.type = LightType.Directional;
			key.intensity = 1.1f;
			key.cullingMask = 1 << 0;
			RenderTexture rt = new RenderTexture(T, T, 24);
			Texture2D tile = new Texture2D(T, T, TextureFormat.RGB24, false);
			Color32[] sheet = new Color32[SciencePartNames.Length * T * 3 * T];
			int found = 0;
			for (int p = 0; p < SciencePartNames.Length; p++)
			{
				string pname = SciencePartNames[p];
				AvailablePart ap = PartLoader.getPartInfoByName(pname);
				Transform src = ap != null && ap.partPrefab != null ? ap.partPrefab.transform.Find("model") : null;
				if (src == null)
				{
					txt.AppendLine(pname + ": NOT LOADED (no part or no model)");
					continue;
				}
				found++;
				GameObject go = UnityEngine.Object.Instantiate(src.gameObject);
				go.transform.position = stage;
				go.transform.rotation = Quaternion.identity;
				go.SetActive(true);
				foreach (Transform tr in go.GetComponentsInChildren<Transform>(true))
				{
					tr.gameObject.layer = 0;
					tr.gameObject.SetActive(true);
				}
				yield return null;
				Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
				Bounds b = new Bounds(stage, Vector3.zero);
				bool first = true;
				foreach (Renderer r in rs)
				{
					if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
				}
				Vector3 lo = b.min - stage, hi = b.max - stage;
				txt.AppendLine(pname + ": mass " + ap.partPrefab.mass.ToString("F2", ci) + " t, attach nodes " + ap.partPrefab.attachNodes.Count
					+ ", srfAttach " + (ap.partPrefab.attachRules != null && ap.partPrefab.attachRules.srfAttach));
				txt.AppendLine("  bounds x " + lo.x.ToString("F3", ci) + ".." + hi.x.ToString("F3", ci) + "  y " + lo.y.ToString("F3", ci) + ".." + hi.y.ToString("F3", ci)
					+ "  z " + lo.z.ToString("F3", ci) + ".." + hi.z.ToString("F3", ci));
				foreach (Renderer r in rs)
				{
					Material m = r.sharedMaterial;
					txt.AppendLine("  renderer " + r.name + ": shader " + (m != null && m.shader != null ? m.shader.name : "none")
						+ ", main " + (m != null && m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "MISSING")
						+ ", emissive " + (m != null && m.HasProperty("_Emissive") && m.GetTexture("_Emissive") != null ? m.GetTexture("_Emissive").name : "MISSING"));
				}
				txt.AppendLine("  colliders " + go.GetComponentsInChildren<Collider>(true).Length);
				Animation anim = go.GetComponentInChildren<Animation>(true);
				bool thump = anim != null && anim.GetClip("thump") != null;
				txt.AppendLine("  clip thump: " + (thump ? anim.GetClip("thump").length.ToString("F2", ci) + " s" : "none"));
				float dist = b.extents.magnitude / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
				Vector3[] dirs = { new Vector3(0.6f, 0.45f, 1f), new Vector3(-0.7f, 0.4f, -1f), new Vector3(1f, 0.15f, 0.35f) };
				for (int view = 0; view < 3; view++)
				{
					if (view == 2)
					{
						if (!thump) break;
						Transform hammer = null;   // it sits below the model's own root object, not directly under "model"
						foreach (Transform tr in go.GetComponentsInChildren<Transform>(true))
							if (tr.name == "hammer") hammer = tr;
						float y0 = hammer != null ? hammer.localPosition.y : 0f;
						anim["thump"].speed = 0f;
						anim.Play("thump");
						anim["thump"].time = 0.5f;
						anim.Sample();
						yield return null;
						txt.AppendLine("  hammer y at rest " + y0.ToString("F3", ci) + ", at 0.5 s " + (hammer != null ? hammer.localPosition.y.ToString("F3", ci) : "no hammer"));
					}
					Vector3 d = dirs[view].normalized;
					Vector3 centre = view == 2 ? b.center + Vector3.down * 0.2f : b.center;
					cam.transform.position = centre + d * dist * (view == 2 ? 1.25f : 1f);
					cam.transform.LookAt(centre, Vector3.up);
					cam.nearClipPlane = dist * 0.02f;
					cam.farClipPlane = dist * 6f;
					keyGo.transform.rotation = Quaternion.LookRotation(cam.transform.forward + cam.transform.right * 0.5f - cam.transform.up * 0.6f);
					cam.targetTexture = rt;
					cam.Render();
					RenderTexture.active = rt;
					tile.ReadPixels(new Rect(0, 0, T, T), 0, 0);
					RenderTexture.active = null;
					cam.targetTexture = null;
					Color32[] px = tile.GetPixels32();
					int sheetW = SciencePartNames.Length * T, row = 2 - view;
					for (int y = 0; y < T; y++)
						Array.Copy(px, y * T, sheet, (row * T + y) * sheetW + p * T, T);
				}
				UnityEngine.Object.Destroy(go);
				yield return null;
			}
			WriteBmp(Path.Combine(outDir, "parts-sheet.bmp"), sheet, SciencePartNames.Length * T, 3 * T);
			txt.AppendLine("loaded " + found + " of " + SciencePartNames.Length);
			File.WriteAllText(Path.Combine(outDir, "parts.txt"), txt.ToString());
			UnityEngine.Object.Destroy(camGo);
			UnityEngine.Object.Destroy(keyGo);
			rt.Release();
			if (found != SciencePartNames.Length) Fail("parts_show: only " + found + " of " + SciencePartNames.Length + " science parts loaded");
		}

		// 24-bit BMP, rows bottom-up like Unity's pixel arrays (the project does not reference Unity's image encoder)
		private static void WriteBmp(string path, Color32[] px, int w, int h)
		{
			int stride = (w * 3 + 3) & ~3;
			using (BinaryWriter bw = new BinaryWriter(File.Create(path)))
			{
				bw.Write((byte)'B'); bw.Write((byte)'M');
				bw.Write(54 + stride * h); bw.Write(0); bw.Write(54);
				bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24);
				bw.Write(0); bw.Write(stride * h); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
				byte[] line = new byte[stride];
				for (int y = 0; y < h; y++)
				{
					for (int x = 0; x < w; x++)
					{
						Color32 c = px[y * w + x];
						line[x * 3] = c.b; line[x * 3 + 1] = c.g; line[x * 3 + 2] = c.r;
					}
					bw.Write(line);
				}
			}
		}
	}
}
