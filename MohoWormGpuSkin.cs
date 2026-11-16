using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Moho creature's body as a GPU worm swarm (mohoWormBody = gpu). Wraps WormSwarmGpu for KSP:
/// - a local frame pinned to the planet surface (lat/lon/alt), placed in the world every frame (floating origin and planet
///   rotation safe), re-centred on the creature when it has travelled 2 km;
/// - a terrain height grid around the creature (128 x 128 cells of 0.75 m, wrapping), filled nearest-first from
///   CelestialBody.TerrainAltitude with a per-frame budget;
/// - the movement director: above a walking speed the swarm burrows (worms that fall behind dive, some leap out ahead, with
///   soil sprays); slow, it comes up and crawls. The ambush (EntityTracker.WormAttack.cs) takes over during an attack;
/// - vacuum dust (MohoWormDust) in the same frame;
/// - stats for the test probe, read back from the GPU without stalling (AsyncGPUReadback, 4 per second).
/// </summary>
public sealed class MohoWormGpuSkin : IWormSwarmSkin
{
	const double RebaseDistance = 2000.0;
	const int TerrainBudget = 400;            // terrain samples per frame
	const float BurrowSpeed = 2.5f;           // m/s: faster than a crawl, the swarm travels under the ground
	public static float GroundLift = 0f;      // m added to the sampled ground (setting mohoWormGroundLift; 2 = the old floating look)
	const float SurfaceBias = 0.03f;          // m: keeps the tubes from z-fighting the terrain mesh they rest on
	const int TerrainMask = 1 << 15;          // KSP layer 15, "Local Scenery": the terrain colliders

	public readonly WormSwarmGpu Core;
	public readonly MohoWormDust Dust;
	readonly GameObject frameObject;
	CelestialBody body;
	double lat0, lon0, alt0;
	Vector3d framePos;
	Quaternion frameRot = Quaternion.identity;
	bool initialized, faulted;
	string fault;
	Vector3 anchorWorld, anchorLocal, lastAnchorLocal, velLocal, target;
	bool haveLast, chasing;
	Func<Vector3, Vector3> groundSampler;

	// terrain grid bookkeeping: which world cell each slot holds
	readonly int[] heldX = new int[WormSwarmGpu.HeightN * WormSwarmGpu.HeightN];
	readonly int[] heldZ = new int[WormSwarmGpu.HeightN * WormSwarmGpu.HeightN];
	// slots that hold a provisional (plane) height for a cell not sampled yet, and the anchor cell of the last fill
	readonly int[] provX = new int[WormSwarmGpu.HeightN * WormSwarmGpu.HeightN];
	readonly int[] provZ = new int[WormSwarmGpu.HeightN * WormSwarmGpu.HeightN];
	int provAx = int.MinValue, provAz;
	static int[] spiral;
	/// <summary>Ground fixes (setting mohoWormGroundFix): clamp after collisions, provisional heights for unsampled cells,
	/// half the sampling budget ahead while travelling. false = the old ground handling.</summary>
	public static bool GroundFix = true;
	/// <summary>Worms burrow head first and drag their body in after them (setting mohoWormTunnels; false = the old upright dive).</summary>
	public static bool Tunnels = true;

	/// <summary>True while the ambush drives the swarm (dive, spire, ball); the movement director then stays out.</summary>
	public bool AmbushControl;
	/// <summary>false: hidden worms stay hidden even when the creature is slow (after a kill the pile is what fed).</summary>
	public bool AllowRise = true;
	/// <summary>Worms leaping out of the ground while the creature travels fast (dolphin arcs; setting mohoWormLeaps; off: soil sprays only).</summary>
	public static bool Leaps = false;

	public MohoWormGpuSkin(Transform parent, ComputeShader sim, Shader tube)
	{
		frameObject = new GameObject("MohoWormFrame");
		frameObject.layer = 0;
		if (parent != null) frameObject.transform.SetParent(parent, true);
		Core = new WormSwarmGpu(sim, tube);
		Core.finishClamp = GroundFix;
		Core.tunnels = Tunnels;
		Core.bendK = BendStiffness;
		Core.spurtShare = SpurtShare; Core.spurtLen = SpurtSeconds; Core.spurtFast = SpurtFast; Core.spurtSlow = SpurtSlow;
		baseAttract = Core.attract; baseUrgency = Core.urgency; baseFollow = Core.follow; baseOrbit = Core.orbit; baseSwirl = Core.swirl;
		if (Serpent) Core.serpent = new WormSerpent(UnityEngine.Random.Range(1, 1 << 30)) { length = SerpentLength, radius = SerpentRadius, waveScale = SerpentWave, submergedStart = SerpentSubmerged };
		Dust = new MohoWormDust(frameObject.transform, new Color(0.62f, 0.57f, 0.50f));
		for (int i = 0; i < heldX.Length; i++) { heldX[i] = int.MinValue; heldZ[i] = int.MinValue; provX[i] = int.MinValue; provZ[i] = int.MinValue; }
		if (spiral == null) BuildSpiral();
		ShaderName = "KK/WormTube";
	}

	/// <summary>True when the shaders are in the bundle and the GPU runs compute shaders.</summary>
	public static bool Available(out ComputeShader sim, out Shader tube)
	{
		sim = null; tube = null;
		if (!WormSwarmGpu.Supported || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11) return false;
		tube = KKShaderBundle.Find("KK/WormTube");
		sim = KKShaderBundle.FindCompute("WormSim");
		return tube != null && tube.isSupported && tube.name == "KK/WormTube" && sim != null;
	}

