using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lit dust for the worm swarm (setting mohoWormDustLit). Plain Unity, shared by
/// the game (MohoWormDust in MohoWormGpuSkin.cs) and the offline rig (its run script copies this file and Shaders/WormDust.shader).
///
/// Look: lunar ejecta. No air: everything flies on ballistic arcs in the swarm's local frame (gravity along its -y) and is gone
/// when it is back down at its start height. Hard sun: a bright sun side, a near-black shadow side.
/// - Puffs: soft dust clouds that light like 3D balls from any angle. Each texture frame stores how the puff is lit from six
///   directions (+x, +y, +z toward the viewer in texture A rgb, -x, -y, -z in texture B rgb, coverage in A alpha); the shader
///   blends them by the sun's direction. The frames are computed here, once, from our own noise (no asset, no outside code;
///   "six-way lighting" is a general, published games technique).
/// - Grit: small sun-lit specks, stretched along their motion.
/// - Clods: little rock meshes, lit, tumbling.
/// - Curtain: the eruption's ejecta: soil torn open in a few uneven lobes, in pulses over ~1 s (more worms punching through),
///   thrown at 30-75 degrees, inner part fast and steep, outer part slow and low; plus heavy soil slumping out low from the rim.
///   No dust column: the dust on the spire comes from the worms themselves (WormDustFromWorms below).
/// </summary>
public sealed class WormDustFx
{
	public float Gravity = 2.7f;
	public float Density = 1f;                 // scales every count (setting mohoWormDustDensity)
	/// <summary>Ground height (local frame) under x, z: particles live until they reach it. Null: until back at their start height.</summary>
	public Func<float, float, float> Ground;
	readonly ParticleSystem puffs, grit, clods;
	readonly Material puffMat, gritMat, clodMat;
	readonly GameObject root;
	readonly Color soil;

	public static bool Available(Shader dust) { return dust != null && dust.isSupported; }

	public WormDustFx(Transform frame, Color soilColour, Shader dust, Shader clodShader, float gravity)
	{
		soil = soilColour;
		Gravity = gravity;
		root = new GameObject("WormDustFx");
		root.layer = 0;
		root.transform.SetParent(frame, false);
		Texture2D a, b;
		SixWayBake.Get(out a, out b);
		puffMat = new Material(dust); puffMat.mainTexture = a; puffMat.SetTexture("_SixB", b); puffMat.EnableKeyword("KK_SIXWAY");
		gritMat = new Material(dust); gritMat.mainTexture = SixWayBake.Speck();
		clodMat = new Material(clodShader != null ? clodShader : dust);
		clodMat.color = soil * 0.55f;
		if (clodMat.HasProperty("_MainTex")) clodMat.mainTexture = Texture2D.whiteTexture;
		puffs = Make("puffs", puffMat, 2500, ParticleSystemRenderMode.Billboard);
		grit = Make("grit", gritMat, 40000, ParticleSystemRenderMode.Stretch);
		clods = Make("clods", clodMat, 1500, ParticleSystemRenderMode.Mesh);
		SetupPuffs(); SetupGrit(); SetupClods();
		SetSun(Vector3.up, Color.white, new Color(0.05f, 0.05f, 0.055f));
		SetSoft(false, 1.5f);
	}

	ParticleSystem Make(string name, Material mat, int max, ParticleSystemRenderMode mode)
	{
		var go = new GameObject(name);
		go.layer = 0;
		go.transform.SetParent(root.transform, false);
		var ps = go.AddComponent<ParticleSystem>();
		ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		var main = ps.main;
		main.playOnAwake = false; main.loop = true; main.duration = 1f; main.maxParticles = max;
		main.simulationSpace = ParticleSystemSimulationSpace.Local;
		main.gravityModifier = 0f;
		main.startLifetime = 4f;
		var em = ps.emission; em.enabled = false;
		var sh = ps.shape; sh.enabled = false;
		var fo = ps.forceOverLifetime;   // gravity along the frame's down (KSP gravity is not world -Y)
		fo.enabled = true; fo.space = ParticleSystemSimulationSpace.Local;
		fo.x = 0f; fo.y = -Gravity; fo.z = 0f;
		var r = go.GetComponent<ParticleSystemRenderer>();
		r.sharedMaterial = mat;
		r.renderMode = mode;
		r.shadowCastingMode = ShadowCastingMode.Off;
		r.receiveShadows = false;
		ps.Play();
		return ps;
	}

