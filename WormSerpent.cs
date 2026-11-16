using System;
using UnityEngine;

/// <summary>
/// The travelling swarm as one serpent (setting mohoWormSerpent). Plain Unity, in the swarm's local frame (y up), shared by
/// the game (WormSwarmGpu, driven from MohoWormGpuSkin) and the offline rig (WormSwarm.cs; its run script copies
/// this file there).
/// - The head's path: the head runs along the creature's route with smooth waves side to side and up and down through the
///   ground. Every point of the body follows the same path, so each wave runs down the body by itself.
/// - Randomness: wave lengths and strength drift slowly, now and then a long run under the dirt or a higher breach.
/// - The attack: the head rises in one high arc, then plunges straight down; the body follows it into the ground.
/// - Release: the body lets go (the creature stopped or it is fully underground).
/// The GPU half (WormSerpentGpu) uploads the path and the uniforms the compute shader reads (WormSim.compute, S_SERPENT).
/// Motion references (mood only, no copied design): Dune's sandworms, the driller in Transformers: Dark of the Moon.
/// </summary>
public sealed class WormSerpent
{
	public const int N = 256;                 // stored path samples
	public float spacing = 0.75f;             // m between samples: 191 m of path
	public float length = 100f;               // m, the body (setting mohoWormSerpentLength)
	public float radius = 3.5f;               // m, the body at its thickest (setting mohoWormSerpentRadius)
	public float twistPerMetre = 0.45f;       // rad per m along the body: the worms wind round it
	public float spinRate = 0.6f;             // rad/s the section turns about the body
	public float share = 0.6f;                // share of all worms that form the body
	public float waveScale = 1f;              // scales both wave amplitudes (setting mohoWormSerpentWave)
	public float sideAmp = 7f, upAmp = 7.5f;  // m
	public float sideWave = 95f, upWave = 62f;   // m of travel per wave (mean)
	public float cruiseDepth = 0.5f;          // m: the wave's centre line relative to the ground (negative = mostly under)
	public float plungeRise = 16f;            // m the head climbs before the attack plunge
	public float plungeSpeed = 38f;           // m/s straight down
	public int joinBudget = 400;              // worms per step that may join the body
	public float submergedStart = 70f;        // m of travel the head stays under the dirt after setting off, so the body
	                                          // surfaces away from the digging heap (setting mohoWormSerpentSubmerged; 0 = old)

	public enum Mode { Off, Travel, Plunge, Release }
	public Mode State { get; private set; }
	public float Hold { get; private set; }
	public float Spin { get; private set; }
	public Vector3 Head { get { return head; } }
	/// <summary>True once the attack plunge has the whole body under the ground.</summary>
	public bool Buried { get; private set; }
	/// <summary>True while the body sinks into the ground on the spot (the creature stopped; it comes up as a heap).</summary>
	public bool Sinking { get; private set; }
	/// <summary>m the head has travelled since this body set off.</summary>
	public float Travelled { get { return travelled; } }

	readonly Vector4[] pos = new Vector4[N], nrm = new Vector4[N];
	int count;
	float d0;
	Vector3 head, fwd = Vector3.forward;
	float sidePhase, upPhase, sideMul = 1f, upMul = 1f, amp = 1f, ampGoal = 1f, dip, dipGoal, eventTimer, driftTimer;
	float plungeT, releaseT, travelled;
	bool noRise;
	Vector3 plungeDir, plungeVel;
	readonly System.Random rng;

	public WormSerpent(int seed) { rng = new System.Random(seed); }
	float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

	public int Count { get { return count; } }
	public float D0 { get { return d0; } }
	public Vector4[] Positions { get { return pos; } }
	public Vector4[] Normals { get { return nrm; } }
	public bool Dirty { get; set; }