	static void BuildSpiral()
	{
		int n = WormSwarmGpu.HeightN, h = n / 2;
		var list = new System.Collections.Generic.List<int>(n * n);
		for (int z = -h; z < h; z++) for (int x = -h; x < h; x++) list.Add((x + h) | ((z + h) << 16));
		list.Sort((a, b) =>
		{
			int ax = (a & 0xffff) - h, az = (a >> 16) - h, bx = (b & 0xffff) - h, bz = (b >> 16) - h;
			return (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
		});
		spiral = list.ToArray();
	}

	// ------------------------------------------------------------------ frame
	void SetOrigin(Vector3d world)
	{
		lat0 = body.GetLatitude(world);
		lon0 = body.GetLongitude(world);
		alt0 = body.TerrainAltitude(lat0, lon0);
		UpdateFrame();
	}

	void UpdateFrame()
	{
		framePos = body.GetWorldSurfacePosition(lat0, lon0, alt0);
		Vector3d up = (framePos - body.position).normalized;
		Vector3d north = Vector3d.Exclude(up, body.transform.up);
		if (north.sqrMagnitude < 1e-8) north = Vector3d.Exclude(up, body.transform.forward);
		frameRot = Quaternion.LookRotation((Vector3)north.normalized, (Vector3)up);
		frameObject.transform.SetPositionAndRotation((Vector3)framePos, frameRot);
	}

	public Vector3 ToLocal(Vector3 world) { return Quaternion.Inverse(frameRot) * (Vector3)((Vector3d)world - framePos); }
	public Vector3 ToWorld(Vector3 local) { return (Vector3)(framePos + (Vector3d)(frameRot * local)); }
	public Quaternion FrameRotation { get { return frameRot; } }

	// ------------------------------------------------------------------ terrain grid
	float SampleLocalHeight(int i, int j)
	{
		float c = WormSwarmGpu.HeightCell;
		Vector3d w = framePos + (Vector3d)(frameRot * new Vector3((i + 0.5f) * c, 0f, (j + 0.5f) * c));
		double lat = body.GetLatitude(w), lon = body.GetLongitude(w);
		Vector3d g = body.GetWorldSurfacePosition(lat, lon, body.TerrainAltitude(lat, lon));
		// the visible ground: a ray down onto the terrain collider (the surface craft stand on); where none is loaded, the terrain
		// function (it can sit a metre or more below the rendered mesh). Then GroundLift (0 by default) and a hair of bias.
		Vector3 up = frameRot * Vector3.up;
		RaycastHit hit;
		if (Physics.Raycast((Vector3)g + up * 60f, -up, out hit, 120f, TerrainMask, QueryTriggerInteraction.Ignore)) { g = hit.point; RaySamples++; }
		return (Quaternion.Inverse(frameRot) * (Vector3)(g - framePos)).y + GroundLift + SurfaceBias;
	}

	int RefreshTerrain(int budget)
	{
		int n = WormSwarmGpu.HeightN, h = n / 2;
		int ax = Mathf.FloorToInt(anchorLocal.x / WormSwarmGpu.HeightCell), az = Mathf.FloorToInt(anchorLocal.z / WormSwarmGpu.HeightCell);
		FillProvisional(ax, az);
		int done = 0;
		float speed = new Vector2(velLocal.x, velLocal.z).magnitude;
		if (GroundFix && speed > BurrowSpeed)
			done += RefreshPass(ax, az, budget / 2, velLocal.x / speed, velLocal.z / speed);   // half the budget on the ground ahead
		done += RefreshPass(ax, az, budget - done, 0f, 0f);
		return done;
	}

	// nearest-first; with a direction (fx, fz) only the cells ahead of the anchor
	int RefreshPass(int ax, int az, int budget, float fx, float fz)
	{
		int n = WormSwarmGpu.HeightN, h = n / 2, m = n - 1;
		bool aheadOnly = fx != 0f || fz != 0f;
		int done = 0;
		for (int k = 0; k < spiral.Length && done < budget; k++)
		{
			int ox = (spiral[k] & 0xffff) - h, oz = (spiral[k] >> 16) - h;
			if (aheadOnly && ox * fx + oz * fz <= 0f) continue;
			int cx = ax + ox, cz = az + oz;
			int slot = (cx & m) + n * (cz & m);
			if (heldX[slot] == cx && heldZ[slot] == cz) continue;
			Core.SetHeight(cx, cz, SampleLocalHeight(cx, cz));
			heldX[slot] = cx; heldZ[slot] = cz;
			done++;
		}
		return done;
	}

	bool Real(int cx, int cz)
	{
		int n = WormSwarmGpu.HeightN, m = n - 1, slot = (cx & m) + n * (cz & m);
		return heldX[slot] == cx && heldZ[slot] == cz;
	}

	// A slot that wrapped round to a new cell still holds the height of a place 192 m away (on a slope: metres off, so worms
	// there floated or sank). Until the real sample comes, it gets a plane through the ground around the anchor instead.
	// Runs only when the anchor enters a new cell.
	void FillProvisional(int ax, int az)
	{
		if (!GroundFix || (ax == provAx && az == provAz)) return;
		provAx = ax; provAz = az;
		int n = WormSwarmGpu.HeightN, h = n / 2, m = n - 1;
		float c = WormSwarmGpu.HeightCell;
		const int R = 6;   // cells out to each side for the slope
		float x0 = (ax + 0.5f) * c, z0 = (az + 0.5f) * c;
		float h0 = Real(ax, az) ? Core.GroundAt(x0, z0) : anchorLocal.y + GroundLift + SurfaceBias;
		float gx = 0f, gz = 0f;
		if (Real(ax - R, az) && Real(ax + R, az)) gx = Mathf.Clamp((Core.GroundAt(x0 + R * c, z0) - Core.GroundAt(x0 - R * c, z0)) / (2f * R * c), -1f, 1f);
		if (Real(ax, az - R) && Real(ax, az + R)) gz = Mathf.Clamp((Core.GroundAt(x0, z0 + R * c) - Core.GroundAt(x0, z0 - R * c)) / (2f * R * c), -1f, 1f);
		for (int cz = az - h; cz < az + h; cz++)
			for (int cx = ax - h; cx < ax + h; cx++)
			{
				int slot = (cx & m) + n * (cz & m);
				if ((heldX[slot] == cx && heldZ[slot] == cz) || (provX[slot] == cx && provZ[slot] == cz)) continue;
				Core.SetHeight(cx, cz, h0 + (gx * (cx - ax) + gz * (cz - az)) * c);
				provX[slot] = cx; provZ[slot] = cz;
				ProvisionalCells++;
			}
	}

	void InvalidateTerrain()
	{
		for (int i = 0; i < heldX.Length; i++) { heldX[i] = int.MinValue; provX[i] = int.MinValue; }
		provAx = int.MinValue;
	}

	// ------------------------------------------------------------------ IWormSwarmSkin
	public void Initialize(CelestialBody celestialBody, Vector3 ground, Func<Vector3, Vector3> sampler)
	{
		body = celestialBody;
		groundSampler = sampler;
		if (body == null) { faulted = true; fault = "no body"; return; }
		Core.gravity = Mathf.Clamp((float)(body.GeeASL * 9.80665), 2.5f, 10f);
		Dust.SetGravity(Core.gravity);
		Start(ground);
	}

	void Start(Vector3 ground)
	{
		SetOrigin(ground);
		anchorWorld = ground;
		anchorLocal = lastAnchorLocal = ToLocal(ground);
		haveLast = true;
		InvalidateTerrain();
		try
		{
			RefreshTerrain(4200);   // the heap's own ground before the first worm is placed (about 97 m across)
			Core.Init(anchorLocal, frameObject.transform, 0);
			Core.View.transform.localPosition = Vector3.zero;
			Core.View.transform.localRotation = Quaternion.identity;
			initialized = true;
		}
		catch (Exception ex) { faulted = true; fault = ex.GetType().Name + ": " + ex.Message; UnityEngine.Debug.Log("[UT Moho] gpu worm swarm failed: " + fault); }
	}

	public void SetGroundSampler(Func<Vector3, Vector3> sampler) { if (sampler != null) groundSampler = sampler; }
	public void SetTarget(Vector3d worldTarget, bool pursue) { target = (Vector3)worldTarget; chasing = pursue; }
	public void ApplyWorldShift(Vector3 delta) { anchorWorld += delta; target += delta; }   // the frame itself is re-placed from the body each frame
	public void Reposition(Vector3 shift)
	{
		if (body == null) return;
		Start(anchorWorld + shift);
	}

	public void Apply(MohoPose pose)
	{
		if (!initialized || faulted || pose == null) return;
		float dt = TimeWarp.deltaTime;
		if (Time.timeScale <= 0f || dt <= 0f) return;
		long start = Stopwatch.GetTimestamp();
		UpdateFrame();
		anchorWorld = pose.Origin;
		Vector3 local = ToLocal(anchorWorld);
		if (new Vector2(local.x, local.z).magnitude > RebaseDistance)
		{
			// re-centre the frame on the creature; every stored position moves into the new frame on the GPU
			Matrix4x4 oldToWorld = Matrix4x4.TRS((Vector3)framePos, frameRot, Vector3.one);
			SetOrigin(anchorWorld);
			Matrix4x4 worldToNew = Matrix4x4.TRS((Vector3)framePos, frameRot, Vector3.one).inverse;
			Core.Rebase(worldToNew * oldToWorld);
			lastAnchorLocal = (worldToNew * oldToWorld).MultiplyPoint3x4(lastAnchorLocal);
			InvalidateTerrain();
			Dust.ResetWorms();
			local = ToLocal(anchorWorld);
			Rebases++;
		}
		anchorLocal = local;
		Vector3 step = haveLast ? anchorLocal - lastAnchorLocal : Vector3.zero;
		step.y = 0f;
		lastAnchorLocal = anchorLocal;
		if (step.magnitude > 300f) step = Vector3.zero;   // a teleport, not motion
		velLocal = Vector3.Lerp(velLocal, step / dt, 1f - Mathf.Exp(-8f * dt));

		long t0 = Stopwatch.GetTimestamp();
		TerrainSamples += RefreshTerrain(TerrainBudget);
		LastTerrainMilliseconds = Ms(t0);

		Core.goal = anchorLocal;
		Core.anchorVel = velLocal;
		if (!AmbushControl) Director(dt);
		SerpentDirector(dt);
		Core.SetBounds(anchorLocal, 400f);
		long t1 = Stopwatch.GetTimestamp();
		Core.Tick(dt);
		LastSimulationMilliseconds = Ms(t1);
		Dust.Step(dt);
		Readback(dt);
		LastApplyMilliseconds = Ms(start);
	}

	// ------------------------------------------------------------------ the serpent (travel as one body)
	/// <summary>Travel as one serpent of worms (setting mohoWormSerpent); its size and wave strength (mohoWormSerpent*).</summary>
	public static bool Serpent = true;
	public static float SerpentLength = 100f, SerpentRadius = 3.5f, SerpentWave = 1f;
	/// <summary>m the serpent runs under the dirt after setting off (mohoWormSerpentSubmerged); stopping sinks it and it
	/// comes up as a heap (mohoWormSerpentSink; false = the old slump onto the ground).</summary>
	public static float SerpentSubmerged = 70f;
	public static bool SerpentSink = true;
	/// <summary>How the body travels. Auto: by speed (the old rule: faster than a crawl it burrows / forms the serpent). Heap: it
	/// crawls along as a heap at any speed (the worm hunt's dormant, search and wander). Serpent: it travels as the serpent.</summary>
	public enum Travel { Auto, Heap, Serpent }
	public Travel TravelMode = Travel.Auto;
	bool WantsSerpent(float speed) { return TravelMode == Travel.Serpent ? speed > 0.5f : (TravelMode == Travel.Auto && speed > BurrowSpeed + 0.5f); }
	public WormSerpent Body { get { return Core.serpent; } }
	float slowTime, breachAcc;
	readonly Vector3[] crossAt = new Vector3[16], crossDir = new Vector3[16];

	void SerpentDirector(float dt)
	{
		WormSerpent s = Core.serpent;
		if (s == null) return;
		float speed = new Vector2(velLocal.x, velLocal.z).magnitude;
		Func<float, float, float> ground = Core.GroundAt;
		if (AmbushControl)
		{
			if (s.State == WormSerpent.Mode.Travel) s.StartPlunge(velLocal);   // the attack: one high arc, then straight down
		}
		else if (s.State == WormSerpent.Mode.Off)
		{
			if (WantsSerpent(speed) && AllowRise && HiddenWorms >= WormTotal / 2) { s.Begin(anchorLocal, velLocal, ground); slowTime = 0f; }
		}
		else if (s.State == WormSerpent.Mode.Travel)
		{
			slowTime = speed < BurrowSpeed ? slowTime + dt : 0f;
			if (TravelMode == Travel.Heap) slowTime = 1f;   // told to be a heap: it goes under on the spot and comes up as one
			if (slowTime > 0.8f)
			{
				if (SerpentSink) s.StartSink(velLocal);   // stopped: the body sinks under the dirt on the spot, then rises as a heap
				else s.Release();                         // old: the body lets go and slumps into a heap
			}
		}
		s.Step(dt, anchorLocal, velLocal, ground);
		// dust where the body cuts through the ground (placeholder look until the dust phase)
		if (s.State == WormSerpent.Mode.Travel || s.State == WormSerpent.Mode.Plunge)
		{
			int n = s.Crossings(ground, crossAt, crossDir);
			float bodySpeed = s.State == WormSerpent.Mode.Plunge ? s.plungeSpeed : speed;
			breachAcc += dt * Mathf.Clamp(bodySpeed, 5f, 60f) * 0.6f;
			while (breachAcc >= 1f)
			{
				breachAcc -= 1f;
				for (int k = 0; k < n; k++)
				{
					Vector3 d = crossDir[k];
					Vector3 o = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) * s.radius;
					Vector3 at = crossAt[k] + o;
					at.y = Core.GroundAt(at.x, at.z);
					Dust.Spray(at, Vector3.up * 1.2f + new Vector3(d.x, 0f, d.z) * 0.6f, 10, Mathf.Lerp(4f, 12f, Mathf.Clamp01(bodySpeed / 50f)), 0.5f, new Vector3(d.x, 0f, d.z) * Mathf.Min(bodySpeed, 40f) * 0.3f);
				}
			}
		}
	}

