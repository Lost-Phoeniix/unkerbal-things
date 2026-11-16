using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Moho creature's worm swarm on the GPU (offline twin: WormSwarm.cs in the Unity test rig).
/// Plain Unity class, no KSP types: positions live in a LOCAL frame (x east, y up, z north) that the owner places in the world
/// every frame (the renderer's transform). Ground comes from a height grid the owner fills (HeightAt cells). Each worm is a
/// chain of 16 points moved only by gravity, its own muscles and anisotropic friction; underground (hidden) worms are the
/// only ones carried along with the creature. Shaders: Shaders/WormSim.compute and Shaders/WormTube.shader (dread.bundle).
/// </summary>
public sealed class WormSwarmGpu : IDisposable
{
	public const int PPW = 16;                 // must match WormSim.compute and WormTube.shader
	const int Bucket = 12;                     // must match K in WormSim.compute
	const int TableBits = 18;
	const int FieldN = 128;
	const float FieldCell = 2.0f;          // 4x the P2 cell: worms are 4x as big, the same counts per cell
	public const int HeightN = 256;            // terrain grid cells per side (wraps); must be a power of two
	public const float HeightCell = 1.5f;   // 384 m of terrain around the swarm (was 192 m: the heap may now spread ~170 m out)

	// ------------------------------------------------------------------ settings (tuned offline in P1-P3)
	public int wormCount = 6000;
	public float radiusMin = 0.16f, radiusMax = 0.32f, spacingPerRadius = 1.45f, spawnRadius = 28f;   // worms 4x the size of the first prototype
	public float gravity = 2.7f;
	public Vector3 goal;
	public float restRadius = 20f;
	public float attract = 1f, wander = 0.6f, wanderTurn = 5f, steerRate = 0.03f, climb = 0.3f, thrust = 0.6f;
	public float crowd = 0.5f, densityRef = 120f, follow = 0.6f, trailRef = 3f, trailDecay = 0.97f;
	public float swirl = 1.0f, swirlScale = 0.35f, restless = 0.6f, urgency = 2.0f, orbit = 1.5f, steerIn = 0.2f;
	public Vector3 reachTarget;
	public float reachShare;
	public Vector3 anchorVel;
	public int diveMode;                       // 0 none, 1 worms that fall behind dive, 2 everyone dives, 3 everyone at once and fast
	public float diveDist = 28f;
	public float leapRate, riseRate;
	public int leapBurst;                      // a pod: this many worms leap in the next step (consumed)
	public float leapUp = 5f, maxSpeedFast = 70f, spawnChance = 0.05f;
	public bool spireOn, spireRelease;
	public Vector3 spireBase, spireCraft;
	public float spireHeight, spireCoil, spireRadius = 5.6f, spireTime;
	public Vector3 ballCenter;
	public float ballBlend, ballRadius = 12f, burst = 0f;
	public float spireHold = 1f;               // 1: guided; fading to 0 hands the spire worms to plain physics (the pile)
	public float spireIrreg, ballIrreg;         // 0 = the old regular spire sway and round ball; 1 = layered sway, lumpy uneven ball (settings mohoSpireIrregular, mohoBallIrregular)
	public Vector3 spireLean;                  // local xz offset of the spire's top (the column bends smoothly)
	public float grabChaos;                    // 0 = the old exact column; 1 = worms hang off it every way, writhe, lash out, drop off (setting mohoGrabChaos)
	public bool mound;                         // the ball is a collapse: a lumpy low mound over the craft (setting mohoGrabCollapse)
	public float moundHeight = 0.45f;          // mound height / radius
	public float squash = 1f;                  // the column is this much wider while the collapse presses it down (1 = not squashed)
	public float squashFrom;                   // the column's height before the squash (its twist keeps it; 0 = not squashing)
	public float releaseShare;                 // the collapse: spire worms below this height share are handed over to plain physics
	public float blastRadius;                  // > 0: the release burst is a blast from ballCenter fading out to this radius (the kill fling)
	public float blastStart;                   // spireTime when the blast went off (worms are thrown from the outside in)
	public int spireBudget = 2000;
	public float activity = 1f;
	/// <summary>Random bursts: share of worms bursting at any moment (0 = off), burst length (s), fast and slow activity factors.</summary>
	public float spurtShare, spurtLen = 0.5f, spurtFast = 2f, spurtSlow = 0.5f;
	public float waveAmp = 0.8f, wavesPerBody = 1.1f, waveHz = 3.5f, periAmp = 0.10f, periWaves = 2f, bendK = 0.3f;
	public float stick = 0.35f, headGrip = 0.15f;
	public bool finishClamp = true;           // the ground once more after the last collision pass (setting mohoWormGroundFix)
	public bool tunnels = true;               // burrow head first along a dig path (setting mohoWormTunnels)
	/// <summary>The travelling body (null = off; setting mohoWormSerpent). Driven by MohoWormGpuSkin.</summary>
	public WormSerpent serpent;
	readonly WormSerpentGpu serpentGpu = new WormSerpentGpu();
	public float muFwd = 0.02f, muBack = 0.80f, muLat = 0.95f, muWorm = 0.3f, adhesion = 0.02f, relax = 0.6f;
	public int substeps = 2, iterations = 4;
	public float drag = 0.995f, maxSpeed = 8f;