	// ------------------------------------------------------------------ path
	/// <summary>Start travelling: the whole body lies underground behind the head, along the way the creature is going.</summary>
	public void Begin(Vector3 anchor, Vector3 direction, Func<float, float, float> ground)
	{
		direction.y = 0f;
		fwd = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
		float depth = -radius - 4f;
		head = anchor; head.y = ground(head.x, head.z) + depth;
		count = N;
		for (int i = 0; i < N; i++)
		{
			Vector3 q = anchor - fwd * (i * spacing);
			q.y = ground(q.x, q.z) + depth;
			pos[i] = q;
			nrm[i] = Vector3.up;
		}
		d0 = spacing;
		pos[0] = head;
		sidePhase = R(0f, 6.28f); upPhase = -1.2f;   // the head is on its way up
		amp = ampGoal = 1f; dip = dipGoal = 0f; eventTimer = R(6f, 12f); driftTimer = 0f;
		Buried = false; Sinking = false; noRise = false; travelled = 0f;
		Hold = 1f;
		State = Mode.Travel;
		Dirty = true;
	}

	void MoveHead(Vector3 h)
	{
		head = h;
		// store a sample every `spacing` m of the head's way, each exactly `spacing` from the one before (even spacing)
		Vector3 p1 = pos[1];
		Vector3 d = head - p1;
		int guard = 0;
		while (d.magnitude >= spacing && guard++ < 64)
		{
			Vector3 next = p1 + d.normalized * spacing;
			Vector3 tOld = SafeDir((Vector3)pos[1] - (Vector3)pos[2]), tNew = SafeDir(next - p1);
			Vector3 n = Quaternion.FromToRotation(tOld, tNew) * (Vector3)nrm[1];   // parallel transport: no flips at vertical
			Array.Copy(pos, 1, pos, 2, N - 2);
			Array.Copy(nrm, 1, nrm, 2, N - 2);
			pos[1] = next; nrm[1] = Orthonormal(n, tNew);
			p1 = next;
			d = head - p1;
		}
		d0 = Mathf.Max(d.magnitude, 1e-3f);
		pos[0] = head;
		Vector3 th = SafeDir(head - p1);
		nrm[0] = Orthonormal(Quaternion.FromToRotation(SafeDir(p1 - (Vector3)pos[2]), th) * (Vector3)nrm[1], th);
		Dirty = true;
	}

	static Vector3 SafeDir(Vector3 v) { float l = v.magnitude; return l > 1e-5f ? v / l : Vector3.forward; }
	static Vector3 Orthonormal(Vector3 n, Vector3 t)
	{
		n -= Vector3.Dot(n, t) * t;
		if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
		return n.normalized;
	}

	/// <summary>Point of the path `s` m behind the head.</summary>
	public Vector3 PathAt(float s)
	{
		float f = s < d0 ? s / d0 : 1f + (s - d0) / spacing;
		f = Mathf.Clamp(f, 0f, count - 1.001f);
		int i = (int)f;
		return Vector3.Lerp(pos[i], pos[i + 1], f - i);
	}

	// ------------------------------------------------------------------ motion
	/// <summary>One frame. anchor: the creature's ground point; velocity: its velocity (local frame).</summary>
	public void Step(float dt, Vector3 anchor, Vector3 velocity, Func<float, float, float> ground)
	{
		if (State == Mode.Off || dt <= 0f) return;
		Spin += spinRate * dt;
		if (State == Mode.Travel) Travel(dt, anchor, velocity, ground);
		else if (State == Mode.Plunge) Plunge(dt, ground);
		else if (State == Mode.Release)
		{
			releaseT += dt;
			Hold = Mathf.Clamp01(1f - releaseT / 1.2f);
			if (releaseT > 1.4f) { State = Mode.Off; Hold = 0f; }
		}
	}