	// The heap must be half underground before the creature sets off as the serpent: until then its moves are held back and
	// the heap digs in (otherwise the worms left behind fall outside the terrain grid and sit on wrong ground). Max 8 s.
	bool departPending;
	float departHeld;
	const float DepartMaxHold = 8f;
	/// <summary>True while the worm hunt is striking: the heap digs in at most HurryHold s before the serpent sets off.</summary>
	public bool Hurry;
	public static float HurryHold = 1.5f;

	/// <summary>The creature wants to move by `worldDelta` this frame. False: hold it still this frame (the heap digs in first).</summary>
	public bool RequestDeparture(Vector3 worldDelta, float dt)
	{
		WormSerpent s = Core.serpent;
		if (s == null || !initialized || faulted || AmbushControl || !AllowRise || dt <= 0f) return true;
		if (s.State != WormSerpent.Mode.Off) { departHeld = 0f; return true; }
		Vector3 d = Quaternion.Inverse(frameRot) * worldDelta;
		d.y = 0f;
		float speed = d.magnitude / dt;
		if (!WantsSerpent(speed)) return true;   // a crawl (or told to stay a heap): no serpent needed
		departPending = true;
		departHeld += dt;
		if (HiddenWorms >= WormTotal / 2 || departHeld > (Hurry ? HurryHold : DepartMaxHold))
		{
			s.Begin(anchorLocal, d / dt, Core.GroundAt);
			slowTime = 0f; departHeld = 0f;
			return true;
		}
		return false;
	}