	void SetupPuffs()
	{
		var tsa = puffs.textureSheetAnimation;   // one of the 8 baked puff shapes per particle, fixed
		tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 2;
		tsa.animation = ParticleSystemAnimationType.WholeSheet;
		tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
		tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 7.999f);
		tsa.cycleCount = 1;
		// a cloud of grains drifting apart: it grows, and thins as it grows (the same amount of dust); the edge erodes last
		var sol = puffs.sizeOverLifetime; sol.enabled = true;
		sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.35f, 1.4f), new Keyframe(1f, 2.8f)));
		var col = puffs.colorOverLifetime; col.enabled = true;
		var g = new Gradient();
		g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.06f), new GradientAlphaKey(0.4f, 0.45f), new GradientAlphaKey(0f, 1f) });
		col.color = g;
		var rend = puffs.GetComponent<ParticleSystemRenderer>();
		rend.sortMode = ParticleSystemSortMode.Distance;
		rend.maxParticleSize = 3f;
	}

	void SetupGrit()
	{
		var rend = grit.GetComponent<ParticleSystemRenderer>();
		rend.velocityScale = 0.035f; rend.lengthScale = 1.2f;   // fast grit reads as short streaks
		var col = grit.colorOverLifetime; col.enabled = true;
		var g = new Gradient();
		g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
		col.color = g;
	}

	void SetupClods()
	{
		var rend = clods.GetComponent<ParticleSystemRenderer>();
		rend.mesh = RockMesh();
		var main = clods.main;
		main.startRotation3D = true;
		var rot = clods.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
		rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
		// a clod settles into the soil at the end of its flight instead of vanishing in one frame
		var sol = clods.sizeOverLifetime; sol.enabled = true;
		sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0f)));
	}

	/// <summary>Sun direction (world, toward the sun), its colour (with intensity) and the ambient light.</summary>
	public void SetSun(Vector3 worldToSun, Color sun, Color ambient)
	{
		Vector4 d = worldToSun.normalized;
		puffMat.SetVector("_KKSunDir", d); gritMat.SetVector("_KKSunDir", d);
		puffMat.SetColor("_KKSunColor", sun); gritMat.SetColor("_KKSunColor", sun);
		puffMat.SetColor("_KKAmbient", ambient); gritMat.SetColor("_KKAmbient", ambient);
	}

	/// <summary>Soft edges where dust meets the ground or a craft (needs the camera's depth texture).</summary>
	public void SetSoft(bool on, float range)
	{
		foreach (var m in new[] { puffMat, gritMat })
		{
			if (on) m.EnableKeyword("KK_SOFT"); else m.DisableKeyword("KK_SOFT");
			m.SetFloat("_Soft", range);
		}
	}

	public void SetGravity(float g)
	{
		Gravity = g;
		foreach (var ps in new[] { puffs, grit, clods }) { var f = ps.forceOverLifetime; f.y = -g; }
	}

	static float R(float a, float b) { return UnityEngine.Random.Range(a, b); }
	static Vector3 Cone(Vector3 dir, float spread) { return (dir.normalized + UnityEngine.Random.onUnitSphere * spread).normalized; }
	// seconds until it is back on the ground (or back at its start height when the ground is not known)
	float Flight(Vector3 at, Vector3 v)
	{
		float h = 0f;
		if (Ground != null) h = Mathf.Max(0f, at.y - Ground(at.x, at.z));
		float vy = Mathf.Max(v.y, h > 0.3f ? v.y : 0.3f);
		return Mathf.Clamp((vy + Mathf.Sqrt(vy * vy + 2f * Gravity * h)) / Gravity, 0.4f, 14f);
	}
	int N(float n) { float x = n * Density; int k = (int)x; return k + (UnityEngine.Random.value < x - k ? 1 : 0); }

	void Emit(ParticleSystem ps, Vector3 at, Vector3 v, float size, Color c, float life)
	{
		var ep = new ParticleSystem.EmitParams();
		ep.position = at; ep.velocity = v; ep.startSize = size; ep.startColor = c; ep.startLifetime = life;
		ps.Emit(ep, 1);
	}

	Color Soil(float spread) { float k = R(1f - spread, 1f + spread); return new Color(soil.r * k, soil.g * k, soil.b * k, 1f); }

	/// <summary>Dust puffs (local frame): n puffs along dir, speed m/s, size m at birth.</summary>
	public void Puffs(Vector3 at, Vector3 dir, float n, float speed, float spread, Vector3 carry, float size)
	{
		int k = N(n);
		for (int i = 0; i < k; i++)
		{
			Vector3 v = carry + Cone(dir, spread) * speed * R(0.35f, 0.9f);
			Emit(puffs, at + UnityEngine.Random.insideUnitSphere * size * 0.3f, v, size * R(0.7f, 1.3f), Soil(0.12f), Flight(at, v) * R(0.9f, 1.1f));
		}
	}

	public void Grit(Vector3 at, Vector3 dir, float n, float speed, float spread, Vector3 carry)
	{
		int k = N(n);
		for (int i = 0; i < k; i++)
		{
			Vector3 v = carry + Cone(dir, spread) * speed * R(0.4f, 1.15f);
			Emit(grit, at + UnityEngine.Random.insideUnitSphere * 0.3f, v, R(0.05f, 0.16f), Soil(0.25f), Flight(at, v));
		}
	}

	public void Clods(Vector3 at, Vector3 dir, float n, float speed, float spread, Vector3 carry)
	{
		int k = N(n);
		for (int i = 0; i < k; i++)
		{
			Vector3 v = carry + Cone(dir, spread) * speed * R(0.3f, 0.8f);
			Emit(clods, at + UnityEngine.Random.insideUnitSphere * 0.3f, v, R(0.08f, 0.3f), Color.white, Flight(at, v));
		}
	}

	/// <summary>The old one-call spray (burrow spots, breaches): mostly grit, some puffs, a few clods.</summary>
	public void Spray(Vector3 at, Vector3 dir, int count, float speed, float spread, Vector3 carry)
	{
		Grit(at, dir, count, speed, spread, carry);
		Puffs(at, dir, count / 4f, speed * 0.8f, spread * 0.8f, carry, Mathf.Lerp(1.4f, 3.6f, Mathf.Clamp01(speed / 12f)));
		Clods(at, dir, count / 10f, speed, spread * 0.7f, carry);
	}

	/// <summary>
	/// Ejecta around `center` (local frame), ring `radius`: strength 1 = a full eruption. The soil does not lift as one clean
	/// ring: it tears open in 2-4 uneven lobes and comes in pulses over ~1 s as more worms punch through, each pulse from a
	/// slightly different spot. Inner part fast and steep, outer part slow and low (the edge lands first), every clump with
	/// its own scatter. Heavy soil slumps out low over the rim. No dust column: the spire's dust comes off the worms.
	/// </summary>
	public void Curtain(Vector3 center, float radius, float strength, float speedScale, Func<float, float, float> ground)
	{
		if (Ground == null) Ground = ground;
		int nl = UnityEngine.Random.Range(2, 5);
		var la = new float[nl]; var lw = new float[nl];
		for (int i = 0; i < nl; i++) { la[i] = R(0f, Mathf.PI * 2f); lw[i] = R(0.6f, 1.5f); }
		for (int i = 0; i < PulseAt.Length; i++)
		{
			Vector2 j = UnityEngine.Random.insideUnitCircle * radius * (i == 0 ? 0.15f : 0.4f);
			var b = new Burst();
			b.when = clock + PulseAt[i] * R(0.8f, 1.25f);
			b.center = center + new Vector3(j.x, 0f, j.y);
			b.radius = radius * R(0.7f, 1.2f);
			b.strength = strength * PulseShare[i];
			b.speed = speedScale * (i == 0 ? 1.1f : R(0.7f, 1f));
			b.ground = ground; b.lobeA = la; b.lobeW = lw;
			queue.Add(b);
		}
	}

	static readonly float[] PulseAt = { 0f, 0.12f, 0.3f, 0.55f, 0.9f };
	static readonly float[] PulseShare = { 0.38f, 0.24f, 0.17f, 0.12f, 0.09f };
	struct Burst { public float when, radius, strength, speed; public Vector3 center; public Func<float, float, float> ground; public float[] lobeA, lobeW; }
	readonly System.Collections.Generic.List<Burst> queue = new System.Collections.Generic.List<Burst>();
	float clock;

	static float LobeWeight(float a, float[] la, float[] lw)
	{
		float w = 0.2f;
		for (int i = 0; i < la.Length; i++) { float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, la[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad; w += lw[i] * Mathf.Exp(-d * d / 0.2f); }
		return w;
	}

	static float LobeAngle(float[] la, float[] lw)
	{
		float wMax = 0.2f; for (int i = 0; i < lw.Length; i++) wMax += lw[i];
		float a = 0f;
		for (int t = 0; t < 10; t++) { a = R(0f, Mathf.PI * 2f); if (UnityEngine.Random.value * wMax <= LobeWeight(a, la, lw)) break; }
		return a;
	}

	void Fire(Burst b)
	{
		int n = (int)(340 * b.strength);
		for (int i = 0; i < n; i++)
		{
			float a = LobeAngle(b.lobeA, b.lobeW), band = UnityEngine.Random.value;
			Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			Vector3 at = b.center + o * b.radius * Mathf.Lerp(0.45f, 1.2f, band);
			at.y = b.ground(at.x, at.z) + 0.1f;
			float elev = (Mathf.Lerp(66f, 38f, band) + R(-12f, 10f)) * Mathf.Deg2Rad;
			float spd = Mathf.Lerp(14f, 5f, band) * b.speed * R(0.65f, 1.3f);
			Vector3 dir = o * Mathf.Cos(elev) + Vector3.up * Mathf.Sin(elev);
			Grit(at, dir, 8f, spd, 0.13f, Vector3.zero);
			Puffs(at, dir, 1.1f, spd * 0.85f, 0.1f, Vector3.zero, Mathf.Lerp(2.2f, 4.4f, band));
			float c = UnityEngine.Random.value;
			if (c < 0.06f) Clods(at, dir, 4f, spd * R(0.7f, 1.1f), 0.12f, Vector3.zero);   // a torn-off clump
			else if (c < 0.36f) Clods(at, dir, 1f, spd, 0.15f, Vector3.zero);
		}
		// heavy soil slumping out low over the rim: slow, flat, a thick low skirt
		int m = (int)(150 * b.strength);
		for (int i = 0; i < m; i++)
		{
			float a = LobeAngle(b.lobeA, b.lobeW);
			Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			Vector3 at = b.center + o * b.radius * R(0.8f, 1.4f);
			at.y = b.ground(at.x, at.z) + 0.1f;
			float elev = R(5f, 22f) * Mathf.Deg2Rad;
			Vector3 dir = o * Mathf.Cos(elev) + Vector3.up * Mathf.Sin(elev);
			float spd = R(2f, 6f) * b.speed;
			Puffs(at, dir, 0.6f, spd, 0.2f, Vector3.zero, R(2.6f, 5f));
			Grit(at, dir, 3f, spd, 0.25f, Vector3.zero);
			if (UnityEngine.Random.value < 0.25f) Clods(at, dir, 1f, spd, 0.25f, Vector3.zero);
		}
	}

	/// <summary>The old ring call mapped onto the curtain (count ~ strength, speed ~ how hard).</summary>
	public void Ring(Vector3 center, float radius, int count, float speed, float upBias, Func<float, float, float> ground)
	{
		Curtain(center, radius, Mathf.Clamp(count / 2200f, 0.2f, 1.6f), Mathf.Clamp(speed / 9f, 0.35f, 1.4f), ground);
	}

	/// <summary>Batch rig only (no play mode): advance the particles by hand.</summary>
	public bool ManualStep;
	/// <summary>Every frame: fires the curtain's pending pulses (and, in the rig, advances the particles by hand).</summary>
	public void Step(float dt)
	{
		clock += dt;
		for (int i = queue.Count - 1; i >= 0; i--)
			if (queue[i].when <= clock) { Burst b = queue[i]; queue.RemoveAt(i); Fire(b); }
		if (ManualStep) foreach (var ps in new[] { puffs, grit, clods }) ps.Simulate(dt, true, false, false);
		ClearClodsAtCamera();
	}

	// Clods are rock meshes (no shader near fade): one flying through the camera would fill the screen with a rock for a
	// frame. Those within ClodCameraClear of the camera are removed.
	const float ClodCameraClear = 2.5f;
	ParticleSystem.Particle[] clodBuf;
	void ClearClodsAtCamera()
	{
		int n = clods.particleCount;
		Camera cam = Camera.main;
		if (n == 0 || cam == null) return;
		if (clodBuf == null || clodBuf.Length < n) clodBuf = new ParticleSystem.Particle[Mathf.Max(n, 256)];
		n = clods.GetParticles(clodBuf, n);
		Vector3 eye = clods.transform.InverseTransformPoint(cam.transform.position);   // local simulation space
		float r2 = ClodCameraClear * ClodCameraClear;
		bool any = false;
		for (int i = 0; i < n; i++)
			if ((clodBuf[i].position - eye).sqrMagnitude < r2) { clodBuf[i].remainingLifetime = 0f; any = true; }
		if (any) clods.SetParticles(clodBuf, n);
	}

	public void Destroy() { if (root != null) UnityEngine.Object.Destroy(root); }

	// ------------------------------------------------------------------ meshes and textures (made once)
	static Mesh rock;
	static Mesh RockMesh()
	{
		if (rock != null) return rock;
		// a low-poly lumpy rock: an octahedron subdivided once, corners pushed in and out
		var v = new System.Collections.Generic.List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
		int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
		var tris = new System.Collections.Generic.List<int>();
		var mid = new System.Collections.Generic.Dictionary<long, int>();
		Func<int, int, int> M = (p, q) =>
		{
			long key = p < q ? ((long)p << 32) | (uint)q : ((long)q << 32) | (uint)p;
			int idx;
			if (!mid.TryGetValue(key, out idx)) { idx = v.Count; v.Add((v[p] + v[q]).normalized); mid[key] = idx; }
			return idx;
		};
		for (int i = 0; i < f.Length; i += 3)
		{
			int a = f[i], b = f[i + 1], c = f[i + 2], ab = M(a, b), bc = M(b, c), ca = M(c, a);
			tris.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
		}
		var rnd = new System.Random(7);
		for (int i = 0; i < v.Count; i++) v[i] = Vector3.Scale(v[i], new Vector3(1.15f, 0.8f, 1f)) * (0.75f + 0.5f * (float)rnd.NextDouble());
		// flat shading: unshare the vertices
		var fv = new Vector3[tris.Count]; var ft = new int[tris.Count];
		for (int i = 0; i < tris.Count; i++) { fv[i] = v[tris[i]] * 0.5f; ft[i] = i; }
		rock = new Mesh { name = "KKRock", vertices = fv, triangles = ft };
		rock.RecalculateNormals(); rock.RecalculateBounds();
		return rock;
	}
}