	void Travel(float dt, Vector3 anchor, Vector3 velocity, Func<float, float, float> ground)
	{
		velocity.y = 0f;
		float speed = velocity.magnitude;
		if (speed > 0.5f) fwd = Vector3.Slerp(fwd, velocity / speed, 1f - Mathf.Exp(-3f * dt));
		Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);
		float dist = speed * dt;
		// slow drift of the wave lengths and strength
		driftTimer -= dt;
		if (driftTimer <= 0f) { driftTimer = R(2.5f, 5f); sideMul = R(0.7f, 1.35f); upMul = R(0.7f, 1.4f); ampGoal = R(0.55f, 1.2f); }
		// rare events: a long run under the dirt, or a higher breach
		eventTimer -= dt;
		if (eventTimer <= 0f)
		{
			eventTimer = R(7f, 15f);
			float roll = (float)rng.NextDouble();
			if (roll < 0.25f) { dipGoal = -R(4f, 6f); ampGoal = 0.5f; }
			else if (roll < 0.55f) { dipGoal = R(1f, 2.5f); ampGoal = 1.35f; }
			else dipGoal = 0f;
		}
		if (eventTimer < 3f && dipGoal < 0f) dipGoal = 0f;   // dives end after a few seconds
		dip = Mathf.MoveTowards(dip, dipGoal, 2.5f * dt);
		amp = Mathf.MoveTowards(amp, ampGoal, 0.25f * dt);
		sidePhase += 6.2831853f * dist / (sideWave * sideMul);
		upPhase += 6.2831853f * dist / (upWave * upMul);
		float sideOff = sideAmp * waveScale * amp * (Mathf.Sin(sidePhase) + 0.3f * Mathf.Sin(2.7f * sidePhase + 1f));
		float h = cruiseDepth + dip + upAmp * waveScale * amp * (Mathf.Sin(upPhase) + 0.35f * Mathf.Sin(2.1f * upPhase + 0.7f));
		// the first stretch runs under the dirt: the body breaks the surface away from the heap it came out of
		travelled += dist;
		if (submergedStart > 0f)
		{
			float w = Mathf.Clamp01((travelled - submergedStart * 0.75f) / (submergedStart * 0.25f));
			h = Mathf.Lerp(-radius - 3f, h, w * w * (3f - 2f * w));
		}
		Vector3 target = anchor + side * sideOff;
		target.y = ground(target.x, target.z) + h;
		// the head goes where the waves put it, but never faster than the creature plus a margin (no snapping)
		float maxStep = (speed + 15f) * dt;
		MoveHead(Vector3.MoveTowards(head, target, maxStep));
	}

	/// <summary>The attack: rise in one high arc, then straight down; the body follows the head into the ground.</summary>
	public void StartPlunge(Vector3 velocity)
	{
		if (State != Mode.Travel) return;
		State = Mode.Plunge;
		plungeT = 0f; noRise = false; Sinking = false;
		velocity.y = 0f;
		float v = Mathf.Max(velocity.magnitude, 20f);
		plungeDir = fwd;
		plungeVel = fwd * v;
	}

	/// <summary>The creature stopped: the head turns down on the spot and the whole body follows it under the dirt (no
	/// rising arc). Once it is buried the body lets go underground and the worms come up head first as a heap.</summary>
	public void StartSink(Vector3 velocity)
	{
		if (State != Mode.Travel) return;
		State = Mode.Plunge;
		plungeT = 0f; noRise = true; Sinking = true;
		velocity.y = 0f;
		plungeDir = fwd;
		plungeVel = fwd * Mathf.Max(velocity.magnitude, 8f);
	}

	void Plunge(float dt, Func<float, float, float> ground)
	{
		plungeT += dt;
		float g = ground(head.x, head.z);
		Vector3 want;
		if (!noRise && plungeT < 0.8f && head.y < g + plungeRise)
			want = (plungeDir * 0.8f + Vector3.up * 0.9f).normalized * Mathf.Max(plungeVel.magnitude, 30f);   // up in an arc
		else
			want = Vector3.down * plungeSpeed + plungeDir * 3f;                                                 // straight down
		plungeVel = Vector3.Slerp(plungeVel, want, 1f - Mathf.Exp(-5f * dt));
		MoveHead(head + plungeVel * dt);
		// buried once the tail's place on the path is well under the ground
		Vector3 tail = PathAt(length);
		if (tail.y < ground(tail.x, tail.z) - radius - 4f && head.y < g - radius - 4f) { Buried = true; Release(); }   // deep enough that worms lagging the guide are under too
		if (plungeT > 8f) { Buried = true; Release(); }
	}

	public void Release()
	{
		if (State == Mode.Off || State == Mode.Release) return;
		State = Mode.Release;
		Sinking = false;
		releaseT = 0f;
		if (Buried) { Hold = 0f; releaseT = 1.3f; }   // fully underground: let go at once
	}

	/// <summary>Where the body cuts through the ground: points and the body's direction there (toward the head).</summary>
	public int Crossings(Func<float, float, float> ground, Vector3[] at, Vector3[] dir)
	{
		int k = 0;
		float step = 2f;
		Vector3 prev = PathAt(0f);
		float prevH = prev.y - ground(prev.x, prev.z);
		for (float s = step; s <= length && k < at.Length; s += step)
		{
			Vector3 q = PathAt(s);
			float h = q.y - ground(q.x, q.z);
			if ((h > 0f) != (prevH > 0f))
			{
				float u = prevH / (prevH - h);
				at[k] = Vector3.Lerp(prev, q, u);
				at[k].y = ground(at[k].x, at[k].z);
				dir[k] = SafeDir(prev - q);
				k++;
			}
			prev = q; prevH = h;
		}
		return k;
	}

	/// <summary>The frame moved: old local -> new local.</summary>
	public void Rebase(Matrix4x4 m)
	{
		for (int i = 0; i < count; i++)
		{
			pos[i] = m.MultiplyPoint3x4(pos[i]);
			nrm[i] = m.MultiplyVector(nrm[i]);
		}
		head = m.MultiplyPoint3x4(head);
		fwd = m.MultiplyVector(fwd);
		plungeDir = m.MultiplyVector(plungeDir); plungeVel = m.MultiplyVector(plungeVel);
		Dirty = true;
	}
}