	/// <summary>True while the serpent forms the body (travel or the attack plunge).</summary>
	public bool SerpentActive { get { return Core.serpent != null && Core.serpent.State != WormSerpent.Mode.Off; } }

	// faster than a crawl: worms that fall behind dive, some leap out ahead (the warning); slow: they come up and crawl
	float sprayAcc;
	float podTimer;
	// ------------------------------------------------------------------ softer bodies, a calm heap
	/// <summary>Bending stiffness of every worm (setting mohoWormBendStiffness; 0.3 = the old, stiffer bodies). The muscle waves
	/// (the wriggle) are not changed.</summary>
	public static float BendStiffness = 0.15f;
	/// <summary>Setting mohoWormCalmHeap: a dormant heap churns in place instead of combing all its worms one way.</summary>
	public static bool CalmHeap = true;
	/// <summary>How far (m, sideways) the centre of the worms above ground trails the creature's own point (from the readback).</summary>
	public float HeapTrail { get; private set; }
	/// <summary>Set by the worm hunt while the heap searches or wanders: its worms crawl harder (setting mohoWormSearchHurry).</summary>
	public bool Chase;
	public static float ChaseUrgency = 2f;
	/// <summary>Setting wormHeapRollMetres: while the heap searches or wanders, worms this far behind dig in and resurface near the
	/// front (a rolling heap). 0 = off: they only crawl.</summary>
	public static float RollDistance = 40f;
	/// <summary>Random bursts (settings mohoWormSpurt*): this share of worms is, at any moment, crawling faster or slower for a
	/// short while (0 = off).</summary>
	public static float SpurtShare = 0.12f, SpurtSeconds = 0.5f, SpurtFast = 2f, SpurtSlow = 0.5f;
	/// <summary>Set by the worm hunt while the swarm is dormant or fed.</summary>
	public bool Calm;
	float baseAttract, baseUrgency, baseFollow, baseOrbit, baseSwirl, baseRest = -1f, calmBlend, chaseBlend;
	/// <summary>Radius (m) a calm heap's worms settle into (setting mohoWormDormantSpread; the heap's normal is 20 m).</summary>
	public static float CalmSpread = 40f;