	public const float StepDt = 1f / 60f;
	public int PointCount { get { return wormCount * PPW; } }
	public bool Ready { get; private set; }
	public float SimTime { get; private set; }
	public int Steps { get; private set; }
	public GameObject View { get { return view; } }
	public int TriangleCount { get { return mesh != null ? (int)(mesh.GetIndexCount(0) / 3) : 0; } }
	public string ShaderName { get { return material != null ? material.shader.name : "missing"; } }

	readonly ComputeShader sim;
	readonly Shader tubeShader;
	ComputeBuffer pos, prev, delta, contact, renderPrev, frame, worm, wormInfo, cellCount, cellItems, density, trail, state, counters, height, leap;
	readonly uint[] zero4 = new uint[4];
	float leapAcc, riseAcc;
	int kCopy, kIntegrate, kClear, kBuild, kCollide, kApply, kChain, kFinish, kFrames, kFieldClear, kFieldSplat, kState, kRebase;
	Material material;
	Mesh mesh;
	GameObject view;
	uint stepCounter;
	float accum, rMax;
	bool spireSpawned;
	Vector3 lastFwd = Vector3.right;
	readonly float[] heights = new float[HeightN * HeightN];
	bool heightDirty = true;

	public static bool Supported { get { return SystemInfo.supportsComputeShaders; } }

	public WormSwarmGpu(ComputeShader simAsset, Shader tube)
	{
		if (simAsset == null || tube == null) throw new ArgumentNullException(simAsset == null ? "WormSim compute shader" : "KK/WormTube shader");
		sim = UnityEngine.Object.Instantiate(simAsset);
		tubeShader = tube;
	}

	/// <summary>Terrain height (local y) of grid cell (i, j) = local xz ((i + 0.5) * HeightCell, (j + 0.5) * HeightCell); i, j wrap.</summary>
	public void SetHeight(int i, int j, float h)
	{
		heights[(i & (HeightN - 1)) + HeightN * (j & (HeightN - 1))] = h;
		heightDirty = true;
	}

	/// <summary>The same bilinear ground the GPU uses.</summary>
	public float GroundAt(float x, float z)
	{
		float gx = x / HeightCell - 0.5f, gz = z / HeightCell - 0.5f;
		int cx = Mathf.FloorToInt(gx), cz = Mathf.FloorToInt(gz);
		float fx = gx - cx, fz = gz - cz;
		return Mathf.Lerp(Mathf.Lerp(H(cx, cz), H(cx + 1, cz), fx), Mathf.Lerp(H(cx, cz + 1), H(cx + 1, cz + 1), fx), fz);
	}
	float H(int i, int j) { return heights[(i & (HeightN - 1)) + HeightN * (j & (HeightN - 1))]; }