/// <summary>GPU side of the serpent: two read-only path buffers and the uniforms of WormSim.compute.</summary>
public sealed class WormSerpentGpu
{
	ComputeBuffer pathP, pathN;

	public void Bind(ComputeShader sim, int[] kernels)
	{
		if (pathP == null)
		{
			pathP = new ComputeBuffer(WormSerpent.N, 16); pathN = new ComputeBuffer(WormSerpent.N, 16);
			var zero = new Vector4[WormSerpent.N];
			for (int i = 0; i < zero.Length; i++) zero[i] = new Vector4(0f, -1000f, -i, 0f);
			pathP.SetData(zero); pathN.SetData(zero);
		}
		for (int i = 0; i < kernels.Length; i++) { sim.SetBuffer(kernels[i], "_PathP", pathP); sim.SetBuffer(kernels[i], "_PathN", pathN); }
	}

	/// <summary>Once per simulation step. s == null or Off: the serpent is off (members leave it).</summary>
	public void Upload(ComputeShader sim, WormSerpent s)
	{
		bool on = s != null && (s.State == WormSerpent.Mode.Travel || s.State == WormSerpent.Mode.Plunge || s.State == WormSerpent.Mode.Release);
		if (on && s.Dirty && pathP != null) { pathP.SetData(s.Positions); pathN.SetData(s.Normals); s.Dirty = false; }
		sim.SetInt("_SerpentOn", on ? 1 : 0);
		sim.SetInt("_SerpentRelease", on && s.State == WormSerpent.Mode.Release && s.Hold <= 0.02f ? (s.Buried ? 2 : 1) : 0);
		sim.SetInt("_SerpentBudget", on && s.State == WormSerpent.Mode.Travel ? s.joinBudget : 0);
		sim.SetInt("_PathCount", on ? Mathf.Max(s.Count, 2) : 2);
		sim.SetFloat("_PathD0", on ? s.D0 : 1f);
		sim.SetFloat("_PathDs", on ? s.spacing : 1f);
		sim.SetFloat("_SerpentLen", on ? s.length : 1f);
		sim.SetFloat("_SerpentR", on ? s.radius : 1f);
		sim.SetFloat("_SerpentTwist", on ? s.twistPerMetre : 0f);
		sim.SetFloat("_SerpentSpin", on ? s.Spin : 0f);
		sim.SetFloat("_SerpentHold", on ? s.Hold : 0f);
		sim.SetFloat("_SerpentShare", on ? s.share : 0f);
	}

	public void Release()
	{
		if (pathP != null) pathP.Release();
		if (pathN != null) pathN.Release();
		pathP = pathN = null;
	}
}
