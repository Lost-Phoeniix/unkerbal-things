using System;
using System.Collections.Generic;
using UnityEngine;

// Moho/Eve creature, part 3 of 3: the code-built skin. Knows nothing about gait or IK: it reads the named joints of a MohoPose and
// lofts smooth tubes along them (torso+neck as one tube, head, four limbs from shoulder to knuckles as one tube each, 20 finger/toe
// tubes). Ring cross-sections (ribs, spine, scapulae, pelvis crests, elbow/knee points, face) are static tables; only the ring centres
// and frames move per frame. Swappable: see MohoBoneSkin in MohoRig.cs for a skinned replacement driven by the same pose.
public sealed class MohoSkinProcedural : IMohoSkin
{
	sealed class Tube
	{
		public int rings, sides, v0;
		public float[] ex, ey, tint, mottle;
		public Vector3[] pos, A, B;
		public Tube(int r, int s, int first)
		{
			rings = r; sides = s; v0 = first;
			ex = new float[r * s]; ey = new float[r * s]; tint = new float[r * s]; mottle = new float[r * s];
			pos = new Vector3[r]; A = new Vector3[r]; B = new Vector3[r];
		}
	}

	public GameObject Object;
	public Mesh MeshData;
	public int VertexCount, TriangleCount;
	readonly List<Tube> tubes = new List<Tube>();
	Tube body, head;
	readonly Tube[] limb = new Tube[4];
	readonly Tube[,] finger = new Tube[4, 5];
	Vector3[] verts;
	int[] tris;
	bool flipChecked;
	int tubeCounter;
	public Material SkinMaterial;
	public string ShaderName;

	const int BodySides = 32, LimbSides = 16, FingerSides = 6, HeadSides = 28;
	static readonly int[] BodyPer = { 8, 40, 40, 10, 14, 14 };
	static readonly float[] BodySeg = { 0f, 4.5f, 12.5f, 20.5f, 25.5f, 31f, 36.5f };
	const int ArmSeg1 = 22, ArmSeg2 = 22, PalmRings = 10, FingerSeg = 4;
	const float HeadH = MohoProportions.HeadHeight;

	public static float HandPalmLength(int limb)
	{
		float p = limb < 2 ? MohoProportions.HandPalm : MohoProportions.FootPalm;
		float dv = (limb < 2 ? MohoProportions.WristLift : MohoProportions.AnkleLift) - MohoProportions.KnuckleLift;
		return Mathf.Sqrt(p * p + dv * dv);
	}

	public MohoSkinProcedural(Transform parent, Shader shader)
	{
		Object = new GameObject("MohoSkin");
		if (parent != null) Object.transform.SetParent(parent, false);
		BuildTopology();
		MeshData = new Mesh();
		MeshData.name = "MohoSkin";
		MeshData.MarkDynamic();
		MeshData.vertices = verts;
		MeshData.uv = BuildUvs();
		MeshData.triangles = tris;
		Object.AddComponent<MeshFilter>().sharedMesh = MeshData;
		MeshRenderer mr = Object.AddComponent<MeshRenderer>();
		// Sun-lit stock shader. On airless Moho the night side of a thin body is black, so where KSP's emissive-diffuse shader exists a faint
		// self-light floor keeps the pale skin readable (the sun still does the shading).
		bool fill = false;
		if (shader == null) { shader = Shader.Find("KSP/Emissive/Diffuse"); fill = shader != null; }
		if (shader == null) shader = Shader.Find("KSP/Diffuse");
		if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
		if (shader == null) shader = Shader.Find("Diffuse");
		SkinMaterial = new Material(shader);
		Texture2D skinTex = MakeSkinTexture();
		SkinMaterial.mainTexture = skinTex;
		if (fill && SkinMaterial.HasProperty("_Emissive") && SkinMaterial.HasProperty("_EmissiveColor"))
		{
			SkinMaterial.SetTexture("_Emissive", skinTex);
			SkinMaterial.SetColor("_EmissiveColor", new Color(0.10f, 0.10f, 0.11f, 1f));
		}
		ShaderName = shader.name;
		if (SkinMaterial.HasProperty("_Color")) SkinMaterial.color = Color.white;
		mr.sharedMaterial = SkinMaterial;
		mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		mr.receiveShadows = false;
		Object.layer = 0;
	}

