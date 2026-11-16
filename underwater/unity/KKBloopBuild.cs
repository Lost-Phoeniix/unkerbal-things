using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Imports bloop.fbx and builds bloop.bundle (the model under the name "bloop"), then lists what the game will find in it.
public static class KKBloopBuild
{
	static string Arg(string name, string def)
	{
		string[] a = Environment.GetCommandLineArgs();
		for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
		return def;
	}

	public static void Run()
	{
		try
		{
			string src = Path.GetFullPath(Arg("-kkFbx", "bloop.fbx"));
			string bundleDir = Path.GetFullPath(Arg("-kkBundleDir", "bundle-out"));
			Directory.CreateDirectory(bundleDir);
			string dstDir = "Assets/KKBloop";
			Directory.CreateDirectory(dstDir);
			File.Copy(src, dstDir + "/bloop.fbx", true);
			AssetDatabase.ImportAsset(dstDir + "/bloop.fbx", ImportAssetOptions.ForceUpdate);
			ModelImporter mi = (ModelImporter)AssetImporter.GetAtPath(dstDir + "/bloop.fbx");
			mi.animationType = ModelImporterAnimationType.Generic;
			mi.importAnimation = false;
			mi.isReadable = true;
			mi.importMaterials = false;
			mi.SaveAndReimport();
			AssetBundleBuild b = new AssetBundleBuild { assetBundleName = "bloop.bundle", assetNames = new[] { dstDir + "/bloop.fbx" }, addressableNames = new[] { "bloop" } };
			if (BuildPipeline.BuildAssetBundles(bundleDir, new[] { b }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64) == null) throw new Exception("build failed");
			GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(dstDir + "/bloop.fbx");
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) Debug.Log("[KKBloop] " + t.name + " " + t.position);
			SkinnedMeshRenderer r = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
			Debug.Log("[KKBloop] mesh " + r.sharedMesh.vertexCount + " vertices, " + r.sharedMesh.triangles.Length / 3 + " triangles, bundle " + new FileInfo(Path.Combine(bundleDir, "bloop.bundle")).Length + " bytes");
			EditorApplication.Exit(0);
		}
		catch (Exception e) { Debug.LogError("[KKBloop] FATAL " + e); EditorApplication.Exit(1); }
	}
}