	// A drifting dormant heap pulls every worm toward its moving spot and worms follow the trails of heads ahead: together they
	// comb the whole heap one way. Calm: less pull and hurry, almost no trail following, a little more circling and eddies.
	void CalmDirector(float dt)
	{
		bool calm = CalmHeap && Calm && !AmbushControl && !SerpentActive;
		calmBlend = Mathf.MoveTowards(calmBlend, calm ? 1f : 0f, dt / 3f);   // eases over 3 s, no sudden change
		float k = calmBlend;
		Core.attract = Mathf.Lerp(baseAttract, baseAttract * 0.45f, k);
		Core.urgency = Mathf.Lerp(baseUrgency, baseUrgency * 0.25f, k);
		Core.follow = Mathf.Lerp(baseFollow, baseFollow * 0.1f, k);
		Core.orbit = Mathf.Lerp(baseOrbit, baseOrbit * 1.4f, k);
		Core.swirl = Mathf.Lerp(baseSwirl, baseSwirl * 1.4f, k);
		// a searching heap: the worms far from its centre hurry much more, so the mass keeps up with a fast search
		chaseBlend = Mathf.MoveTowards(chaseBlend, (Chase && !AmbushControl && !SerpentActive) ? 1f : 0f, dt / 1.5f);
		Core.urgency *= Mathf.Lerp(1f, ChaseUrgency, chaseBlend);
		if (baseRest < 0f) baseRest = Core.restRadius;
		Core.restRadius = Mathf.Lerp(baseRest, Mathf.Max(baseRest, CalmSpread), k);   // a resting heap spreads out wide
	}

