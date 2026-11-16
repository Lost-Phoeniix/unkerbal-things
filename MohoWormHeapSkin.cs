using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Moho/Eve creature's body: a swarm of about 520 worms crawling over the ground.
///
/// Approach and why (third design; the earlier ones are worth naming so nobody goes back to them):
/// - A full rope-and-contact simulation of every worm (the first skin) cost about 57 ms.
/// - Worms pinned to analytic ellipsoid shells (the second) were cheap but read as a ball with worms taped on: a perfect dome,
///   worms lined up along the shells, a visible core, and "rolling" was a rigid rotation, so nothing ever tumbled.
/// - This one is a ground swarm. Every worm is an independent agent: its head crawls over the terrain with its own wandering
///   heading, the body follows the head as a chain. Nothing defines the shape. A density grid of all worm segments gives the pile
///   height at every point (worm volume per area), so the heap is exactly as tall as the worms in it make it, worms on top slide
///   down its slopes, and a soft cohesion to the creature's anchor keeps the swarm together. Each worm also has a depth in the
///   pile (0 on top, 1 at the bottom); its points sit at that fraction of the local pile height, so worms really lie on worms.
/// - Moving: bottom worms grip the ground, so relative to the swarm they slide backward; at the back they climb up, ride forward
///   on top and pour down the front, where the swarm runs over them again. That conveyor is the rolling, and it emerges per worm
///   (uneven, loose), not from a rotation. The swarm is simulated in a frame that follows the creature, so it always keeps up;
///   the visible tumble speed is capped and above it the swarm stretches into a long, low surge with a raised front.
/// - Where two worms still intersect, the one higher in the pile is lifted just enough to ride over the other (spatial hash).
/// - Cost: simulation and mesh build run on a persistent worker thread (pure math on preallocated arrays, no Unity API); the
///   main thread samples the terrain a few times a second, uploads the finished mesh and starts the next step. Nothing is
///   allocated per frame.
/// - Rendering: 6-sided capped tubes, vertex colour carries ambient occlusion (how deep below the pile surface a point lies) and a
///   per-worm tint shift; shader KK/WormFlesh from the shader bundle (wet two-lobe highlight), else KSP's own specular shaders.
/// </summary>
public sealed class MohoWormHeapSkin : IWormSwarmSkin
{
	// ------------------------------------------------------------ layout
	public const int WormCount = 520;
	public string BodyName { get { return "heap"; } }
	public float HeadingCoherence { get { return 0f; } }
	public float HeapAspect { get { return 0f; } }
	public int WormTotal { get { return WormCount; } }
	public const int PointsPerWorm = 10;
	private const int Sides = 6;
	private const float Spacing = 0.12f;
	private const int PointCountTotal = WormCount * PointsPerWorm;
	private const float MaxHorizontal = 6.0f;
	private const float RestRadius = 2.1f;
	private const float Packing = 0.55f;
	private const float TumbleCap = 2.5f;
	private const float BodyCarry = 0.55f;   // 0: bodies purely follow their heads (combed, strudel-like), 1: carried rigidly (tails stick out)      // m/s: the fastest the conveyor runs; above it the swarm stretches instead
	private static readonly float[] Profile = { 0.62f, 0.92f, 1f, 1.04f, 1.06f, 1f, 1f, 0.94f, 0.8f, 0.52f };

	// density grid (local frame, centred on the anchor)
	private const int Grid = 36;
	private const float GridCell = 0.4f;
	private const float GridHalf = Grid * GridCell * 0.5f;
	private readonly float[] density = new float[Grid * Grid];
	private readonly float[] height = new float[Grid * Grid];

	public int PointCount { get { return PointCountTotal; } }

	// ------------------------------------------------------------ worker-owned simulation state (local frame: x right, y up, z = reference forward)
	private readonly Vector3[] pts = new Vector3[PointCountTotal];
	private readonly float[] headX = new float[WormCount], headZ = new float[WormCount];   // heading, unit, in the ground plane
	private readonly float[] depth = new float[WormCount], depthGoal = new float[WormCount], depthTimer = new float[WormCount];
	private readonly float[] rad = new float[WormCount], phase = new float[WormCount], crawl = new float[WormCount], spill = new float[WormCount];
	private readonly bool[] rearFlag = new bool[WormCount];
	private readonly float[] flowX = new float[WormCount], flowZ = new float[WormCount];   // carries the whole worm; crawling only steers its head
	private readonly int[] wormOfPoint = new int[PointCountTotal];
	private readonly float[] volumePerPoint = new float[WormCount];
	private uint rng = 0x9E3779B9u;

	// spatial hash
	private const float Cell = 0.2f;
	private const int HashSize = 8192;
	private readonly int[] cellHead = new int[HashSize];
	private readonly int[] nextInCell = new int[PointCountTotal];
	private readonly float[] liftBuf = new float[PointCountTotal];

	// mesh
	private readonly int vertexCount = WormCount * (PointsPerWorm * Sides + 2);
	private readonly Vector3[] vertices, normals;
	private readonly Color32[] colors;
	private readonly Vector3[] ring = new Vector3[PointsPerWorm];
	private readonly int[][] submeshTriangles = new int[3][];
	private static readonly float[] CosS = new float[Sides], SinS = new float[Sides];