/// <summary>The six-way puff frames (8 shapes, 4 x 2 atlas), computed from our own 3D noise, once per game run.</summary>
public static class SixWayBake
{
	const int Frames = 8, Px = 48, Vox = 32;
	static Texture2D texA, texB, speck;

	public static void Get(out Texture2D a, out Texture2D b)
	{
		if (texA == null) Bake();
		a = texA; b = texB;
	}

	/// <summary>A soft round speck for grit (alpha only).</summary>
	public static Texture2D Speck()
	{
		if (speck != null) return speck;
		const int n = 32;
		speck = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "KKDustSpeck" };
		var px = new Color[n * n];
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
				float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
				px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Pow(a, 1.5f));
			}
		speck.SetPixels(px); speck.Apply();
		return speck;
	}

	// value noise in 3D (hash lattice, smooth), and a few octaves of it
	static float Hash(int x, int y, int z, int s)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + s * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}
	static float Noise(float x, float y, float z, int s)
	{
		int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
		float fx = x - ix, fy = y - iy, fz = z - iz;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
		float a = Mathf.Lerp(Hash(ix, iy, iz, s), Hash(ix + 1, iy, iz, s), fx), b = Mathf.Lerp(Hash(ix, iy + 1, iz, s), Hash(ix + 1, iy + 1, iz, s), fx);
		float c = Mathf.Lerp(Hash(ix, iy, iz + 1, s), Hash(ix + 1, iy, iz + 1, s), fx), d = Mathf.Lerp(Hash(ix, iy + 1, iz + 1, s), Hash(ix + 1, iy + 1, iz + 1, s), fx);
		return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
	}
	static float Fbm(float x, float y, float z, int s)
	{
		float v = 0f, amp = 0.5f, f = 1f;
		for (int o = 0; o < 4; o++) { v += amp * Noise(x * f, y * f, z * f, s + o * 31); f *= 2.03f; amp *= 0.5f; }
		return v;
	}

	static void Bake()
	{
		int W = Px * 4, H = Px * 2;
		var pa = new Color[W * H]; var pb = new Color[W * H];
		var rho = new float[Vox * Vox * Vox];
		var tr = new float[6][];
		for (int d = 0; d < 6; d++) tr[d] = new float[rho.Length];
		const float K = 4.5f;   // extinction per unit density across the whole cube (soft: thin, ragged, overlapping puffs)
		float dv = 2f / Vox;
		for (int fr = 0; fr < Frames; fr++)
		{
			var rnd = new System.Random(1000 + fr * 77);
			// a puff: 3 overlapping blobs (irregular outline) carved by fBm noise
			Vector3[] bc = new Vector3[3]; float[] br = new float[3];
			for (int k = 0; k < 3; k++)
			{
				bc[k] = k == 0 ? Vector3.zero : new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.55f;
				br[k] = k == 0 ? 0.62f : 0.32f + 0.18f * (float)rnd.NextDouble();
			}
			float freq = 2.4f + 1.2f * (float)rnd.NextDouble();
			for (int z = 0; z < Vox; z++)
				for (int y = 0; y < Vox; y++)
					for (int x = 0; x < Vox; x++)
					{
						Vector3 p = new Vector3((x + 0.5f) * dv - 1f, (y + 0.5f) * dv - 1f, (z + 0.5f) * dv - 1f);
						float shape = 0f;
						for (int k = 0; k < 3; k++) shape = Mathf.Max(shape, 1f - (p - bc[k]).magnitude / br[k]);
						float n = Fbm(p.x * freq + fr * 5.1f, p.y * freq, p.z * freq, fr);
						rho[(z * Vox + y) * Vox + x] = Mathf.Clamp01(shape * 1.1f + (n - 0.5f) * 1.8f) * Mathf.Clamp01(shape * 3f + 0.3f);
					}
			// light reaching each voxel from each axis direction (transmittance toward +x, -x, +y, -y, +z, -z)
			for (int d = 0; d < 6; d++)
			{
				int axis = d / 2, sgn = d % 2 == 0 ? 1 : -1;
				for (int u = 0; u < Vox; u++)
					for (int v = 0; v < Vox; v++)
					{
						float acc = 0f;
						for (int t = 0; t < Vox; t++)
						{
							int s = sgn > 0 ? Vox - 1 - t : t;   // walk from the lit side inward
							int x = axis == 0 ? s : u, y = axis == 1 ? s : (axis == 0 ? u : v), z = axis == 2 ? s : v;
							int idx = (z * Vox + y) * Vox + x;
							tr[d][idx] = Mathf.Exp(-K * dv * 0.5f * acc);
							acc += rho[idx];
						}
					}
			}
			// view along -z (the viewer at +z): front to back, gather the light each voxel scatters toward the viewer
			int ox = (fr % 4) * Px, oy = (fr / 4) * Px;
			for (int j = 0; j < Px; j++)
				for (int i = 0; i < Px; i++)
				{
					int vx = Mathf.Min(Vox - 1, i * Vox / Px), vy = Mathf.Min(Vox - 1, j * Vox / Px);
					float T = 1f;
					float[] L = new float[6];
					for (int vz = Vox - 1; vz >= 0; vz--)
					{
						int idx = (vz * Vox + vy) * Vox + vx;
						float a = 1f - Mathf.Exp(-K * dv * 0.5f * rho[idx]);
						if (a <= 0f) continue;
						for (int d = 0; d < 6; d++) L[d] += T * a * tr[d][idx];
						T *= 1f - a;
						if (T < 0.01f) break;
					}
					float alpha = 1f - T;
					float inv = alpha > 1e-3f ? 1f / alpha : 0f;
					// a soft circular window so no frame ever shows a square edge
					float ex = (i + 0.5f) / Px * 2f - 1f, ey = (j + 0.5f) / Px * 2f - 1f;
					float win = Mathf.Clamp01((1f - Mathf.Sqrt(ex * ex + ey * ey)) * 4f);
					int o = (oy + j) * W + ox + i;
					pa[o] = new Color(L[0] * inv, L[2] * inv, L[4] * inv, alpha * win);
					pb[o] = new Color(L[1] * inv, L[3] * inv, L[5] * inv, 1f);
				}
		}
		texA = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "KKDustSixA" };
		texB = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "KKDustSixB" };
		texA.SetPixels(pa); texA.Apply(true, true);
		texB.SetPixels(pb); texB.Apply(true, true);
	}
}