	void Director(float dt)
	{
		CalmDirector(dt);
		float speed = velLocal.magnitude;
		Core.reachShare = 0f;
		bool depart = departPending;
		departPending = false;
		if (SerpentActive || depart)
		{
			// the body travels (or is about to): every worm still on the ground digs in (head first) and joins it underground
			Core.diveMode = Hurry ? 3 : 2; Core.leapRate = 0f; Core.riseRate = 0f; sprayAcc = 0f;
			return;
		}
		if (speed > BurrowSpeed && TravelMode != Travel.Heap)
		{
			Core.diveMode = 1;
			// pods at irregular gaps (each pod at its own random spot, chosen on the GPU), not a steady stream
			Core.leapRate = 0f;
			podTimer -= dt;
			if (Leaps && podTimer <= 0f)
			{
				Core.leapBurst = UnityEngine.Random.Range(8, 21);
				podTimer = UnityEngine.Random.Range(0.15f, 0.6f) / Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(speed / 20f));
			}
			Core.riseRate = 0f;
			sprayAcc += dt * 60f * Mathf.Clamp01(speed / 20f);
			Vector3 fw = velLocal / Mathf.Max(speed, 0.01f), sd = new Vector3(-fw.z, 0f, fw.x);
			Vector3 carry = Vector3.ClampMagnitude(velLocal, 60f) * 0.6f;
			while (sprayAcc >= 1f)
			{
				sprayAcc -= 1f;
				for (int e = 0; e < 3; e++)
				{
					Vector3 at = anchorLocal + fw * UnityEngine.Random.Range(8f, 40f) + sd * UnityEngine.Random.Range(-28f, 28f);
					at.y = Core.GroundAt(at.x, at.z);
					Dust.Spray(at, Vector3.up + fw * 0.3f, 14, 6f, 0.5f, carry);
				}
			}
		}
		else
		{
			// a searching heap rolls: worms falling more than RollDistance behind dig in head first (in sight) and come up again
			// near the front, so it keeps up with a fast search without stranding anyone (worms crawl only ~1.5 m/s on average)
			bool roll = Chase && RollDistance > 0f && !AmbushControl;
			Core.diveMode = roll ? 1 : 0;
			Core.diveDist = roll ? RollDistance : 28f;
			Core.leapRate = 0f;
			Core.riseRate = (AllowRise && HiddenWorms > 0) ? 400f : 0f;
			sprayAcc = 0f;
			if (AllowRise && HiddenWorms > 50 && !Dust.WormDriven && UnityEngine.Random.value < dt * 30f)   // the old random puffs
			{
				Vector2 d = UnityEngine.Random.insideUnitCircle * Core.restRadius;
				Vector3 at = anchorLocal + new Vector3(d.x, 0f, d.y);
				at.y = Core.GroundAt(at.x, at.z);
				Dust.Spray(at, Vector3.up, 4, 1.8f, 0.6f, Vector3.zero);
			}
		}
	}

	// ------------------------------------------------------------------ stats (non-blocking)
	float readTimer;
	bool posPending, statePending;
	public int HiddenWorms { get; private set; }
	public int VisibleWorms { get; private set; }
	public int LeapingWorms { get; private set; }
	public int SpireWorms { get; private set; }
	public float SpireTop { get; private set; }          // m above the spire base, highest spire point
	public int Rebases { get; private set; }
	public long TerrainSamples { get; private set; }
	public long RaySamples { get; private set; }
	public long ProvisionalCells { get; private set; }
	Vector3 centroidLocal;
	uint[] stateCopy, statePrev;
	bool[] wasAbove;   // per worm, from the last position readback: its middle point was above the ground
	/// <summary>Worms that went under the ground by digging in (dive state first): the right way.</summary>
	public long DugIn { get; private set; }
	/// <summary>Worms switched off straight from the surface, the serpent or a rise: they vanished in sight (should stay 0).
	/// Spire worms are not counted (the strike's own handover).</summary>
	public long PoppedInSight { get; private set; }

	void Readback(float dt)
	{
		readTimer -= dt;
		if (readTimer > 0f) return;
		readTimer = (Dust.WormsBusy || AmbushControl) ? 0.1f : 0.25f;   // faster while the worms throw dust (smoother, closer to the bodies)
		if (!statePending) { statePending = true; Core.RequestStates(OnStates); }
		if (!posPending) { posPending = true; Core.RequestPositions(OnPositions); }
	}

	void OnStates(AsyncGPUReadbackRequest r)
	{
		statePending = false;
		if (r.hasError || !initialized) return;
		var data = r.GetData<uint>();
		if (stateCopy == null || stateCopy.Length != data.Length) stateCopy = new uint[data.Length];
		data.CopyTo(stateCopy);
		if (statePrev != null && statePrev.Length == stateCopy.Length)
		{
			for (int i = 0; i < stateCopy.Length; i++)
			{
				if (stateCopy[i] != 2u || statePrev[i] == 2u) continue;
				uint was = statePrev[i];
				if (was == 1u) DugIn++;
				else if ((was == 0u || was == 5u || was == 6u) && wasAbove != null && i < wasAbove.Length && wasAbove[i]) PoppedInSight++;   // really seen to vanish
			}
		}
		if (statePrev == null || statePrev.Length != stateCopy.Length) statePrev = new uint[stateCopy.Length];
		Array.Copy(stateCopy, statePrev, stateCopy.Length);
		int hidden = 0, leap = 0, spire = 0;
		for (int i = 0; i < stateCopy.Length; i++) { uint s = stateCopy[i]; if (s == 2u) hidden++; else if (s == 3u) leap++; else if (s == 4u) spire++; }
		HiddenWorms = hidden; LeapingWorms = leap; SpireWorms = spire; VisibleWorms = stateCopy.Length - hidden;
	}

	void OnPositions(AsyncGPUReadbackRequest r)
	{
		posPending = false;
		if (r.hasError || !initialized) return;
		var data = r.GetData<Vector4>();
		int P = WormSwarmGpu.PPW, W = data.Length / P;
		double cx = 0, cy = 0, cz = 0; int n = 0;
		float maxR = 0f, maxSeg = 0f, top = 0f, pile = 0f;
		if (wasAbove == null || wasAbove.Length != W) wasAbove = new bool[W];
		for (int w = 0; w < W; w++)
		{
			Vector4 m = data[w * P + P / 2];
			wasAbove[w] = m.y > Core.GroundAt(m.x, m.z) - m.w;
		}
		for (int w = 0; w < W; w++)
		{
			uint s = stateCopy != null && w < stateCopy.Length ? stateCopy[w] : 0u;
			if (s == 2u) continue;
			for (int k = 0; k < P; k += 3)
			{
				Vector4 q = data[w * P + k];
				cx += q.x; cy += q.y; cz += q.z; n++;
				float dx = q.x - anchorLocal.x, dz = q.z - anchorLocal.z;
				maxR = Mathf.Max(maxR, Mathf.Sqrt(dx * dx + dz * dz));
				float hg = q.y - Core.GroundAt(q.x, q.z);
				if (s == 4u) top = Mathf.Max(top, q.y - Core.spireBase.y);
				else if (s == 0u) pile = Mathf.Max(pile, hg);
			}
			for (int k = 0; k + 1 < P; k++) maxSeg = Mathf.Max(maxSeg, ((Vector3)(data[w * P + k + 1] - data[w * P + k])).magnitude);
		}
		if (n > 0) centroidLocal = new Vector3((float)(cx / n), (float)(cy / n), (float)(cz / n));
		HeapTrail = n > 0 ? new Vector2(centroidLocal.x - anchorLocal.x, centroidLocal.z - anchorLocal.z).magnitude : 0f;
		MaxPointRadius = maxR; MaxSegmentLength = maxSeg; SpireTop = top; PileHeight = pile;
		if (Diagnostics) MeasureGroundGap(data, P, W);
		Dust.FromWorms(data, stateCopy, P, Core.GroundAt);
	}

	/// <summary>Test probe only (KKTestBoot sets it): compare worms with the terrain collider (raycasts, at most 64 per readback).</summary>
	public static bool Diagnostics;
	readonly System.Collections.Generic.List<float> gaps = new System.Collections.Generic.List<float>(64);
	/// <summary>Median gap (m) between the underside of resting surface worms and the terrain collider (0 = lying on it).</summary>
	public float GroundGapMedian { get; private set; }
	/// <summary>Share of those resting points more than 0.3 m off the collider (either way).</summary>
	public float GroundOffFraction { get; private set; }
	/// <summary>Share of sampled surface-worm points that are wholly more than 0.3 m under the collider (fell through).</summary>
	public float GroundSunkFraction { get; private set; }
	public int GroundGapSamples { get; private set; }

	void MeasureGroundGap(Unity.Collections.NativeArray<Vector4> data, int P, int W)
	{
		if (stateCopy == null || W == 0) return;
		gaps.Clear();
		Vector3 up = frameRot * Vector3.up;
		int sampled = 0, sunk = 0, off = 0;
		int stride = Mathf.Max(1, W / 97);
		for (int w = (int)(Time.frameCount % stride); w < W && sampled < 64; w += stride)
		{
			if (w >= stateCopy.Length || stateCopy[w] != 0u) continue;
			Vector4 q = data[w * P + (w % P)];
			RaycastHit hit;
			Vector3 world = ToWorld(q);
			if (!Physics.Raycast(world + up * 20f, -up, out hit, 60f, TerrainMask, QueryTriggerInteraction.Ignore)) continue;
			float coll = ToLocal(hit.point).y;
			sampled++;
			if (q.y + q.w < coll - 0.3f) { sunk++; continue; }
			// resting on the swarm's own ground (not lying on other worms): how far is that from the real ground?
			if (q.y - q.w - (Core.GroundAt(q.x, q.z) - GroundLift - SurfaceBias) < 0.3f + GroundLift)
			{
				float gap = q.y - q.w - coll;
				gaps.Add(gap);
				if (Mathf.Abs(gap) > 0.3f) off++;
			}
		}
		GroundGapSamples = sampled;
		GroundSunkFraction = sampled > 0 ? sunk / (float)sampled : 0f;
		GroundOffFraction = gaps.Count > 0 ? off / (float)gaps.Count : 0f;
		if (gaps.Count > 0) { gaps.Sort(); GroundGapMedian = gaps[gaps.Count / 2]; }
	}

	static float Ms(long since) { return (float)((Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency); }

	// ------------------------------------------------------------------ IWormSwarmSkin properties
	public string BodyName { get { return faulted ? "gpu-failed: " + fault : "gpu"; } }
	public int WormTotal { get { return Core.wormCount; } }
	public int PointCount { get { return Core.PointCount; } }
	public string ShaderName { get; private set; }
	public int RenderedTriangles { get { return Core.TriangleCount; } }
	public bool RendererEnabled { get { return Core.View != null && Core.View.activeInHierarchy; } }
	public Bounds RenderBounds { get { return new Bounds(anchorWorld, Vector3.one * 220f); } }
	public Vector3 Anchor { get { return anchorWorld; } }
	public Quaternion Frame { get { return frameRot; } }
	public Vector3 PointCentroid { get { return initialized ? ToWorld(centroidLocal) : anchorWorld; } }
	public int SpilledWorms { get { return LeapingWorms; } }
	public float MaxPointRadius { get; private set; }
	public float MaxSegmentLength { get; private set; }
	public float OverlapFraction { get { return 0f; } }
	public float RearDragFraction { get { return 0f; } }
	public float PileHeight { get; private set; }
	public int TumbleCount { get { return 0; } }
	public float HeadingCoherence { get { return 0f; } }
	public float HeapAspect { get { return 0f; } }
	public float LastApplyMilliseconds { get; private set; }
	public float LastTerrainMilliseconds { get; private set; }
	public float LastUploadMilliseconds { get { return 0f; } }
	public float LastSimulationMilliseconds { get; private set; }
	public float LastMeshMilliseconds { get { return 0f; } }
	public int WorkerSteps { get { return Core.Steps; } }
	public bool Threaded { get { return false; } }
	public string Fault { get { return fault; } }

	public void Dispose()
	{
		initialized = false;
		Core.Dispose();
		Dust.Destroy();
		if (frameObject != null) UnityEngine.Object.Destroy(frameObject);
	}
}