	public void Destroy()
	{
		if (Object != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(Object); else UnityEngine.Object.DestroyImmediate(Object); }
	}

	// vertex world position of a finger tip (for tests): tip ring - 1 of the middle finger
	public Vector3 TipWorld(int limbIndex)
	{
		Tube t = finger[limbIndex, 2];
		Vector3 l = verts[t.v0 + (t.rings - 2) * t.sides];
		return Object.transform.TransformPoint(l);
	}

	// ============================================================ topology and static tables
	Tube NewTube(int rings, int sides)
	{
		int first = 0;
		if (tubes.Count > 0) { Tube p = tubes[tubes.Count - 1]; first = p.v0 + p.rings * p.sides; }
		Tube t = new Tube(rings, sides, first);
		tubes.Add(t);
		tubeCounter++;
		return t;
	}

	void BuildTopology()
	{
		int bodyRings = 1; for (int i = 0; i < BodyPer.Length; i++) bodyRings += BodyPer[i];
		body = NewTube(bodyRings, BodySides); BuildBodyTable(body);
		head = NewTube(44, HeadSides); BuildHeadTable(head);
		for (int l = 0; l < 4; l++)
		{
			int rings = ArmSeg1 + ArmSeg2 + 1 + PalmRings;
			limb[l] = NewTube(rings, LimbSides);
			BuildLimbTable(limb[l], l);
			for (int f = 0; f < 5; f++)
			{
				finger[l, f] = NewTube(1 + 3 * FingerSeg, FingerSides);
				BuildFingerTable(finger[l, f], l, f);
			}
		}
		Tube last = tubes[tubes.Count - 1];
		VertexCount = last.v0 + last.rings * last.sides;
		verts = new Vector3[VertexCount];
		List<int> ti = new List<int>();
		foreach (Tube t in tubes)
		{
			for (int i = 0; i < t.rings - 1; i++)
				for (int j = 0; j < t.sides; j++)
				{
					int j2 = (j + 1) % t.sides;
					int a = t.v0 + i * t.sides + j, b = t.v0 + i * t.sides + j2, c = t.v0 + (i + 1) * t.sides + j, d = t.v0 + (i + 1) * t.sides + j2;
					ti.Add(a); ti.Add(c); ti.Add(b);
					ti.Add(b); ti.Add(c); ti.Add(d);
				}
		}
		tris = ti.ToArray();
		TriangleCount = tris.Length / 3;
	}

	Vector2[] BuildUvs()
	{
		Vector2[] uv = new Vector2[VertexCount];
		foreach (Tube t in tubes)
			for (int k = 0; k < t.rings * t.sides; k++)
				uv[t.v0 + k] = new Vector2(Mathf.Clamp01(t.mottle[k]), Mathf.Clamp01(t.tint[k]));
		return uv;
	}

	static Texture2D MakeSkinTexture()
	{
		int w = 16, h = 64;
		Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
		float[] ky = { 0f, 0.25f, 0.6f, 1f };
		Color[] kc = { new Color(0.78f, 0.76f, 0.72f), new Color(0.68f, 0.67f, 0.65f), new Color(0.46f, 0.46f, 0.50f), new Color(0.22f, 0.20f, 0.25f) };
		Color[] px = new Color[w * h];
		for (int y = 0; y < h; y++)
		{
			float v = y / (float)(h - 1);
			int i = 0; while (i < 2 && v > ky[i + 1]) i++;
			Color c = Color.Lerp(kc[i], kc[i + 1], Mathf.Clamp01((v - ky[i]) / (ky[i + 1] - ky[i])));
			for (int x = 0; x < w; x++)
			{
				float m = 1f - 0.16f * (x / (float)(w - 1));
				px[y * w + x] = new Color(c.r * m, c.g * m, c.b * m, 1f);
			}
		}
		tex.SetPixels(px);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		tex.Apply(false);
		return tex;
	}

	// ---------------------------------------------------------------- math helpers
	static float Slope(float[] k, float[] v, int i)
	{
		int n = k.Length;
		if (i == 0) return (v[1] - v[0]) / (k[1] - k[0]);
		if (i == n - 1) return (v[n - 1] - v[n - 2]) / (k[n - 1] - k[n - 2]);
		return (v[i + 1] - v[i - 1]) / (k[i + 1] - k[i - 1]);
	}