	// ------------------------------------------------------------ main-thread state
	private readonly GameObject meshObject;
	private readonly Mesh mesh;
	private readonly MeshRenderer renderer;
	private readonly Material[] materials = new Material[3];
	private readonly Texture2D white;
	private Func<Vector3, Vector3> groundSampler;
	private CelestialBody body;
	private Vector3 anchor, lastAnchor, up = Vector3.up, refForward = Vector3.forward, target, groundSampledAt;
	private Quaternion rotation = Quaternion.identity;
	private bool initialized, haveLast, chasing;
	private float speedSmooth, dtAccum, groundAge = 99f, moveX, moveZ = 1f;
	private float g0, gx, gz;   // ground plane in the local frame: y = g0 + gx x + gz z

	// ------------------------------------------------------------ job hand-off
	private readonly Thread worker;
	private readonly AutoResetEvent go = new AutoResetEvent(false);
	private volatile bool busy, quit, resultReady, faulted;
	private string fault;
	private float jobDt, jobSpeed, jobTime, jobG0, jobGx, jobGz, jobMx, jobMz = 1f;
	private bool jobChasing;

	// ------------------------------------------------------------ diagnostics
	public string ShaderName { get; private set; }
	public int RenderedTriangles { get; private set; }
	public bool RendererEnabled { get { return renderer != null && renderer.enabled; } }
	public Bounds RenderBounds { get { return renderer != null ? renderer.bounds : new Bounds(); } }
	public Vector3 Anchor { get { return anchor; } }
	public Quaternion Frame { get { return rotation; } }
	public Vector3 MoveDirection { get { return rotation * new Vector3(moveX, 0f, moveZ); } }
	public Vector3 PointCentroid { get { return anchor + rotation * centroidLocal; } }
	public int SpilledWorms { get; private set; }
	public float MaxPointRadius { get; private set; }
	public float MaxSegmentLength { get; private set; }
	public float OverlapFraction { get; private set; }
	public float RearDragFraction { get; private set; }
	public float PileHeight { get; private set; }
	public int TumbleCount { get; private set; }
	public float LastApplyMilliseconds { get; private set; }
	public float LastTerrainMilliseconds { get; private set; }
	public float LastUploadMilliseconds { get; private set; }
	public float LastSimulationMilliseconds { get; private set; }
	public float LastMeshMilliseconds { get; private set; }
	public int WorkerSteps { get; private set; }
	public bool Threaded { get { return worker != null && !faulted; } }
	private Vector3 centroidLocal;
	private int wSpilled, wSteps, wTumbles;
	private float wMaxRadius, wMaxSegment, wOverlap, wSimMs, wMeshMs, wRearDrag, wPileHeight;
	private Vector3 wCentroid;

	static MohoWormHeapSkin()
	{
		for (int s = 0; s < Sides; s++) { float a = 2f * Mathf.PI * s / Sides; CosS[s] = Mathf.Cos(a); SinS[s] = Mathf.Sin(a); }
	}

	public MohoWormHeapSkin(Transform parent)
	{
		for (int w = 0; w < WormCount; w++) for (int p = 0; p < PointsPerWorm; p++) wormOfPoint[w * PointsPerWorm + p] = w;
		vertices = new Vector3[vertexCount];
		normals = new Vector3[vertexCount];
		colors = new Color32[vertexCount];

		meshObject = new GameObject("MohoWormSwarm");
		meshObject.layer = 0;
		meshObject.transform.SetParent(parent, false);
		mesh = new Mesh { name = "MohoWormSwarmMesh" };
		mesh.MarkDynamic();
		meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		renderer = meshObject.AddComponent<MeshRenderer>();
		renderer.shadowCastingMode = ShadowCastingMode.On;
		renderer.receiveShadows = true;

		white = new Texture2D(2, 2, TextureFormat.RGB24, false);
		white.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
		white.Apply();
		Shader shader = PickShader();
		ShaderName = shader != null ? shader.name : "missing";
		bool own = ShaderName == "KK/WormFlesh";
		Color[] tints = { new Color(0.70f, 0.12f, 0.12f), new Color(0.56f, 0.08f, 0.10f), new Color(0.78f, 0.20f, 0.18f) };
		for (int i = 0; i < 3; i++) materials[i] = MakeMaterial(shader, tints[i], own ? 0.82f : 0.62f, own ? 0.03f : 0.05f);
		renderer.sharedMaterials = materials;

		BuildTopology();
		try { worker = new Thread(WorkerLoop) { IsBackground = true, Name = "KK worm swarm", Priority = System.Threading.ThreadPriority.AboveNormal }; }
		catch (Exception ex) { worker = null; fault = ex.Message; }
	}

	private Shader PickShader()
	{
		Shader s = KKShaderBundle.Find("KK/WormFlesh");
		if (s != null && s.isSupported) return s;
		string[] names = { "KSP/Emissive/Specular", "KSP/Specular", "KSP/Emissive/Bumped Specular", "KSP/Bumped Specular", "Legacy Shaders/Specular", "Specular" };
		for (int i = 0; i < names.Length; i++) { s = Shader.Find(names[i]); if (s != null) return s; }
		return Shader.Find("Diffuse");
	}