/// <summary>
/// Soil thrown up by the worms, in the swarm's local frame (y up). No air on Moho: grit and clods fly in ballistic arcs and
/// disappear as they come back down to the height they started from (no lingering cloud). Gravity is applied in the local
/// frame (a force over lifetime), never along world -Y.
/// </summary>
public sealed class MohoWormDust
{
	readonly ParticleSystem grit, clods;
	readonly GameObject root;
	public float Gravity = 2.7f;
	/// <summary>Lit dust (WormDustFx: six-way lit puffs, grit streaks, rock clods, ejecta curtain). Settings mohoWormDust*.</summary>
	public static bool Lit = true, Soft = true;
	public static float DensityScale = 1f, SunScale = 1f;
	readonly WormDustFx fx;
	Shader dustShader;
	WormBlastFx blast;
	bool blastSoft;
	bool softSet;

	public MohoWormDust(Transform frame, Color soil)
	{
		root = new GameObject("MohoWormDust");
		root.layer = 0;
		root.transform.SetParent(frame, false);
		if (Lit)
		{
			Shader s = KKShaderBundle.Find("KK/WormDust");
			if (WormDustFx.Available(s))
			{
				try
				{
					Shader rockShader = Shader.Find("KSP/Diffuse");
					if (rockShader == null) rockShader = Shader.Find("Legacy Shaders/Diffuse");
					fx = new WormDustFx(root.transform, soil, s, rockShader, Gravity) { Density = DensityScale };
					dustShader = s;
					return;
				}
				catch (Exception ex) { UnityEngine.Debug.Log("[UT Moho] lit dust failed, using the old dust: " + ex.Message); fx = null; }
			}
			else UnityEngine.Debug.Log("[UT Moho] shader KK/WormDust not in the bundle: the old dust is used");
		}
		Texture2D tex = SoftDot(64);
		grit = Make("grit", tex, soil, 0.15f, 0.4f, 30000);
		clods = Make("clods", tex, soil * 0.55f, 0.35f, 0.9f, 4000);
	}

	/// <summary>True when the lit dust runs.</summary>
	public bool IsLit { get { return fx != null; } }

	// the sun as the dust sees it: direction from the swarm to the sun, the scene's sun light colour, almost no ambient (no air)
	void UpdateSun()
	{
		if (fx == null || root == null) return;
		Vector3 toSun = Vector3.up;
		if (FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0 && FlightGlobals.Bodies[0] != null)
			toSun = (Vector3)(FlightGlobals.Bodies[0].position - (Vector3d)root.transform.position);
		Color c = Color.white;
		if (Sun.Instance != null && Sun.Instance.sunLight != null) c = Sun.Instance.sunLight.color * Mathf.Max(Sun.Instance.sunLight.intensity, 0.2f);
		fx.SetSun(toSun, c * SunScale, new Color(0.035f, 0.034f, 0.036f));
		if (!softSet)
		{
			Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
			if (cam == null) return;   // decide once the flight camera exists (else the soft edges stay off for this swarm)
			softSet = true;
			bool soft = Soft;
			if (soft) cam.depthTextureMode |= DepthTextureMode.Depth;   // the soft edges read the scene depth
			fx.SetSoft(soft, 1.5f);
			blastSoft = soft;
			// build the kill blast now, not in the kill frame (that hitch used to swallow the flash); it is inert until Fire
			try { if (blast == null && dustShader != null) { blast = new WormBlastFx(root.transform, dustShader, fx); blast.SetSoft(blastSoft, 1.5f); } }
			catch (Exception ex) { UnityEngine.Debug.Log("[UT Moho] blast fx failed: " + ex.Message); }
		}
	}

	/// <summary>The eruption's ejecta curtain (lit dust only; the old dust falls back to a ring).</summary>
	public void Curtain(Vector3 center, float radius, float strength, float speedScale, Func<float, float, float> ground)
	{
		if (fx != null) fx.Curtain(center, radius, strength, speedScale, ground);
		else Ring(center, radius, (int)(2200 * strength), 9f * speedScale, 1.4f, ground);
	}

