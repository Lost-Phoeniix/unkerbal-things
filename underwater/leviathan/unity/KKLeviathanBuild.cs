using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Builds leviathan.bundle: the Alien Fish model ("leviathan", legacy Animation with the clips of anim_build.py: the loops "swim", "sprint" and
// "swim_classic" (the model's own clip), and the poses "turn_left", "turn_right", "attack" that the game lays over them additively) and its textures
// ("albedo_A", "albedo_B", "glow_A", "glow_B", "normal"). Then lists what the game will find in it.
public static class KKLeviathanBuild
{
	static string Arg(string name, string def)
	{
		string[] a = Environment.GetCommandLineArgs();
		for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
		return def;
	}

	static string CopyIn(string src, string dstDir, string name)
	{
		string dst = dstDir + "/" + name;
		File.Copy(src, dst, true);
		AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
		return dst;
	}

	static void Texture(string path, bool normal, bool srgb)
	{
		TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
		ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
		ti.sRGBTexture = srgb;
		ti.maxTextureSize = 2048;
		ti.mipmapEnabled = true;
		ti.textureCompression = TextureImporterCompression.CompressedHQ;
		ti.SaveAndReimport();
	}

	// Lays the poses over the swim loop the way the game does and prints where the tail, the nose and the jaw end up (model units; the fish faces +Z, its left is -X).
	static void PoseCheck(GameObject prefab)
	{
		GameObject inst = UnityEngine.Object.Instantiate(prefab);
		Animation a = inst.GetComponent<Animation>();
		Transform tail = null, nose = null, jaw = null;
		foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
		{
			if (t.name == "Bone.003_end") tail = t;
			if (t.name == "Bone.006_end") nose = t;
			if (t.name == "Bone.007_end") jaw = t;
		}
		Debug.Log("[KKLev] pose rest: tail " + tail.position.ToString("F3") + " nose " + nose.position.ToString("F3") + " jaw " + jaw.position.ToString("F3"));
		foreach (AnimationState st in a) { st.enabled = false; st.weight = 0f; }
		AnimationState swim = a["swim"];
		swim.layer = 0; swim.enabled = true; swim.weight = 1f; swim.speed = 0f;
		float lo = 1e9f, hi = -1e9f;
		for (int i = 0; i < 40; i++)
		{
			swim.normalizedTime = i / 40f;
			a.Sample();
			lo = Mathf.Min(lo, tail.position.x); hi = Mathf.Max(hi, tail.position.x);
		}
		Debug.Log("[KKLev] pose swim: tail sideways " + lo.ToString("F3") + " .. " + hi.ToString("F3"));
		swim.normalizedTime = 0f;
		a.Sample();
		Vector3 t0 = tail.position, n0 = nose.position, j0 = jaw.position;
		foreach (string name in new[] { "turn_left", "turn_right", "attack" })
		{
			AnimationState st = a[name];
			st.blendMode = AnimationBlendMode.Additive; st.layer = 2; st.wrapMode = WrapMode.ClampForever; st.speed = 0f; st.enabled = true;
			foreach (float w in new[] { 0.5f, 1f })
			{
				st.weight = w; st.normalizedTime = 1f;
				a.Sample();
				Debug.Log("[KKLev] pose swim + " + name + " x " + w.ToString("F1") + ": tail moved " + (tail.position - t0).ToString("F3") + " nose " + (nose.position - n0).ToString("F3") + " jaw " + (jaw.position - j0).ToString("F3"));
			}
			st.enabled = false; st.weight = 0f;
		}
		UnityEngine.Object.DestroyImmediate(inst);
	}

	public static void Run()
	{
		try
		{
			string src = Path.GetFullPath(Arg("-kkSrc", "."));
			string bundleDir = Path.GetFullPath(Arg("-kkBundleDir", "bundle-out"));
			Directory.CreateDirectory(bundleDir);
			string dstDir = "Assets/KKLeviathan";
			Directory.CreateDirectory(dstDir);
			string fbx = CopyIn(Path.Combine(src, "leviathan.fbx"), dstDir, "leviathan.fbx");
			ModelImporter mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
			mi.animationType = ModelImporterAnimationType.Legacy;
			mi.importAnimation = true;
			mi.materialImportMode = ModelImporterMaterialImportMode.None;
			mi.isReadable = false;
			ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
			if (clips.Length == 0) throw new Exception("no animation in the FBX");
			foreach (ModelImporterClipAnimation c in clips)
			{
				// Blender names a take "Armature|swim"
				c.name = c.name.Substring(c.name.LastIndexOf('|') + 1);
				bool loop = c.name.StartsWith("swim") || c.name == "sprint";
				c.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
				c.loopTime = loop;
			}
			mi.clipAnimations = clips;
			mi.animationCompression = ModelImporterAnimationCompression.Off;
			mi.SaveAndReimport();

			string[] names = { "albedo_A", "albedo_B", "glow_A", "glow_B", "normal" };
			string[] files = { "leviathan_A.png", "leviathan_B.png", "leviathan_A_glow.png", "leviathan_B_glow.png", "leviathan_normal.png" };
			string[] assets = new string[names.Length + 1];
			string[] addresses = new string[names.Length + 1];
			assets[0] = fbx;
			addresses[0] = "leviathan";
			for (int i = 0; i < names.Length; i++)
			{
				assets[i + 1] = CopyIn(Path.Combine(src, "textures", files[i]), dstDir, files[i]);
				Texture(assets[i + 1], names[i] == "normal", names[i].StartsWith("albedo") || names[i].StartsWith("glow"));
				addresses[i + 1] = names[i];
			}
			AssetBundleBuild b = new AssetBundleBuild { assetBundleName = "leviathan.bundle", assetNames = assets, addressableNames = addresses };
			if (BuildPipeline.BuildAssetBundles(bundleDir, new[] { b }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64) == null) throw new Exception("build failed");

			GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) Debug.Log("[KKLev] " + t.name + " " + t.position.ToString("F3") + " parent " + (t.parent != null ? t.parent.name : "-"));
			Animation anim = go.GetComponent<Animation>();
			if (anim == null) throw new Exception("no Animation component");
			foreach (AnimationState st in anim) Debug.Log("[KKLev] clip " + st.name + " " + st.length.ToString("F3") + " s, legacy " + st.clip.legacy + ", wrap " + st.clip.wrapMode);
			foreach (string need in new[] { "swim", "sprint", "swim_classic", "turn_left", "turn_right", "attack" }) if (anim.GetClip(need) == null) throw new Exception("clip missing: " + need);
			PoseCheck(go);
			SkinnedMeshRenderer r = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
			Debug.Log("[KKLev] mesh " + r.sharedMesh.vertexCount + " vertices, bounds " + r.bounds.ToString("F3") + ", bundle " + new FileInfo(Path.Combine(bundleDir, "leviathan.bundle")).Length + " bytes");
			EditorApplication.Exit(0);
		}
		catch (Exception e) { Debug.LogError("[KKLev] FATAL " + e); EditorApplication.Exit(1); }
	}
}