	private Material MakeMaterial(Shader shader, Color tint, float shininess, float selfLight)
	{
		Material m = new Material(shader);
		m.mainTexture = white;
		if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
		if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(1f, 0.86f, 0.84f, 1f));
		if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", shininess);
		if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", selfLight);
		if (m.HasProperty("_RimFalloff")) m.SetFloat("_RimFalloff", 2.5f);
		if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(0.25f, 0.05f, 0.05f, 1f));
		if (m.HasProperty("_Emissive")) m.SetTexture("_Emissive", white);
		if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", new Color(selfLight, selfLight * 0.18f, selfLight * 0.18f, 1f));
		return m;
	}

	// ------------------------------------------------------------ public interface
	public void Initialize(CelestialBody celestialBody, Vector3 ground, Func<Vector3, Vector3> sampler)
	{
		body = celestialBody;
		groundSampler = sampler;
		anchor = lastAnchor = ground;
		haveLast = true;
		up = body != null ? (Vector3)((Vector3d)ground - body.position).normalized : Vector3.up;
		refForward = Vector3.ProjectOnPlane(Vector3.forward, up);
		if (refForward.sqrMagnitude < 0.01f) refForward = Vector3.ProjectOnPlane(Vector3.right, up);
		refForward.Normalize();
		rotation = Quaternion.LookRotation(refForward, up);
		if (groundSampler != null) SampleGround();
		jobG0 = g0; jobGx = gx; jobGz = gz;
		Seed();
		if (!faulted)
		{
			for (int i = 0; i < 10; i++) StepSimulation(1f / 30f, 0f, 0f, 1f, i / 30f, false);
			BuildMesh(0f);
			Upload();
		}
		meshObject.transform.SetPositionAndRotation(anchor, rotation);
		if (worker != null && !worker.IsAlive) worker.Start();
		initialized = true;
	}

	public void SetGroundSampler(Func<Vector3, Vector3> sampler) { if (sampler != null) groundSampler = sampler; }
	public void SetTarget(Vector3d worldTarget, bool pursue) { target = (Vector3)worldTarget; chasing = pursue; }
	public void ApplyWorldShift(Vector3 delta) { anchor += delta; lastAnchor += delta; target += delta; groundSampledAt += delta; }
	public void Reposition(Vector3 shift) { anchor += shift; lastAnchor = anchor; groundAge = 99f; if (meshObject != null) meshObject.transform.position = anchor; }

	public void Apply(MohoPose pose)
	{
		if (!initialized || pose == null) return;
		float frameDt = TimeWarp.deltaTime;
		if (Time.timeScale <= 0f || frameDt <= 0f) return;
		long start = Stopwatch.GetTimestamp();

		anchor = pose.Origin;
		Vector3 newUp = body != null ? (Vector3)((Vector3d)anchor - body.position).normalized : pose.Frame * Vector3.up;
		if (newUp.sqrMagnitude > 0.5f) up = newUp;
		// the swarm's frame is carried along without yawing with the creature: turns are something the worms have to follow
		Vector3 f = Vector3.ProjectOnPlane(refForward, up);
		if (f.sqrMagnitude > 1e-4f) refForward = f.normalized;
		rotation = Quaternion.LookRotation(refForward, up);
		Vector3 travel = haveLast ? Vector3.ProjectOnPlane(anchor - lastAnchor, up) : Vector3.zero;
		lastAnchor = anchor;
		haveLast = true;
		float raw = Mathf.Min(travel.magnitude / frameDt, 200f);
		float blend = 1f - Mathf.Exp(-6f * frameDt);
		speedSmooth += (raw - speedSmooth) * blend;
		Vector3 dirLocal = Quaternion.Inverse(rotation) * (raw > 0.3f ? travel : pose.Frame * Vector3.forward);
		float dl = Mathf.Sqrt(dirLocal.x * dirLocal.x + dirLocal.z * dirLocal.z);
		if (dl > 1e-4f)
		{
			moveX += (dirLocal.x / dl - moveX) * blend; moveZ += (dirLocal.z / dl - moveZ) * blend;
			float ml = Mathf.Sqrt(moveX * moveX + moveZ * moveZ);
			if (ml > 1e-4f) { moveX /= ml; moveZ /= ml; }
		}
		dtAccum += Mathf.Min(frameDt, 0.1f);

		long stage = Stopwatch.GetTimestamp();
		groundAge += frameDt;
		if (groundSampler != null && (groundAge > 0.3f || (anchor - groundSampledAt).sqrMagnitude > 2.25f)) SampleGround();
		LastTerrainMilliseconds = Elapsed(stage);

		LastUploadMilliseconds = 0f;
		if (faulted && fault != null) { UnityEngine.Debug.Log("[UT Moho] worm swarm worker stopped: " + fault); fault = null; }
		if (!busy && !faulted)
		{
			if (resultReady)
			{
				stage = Stopwatch.GetTimestamp();
				Upload();
				LastUploadMilliseconds = Elapsed(stage);
				resultReady = false;
				SpilledWorms = wSpilled; WorkerSteps = wSteps; MaxPointRadius = wMaxRadius; MaxSegmentLength = wMaxSegment; TumbleCount = wTumbles;
				OverlapFraction = wOverlap; RearDragFraction = wRearDrag; PileHeight = wPileHeight;
				LastSimulationMilliseconds = wSimMs; LastMeshMilliseconds = wMeshMs; centroidLocal = wCentroid;
			}
			jobDt = Mathf.Min(dtAccum, 0.05f);
			dtAccum = 0f;
			jobSpeed = speedSmooth;
			jobMx = moveX; jobMz = moveZ;
			jobChasing = chasing;
			jobTime = Time.time;
			jobG0 = g0; jobGx = gx; jobGz = gz;
			if (worker != null) { busy = true; go.Set(); }
			else RunJob();
		}
		meshObject.transform.SetPositionAndRotation(anchor, rotation);
		LastApplyMilliseconds = Elapsed(start);
	}

	private void SampleGround()
	{
		groundAge = 0f;
		groundSampledAt = anchor;
		Vector3 right = rotation * Vector3.right, fwd = rotation * Vector3.forward;
		float h0 = Vector3.Dot(groundSampler(anchor) - anchor, up);
		float hx = Vector3.Dot(groundSampler(anchor + right * 4f) - anchor, up);
		float hz = Vector3.Dot(groundSampler(anchor + fwd * 4f) - anchor, up);
		g0 = h0;
		gx = Mathf.Clamp((hx - h0) / 4f, -0.6f, 0.6f);
		gz = Mathf.Clamp((hz - h0) / 4f, -0.6f, 0.6f);
	}

	private void Upload()
	{
		mesh.SetVertices(vertices);
		mesh.SetNormals(normals);
		mesh.SetColors(colors);
	}

	public void Dispose()
	{
		quit = true;
		go.Set();
		if (worker != null && worker.IsAlive) worker.Join(200);
		if (meshObject != null) UnityEngine.Object.Destroy(meshObject);
		if (mesh != null) UnityEngine.Object.Destroy(mesh);
		for (int i = 0; i < materials.Length; i++) if (materials[i] != null) UnityEngine.Object.Destroy(materials[i]);
		if (white != null) UnityEngine.Object.Destroy(white);
	}

	// ------------------------------------------------------------ worker
	private void WorkerLoop()
	{
		while (true)
		{
			go.WaitOne();
			if (quit) return;
			try { RunJob(); }
			catch (Exception ex) { fault = ex.GetType().Name + ": " + ex.Message; faulted = true; }
			Thread.MemoryBarrier();
			busy = false;
		}
	}

	private void RunJob()
	{
		long t0 = Stopwatch.GetTimestamp();
		StepSimulation(jobDt, jobSpeed, jobMx, jobMz, jobTime, jobChasing);
		long t1 = Stopwatch.GetTimestamp();
		BuildMesh(jobTime);
		long t2 = Stopwatch.GetTimestamp();
		wSimMs = (float)((t1 - t0) * 1000.0 / Stopwatch.Frequency);
		wMeshMs = (float)((t2 - t1) * 1000.0 / Stopwatch.Frequency);
		wSteps++;
		Thread.MemoryBarrier();
		resultReady = true;
	}

	private float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

	// ------------------------------------------------------------ ground and pile height
	private float GroundY(float x, float z) { return jobG0 + jobGx * x + jobGz * z; }

	// pile height above the ground at (x, z), bilinear on the smoothed density grid
	private float PileH(float x, float z)
	{
		float fx = (x + GridHalf) / GridCell - 0.5f, fz = (z + GridHalf) / GridCell - 0.5f;
		int ix = (int)Mathf.Floor(fx), iz = (int)Mathf.Floor(fz);
		float tx = fx - ix, tz = fz - iz;
		return Lerp2(Sample(ix, iz), Sample(ix + 1, iz), Sample(ix, iz + 1), Sample(ix + 1, iz + 1), tx, tz);
	}

	private float Sample(int ix, int iz)
	{
		if (ix < 0 || iz < 0 || ix >= Grid || iz >= Grid) return 0f;
		return height[iz * Grid + ix];
	}

	private static float Lerp2(float a, float b, float c, float d, float tx, float tz)
	{
		float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
		return ab + (cd - ab) * tz;
	}

	private float SurfaceY(float x, float z, float dep, float r)
	{
		float h = PileH(x, z) - 2f * r;
		return GroundY(x, z) + r + (h > 0f ? h * (1f - dep) : 0f);
	}

	private void BuildHeight()
	{
		for (int i = 0; i < density.Length; i++) density[i] = 0f;
		float inv = 1f / (Packing * GridCell * GridCell);
		for (int i = 0; i < PointCountTotal; i++)
		{
			Vector3 p = pts[i];
			float fx = (p.x + GridHalf) / GridCell - 0.5f, fz = (p.z + GridHalf) / GridCell - 0.5f;
			int ix = (int)Mathf.Floor(fx), iz = (int)Mathf.Floor(fz);
			if (ix < 0 || iz < 0 || ix >= Grid - 1 || iz >= Grid - 1) continue;
			float tx = fx - ix, tz = fz - iz, v = volumePerPoint[wormOfPoint[i]] * inv;
			int k = iz * Grid + ix;
			density[k] += v * (1f - tx) * (1f - tz);
			density[k + 1] += v * tx * (1f - tz);
			density[k + Grid] += v * (1f - tx) * tz;
			density[k + Grid + 1] += v * tx * tz;
		}
		// two 3x3 blurs: a pile has no sharp steps, and its base slumps out
		Blur(density, height);
		Blur(height, density);
		float peak = 0f;
		for (int i = 0; i < height.Length; i++) { height[i] = density[i]; if (height[i] > peak) peak = height[i]; }
		wPileHeight = peak;
	}

	private static void Blur(float[] src, float[] dst)
	{
		for (int z = 0; z < Grid; z++)
			for (int x = 0; x < Grid; x++)
			{
				float sum = 0f, wsum = 0f;
				for (int oz = -1; oz <= 1; oz++)
					for (int ox = -1; ox <= 1; ox++)
					{
						int xx = x + ox, zz = z + oz;
						if (xx < 0 || zz < 0 || xx >= Grid || zz >= Grid) continue;
						float wgt = (ox == 0 ? 2f : 1f) * (oz == 0 ? 2f : 1f);
						sum += src[zz * Grid + xx] * wgt; wsum += wgt;
					}
				dst[z * Grid + x] = sum / wsum;
			}
	}

	// ------------------------------------------------------------ seeding
	private void Seed()
	{
		for (int w = 0; w < WormCount; w++)
		{
			float a = Rand() * 6.2831853f, rr = RestRadius * Mathf.Sqrt(Rand()) * 0.9f;
			float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
			float h = Rand() * 6.2831853f;
			headX[w] = Mathf.Cos(h); headZ[w] = Mathf.Sin(h);
			rad[w] = 0.06f + Rand() * 0.03f;
			phase[w] = Rand() * 6.2831853f;
			crawl[w] = 0.25f + Rand() * 0.3f;
			depth[w] = depthGoal[w] = Rand();
			depthTimer[w] = 2f + Rand() * 6f;
			volumePerPoint[w] = Mathf.PI * rad[w] * rad[w] * Spacing;
			int b = w * PointsPerWorm;
			for (int p = 0; p < PointsPerWorm; p++) pts[b + p] = new Vector3(x - headX[w] * p * Spacing, 0f, z - headZ[w] * p * Spacing);
		}
	}

	// ------------------------------------------------------------ simulation (worker thread)
	private void StepSimulation(float dt, float speed, float mx, float mz, float time, bool agitated)
	{
		if (dt <= 0f) return;
		BuildHeight();

		bool moving = speed > 1f;
		float k = Mathf.Clamp01(speed / 30f);
		float u = Mathf.Min(speed, TumbleCap);
		// the swarm's footprint: a disc at rest, a long surge when fast (longer behind, the front a little ahead of the anchor)
		float lead = 0.8f * k;
		float cx = mx * lead, cz = mz * lead;
		float rFront = RestRadius + 0.7f * k, rBack = RestRadius + 2.0f * k, rSide = RestRadius - 0.6f * k;
		float spillRate = agitated ? 0.06f : 0.035f;
		int spilled = 0, rearDrag = 0;

		for (int w = 0; w < WormCount; w++)
		{
			int b = w * PointsPerWorm;
			Vector3 head = pts[b];
			float qx = head.x - cx, qz = head.z - cz;
			float along = qx * mx + qz * mz, side = qx * mz - qz * mx;
			float reach = along > 0f ? rFront : rBack;
			float e = Mathf.Sqrt((along / reach) * (along / reach) + (side / rSide) * (side / rSide));
			float dep = depth[w];
			float surface = 1f - dep;

			// heading: wander, cohesion, sliding down the pile, spilling out
			float ph = phase[w];
			float turn = (1.6f * Mathf.Sin(time * 0.8f + ph) + 0.9f * Mathf.Sin(time * 2.1f + ph * 2.3f)) * (speed > 1f ? 2f : 1f);
			float hx = headX[w], hz = headZ[w];
			float ang = turn * dt, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
			float nhx = hx * ca - hz * sa, nhz = hx * sa + hz * ca;
			float dx = nhx, dz = nhz;
			if (spill[w] > 0f)
			{
				spill[w] -= dt;
				float ql = Mathf.Sqrt(qx * qx + qz * qz);
				if (ql > 1e-3f) { dx += qx / ql * 1.5f; dz += qz / ql * 1.5f; }
				spilled++;
			}
			else
			{
				if (e > 0.55f)
				{
					float ql = Mathf.Sqrt(qx * qx + qz * qz);
					float pull = (e - 0.55f) * 4.5f;
					if (ql > 1e-3f) { dx -= qx / ql * pull; dz -= qz / ql * pull; }
				}
				if (dep < 0.25f && e > 0.75f && Rand() < spillRate * dt) spill[w] = 1f + Rand() * 2f;
			}
			// slide down the slope of the pile (worms on top fall off the sides instead of spreading from the apex)
			float gxH = (PileH(head.x + 0.2f, head.z) - PileH(head.x - 0.2f, head.z)) / 0.4f;
			float gzH = (PileH(head.x, head.z + 0.2f) - PileH(head.x, head.z - 0.2f)) / 0.4f;
			dx -= gxH * 0.15f * surface * surface; dz -= gzH * 0.15f * surface * surface;   // sliding carries the worm; it barely turns it
			float dl = Mathf.Sqrt(dx * dx + dz * dz);
			if (dl > 1e-4f) { dx /= dl; dz /= dl; }
			float steer = Mathf.Min(1f, 4f * dt);
			hx = nhx + (dx - nhx) * steer; hz = nhz + (dz - nhz) * steer;
			float hl = Mathf.Sqrt(hx * hx + hz * hz);
			if (hl > 1e-4f) { hx /= hl; hz /= hl; }
			headX[w] = hx; headZ[w] = hz;

			// velocity relative to the swarm
			float sp = crawl[w] * (agitated ? 1.6f : 1f) * (moving ? 2.5f : 1f);   // a moving swarm writhes hard: bodies trail at their own angles, not combed by the flow
			float slide = Mathf.Sqrt(gxH * gxH + gzH * gzH) * surface * surface * 1.0f;   // gravity on a slope, not crawling
			float vx = hx * sp, vz = hz * sp;          // crawling: the head only
			float fx = -gxH * slide, fz = -gzH * slide; // flow: the whole worm
			// cohesion also carries the whole worm a little, so the pile stays round instead of smearing
			if (e > 0.85f && spill[w] <= 0f) { float ql = Mathf.Sqrt(qx * qx + qz * qz); if (ql > 1e-3f) { fx -= qx / ql * (e - 0.85f) * 2.5f; fz -= qz / ql * (e - 0.85f) * 2.5f; } }
			if (moving)
			{
				// slowing near the ends of the conveyor keeps worms from shooting out of the back or the front
				float backRoom = Mathf.Clamp01((along + rBack) / (0.45f * rBack));
				float frontRoom = Mathf.Clamp01((rFront - along) / (0.45f * rFront));
				if (dep > 0.62f) { vx *= 0.25f; vz *= 0.25f; fx -= mx * u * backRoom; fz -= mz * u * backRoom; }   // gripping the ground: the swarm passes over
				else if (dep < 0.38f) { fx += mx * u * 0.95f * frontRoom; fz += mz * u * 0.95f * frontRoom; }        // riding forward on top
				else { fx -= mx * u * 0.3f * backRoom; fz -= mz * u * 0.3f * backRoom; }
				// the conveyor: up at the back, over the top, down the front, under again
				if (along < -0.2f * rBack && dep > 0.2f) depth[w] = Mathf.Max(0f, dep - 2.4f * dt);
				else if (along > 0.3f * rFront && dep < 0.75f) depth[w] = Mathf.Min(1f, dep + 2.0f * dt);
				if (dep < 0.38f && along < -0.25f * rBack) rearFlag[w] = true;
				if (dep < 0.45f && along > 0.25f * rFront && rearFlag[w]) { rearFlag[w] = false; wTumbles++; }
				if (along < -(rBack + 1.5f)) rearDrag++;
			}
			else
			{
				// at rest worms slowly surface and burrow, so the outside keeps changing
				depthTimer[w] -= dt;
				if (depthTimer[w] <= 0f) { depthGoal[w] = Rand(); depthTimer[w] = 3f + Rand() * 6f; }
				depth[w] = dep + (depthGoal[w] - dep) * Mathf.Min(1f, 0.35f * dt);
				rearFlag[w] = false;
			}
			flowX[w] = fx; flowZ[w] = fz;
			head.x += (vx + fx) * dt; head.z += (vz + fz) * dt;
			float h2 = head.x * head.x + head.z * head.z;
			if (h2 > MaxHorizontal * MaxHorizontal) { float s = MaxHorizontal / Mathf.Sqrt(h2); head.x *= s; head.z *= s; }
			head.y = SurfaceY(head.x, head.z, depth[w], rad[w]);
			pts[b] = head;
		}

		// bodies follow their heads and drape over the pile at their depth
		float maxR2 = 0f, maxSeg = 0f;
		Vector3 centroid = Vector3.zero;
		for (int w = 0; w < WormCount; w++)
		{
			int b = w * PointsPerWorm;
			float r = rad[w], dep = depth[w];
			centroid += pts[b];
			float carryX = flowX[w] * dt * BodyCarry, carryZ = flowZ[w] * dt * BodyCarry;   // the body only partly carried: it trails its head through the roll
			for (int p = 1; p < PointsPerWorm; p++)
			{
				int i = b + p;
				Vector3 prev = pts[i - 1], c = pts[i];
				c.x += carryX; c.z += carryZ;
				float ddx = c.x - prev.x, ddy = c.y - prev.y, ddz = c.z - prev.z;
				float len = Mathf.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
				if (len > 1e-5f) { float s = Spacing / len; c.x = prev.x + ddx * s; c.y = prev.y + ddy * s; c.z = prev.z + ddz * s; }
				else c.z = prev.z - Spacing;
				float h2 = c.x * c.x + c.z * c.z;
				if (h2 > MaxHorizontal * MaxHorizontal) { float s = MaxHorizontal / Mathf.Sqrt(h2); c.x *= s; c.z *= s; }
				c.y = SurfaceY(c.x, c.z, dep, r);
				float ex = c.x - prev.x, ey = c.y - prev.y, ez = c.z - prev.z;
				float el = Mathf.Sqrt(ex * ex + ey * ey + ez * ez);
				if (el > 1.6f * Spacing) { float s = 1.6f * Spacing / el; c.x = prev.x + ex * s; c.y = prev.y + ey * s; c.z = prev.z + ez * s; }
				pts[i] = c;
				centroid += c;
			}
		}

		Collide();

		for (int w = 0; w < WormCount; w++)
		{
			int b = w * PointsPerWorm;
			for (int p = 0; p < PointsPerWorm; p++)
			{
				Vector3 c = pts[b + p];
				float d2 = c.x * c.x + c.y * c.y + c.z * c.z;
				if (d2 > maxR2) maxR2 = d2;
				if (p > 0) { float s = (c - pts[b + p - 1]).magnitude; if (s > maxSeg) maxSeg = s; }
			}
		}
		wSpilled = spilled;
		wRearDrag = (float)rearDrag / WormCount;
		wMaxRadius = Mathf.Sqrt(maxR2);
		wMaxSegment = maxSeg;
		wCentroid = centroid / PointCountTotal;
	}

	// ------------------------------------------------------------ contacts (worker thread)
	private static int Key(int ix, int iy, int iz) { return ((ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791)) & (HashSize - 1); }
	private static int Coord(float v) { return (int)Mathf.Floor(v / Cell); }

	private void Collide()
	{
		for (int i = 0; i < HashSize; i++) cellHead[i] = -1;
		for (int i = 0; i < PointCountTotal; i++)
		{
			Vector3 p = pts[i];
			int key = Key(Coord(p.x), Coord(p.y), Coord(p.z));
			nextInCell[i] = cellHead[key];
			cellHead[key] = i;
		}
		// the worm higher in the pile rides over the one it touches: lifted just enough to clear it. Only worms near the surface
		// (deep ones are never seen), lifts smoothed along the worm (no single point spiking up), and a worm that keeps riding
		// over others climbs in the pile, so the next frames resolve it through its depth instead of through lifts.
		for (int i = 0; i < PointCountTotal; i++)
		{
			int w = wormOfPoint[i];
			liftBuf[i] = 0f;
			if (depth[w] > 0.6f) continue;
			Vector3 c = pts[i];
			int cx = Coord(c.x), cy = Coord(c.y), cz = Coord(c.z);
			float lift = 0f;
			for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) for (int oz = -1; oz <= 1; oz++)
			{
				for (int j = cellHead[Key(cx + ox, cy + oy, cz + oz)]; j >= 0; j = nextInCell[j])
				{
					int o = wormOfPoint[j];
					if (o == w) continue;
					bool rides = depth[w] < depth[o] || (depth[w] == depth[o] && w > o);
					if (!rides) continue;
					Vector3 q = pts[j];
					float min = rad[w] + rad[o];
					float hx = c.x - q.x, hz = c.z - q.z;
					float horiz2 = hx * hx + hz * hz;
					if (horiz2 >= min * min) continue;
					float need = q.y + Mathf.Sqrt(min * min - horiz2) - (c.y + lift);
					if (need > 0f) lift += need;
				}
			}
			liftBuf[i] = Mathf.Min(lift, 0.16f);
		}
		for (int w = 0; w < WormCount; w++)
		{
			if (depth[w] > 0.6f) continue;
			int b = w * PointsPerWorm;
			float most = 0f;
			for (int p = 0; p < PointsPerWorm; p++)
			{
				float l = liftBuf[b + p];
				float left = p > 0 ? liftBuf[b + p - 1] : l, right = p < PointsPerWorm - 1 ? liftBuf[b + p + 1] : l;
				float smooth = Mathf.Max(l, 0.5f * (left + right));
				if (smooth > most) most = smooth;
				if (smooth > 0f) { Vector3 c = pts[b + p]; c.y += smooth; pts[b + p] = c; }
			}
			if (most > 0.05f) depth[w] = Mathf.Max(0f, depth[w] - 0.01f);
		}
		// a lift never tears a worm apart: each point stays within reach of the one before it
		for (int w = 0; w < WormCount; w++)
		{
			int b = w * PointsPerWorm;
			for (int p = 1; p < PointsPerWorm; p++)
			{
				Vector3 prev = pts[b + p - 1], c = pts[b + p];
				Vector3 e = c - prev;
				float el = e.magnitude;
				if (el > 1.5f * Spacing) pts[b + p] = prev + e * (1.5f * Spacing / el);
			}
		}
		// what is left: sampled visible points that still sit inside another worm
		int sampled = 0, overlapping = 0;
		for (int i = 0; i < PointCountTotal; i += 9)
		{
			Vector3 c = pts[i];
			if (c.y < PileH(c.x, c.z) * 0.6f + GroundY(c.x, c.z)) continue;   // buried deep in the pile: never seen
			sampled++;
			int w = wormOfPoint[i];
			int cx = Coord(c.x), cy = Coord(c.y), cz = Coord(c.z);
			bool hit = false;
			for (int ox = -1; ox <= 1 && !hit; ox++) for (int oy = -1; oy <= 1 && !hit; oy++) for (int oz = -1; oz <= 1 && !hit; oz++)
			{
				for (int j = cellHead[Key(cx + ox, cy + oy, cz + oz)]; j >= 0; j = nextInCell[j])
				{
					int o = wormOfPoint[j];
					if (o == w) continue;
					float min = 0.8f * (rad[w] + rad[o]);
					if ((pts[j] - c).sqrMagnitude < min * min) { hit = true; break; }
				}
			}
			if (hit) overlapping++;
		}
		wOverlap = sampled > 0 ? (float)overlapping / sampled : 0f;
	}

	// ------------------------------------------------------------ mesh
	private void BuildTopology()
	{
		int perWorm = ((PointsPerWorm - 1) * Sides * 2 + 2 * Sides) * 3;
		int[] count = new int[3];
		for (int w = 0; w < WormCount; w++) count[w % 3] += perWorm;
		for (int g = 0; g < 3; g++) submeshTriangles[g] = new int[count[g]];
		int[] at = new int[3];
		for (int w = 0; w < WormCount; w++)
		{
			int g = w % 3, o = w * (PointsPerWorm * Sides + 2);
			int[] tri = submeshTriangles[g];
			int t = at[g];
			for (int p = 0; p < PointsPerWorm - 1; p++)
				for (int s = 0; s < Sides; s++)
				{
					int a = o + p * Sides + s, b = o + p * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
					tri[t++] = a; tri[t++] = b; tri[t++] = c;
					tri[t++] = b; tri[t++] = d; tri[t++] = c;
				}
			int headCap = o + PointsPerWorm * Sides, tailCap = headCap + 1, last = o + (PointsPerWorm - 1) * Sides;
			for (int s = 0; s < Sides; s++)
			{
				tri[t++] = headCap; tri[t++] = o + (s + 1) % Sides; tri[t++] = o + s;
				tri[t++] = tailCap; tri[t++] = last + s; tri[t++] = last + (s + 1) % Sides;
			}
			at[g] = t;
		}
		RenderedTriangles = (count[0] + count[1] + count[2]) / 3;
		mesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.SetVertices(vertices);
		mesh.SetNormals(normals);
		mesh.SetColors(colors);
		mesh.subMeshCount = 3;
		for (int g = 0; g < 3; g++) mesh.SetTriangles(submeshTriangles[g], g, false);
		mesh.bounds = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(14f, 7f, 14f));
	}

	private void BuildMesh(float time)
	{
		for (int w = 0; w < WormCount; w++)
		{
			int b = w * PointsPerWorm, o = w * (PointsPerWorm * Sides + 2);
			float r = rad[w], ph = phase[w];
			float open = 1f - depth[w];
			// loose ends: worms near the top lift their heads and curl their tails up now and then (render only)
			float lt = Mathf.Sin(time * 0.6f + ph * 1.7f), lh = Mathf.Sin(time * 0.45f + ph * 3.1f);
			float tailLift = lt > 0f ? lt * lt * lt * 0.08f * open * open : 0f;
			float headLift = lh > 0f ? lh * lh * lh * lh * 0.32f * open : 0f;
			if (spill[w] > 0f) headLift += 0.12f;
			for (int p = 0; p < PointsPerWorm; p++)
			{
				Vector3 c = pts[b + p];
				float raise = 0f;
				if (p <= 2) raise += headLift * (3 - p) * (3 - p) / 9f;
				if (p >= PointsPerWorm - 3) { float q = p - (PointsPerWorm - 4); raise += tailLift * q * q / 9f; }
				c.y += raise;
				ring[p] = c;
			}
			byte tint = (byte)(128 + 127 * Mathf.Sin(ph * 3.7f));
			Vector3 prevSide = Vector3.zero;
			for (int p = 0; p < PointsPerWorm; p++)
			{
				Vector3 T = p == 0 ? ring[1] - ring[0] : (p == PointsPerWorm - 1 ? ring[p] - ring[p - 1] : ring[p + 1] - ring[p - 1]);
				float tl = T.magnitude;
				T = tl > 1e-5f ? T / tl : Vector3.forward;
				Vector3 side;
				if (p == 0)
				{
					side = Vector3.Cross(T, Vector3.up);
					if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(T, Vector3.right);
				}
				else side = prevSide - T * Vector3.Dot(prevSide, T);   // parallel transport: no twisting along the tube
				side.Normalize();
				prevSide = side;
				Vector3 bin = Vector3.Cross(T, side);
				float wig = 0.035f * Mathf.Sin(time * 6f - p * 0.9f + ph) * (p == 0 || p == PointsPerWorm - 1 ? 0.5f : 1f);
				Vector3 centre = ring[p] + side * wig;
				float rr = r * Profile[p];
				// occlusion: how deep below the pile's surface this point lies, and contact with the ground
				float ground = GroundY(centre.x, centre.z);
				float buried = Mathf.Clamp01((ground + PileH(centre.x, centre.z) - centre.y) / 1.1f);
				float ao = 1f - 0.75f * buried;
				if (centre.y - ground < 0.14f) ao *= 0.85f;
				Color32 col = new Color32((byte)(255f * ao), tint, 0, 255);
				int baseV = o + p * Sides;
				for (int s = 0; s < Sides; s++)
				{
					Vector3 nn = side * CosS[s] + bin * SinS[s];
					vertices[baseV + s] = centre + nn * rr;
					normals[baseV + s] = nn;
					colors[baseV + s] = col;
				}
				if (p == 0) { int v = o + PointsPerWorm * Sides; vertices[v] = centre - T * (rr * 0.9f); normals[v] = -T; colors[v] = col; }
				if (p == PointsPerWorm - 1) { int v = o + PointsPerWorm * Sides + 1; vertices[v] = centre + T * (rr * 0.8f); normals[v] = T; colors[v] = col; }
			}
		}
	}

	private static float Elapsed(long start) { return (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency); }
}