	ParticleSystem Make(string name, Texture2D tex, Color col, float sizeMin, float sizeMax, int max)
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
		main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
		main.startColor = col;
		var em = ps.emission; em.enabled = false;
		var sh = ps.shape; sh.enabled = false;
		var fo = ps.forceOverLifetime;   // gravity along the frame's down (KSP gravity is not world -Y)
		fo.enabled = true; fo.space = ParticleSystemSimulationSpace.Local;
		fo.x = 0f; fo.y = -Gravity; fo.z = 0f;
		var col2 = ps.colorOverLifetime;
		col2.enabled = true;
		var g = new Gradient();
		g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
		col2.color = g;
		var r = go.GetComponent<ParticleSystemRenderer>();
		Shader s = Shader.Find("KSP/Particles/Alpha Blended");
		if (s == null) s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
		if (s == null) s = Shader.Find("Particles/Alpha Blended");
		var mat = new Material(s);
		mat.mainTexture = tex;
		r.sharedMaterial = mat;
		r.renderMode = ParticleSystemRenderMode.Billboard;
		r.shadowCastingMode = ShadowCastingMode.Off;
		ps.Play();
		return ps;
	}

	static Texture2D SoftDot(int n)
	{
		var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
		var px = new Color[n * n];
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
				float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
				px[y * n + x] = new Color(1f, 1f, 1f, a * a);
			}
		t.SetPixels(px); t.Apply();
		return t;
	}

	static Vector3 Cone(Vector3 dir, float spread) { return (dir.normalized + UnityEngine.Random.onUnitSphere * spread).normalized; }

	void Emit(ParticleSystem ps, Vector3 at, Vector3 v)
	{
		var ep = new ParticleSystem.EmitParams();
		ep.position = at;
		ep.velocity = v;
		// it lives until it comes back down to where it started (ballistic in the local frame)
		ep.startLifetime = Mathf.Clamp(2f * Mathf.Max(v.y, 0.3f) / Gravity, 0.3f, 12f);
		ps.Emit(ep, 1);
	}

	/// <summary>A spray from one point (local frame): count grit and count/8 clods along dir, speed in m/s, plus carry velocity.</summary>
	public void Spray(Vector3 at, Vector3 dir, int count, float speed, float spread, Vector3 carry)
	{
		if (fx != null) { fx.Spray(at, dir, count, speed, spread, carry); return; }
		for (int i = 0; i < count; i++) Emit(grit, at + UnityEngine.Random.insideUnitSphere * 0.25f, carry + Cone(dir, spread) * speed * UnityEngine.Random.Range(0.4f, 1.1f));
		for (int i = 0; i < count / 8; i++) Emit(clods, at + UnityEngine.Random.insideUnitSphere * 0.3f, carry + Cone(dir, spread * 0.7f) * speed * UnityEngine.Random.Range(0.3f, 0.8f));
	}

	/// <summary>A ring burst on the ground around a centre (local frame): eruption, impact.</summary>
	public void Ring(Vector3 center, float radius, int count, float speed, float upBias, Func<float, float, float> ground)
	{
		if (fx != null) { fx.Ring(center, radius, count, speed, upBias, ground); return; }
		for (int i = 0; i < count; i++)
		{
			float a = UnityEngine.Random.value * Mathf.PI * 2f, rr = radius * Mathf.Sqrt(UnityEngine.Random.value);
			Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			Vector3 at = center + o * rr;
			at.y = ground(at.x, at.z) + 0.05f;
			Spray(at, o + Vector3.up * upBias, 1, speed, 0.35f, Vector3.zero);
			if (UnityEngine.Random.value < 0.15f) Spray(at, o + Vector3.up * upBias, 8, speed * 0.6f, 0.4f, Vector3.zero);
		}
	}

	public void SetGravity(float g)
	{
		Gravity = g;
		if (fx != null) { fx.SetGravity(g); return; }
		var a = grit.forceOverLifetime; a.y = -g;
		var b = clods.forceOverLifetime; b.y = -g;
	}

	public void Step(float dt)
	{
		UpdateSun();
		if (fx == null) return;
		fx.Step(dt);
		if (fromWorms != null) fromWorms.Step(dt);
		if (blast != null) blast.Step(dt);
	}

	/// <summary>Our own vacuum blast at the kill (no Volumetric Explosion FX): flash, light, hot ball, sparks, ejecta.</summary>
	public void Blast(Vector3 at, float fireballRadius, float domeRadius, float fireFraction, float wreckage, Func<float, float, float> ground)
	{
		if (fx == null)
		{
			Ring(new Vector3(at.x, ground(at.x, at.z), at.z), 6f, 2600, 9f, 1.2f, ground);   // the old dots: a big ring
			return;
		}
		try
		{
			if (blast == null) { blast = new WormBlastFx(root.transform, dustShader, fx); blast.SetSoft(blastSoft, 1.5f); }
			if (fx.Ground == null) fx.Ground = ground;
			blast.Fire(at, fireballRadius, domeRadius, fireFraction, wreckage);
		}
		catch (Exception ex) { UnityEngine.Debug.Log("[UT Moho] blast fx failed: " + ex.Message); }
	}

	// dust from the worms' own bodies (lit dust only): soil where bodies go through the ground, soil falling off worms that came up
	WormDustFromWorms fromWorms;
	Vector4[] posBuf;
	float lastSnap = -1f;
	/// <summary>Setting mohoWormDustFromWorms: dust made by the worm bodies themselves (lit dust only).</summary>
	public static bool FromWormsOn = true;
	/// <summary>True while the worms' own dust has something to emit (the readback then runs faster).</summary>
	public bool WormsBusy { get { return fromWorms != null && fromWorms.Active; } }
	public bool WormDriven { get { return fx != null && FromWormsOn; } }

	public void FromWorms(Unity.Collections.NativeArray<Vector4> data, uint[] states, int P, Func<float, float, float> ground)
	{
		if (!WormDriven || states == null) return;
		if (fromWorms == null) fromWorms = new WormDustFromWorms(fx) { Strength = DensityScale };
		if (posBuf == null || posBuf.Length != data.Length) posBuf = new Vector4[data.Length];
		data.CopyTo(posBuf);
		fx.Ground = ground;
		float now = Time.time, iv = lastSnap < 0f ? 0.25f : now - lastSnap;
		lastSnap = now;
		fromWorms.Snapshot(posBuf, states, P, ground, iv);
	}

	/// <summary>The frame moved: the last snapshot is in old coordinates.</summary>
	public void ResetWorms() { if (fromWorms != null) fromWorms.Reset(); lastSnap = -1f; }

	public void Destroy() { if (root != null) UnityEngine.Object.Destroy(root); }
}
