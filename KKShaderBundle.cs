using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Loads and retains shader references from the shared Dread asset bundle.</summary>
public static class KKShaderBundle
{
	private static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
	private static bool loadTried;
	private static string failure;

	private static readonly Dictionary<string, ComputeShader> computes = new Dictionary<string, ComputeShader>(StringComparer.Ordinal);

	public static string Failure { get { return failure; } }

	/// <summary>A compute shader from the bundle by asset name (e.g. "WormSim"), or null.</summary>
	public static ComputeShader FindCompute(string name)
	{
		if (!loadTried) Find("KK/WormTube");
		ComputeShader c;
		return computes.TryGetValue(name, out c) ? c : null;
	}

	public static Shader Find(string name)
	{
		Shader shader;
		if (shaders.TryGetValue(name, out shader)) return shader;
		if (!loadTried)
		{
			string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "UnkerbalThings", "Shaders", "dread.bundle");
			Load(path);
		}
		return shaders.TryGetValue(name, out shader) ? shader : Shader.Find(name);
	}

	public static Shader Find(string name, string bundlePath)
	{
		Shader shader;
		if (shaders.TryGetValue(name, out shader)) return shader;
		if (!loadTried) Load(bundlePath);
		return shaders.TryGetValue(name, out shader) ? shader : Shader.Find(name);
	}

	private static void Load(string path)
	{
		loadTried = true;
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) { failure = "bundle not found: " + path; return; }
		AssetBundle bundle = null;
		try
		{
			bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null) { failure = "bundle failed to load: " + path; return; }
			Shader[] all = bundle.LoadAllAssets<Shader>();
			for (int i = 0; i < all.Length; i++) if (all[i] != null) shaders[all[i].name] = all[i];
			ComputeShader[] cs = bundle.LoadAllAssets<ComputeShader>();
			for (int i = 0; i < cs.Length; i++) if (cs[i] != null) computes[cs[i].name] = cs[i];
		}
		catch (Exception ex) { failure = ex.GetType().Name + ": " + ex.Message; }
		finally { if (bundle != null) bundle.Unload(false); }
	}
}