	/// <summary>Create the swarm as a heap of worms around `center` (local), on the current height grid.</summary>
	public void Init(Vector3 center, Transform parent, int layer)
	{
		ReleaseGpu();
		int n = PointCount;
		pos = new ComputeBuffer(n, 16); prev = new ComputeBuffer(n, 16); delta = new ComputeBuffer(n, 16);
		contact = new ComputeBuffer(n, 16); renderPrev = new ComputeBuffer(n, 16); frame = new ComputeBuffer(n, 16);
		worm = new ComputeBuffer(wormCount, 16); wormInfo = new ComputeBuffer(wormCount, 16);
		cellCount = new ComputeBuffer(1 << TableBits, 4); cellItems = new ComputeBuffer((1 << TableBits) * Bucket, 4);
		density = new ComputeBuffer(FieldN * FieldN, 4); trail = new ComputeBuffer(FieldN * FieldN * 3, 4);
		density.SetData(new uint[FieldN * FieldN]); trail.SetData(new int[FieldN * FieldN * 3]);
		state = new ComputeBuffer(wormCount, 4); state.SetData(new uint[wormCount]); counters = new ComputeBuffer(4, 4);
		leap = new ComputeBuffer(wormCount, 16); leap.SetData(new Vector4[wormCount]);
		height = new ComputeBuffer(HeightN * HeightN, 4); height.SetData(heights); heightDirty = false;
		goal = center;

		var rng = new System.Random(1234);
		var p = new Vector4[n];
		var ws = new Vector4[wormCount];
		var wi = new Vector4[wormCount];
		rMax = radiusMax;
		// a heap: straight worms in jittered rows inside a disc, stacked in layers; they settle in the first seconds
		float rowGap = radiusMax * 2.6f, colGap = (PPW - 1) * radiusMax * spacingPerRadius + 0.15f, layerGap = radiusMax * 2.4f;
		int placed = 0, layer2 = 0;
		var slots = new System.Collections.Generic.List<Vector2>();
		while (placed < wormCount && layer2 < 64)
		{
			float off = (float)rng.NextDouble() * colGap;
			slots.Clear();
			for (float z = -spawnRadius; z <= spawnRadius; z += rowGap)
				for (float x = -spawnRadius + off; x <= spawnRadius; x += colGap)
					if (x * x + z * z <= spawnRadius * spawnRadius) slots.Add(new Vector2(x, z));
			for (int k = slots.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); Vector2 tmp = slots[k]; slots[k] = slots[j]; slots[j] = tmp; }
			for (int si = 0; si < slots.Count && placed < wormCount; si++)
			{
				int w = placed++;
				float r = Mathf.Lerp(radiusMin, radiusMax, Mathf.Pow((float)rng.NextDouble(), 1.5f));
				float sp = r * spacingPerRadius;
				float ang = ((float)rng.NextDouble() - 0.5f) * 0.5f + (layer2 % 2 == 0 ? 0f : Mathf.PI);
				Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
				Vector3 mid = center + new Vector3(slots[si].x, 0, slots[si].y);
				float y = GroundAt(mid.x, mid.z) + 0.06f + layer2 * layerGap;
				for (int s = 0; s < PPW; s++)
				{
					Vector3 q = mid + dir * ((PPW - 1) * 0.5f - s) * sp;
					q.y = Mathf.Max(y, GroundAt(q.x, q.z) + r);
					p[w * PPW + s] = new Vector4(q.x, q.y, q.z, r);
				}
				ws[w] = new Vector4((float)rng.NextDouble() * 6.2831853f, (float)rng.NextDouble() * 6.2831853f, Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble()), 0);
				wi[w] = new Vector4(r, sp, Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()), (float)rng.NextDouble());
			}
			layer2++;
		}
		pos.SetData(p); prev.SetData(p); renderPrev.SetData(p);
		contact.SetData(new Vector4[n]); delta.SetData(new Vector4[n]); frame.SetData(new Vector4[n]);
		worm.SetData(ws); wormInfo.SetData(wi);

		kCopy = sim.FindKernel("CopyRender"); kIntegrate = sim.FindKernel("Integrate"); kClear = sim.FindKernel("ClearGrid");
		kBuild = sim.FindKernel("BuildGrid"); kCollide = sim.FindKernel("Collide"); kApply = sim.FindKernel("ApplyDelta");
		kChain = sim.FindKernel("ChainSolve"); kFinish = sim.FindKernel("Finish"); kFrames = sim.FindKernel("Frames");
		kFieldClear = sim.FindKernel("FieldClear"); kFieldSplat = sim.FindKernel("FieldSplat"); kState = sim.FindKernel("StateUpdate");
		kRebase = sim.FindKernel("Rebase");
		int[] all = { kCopy, kIntegrate, kClear, kBuild, kCollide, kApply, kChain, kFinish, kFrames, kFieldClear, kFieldSplat, kState, kRebase };
		for (int i = 0; i < all.Length; i++)
		{
			int k = all[i];
			sim.SetBuffer(k, "_Pos", pos); sim.SetBuffer(k, "_Prev", prev); sim.SetBuffer(k, "_Delta", delta);
			sim.SetBuffer(k, "_Contact", contact); sim.SetBuffer(k, "_RenderPrev", renderPrev); sim.SetBuffer(k, "_Frame", frame);
			sim.SetBuffer(k, "_Worm", worm); sim.SetBuffer(k, "_WormInfo", wormInfo);
			sim.SetBuffer(k, "_CellCount", cellCount); sim.SetBuffer(k, "_CellItems", cellItems);
			sim.SetBuffer(k, "_Density", density); sim.SetBuffer(k, "_Trail", trail);
			sim.SetBuffer(k, "_State", state); sim.SetBuffer(k, "_Counters", counters); sim.SetBuffer(k, "_Height", height); sim.SetBuffer(k, "_Leap", leap);
		}
		serpentGpu.Bind(sim, all);
		if (view == null) BuildView(parent, layer);
		material.SetBuffer("_Pos", pos); material.SetBuffer("_RenderPrev", renderPrev);
		material.SetBuffer("_Frame", frame); material.SetBuffer("_WormInfo", wormInfo); material.SetBuffer("_State", state);
		material.SetFloat("_Alpha", 1f);
		SetUniforms(StepDt / substeps);
		DispatchWorms(kFrames);
		SimTime = 0f; stepCounter = 0; accum = 0f; spireSpawned = false; spireOn = false; spireRelease = false;
		Ready = true;
	}

	void BuildView(Transform parent, int layer)
	{
		const int sub = 2, sides = 8;
		int rings = (PPW - 1) * sub + 1, vpw = rings * (sides + 1);
		int nv = wormCount * vpw;
		var verts = new Vector3[nv];
		var uv = new Vector2[nv];
		var uv2 = new Vector2[nv];
		var idx = new int[wormCount * (rings - 1) * sides * 6];
		int vi = 0, ii = 0;
		for (int w = 0; w < wormCount; w++)
		{
			int b0 = vi;
			for (int r = 0; r < rings; r++)
				for (int s = 0; s <= sides; s++)
				{
					uv[vi] = new Vector2(r / (float)(rings - 1), s / (float)sides);
					uv2[vi] = new Vector2(w, 0);
					vi++;
				}
			for (int r = 0; r < rings - 1; r++)
				for (int s = 0; s < sides; s++)
				{
					int a = b0 + r * (sides + 1) + s, b = a + sides + 1;
					idx[ii++] = a; idx[ii++] = a + 1; idx[ii++] = b;
					idx[ii++] = a + 1; idx[ii++] = b + 1; idx[ii++] = b;
				}
		}
		mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "MohoWormTubes" };
		mesh.vertices = verts; mesh.uv = uv; mesh.uv2 = uv2;
		mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
		mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
		material = new Material(tubeShader);
		view = new GameObject("MohoWormSwarmGpu");
		view.layer = layer;
		if (parent != null) view.transform.SetParent(parent, false);
		view.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer mr = view.AddComponent<MeshRenderer>();
		mr.sharedMaterial = material;
		mr.shadowCastingMode = ShadowCastingMode.On;
		mr.receiveShadows = true;
	}

	/// <summary>Keeps the culling box around the swarm (local).</summary>
	public void SetBounds(Vector3 centerLocal, float size) { if (mesh != null) mesh.bounds = new Bounds(centerLocal, Vector3.one * size); }

	void DispatchPoints(int k) { sim.Dispatch(k, (PointCount + 255) / 256, 1, 1); }
	void DispatchWorms(int k) { sim.Dispatch(k, (wormCount + 63) / 64, 1, 1); }

	void SetUniforms(float dt)
	{
		sim.SetInt("_PointCount", PointCount); sim.SetInt("_WormCount", wormCount);
		sim.SetInt("_TableMask", (1 << TableBits) - 1); sim.SetInt("_Iter", iterations);
		sim.SetFloat("_Dt", dt); sim.SetFloat("_Gravity", gravity); sim.SetFloat("_CellSize", 2f * rMax);
		sim.SetFloat("_Drag", drag); sim.SetFloat("_MaxDisp", maxSpeed * dt);
		sim.SetVector("_Goal", new Vector4(goal.x, goal.y, goal.z, restRadius));
		sim.SetFloat("_Attract", attract); sim.SetFloat("_Wander", wander); sim.SetFloat("_WanderTurn", wanderTurn);
		sim.SetFloat("_SteerRate", steerRate); sim.SetFloat("_Climb", climb); sim.SetFloat("_Thrust", thrust);
		sim.SetFloat("_WaveAmp", waveAmp); sim.SetFloat("_WaveK", 6.2831853f * wavesPerBody / (PPW - 1));
		sim.SetFloat("_WaveW", 6.2831853f * waveHz); sim.SetFloat("_PeriAmp", periAmp);
		sim.SetFloat("_PeriK", 6.2831853f * periWaves / (PPW - 1)); sim.SetFloat("_BendK", bendK);
		sim.SetFloat("_MuFwd", muFwd); sim.SetFloat("_MuBack", muBack); sim.SetFloat("_MuLat", muLat);
		sim.SetFloat("_MuWorm", muWorm); sim.SetFloat("_Adhesion", adhesion); sim.SetFloat("_Relax", relax);
		sim.SetInt("_FieldN", FieldN); sim.SetFloat("_FieldCell", FieldCell); sim.SetFloat("_TrailDecay", trailDecay); sim.SetFloat("_Time", SimTime);
		sim.SetFloat("_Crowd", crowd); sim.SetFloat("_DensityRef", densityRef); sim.SetFloat("_Follow", follow); sim.SetFloat("_TrailRef", trailRef);
		sim.SetFloat("_Swirl", swirl); sim.SetFloat("_SwirlScale", swirlScale); sim.SetFloat("_Restless", restless); sim.SetFloat("_Urgency", urgency);
		sim.SetFloat("_Orbit", orbit); sim.SetFloat("_SteerIn", steerIn);
		sim.SetVector("_Reach", new Vector4(reachTarget.x, reachTarget.y, reachTarget.z, reachShare));
		sim.SetInt("_FinishClamp", finishClamp ? 1 : 0);
		sim.SetInt("_TunnelOn", tunnels ? 1 : 0);
		sim.SetFloat("_GridReach", HeightN * HeightCell * 0.45f);   // 173 m: the grid is filled around the creature; worms dig in from 85 % of it
		sim.SetInt("_HFMode", 1); sim.SetInt("_HFN", HeightN); sim.SetFloat("_HFCell", HeightCell); sim.SetFloat("_GroundAmp", 0f);
		sim.SetFloat("_Activity", activity); sim.SetFloat("_Stick", stick); sim.SetFloat("_HeadGrip", headGrip);
		sim.SetFloat("_SpurtShare", spurtShare); sim.SetFloat("_SpurtLen", spurtLen); sim.SetFloat("_SpurtFast", spurtFast); sim.SetFloat("_SpurtSlow", spurtSlow);
	}

	/// <summary>One fixed 1/60 s step.</summary>
	public void Step()
	{
		if (!Ready) return;
		float dt = StepDt / substeps;
		if (heightDirty) { height.SetData(heights); heightDirty = false; }
		SetUniforms(dt);
		Vector3 fwd = new Vector3(anchorVel.x, 0, anchorVel.z);
		if (fwd.sqrMagnitude > 1e-4f) lastFwd = fwd.normalized;
		leapAcc += leapRate * StepDt; riseAcc += riseRate * StepDt;
		int leapB = (int)leapAcc + leapBurst, riseB = (int)riseAcc;
		leapAcc -= (int)leapAcc; leapBurst = 0;
		riseAcc -= riseB;
		if (spireOn) spireTime += StepDt;
		bool spawnSpire = spireOn && !spireRelease && !spireSpawned;
		if (spawnSpire) spireSpawned = true;
		counters.SetData(zero4);
		// leaping worms keep at most 60 m/s of the creature's speed (they are thrown, the creature may run faster)
		Vector3 carry = anchorVel.magnitude > 70f ? anchorVel.normalized * 70f : anchorVel;
		sim.SetVector("_Anchor", new Vector4(goal.x, goal.y, goal.z, anchorVel.magnitude));
		sim.SetVector("_AnchorVel", carry); sim.SetVector("_Fwd", lastFwd);
		sim.SetInt("_DiveMode", diveMode); sim.SetFloat("_DiveDist", diveDist);
		sim.SetInt("_LeapBudget", leapB); sim.SetInt("_RiseBudget", riseB);
		sim.SetInt("_SpireBudget", spawnSpire ? spireBudget : 0);
		sim.SetInt("_SpireRelease", spireRelease ? 1 : 0); sim.SetFloat("_SpawnP", spawnChance);
		sim.SetVector("_Spire", new Vector4(spireBase.x, spireBase.y, spireBase.z, spireHeight));
		sim.SetVector("_SpireCraft", new Vector4(spireCraft.x, spireCraft.y, spireCraft.z, spireCoil));
		sim.SetVector("_Ball", new Vector4(ballCenter.x, ballCenter.y, ballCenter.z, ballBlend)); sim.SetFloat("_BallR", ballRadius); sim.SetFloat("_Burst", burst); sim.SetFloat("_SpireHold", spireHold); sim.SetVector("_SpireLean", spireLean);
		sim.SetVector("_Irreg", new Vector4(spireIrreg, ballIrreg, grabChaos, 0f));
		sim.SetVector("_Mound", new Vector4(mound ? 1f : 0f, moundHeight, squash, squashFrom)); sim.SetFloat("_BlastR", blastRadius); sim.SetFloat("_BlastT0", blastStart); sim.SetFloat("_ReleaseU", releaseShare);
		sim.SetFloat("_SpireR", spireRadius); sim.SetFloat("_SpireT", spireTime); sim.SetFloat("_LeapUp", leapUp);
		sim.SetFloat("_MaxDispFast", maxSpeedFast * dt); sim.SetFloat("_StepDt", StepDt);
		serpentGpu.Upload(sim, serpent);
		DispatchWorms(kState);
		DispatchPoints(kCopy);
		sim.Dispatch(kFieldClear, (FieldN * FieldN + 255) / 256, 1, 1);
		DispatchPoints(kFieldSplat);
		for (int s = 0; s < substeps; s++)
		{
			sim.SetInt("_Step", (int)(stepCounter++));
			sim.SetFloat("_SubT", s * dt);
			DispatchPoints(kIntegrate);
			sim.Dispatch(kClear, (1 << TableBits) / 256, 1, 1);
			DispatchPoints(kBuild);
			for (int pass = 0; pass < 2; pass++)
			{
				DispatchPoints(kCollide);
				DispatchPoints(kApply);
				sim.SetInt("_Iter", pass == 0 ? iterations : 1);
				sim.SetInt("_Advance", pass == 0 ? 1 : 0);
				DispatchWorms(kChain);
			}
			DispatchWorms(kFinish);
		}
		DispatchWorms(kFrames);
		SimTime += StepDt;
		Steps++;
	}

	/// <summary>Real time: fixed steps (at most 4 per frame), the render interpolated between the last two.</summary>
	public void Tick(float dt)
	{
		if (!Ready) return;
		accum += Mathf.Min(dt, 0.1f);
		int n = 0;
		while (accum >= StepDt && n < 4) { Step(); accum -= StepDt; n++; }
		if (n == 4) accum = 0f;
		material.SetFloat("_Alpha", Mathf.Clamp01(accum / StepDt));
	}

	/// <summary>Move every stored position into a new local frame (matrix: old local -> new local).</summary>
	public void Rebase(Matrix4x4 oldToNew)
	{
		if (!Ready) return;
		sim.SetMatrix("_Rebase", oldToNew);
		if (serpent != null) serpent.Rebase(oldToNew);
		sim.SetInt("_PointCount", PointCount);
		DispatchPoints(kRebase);
		trail.SetData(new int[FieldN * FieldN * 3]);
	}

	/// <summary>Start of a new attack: the spire may spawn again.</summary>
	public void ResetSpire() { spireHold = 1f; spireLean = Vector3.zero; spireOn = false; spireRelease = false; spireSpawned = false; spireTime = 0f; ballBlend = 0f; spireCoil = 0f; spireHeight = 0f; burst = 0f; blastRadius = 0f; squash = 1f; squashFrom = 0f; releaseShare = 0f; }

	public void RequestPositions(Action<AsyncGPUReadbackRequest> done) { if (Ready) AsyncGPUReadback.Request(pos, done); }
	public void RequestStates(Action<AsyncGPUReadbackRequest> done) { if (Ready) AsyncGPUReadback.Request(state, done); }

	void ReleaseGpu()
	{
		Ready = false;
		ComputeBuffer[] bufs = { pos, prev, delta, contact, renderPrev, frame, worm, wormInfo, cellCount, cellItems, density, trail, state, counters, height, leap };
		for (int i = 0; i < bufs.Length; i++) if (bufs[i] != null) bufs[i].Release();
		pos = prev = delta = contact = renderPrev = frame = worm = wormInfo = cellCount = cellItems = density = trail = state = counters = height = leap = null;
		serpentGpu.Release();
	}

	public void Dispose()
	{
		ReleaseGpu();
		if (view != null) UnityEngine.Object.Destroy(view);
		if (mesh != null) UnityEngine.Object.Destroy(mesh);
		if (material != null) UnityEngine.Object.Destroy(material);
		if (sim != null) UnityEngine.Object.Destroy(sim);
		view = null; mesh = null; material = null;
	}
}