	static float Prof(float[] k, float[] v, float x)
	{
		int n = k.Length;
		if (x <= k[0]) return v[0];
		if (x >= k[n - 1]) return v[n - 1];
		int i = 0; while (x > k[i + 1]) i++;
		float h = k[i + 1] - k[i], t = (x - k[i]) / h;
		float t2 = t * t, t3 = t2 * t;
		return (2 * t3 - 3 * t2 + 1) * v[i] + (t3 - 2 * t2 + t) * h * Slope(k, v, i) + (-2 * t3 + 3 * t2) * v[i + 1] + (t3 - t2) * h * Slope(k, v, i + 1);
	}

	static float Gauss(float x, float s) { return Mathf.Exp(-x * x / (2f * s * s)); }
	static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
	static float AngDiff(float a, float b) { float d = Mathf.Repeat(a - b + Mathf.PI, 2f * Mathf.PI) - Mathf.PI; return d; }

	void Put(Tube t, int i, int j, float rx, float ry, float th, float off, float tint, float mottleSeed)
	{
		float c = Mathf.Cos(th), s = Mathf.Sin(th);
		float px = rx * c, py = ry * s;
		float nx = c / Mathf.Max(rx, 1e-3f), ny = s / Mathf.Max(ry, 1e-3f);
		float nl = Mathf.Sqrt(nx * nx + ny * ny);
		if (nl > 1e-6f) { nx /= nl; ny /= nl; }
		int k = i * t.sides + j;
		t.ex[k] = px + nx * off; t.ey[k] = py + ny * off; t.tint[k] = tint;
		float m = 0.5f * (Mathf.PerlinNoise(i * 0.09f + tubeCounter * 7.1f + c * 0.8f, 3f + s * 0.8f) + Mathf.PerlinNoise(i * 0.31f + 40f + c * 1.3f, tubeCounter + s * 1.3f));
		t.mottle[k] = m;
	}

	// ---------------------------------------------------------------- torso + neck
	static readonly float[] TS = { 0f, 1.2f, 2.8f, 4.5f, 6.5f, 8.5f, 11f, 14f, 17f, 19.5f, 21.5f, 23.5f, 25.5f, 27.5f, 31f, 36.5f };
	static readonly float[] TRX = { 1.5f, 1.5f, 2.1f, 2.5f, 2.0f, 1.65f, 1.55f, 2.3f, 3.2f, 3.65f, 3.7f, 3.1f, 1.9f, 1.5f, 1.3f, 1.25f };
	static readonly float[] TRY = { 1.3f, 1.3f, 1.8f, 2.1f, 1.75f, 1.4f, 1.35f, 1.9f, 2.6f, 2.95f, 3.0f, 2.6f, 1.7f, 1.4f, 1.2f, 1.15f };

	void BuildBodyTable(Tube t)
	{
		int ring = 0;
		for (int k = 0; k < BodyPer.Length; k++)
		{
			for (int m = 0; m < BodyPer[k]; m++) { BodyRing(t, ring, BodySeg[k] + (BodySeg[k + 1] - BodySeg[k]) * m / BodyPer[k]); ring++; }
		}
		BodyRing(t, ring, BodySeg[BodySeg.Length - 1]);
	}