/// <summary>
/// Dust made by the worms themselves, from snapshots of their points (the game's GPU readback, the rig's GetData), so it
/// follows what the bodies really do instead of fixed shapes:
/// - where a body goes through the ground, soil is thrown along its motion: worms coming up (the spire bursting out, a heap
///   rising) throw it up and out, worms digging in kick a little up and back. More and faster for a faster body.
/// - a worm that just came out of the ground is coated in soil: it falls off as the worm moves (grit, small clumps, thin
///   dust), most in the first second, gone after ~3 s. On the rising spire this is the dust that streams off the column.
/// Emission between snapshots is spread evenly over the gap; shedding points move on with their worm's velocity.
/// </summary>
public sealed class WormDustFromWorms
{
	readonly WormDustFx fx;
	Vector4[] prev;
	float[] load;           // soil still on each worm (1 = just out of the ground)
	public float Strength = 1f;
	public int MaxCrossings = 160, MaxShed = 260;
	/// <summary>Budgets (grit per second, all sources together) so a 6000-worm eruption stays affordable.</summary>
	public float CrossingGritPerSecond = 4500f, ShedGritPerSecond = 3500f;
	/// <summary>True while the last snapshot had anything to emit (the game then reads back more often).</summary>
	public bool Active { get; private set; }

	struct Src { public Vector3 at, v, axis; public float rate, speed; public byte kind; }   // kind 0 out, 1 in, 2 shed, 3 landing
	readonly System.Collections.Generic.List<Src> src = new System.Collections.Generic.List<Src>();
	readonly System.Collections.Generic.List<Src> cross = new System.Collections.Generic.List<Src>();
	readonly System.Collections.Generic.List<Src> shed = new System.Collections.Generic.List<Src>();
	readonly System.Collections.Generic.List<Src> land = new System.Collections.Generic.List<Src>();
	public int MaxLandings = 120;
	public float LandingGritPerSecond = 3000f;
	float age, window;

	public WormDustFromWorms(WormDustFx fx) { this.fx = fx; }

	/// <summary>Forget the last snapshot (the frame moved: velocities from it would be wrong).</summary>
	public void Reset() { prev = null; src.Clear(); Active = false; }

	/// <summary>
	/// pts: every worm's points (P per worm, xyz local, w radius); states: per worm (0 surface, 1 dive, 2 under, 3 leap,
	/// 4 spire, 5 rise, 6 serpent); interval: seconds since the previous snapshot.
	/// </summary>
	public void Snapshot(Vector4[] pts, uint[] states, int P, Func<float, float, float> ground, float interval)
	{
		int W = pts.Length / P;
		src.Clear(); cross.Clear(); shed.Clear(); land.Clear();
		age = 0f; window = Mathf.Clamp(interval, 0.05f, 0.5f);
		if (load == null || load.Length != W) load = new float[W];
		bool havePrev = prev != null && prev.Length == pts.Length && interval > 0.01f && interval < 0.8f;
		float decay = Mathf.Exp(-interval / 1.1f);
		float inv = interval > 0.01f ? 1f / interval : 0f;
		for (int w = 0; w < W; w++)
		{
			uint s = states != null && w < states.Length ? states[w] : 0u;
			if (s == 2u) { load[w] = 1f; continue; }   // underground: coated again
			load[w] *= decay;
			if (havePrev && s == 0u)
			{
				// a worm that comes down hard (thrown by the kill blast, or off the spire) kicks up soil where it lands
				int mi = w * P + P / 2;
				Vector4 qm = pts[mi];
				Vector3 vm = ((Vector3)(qm - prev[mi])) * inv;
				if (vm.y < -3f && vm.sqrMagnitude < 10000f && qm.y - ground(qm.x, qm.z) < 0.9f)
				{
					var e = new Src();
					e.at = qm; e.at.y = ground(qm.x, qm.z); e.v = vm; e.speed = vm.magnitude; e.kind = 3;
					e.rate = (4f + e.speed * 1.5f) / window;   // one splash, spread over this snapshot
					land.Add(e);
				}
				continue;
			}
			if (!havePrev || !(s == 1u || s == 3u || s == 4u || s == 5u)) continue;   // the serpent (6) has its own breach dust
			int b = w * P;
			Vector4 a = pts[b];
			float ha = a.y - ground(a.x, a.z);
			for (int k = 0; k + 1 < P; k++)
			{
				Vector4 c = pts[b + k + 1];
				float hc = c.y - ground(c.x, c.z);
				if ((ha > 0f) != (hc > 0f))
				{
					int upI = ha > 0f ? k : k + 1;
					Vector3 v = ((Vector3)(pts[b + upI] - prev[b + upI])) * inv;
					float sp = v.magnitude;
					if (sp > 0.4f && sp < 100f)
					{
						float f = ha / (ha - hc);
						Vector3 x = Vector3.Lerp(a, c, f);
						x.y = ground(x.x, x.z);
						Vector3 axis = ha > 0f ? (Vector3)(a - c) : (Vector3)(c - a);   // from the buried point out of the ground
						axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
						bool outward = Vector3.Dot(v, axis) > 0f;
						var e = new Src();
						e.at = x; e.v = v; e.axis = axis; e.speed = sp; e.kind = (byte)(outward ? 0 : 1);
						e.rate = Mathf.Min(sp, 30f) * (outward ? 2f : 1.1f);   // grit per second at this hole
						cross.Add(e);
					}
				}
				a = c; ha = hc;
			}
			if (load[w] > 0.04f && (s == 3u || s == 4u || s == 5u))
			{
				int k = UnityEngine.Random.Range(0, P);
				Vector4 q = pts[b + k];
				float h = q.y - ground(q.x, q.z);
				if (h > 0.4f)
				{
					Vector3 v = ((Vector3)(q - prev[b + k])) * inv;
					float sp = v.magnitude;
					if (sp < 100f)
					{
						var e = new Src();
						e.at = q; e.v = v; e.speed = sp; e.kind = 2;
						e.rate = load[w] * (1.5f + Mathf.Min(sp, 40f) * 0.35f) * 2f;
						shed.Add(e);
					}
				}
			}
		}
		Pick(cross, MaxCrossings, CrossingGritPerSecond);
		Pick(shed, MaxShed, ShedGritPerSecond);
		Pick(land, MaxLandings, LandingGritPerSecond);
		Active = src.Count > 0;
		if (prev == null || prev.Length != pts.Length) prev = new Vector4[pts.Length];
		Array.Copy(pts, prev, pts.Length);
	}