	void BodyRing(Tube t, int i, float s)
	{
		float rx = Prof(TS, TRX, s) * 1.2f, ry = Prof(TS, TRY, s) * 1.2f;
		if (s < 1.4f) { float cap = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((1.4f - s) / 1.4f, 2f))); rx *= cap; ry *= cap; }
		for (int j = 0; j < t.sides; j++)
		{
			float th = 2f * Mathf.PI * j / t.sides;
			float sn = Mathf.Sin(th);
			float dors = AngDiff(th, Mathf.PI * 0.5f);
			float off = 0f, tint = 0.10f + 0.14f * Mathf.Max(0f, -sn);
			// ribs: slanted pulses on the sides and belly, between spine and sternum
			float wr = SS(11.5f, 13.5f, s) * (1f - SS(22f, 24f, s));
			if (wr > 0f)
			{
				float phase = (s + 0.9f * sn - 12f) / 1.05f;
				float pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * phase), 2f);
				float mask = SS(0.92f, 0.55f, sn) * SS(-0.97f, -0.6f, sn);
				off += (0.30f * pulse - 0.10f * (1f - pulse)) * mask * wr;
				tint += 0.20f * (1f - pulse) * mask * wr;
			}
			// spine: vertebra knobs on the back, continuing faintly up the neck
			float wsp = SS(2.5f, 4.5f, s) * (1f - 0.65f * SS(24.5f, 28f, s)) * (1f - SS(34f, 36.5f, s));
			float vert = 0.55f + 0.45f * Mathf.Cos(2f * Mathf.PI * s / 0.85f);
			off += (0.10f + 0.28f * vert) * Gauss(dors, 0.15f) * wsp;
			tint += 0.10f * Gauss(dors, 0.2f) * wsp;
			// shoulder blades, pelvis crests, sternum, collar tendons
			float sc = Gauss(s - 19.5f, 2.4f);
			off += 0.6f * sc * (Gauss(AngDiff(th, Mathf.PI * 0.5f - 0.78f), 0.30f) + Gauss(AngDiff(th, Mathf.PI * 0.5f + 0.78f), 0.30f));
			float pc = Gauss(s - 5.0f, 1.3f);
			off += 0.38f * pc * (Gauss(AngDiff(th, 0.65f), 0.30f) + Gauss(AngDiff(th, Mathf.PI - 0.65f), 0.30f));
			off += 0.22f * Gauss(AngDiff(th, Mathf.PI * 1.5f), 0.12f) * SS(14f, 16f, s) * (1f - SS(23f, 25f, s));
			float wn = SS(25.5f, 28f, s);
			off += 0.14f * wn * (Gauss(AngDiff(th, Mathf.PI * 1.5f - 0.55f), 0.2f) + Gauss(AngDiff(th, Mathf.PI * 1.5f + 0.55f), 0.2f));
			// gaunt belly
			float wb = SS(5f, 7.5f, s) * (1f - SS(15f, 18f, s));
			off -= 0.55f * wb * Mathf.Max(0f, -sn);
			tint += 0.08f * wb;
			Put(t, i, j, rx, ry, th, off, tint, 0f);
		}
	}

	// ---------------------------------------------------------------- head
	static readonly float[] HY = { 0f, 0.03f, 0.10f, 0.22f, 0.38f, 0.55f, 0.70f, 0.85f, 0.95f, 1f };
	static readonly float[] HW = { 0f, 0.22f, 0.45f, 0.66f, 0.86f, 0.97f, 1f, 0.90f, 0.62f, 0f };
	static readonly float[] HDF = { 0f, 0.30f, 0.55f, 0.85f, 0.95f, 1f, 0.97f, 0.80f, 0.55f, 0f };
	static readonly float[] HDB = { 0f, 0.20f, 0.35f, 0.55f, 0.80f, 0.97f, 1f, 0.95f, 0.70f, 0f };

	static float Sp(float x, float p) { return Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), p); }

	void BuildHeadTable(Tube t)
	{
		for (int i = 0; i < t.rings; i++)
		{
			float y = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / (t.rings - 1));
			float w = Prof(HY, HW, y) * MohoProportions.HeadWidth * 0.5f;
			float df = Prof(HY, HDF, y) * 3.2f, db = Prof(HY, HDB, y) * 4.0f;
			float pole = SS(0f, 0.08f, y) * SS(1f, 0.92f, y);
			for (int j = 0; j < t.sides; j++)
			{
				float th = 2f * Mathf.PI * j / t.sides;
				float c = Mathf.Cos(th), s = Mathf.Sin(th);
				float depth = c >= 0f ? df : db;
				float fw = SS(0.15f, 0.65f, c);
				float off = 0f, tint = 0.05f + 0.10f * (1f - fw);
				float g;
				// nose: bridge and soft tip, brow ridge, cheekbones
				off += 0.45f * Gauss(s, 0.10f) * Gauss(y - 0.42f, 0.11f) * fw;
				off += 0.30f * Gauss(s, 0.09f) * Gauss(y - 0.34f, 0.04f) * fw;
				off += 0.26f * Gauss(y - 0.63f, 0.045f) * fw;
				off += 0.20f * Gauss(Mathf.Abs(s) - 0.65f, 0.18f) * Gauss(y - 0.47f, 0.08f) * fw;
				// eye sockets: shallow dents under closed skin, darker
				g = (Gauss(s - 0.42f, 0.15f) + Gauss(s + 0.42f, 0.15f)) * Gauss(y - 0.58f, 0.05f) * fw;
				off -= 0.30f * g; tint += 0.45f * g;
				// mouth: one faint crease
				g = Gauss(y - 0.20f, 0.016f) * Gauss(s, 0.34f) * fw;
				off -= 0.13f * g; tint += 0.5f * g;
				// ears
				g = (Gauss(AngDiff(th, Mathf.PI * 0.5f), 0.22f) + Gauss(AngDiff(th, Mathf.PI * 1.5f), 0.22f)) * Gauss(y - 0.52f, 0.09f);
				off += 0.35f * g; tint += 0.08f * g;
				tint += 0.12f * SS(0.8f, 1f, y);
				Put(t, i, j, Mathf.Max(depth, 1e-3f) * 1f, Mathf.Max(w, 1e-3f), th, off * pole, tint, 0f);
				int k = i * t.sides + j;
				// superellipse flattening of the section (flatter face, fuller skull) applied on the base point
				float ax = Sp(c, 0.92f) * depth, ay = Sp(s, 0.92f) * w;
				float dx = t.ex[k] - Mathf.Max(depth, 1e-3f) * c, dy = t.ey[k] - Mathf.Max(w, 1e-3f) * s;
				t.ex[k] = ax + dx; t.ey[k] = ay + dy;
			}
		}
	}

	// ---------------------------------------------------------------- limbs
	static readonly float[] ArmS = { 0f, 3f, 8f, 14f, 22f, 29f, 31f, 32f, 33f, 36f, 44f, 52f, 60f, 64f };
	static readonly float[] ArmR = { 1.5f, 1.35f, 1.12f, 0.92f, 0.80f, 0.82f, 0.98f, 1.05f, 0.95f, 0.74f, 0.60f, 0.52f, 0.47f, 0.50f };
	static readonly float[] LegS = { 0f, 3f, 9f, 16f, 24f, 31f, 33f, 34f, 35f, 38f, 46f, 56f, 64f, 68f };
	static readonly float[] LegR = { 1.7f, 1.5f, 1.3f, 1.12f, 0.92f, 0.90f, 1.05f, 1.12f, 1.0f, 0.80f, 0.66f, 0.56f, 0.52f, 0.56f };
	static readonly float[] PQ = { 0f, 0.15f, 0.35f, 0.6f, 0.9f, 1.0f, 1.05f, 1.1f, 1.15f, 1.2f };
	static readonly float[] PRX = { 0.5f, 0.9f, 1.5f, 1.85f, 1.85f, 1.6f, 1.2f, 0.7f, 0.3f, 0f };
	static readonly float[] PRY = { 0.5f, 0.45f, 0.38f, 0.34f, 0.32f, 0.28f, 0.2f, 0.12f, 0.06f, 0f };

	void BuildLimbTable(Tube t, int l)
	{
		bool arm = l < 2;
		float a = arm ? MohoProportions.ArmUpper : MohoProportions.LegUpper, b = arm ? MohoProportions.ArmLower : MohoProportions.LegLower;
		float[] ks = arm ? ArmS : LegS, kr = arm ? ArmR : LegR;
		float total = a + b;
		float palmScale = arm ? 1f : 0.8f;
		for (int i = 0; i < t.rings; i++)
		{
			float s = 0f, q = -1f;
			if (i <= ArmSeg1) s = a * i / ArmSeg1;
			else if (i <= ArmSeg1 + ArmSeg2) s = a + b * (i - ArmSeg1) / ArmSeg2;
			else q = 1.2f * (i - (ArmSeg1 + ArmSeg2)) / PalmRings;
			for (int j = 0; j < t.sides; j++)
			{
				float th = 2f * Mathf.PI * j / t.sides;
				float rx, ry, off = 0f, tint;
				if (q < 0f)
				{
					float r = Prof(ks, kr, s) * 1.5f;
					if (s < 1.2f) r *= Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((1.2f - s) / 1.2f, 2f)));
					rx = r * 1.05f; ry = r * 0.92f;
					float u = s / total;
					tint = 0.14f + 0.18f * SS(0.4f, 1f, u);
					// elbow / knee point on the bend side (th = 90 deg is the side the joint points to), forearm bone ridge
					float ej = Gauss(s - (a + 0.5f), 1.5f);
					off += (arm ? 0.45f : 0.55f) * Gauss(AngDiff(th, Mathf.PI * 0.5f), 0.5f) * ej;
					tint += 0.22f * ej;
					off += 0.07f * Gauss(AngDiff(th, Mathf.PI * 1.5f), 0.3f) * SS(a, a + 6f, s);
				}
				else
				{
					rx = Prof(PQ, PRX, q) * palmScale * 1.4f; ry = Prof(PQ, PRY, q) * 1.4f;
					tint = 0.30f + 0.2f * SS(0.4f, 1f, q);
					off += 0.08f * Gauss(AngDiff(th, Mathf.PI * 0.5f), 0.9f) * Gauss(q - 1f, 0.08f);
				}
				Put(t, i, j, rx, ry, th, off, tint, 0f);
			}
		}
	}

	static readonly float[] FU = { 0f, 0.08f, 0.30f, 0.33f, 0.38f, 0.62f, 0.66f, 0.72f, 1f };
	static readonly float[] FR = { 0.30f, 0.26f, 0.20f, 0.22f, 0.18f, 0.15f, 0.17f, 0.14f, 0.09f };

	void BuildFingerTable(Tube t, int l, int f)
	{
		for (int i = 0; i < t.rings; i++)
		{
			float u = Mathf.Min(1f, i / (float)(t.rings - 2));
			float cap = i == t.rings - 1 ? 0.0f : (i == t.rings - 2 ? 0.8f : 1f);
			float r = Prof(FU, FR, u) * cap * (l < 2 ? 1.5f : 1.8f);
			for (int j = 0; j < t.sides; j++)
			{
				float th = 2f * Mathf.PI * j / t.sides;
				Put(t, i, j, r, r * 0.82f, th, 0f, 0.42f + 0.25f * u, 0f);
			}
		}
	}

	// ============================================================ per-frame
	static readonly float[] FingerLat = { -1.4f, -0.47f, 0.47f, 1.4f };
	static readonly float[] FingerSplay = { -14f, -5f, 5f, 14f };
	static readonly float[] FingerScale = { 0.78f, 1.0f, 1.08f, 0.90f };
	static readonly float[] FingerLen = { 4.4f, 3.7f, 3.0f };
	static readonly float[] PitchPlanted = { 0f, -1f, 2f };
	static readonly float[] PitchCurled = { 20f, 50f, 80f };

	static Vector3 H(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
	{
		float t2 = t * t, t3 = t2 * t;
		return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
	}

	static void Tangents(Vector3[] P, int n, Vector3[] M)
	{
		for (int k = 0; k < n; k++)
		{
			Vector3 a = P[Mathf.Max(k - 1, 0)], c = P[Mathf.Min(k + 1, n - 1)];
			Vector3 d = c - a;
			float len = ((P[k] - a).magnitude + (c - P[k]).magnitude) * (k == 0 || k == n - 1 ? 1f : 0.5f);
			M[k] = d.sqrMagnitude > 1e-8f ? d.normalized * len : Vector3.zero;
		}
	}

	static int Sample(Vector3[] P, Vector3[] M, int segs, int[] per, Vector3[] outPos)
	{
		int o = 0;
		for (int k = 0; k < segs; k++)
		{
			float len = (P[k + 1] - P[k]).magnitude;
			for (int m = 0; m < per[k]; m++) outPos[o++] = H(P[k], M[k], P[k + 1], M[k + 1], m / (float)per[k]);
		}
		outPos[o++] = P[segs];
		return o;
	}

	static void Frames(Tube t, int count, Vector3 a0)
	{
		Vector3 prevT = Vector3.zero, A = a0;
		for (int i = 0; i < count; i++)
		{
			Vector3 T = t.pos[Mathf.Min(i + 1, t.rings - 1)] - t.pos[Mathf.Max(i - 1, 0)];
			if (T.sqrMagnitude < 1e-10f) T = prevT.sqrMagnitude > 0f ? prevT : Vector3.forward;
			T.Normalize();
			if (i == 0) { A = A - T * Vector3.Dot(A, T); if (A.sqrMagnitude < 1e-8f) A = Vector3.Cross(T, Vector3.up); }
			else A = Quaternion.FromToRotation(prevT, T) * A;
			A = (A - T * Vector3.Dot(A, T)).normalized;
			t.A[i] = A; t.B[i] = Vector3.Cross(T, A);
			prevT = T;
		}
	}

	readonly Vector3[] pp = new Vector3[8], mm = new Vector3[8];
	static readonly int[] LimbPer = { ArmSeg1, ArmSeg2 };
	static readonly int[] FingerPer = { FingerSeg, FingerSeg, FingerSeg };

	public void Apply(MohoPose pose)
	{
		Transform tr = Object.transform;
		tr.position = pose.Origin;
		tr.rotation = pose.Frame;
		if (tr.parent != null)
		{
			float ps = tr.parent.lossyScale.x;
			tr.localScale = Vector3.one * (ps > 1e-4f ? 1f / ps : 1f);
		}
		BuildBody(pose);
		BuildHead(pose);
		for (int l = 0; l < 4; l++) { BuildLimb(pose, l); for (int f = 0; f < 5; f++) BuildFinger(pose, l, f); }
		Quaternion inv = Quaternion.Inverse(pose.Frame);
		Vector3 org = pose.Origin;
		foreach (Tube t in tubes)
		{
			for (int i = 0; i < t.rings; i++)
			{
				Vector3 c = inv * (t.pos[i] - org), a = inv * t.A[i], b = inv * t.B[i];
				int basei = t.v0 + i * t.sides, bk = i * t.sides;
				for (int j = 0; j < t.sides; j++) verts[basei + j] = c + a * t.ex[bk + j] + b * t.ey[bk + j];
			}
		}
		MeshData.vertices = verts;
		if (!flipChecked)
		{
			flipChecked = true;
			double vol = 0;
			for (int k = 0; k < tris.Length; k += 3)
				vol += Vector3.Dot(verts[tris[k]], Vector3.Cross(verts[tris[k + 1]], verts[tris[k + 2]]));
			if (vol < 0)
			{
				for (int k = 0; k < tris.Length; k += 3) { int x = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = x; }
				MeshData.triangles = tris;
			}
		}
		MeshData.RecalculateNormals();
		MeshData.bounds = new Bounds(new Vector3(0f, 30f, 0f), new Vector3(220f, 140f, 220f));
	}

	void BuildBody(MohoPose pose)
	{
		MohoJoint[] js = { MohoJoint.Tail, MohoJoint.Pelvis, MohoJoint.SpineMid, MohoJoint.Chest, MohoJoint.NeckBase, MohoJoint.NeckMid, MohoJoint.Head };
		for (int k = 0; k < 7; k++) pp[k] = pose.P(js[k]);
		Tangents(pp, 7, mm);
		int n = Sample(pp, mm, 6, BodyPer, body.pos);
		Vector3 right = pose.Rotation[(int)MohoJoint.Pelvis] * Vector3.right;
		Frames(body, n, right);
	}

	void BuildHead(MohoPose pose)
	{
		Quaternion r = pose.Rotation[(int)MohoJoint.Head];
		Vector3 hf = r * Vector3.forward, hu = r * Vector3.up;
		Vector3 chin = pose.P(MohoJoint.Head) - hu * MohoProportions.HeadPivotAboveChin + hf * MohoProportions.HeadPivotBehindFace;
		for (int i = 0; i < head.rings; i++)
		{
			float y = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / (head.rings - 1));
			head.pos[i] = chin + hu * (HeadH * y);
			head.A[i] = hf; head.B[i] = Vector3.Cross(hu, hf);
		}
	}

	void BuildLimb(MohoPose pose, int l)
	{
		Tube t = limb[l];
		Vector3 S = pose.Position[MohoRigInfo.LimbJoint(l, 0)], E = pose.Position[MohoRigInfo.LimbJoint(l, 1)], W = pose.Position[MohoRigInfo.LimbJoint(l, 2)];
		Vector3 P = pose.HandForward[l], Np = pose.HandNormal[l];
		float side = (l % 2 == 0) ? -1f : 1f;
		Quaternion torsoRot = pose.Rotation[(int)(l < 2 ? MohoJoint.Chest : MohoJoint.Pelvis)];
		Vector3 outward = torsoRot * Vector3.right * side;
		pp[0] = S; pp[1] = E; pp[2] = W;
		Tangents(pp, 3, mm);
		Vector3 se = (E - S);
		mm[0] = (outward + se.normalized * 0.8f).normalized * se.magnitude;
		mm[2] = P * ((W - E).magnitude * 0.9f);
		int n = Sample(pp, mm, 2, LimbPer, t.pos);   // n = 45, ends at the wrist
		Vector3 dirSE = se.normalized;
		Vector3 poleN = E - (S + W) * 0.5f;
		if (poleN.sqrMagnitude < 1e-4f) poleN = Vector3.up;
		poleN.Normalize();
		Vector3 a0 = Vector3.Cross(poleN, dirSE);
		if (a0.sqrMagnitude < 1e-6f) a0 = Vector3.Cross(dirSE, Vector3.up);
		float len = HandPalmLength(l);
		for (int m = 1; m <= PalmRings; m++) t.pos[n - 1 + m] = W + P * (len * 1.2f * m / PalmRings);
		// transported frames to the wrist, then blend to the hand frame along the palm
		Tube tmp = t;
		FramesRange(t, n, a0);
		Vector3 aw = t.A[n - 1];
		Vector3 aHand = Vector3.Cross(Np, P).normalized;
		if (Vector3.Dot(aHand, aw) < 0f) aHand = -aHand;
		for (int m = 1; m <= PalmRings; m++)
		{
			float q = 1.2f * m / PalmRings;
			float w = SS(0f, 0.4f, q);
			Vector3 A = Vector3.Slerp(aw, aHand, w);
			A = (A - P * Vector3.Dot(A, P)).normalized;
			t.A[n - 1 + m] = A; t.B[n - 1 + m] = Vector3.Cross(P, A);
		}
	}

	static void FramesRange(Tube t, int count, Vector3 a0)
	{
		// tangents from the first `count` rings only (the wrist ring uses a one-sided difference)
		Vector3 prevT = Vector3.zero, A = a0;
		for (int i = 0; i < count; i++)
		{
			Vector3 T = t.pos[Mathf.Min(i + 1, count - 1)] - t.pos[Mathf.Max(i - 1, 0)];
			if (T.sqrMagnitude < 1e-10f) T = prevT.sqrMagnitude > 0f ? prevT : Vector3.forward;
			T.Normalize();
			if (i == 0) { A = A - T * Vector3.Dot(A, T); if (A.sqrMagnitude < 1e-8f) A = Vector3.Cross(T, Vector3.up); }
			else A = Quaternion.FromToRotation(prevT, T) * A;
			A = (A - T * Vector3.Dot(A, T)).normalized;
			t.A[i] = A; t.B[i] = Vector3.Cross(T, A);
			prevT = T;
		}
	}

	void BuildFinger(MohoPose pose, int l, int f)
	{
		Tube t = finger[l, f];
		Vector3 W = pose.Position[MohoRigInfo.LimbJoint(l, 2)];
		Vector3 P = pose.HandForward[l], Np = pose.HandNormal[l];
		float side = (l % 2 == 0) ? -1f : 1f;
		float palm = HandPalmLength(l);
		Vector3 S = Vector3.Cross(Np, P).normalized;   // lateral axis of the hand
		bool thumb = f == 4;
		Vector3 baseP;
		float splay, scale;
		if (thumb)
		{
			baseP = W + P * (palm * 0.42f) - S * (side * (l < 2 ? 2.1f : 1.6f));   // inner side
			splay = -side * 40f; scale = 0.72f;
		}
		else
		{
			float lat = FingerLat[f] * (l < 2 ? 1.5f : 1.2f);
			baseP = W + P * (palm * 0.9f) + S * lat;
			splay = FingerSplay[f]; scale = FingerScale[f];
		}
		if (l >= 2) scale *= 1.25f;
		Vector3 flat = P - Np * Vector3.Dot(P, Np);
		flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : P;
		Vector3 dirFlat = Quaternion.AngleAxis(splay, Np) * flat;
		float curl = pose.Curl[l];
		pp[0] = baseP;
		for (int j = 0; j < 3; j++)
		{
			float pitch = Mathf.Lerp(PitchPlanted[j], PitchCurled[j], curl) * Mathf.Deg2Rad;
			if (thumb) pitch *= 0.6f;
			Vector3 d = dirFlat * Mathf.Cos(pitch) - Np * Mathf.Sin(pitch);
			pp[j + 1] = pp[j] + d * (FingerLen[j] * scale * 1.35f);
		}
		Tangents(pp, 4, mm);
		Sample(pp, mm, 3, FingerPer, t.pos);
		Frames(t, t.rings,Vector3.Cross(Np, dirFlat));
	}
}