	// keep at most `max` sources (random), scaled up to stand for the rest, then capped to the budget
	void Pick(System.Collections.Generic.List<Src> all, int max, float budget)
	{
		int n = all.Count;
		if (n == 0) return;
		for (int i = 0; i < Mathf.Min(max, n); i++) { int j = UnityEngine.Random.Range(i, n); Src t = all[i]; all[i] = all[j]; all[j] = t; }
		int keep = Mathf.Min(max, n);
		float scale = n / (float)keep, total = 0f;
		for (int i = 0; i < keep; i++) total += all[i].rate * scale;
		if (total > budget) scale *= budget / total;
		for (int i = 0; i < keep; i++) { Src e = all[i]; e.rate *= scale * Strength; src.Add(e); }
	}

	/// <summary>Every frame: emit the current snapshot's share for dt.</summary>
	public void Step(float dt)
	{
		if (src.Count == 0 || dt <= 0f) return;
		age += dt;
		if (age > window * 1.6f) { src.Clear(); Active = false; return; }   // no new snapshot came: stop, do not guess
		for (int i = 0; i < src.Count; i++)
		{
			Src e = src[i];
			float n = e.rate * dt;
			Vector3 flat = new Vector3(e.v.x, 0f, e.v.z);
			if (e.kind == 0)
			{
				// thrown up and out along the body coming through the hole; a fast body tears clumps loose
				Vector3 dir = e.axis * 0.6f + Vector3.up * 0.8f + flat.normalized * 0.25f;
				float spd = Mathf.Clamp(e.speed * 0.55f, 1.5f, 14f);
				fx.Grit(e.at, dir, n, spd, 0.45f, e.v * 0.3f);
				fx.Puffs(e.at, dir, n * 0.06f, spd * 0.7f, 0.4f, e.v * 0.2f, Mathf.Lerp(1.2f, 3.4f, Mathf.Clamp01(e.speed / 20f)));
				fx.Clods(e.at, dir, n * 0.1f, spd, 0.4f, e.v * 0.3f);
			}
			else if (e.kind == 1)
			{
				// digging in: soil kicked up and back from the hole
				Vector3 dir = Vector3.up - flat.normalized * 0.4f;
				float spd = Mathf.Clamp(e.speed * 0.45f, 1f, 5f);
				fx.Grit(e.at, dir, n, spd, 0.55f, Vector3.zero);
				fx.Puffs(e.at, dir, n * 0.05f, spd * 0.6f, 0.5f, Vector3.zero, R(1.1f, 2f));
				fx.Clods(e.at, dir, n * 0.05f, spd, 0.5f, Vector3.zero);
			}
			else if (e.kind == 3)
			{
				// a worm landing hard: a low splash of soil out from where it hit
				Vector3 dir = Vector3.up + flat.normalized * 0.5f;
				float spd = Mathf.Clamp(e.speed * 0.3f, 1.2f, 5f);
				fx.Grit(e.at, dir, n, spd, 0.7f, flat * 0.2f);
				fx.Puffs(e.at, dir, n * 0.07f, spd * 0.5f, 0.6f, flat * 0.15f, R(1.2f, 2.4f));
				fx.Clods(e.at, dir, n * 0.04f, spd, 0.6f, Vector3.zero);
			}
			else
			{
				// soil falling off a worm that just came up: it leaves with most of the worm's speed, then falls
				Vector3 at = e.at + e.v * age;
				fx.Grit(at, Vector3.up, n, 1.3f, 1f, e.v * R(0.55f, 0.95f));
				fx.Puffs(at, Vector3.up, n * 0.035f, 0.8f, 1f, e.v * 0.6f, R(0.9f, 1.8f));
				fx.Clods(at, Vector3.down, n * 0.08f, 0.6f, 1f, e.v * R(0.5f, 0.9f));
			}
		}
	}

	static float R(float a, float b) { return UnityEngine.Random.Range(a, b); }
}
