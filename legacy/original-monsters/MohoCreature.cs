using System;
using UnityEngine;

public enum MohoState { Idle, Listening, Moving }

// Moho/Eve creature, part 2 of 3: procedural gait, IK, head and body motion. No clips, no geometry.
// Inputs (public): Position, Heading, Speed, LookTarget, State, Up. Output: Pose (named joints, see MohoRig.cs) pushed to Skin,
// plus the FootFall event. Call Step(dt) once per frame (the game adapter does; the offline rig does with a fixed dt).
// Gait: a foot lifts when it trails the stride limit behind its home spot; the two diagonal pairs (LF+RH, RF+LH) alternate, a pair may
// only lift while the other pair stands. A planted foot never moves: its world position is stored and the legs are solved onto it.
public sealed class MohoCreature : MonoBehaviour
{
	// ------------------------------------------------------------ inputs
	public Vector3 Position;                 // ground point below the body centre (world)
	public Vector3 Heading = Vector3.forward;// where the body should point / travel (any length, projected onto the ground plane)
	public float Speed;                      // travel speed in m/s, along the body heading
	public Vector3? LookTarget;              // world point the head should track, or null
	public MohoState State;                  // Idle, Listening (still, head tilted) or Moving
	public Vector3 Up = Vector3.up;          // away from the planet
	public Func<Vector3, Vector3> GroundSampler;   // world point -> ground point below/above it; null = flat plane through Position
	// True: Heading is the monster's own heading (HeadingMotion), already turn-rate limited and the only way it moves, so the
	// body takes it as it is instead of smoothing towards it (the old smoothing lagged and made the body face other than it travelled).
	// The yaw rate is then measured from the heading's change, and the body leans into the arc.
	public bool HeadingExact;
	public bool TurnLean = true;             // lean, spine/shoulder lead, outer strides while turning (mohoTurnLean)
	public float LeanMaxDeg = 14f, LeadSeconds = 0.55f, LeadMaxDeg = 22f;

	// ------------------------------------------------------------ outputs
	public readonly MohoPose Pose = new MohoPose();
	public IMohoSkin Skin;
	// footIndex (0 LF, 1 RF, 2 LH, 3 RH), world position of the foot, strength 0..1 (from stride and speed)
	public event Action<int, Vector3, float> FootFall;
	public int FootFallCount;
	public Vector3 HeadPosition { get { return Pose.Position[(int)MohoJoint.Head]; } }
	public Vector3 HeadForward { get { return Pose.Rotation[(int)MohoJoint.Head] * Vector3.forward; } }
	public Vector3 BodyForward { get { return fb; } }
	public bool IsPlanted(int foot) { return feet[foot].mode == 0; }
	public Vector3 FootContact(int foot) { return feet[foot].pos; }
	public float CurrentSpeed { get { return spd; } }
	public float YawRateDeg { get { return yawRate; } }
	public float LeanDeg { get { return leanDeg; } }
	public float ChestLeadDeg { get { return leadDeg; } }
	public float MaxStretchRatio;            // diagnostic: largest shoulder-to-wrist distance / limb length seen on a planted foot

	// ------------------------------------------------------------ tuning (metres, degrees)
	public float StandHeight = MohoProportions.StandHeight;
	public float MaxTurnRate = 24f;          // deg/s
	public float StrideMin = 5f, StrideMax = 28f;

	// ------------------------------------------------------------ proportions: two profiles
	// The default profile is the one the procedural skin is built for. UseModelProfile() switches to the hand-made skinned model
	// (MohoModelProportions: arm 27.9 + 31, leg 31.8 + 32.4, hind toes pointing backward, a stump instead of a head).
	float homeFront = 34f, homeHind = -34f, homeLatFront = 17f, homeLatHind = 16f;
	float galFront = 36f, galHind = -31f;
	float toeOutFront = 18f, toeOutHind = 8f;
	float torsoHalf = MohoProportions.TorsoLength * 0.5f;
	float restPitch;
	float armUpper = MohoProportions.ArmUpper, armLower = MohoProportions.ArmLower, legUpper = MohoProportions.LegUpper, legLower = MohoProportions.LegLower;
	float handPalm = MohoProportions.HandPalm, footPalm = MohoProportions.FootPalm, wristLift = MohoProportions.WristLift, ankleLift = MohoProportions.AnkleLift, knuckleLift = MohoProportions.KnuckleLift;
	Vector3 ppOff = new Vector3(0f, -0.8f, -MohoProportions.TorsoLength * 0.5f), pcOff = new Vector3(0f, 1.0f, MohoProportions.TorsoLength * 0.5f), pmOff = new Vector3(0f, 0.5f, 0f), tailOff = new Vector3(0f, -0.9f, -4.5f), nbOff = new Vector3(0f, 0.4f, 6f);
	Vector3 shoulderOff = new Vector3(MohoProportions.ShoulderHalfSpan, 0.5f, 0f), hipOff = new Vector3(MohoProportions.HipHalfSpan, -0.2f, 0f);
	Vector3 neck1Off, neck2Off;
	bool modelNeck, reverseHind;
	Vector3 armPoleW = new Vector3(0.5f, 1f, -0.2f), legPoleW = new Vector3(0.5f, 1f, 0.3f);   // up, out, forward weights of the elbow and knee bend direction
	float headMotion = 1f;
	float restFront, restHind, restHeight;
	public bool ModelProfile { get { return modelNeck; } }
	float TorsoHalf { get { return torsoHalf; } }
	float HandPalm { get { return handPalm; } }
	float FootPalm { get { return footPalm; } }

	// The hand-made model (see MohoModelProportions). Call before the first Step.
	public void UseModelProfile()
	{
		modelNeck = true; reverseHind = true;
		StandHeight = MohoModelProportions.StandHeight;
		// the arms are nearly straight in the rest stance (97% of their length), so the walking stance and the strides are shorter than the rest stance,
		// or a hand touching down ahead of the body would be beyond the reach of the arm
		homeFront = 48f; homeHind = -21f; homeLatFront = 16.5f; homeLatHind = 13f;
		galFront = 44f; galHind = -17f;
		StrideMax = 20f;
		toeOutFront = 0f; toeOutHind = 0f;
		torsoHalf = MohoModelProportions.TorsoLength * 0.5f;
		restPitch = MohoModelProportions.RestPitchDeg;
		armUpper = MohoModelProportions.ArmUpper; armLower = MohoModelProportions.ArmLower; legUpper = MohoModelProportions.LegUpper; legLower = MohoModelProportions.LegLower;
		handPalm = MohoModelProportions.HandPalm; footPalm = MohoModelProportions.FootPalm; wristLift = MohoModelProportions.WristLift; ankleLift = MohoModelProportions.AnkleLift; knuckleLift = MohoModelProportions.KnuckleLift;
		ppOff = new Vector3(0f, 0f, -torsoHalf); pcOff = new Vector3(0f, 0f, torsoHalf);
		pmOff = new Vector3(0f, -0.42f, -2.43f); tailOff = Vector3.zero;
		nbOff = new Vector3(0f, -0.33f, 12.8f);
		neck1Off = new Vector3(0f, -2.87f, 3.2f); neck2Off = new Vector3(0f, -3.1f, -0.78f);
		shoulderOff = new Vector3(MohoModelProportions.ShoulderHalfSpan, 1.34f, 10.65f);
		hipOff = new Vector3(MohoModelProportions.HipHalfSpan, -0.39f, -0.1f);
		armPoleW = new Vector3(-0.15f, 1f, -0.9f); legPoleW = new Vector3(-0.1f, 0.28f, 1f);
		headMotion = 0.2f;
		restFront = MohoModelProportions.RestFront; restHind = MohoModelProportions.RestHind; restHeight = MohoModelProportions.RestBodyHeight;
	}

	// The pose of the model's own rest stance (feet where the model stands, body level in the tilted rest frame): used once to capture the bind.
	// Position, Heading and Up must be set; the ground is the plane through Position unless GroundSampler is set.
	public MohoPose BuildRestPose()
	{
		float f0 = homeFront, h0 = homeHind, s0 = StandHeight;
		homeFront = restFront; homeHind = restHind; StandHeight = restHeight;
		Vector3 up = Up.sqrMagnitude > 0.5f ? Up.normalized : Vector3.up;
		Init(up);
		Vector3 rb = Vector3.Cross(up, fb);
		bodyH = restHeight;
		pitchDeg = 0f; rollDeg = 0f; spineArch = 0f;
		BuildPose(bodyH, up, fb, rb, 0f, 0f, 0f, 10f, false);
		homeFront = f0; homeHind = h0; StandHeight = s0;
		MohoPose copy = new MohoPose();
		for (int i = 0; i < MohoRigInfo.JointCount; i++) { copy.Position[i] = Pose.Position[i]; copy.Rotation[i] = Pose.Rotation[i]; }
		for (int i = 0; i < MohoRigInfo.LimbCount; i++) { copy.Planted[i] = Pose.Planted[i]; copy.Curl[i] = Pose.Curl[i]; copy.Contact[i] = Pose.Contact[i]; copy.HandForward[i] = Pose.HandForward[i]; copy.HandNormal[i] = Pose.HandNormal[i]; }
		copy.Origin = Pose.Origin; copy.Frame = Pose.Frame;
		init = false;
		return copy;
	}

	sealed class Foot
	{
		public int index, pair;
		public bool front;
		public float side;          // -1 left, +1 right
		public int mode;            // 0 planted, 1 pending (lag before lift), 2 swinging
		public Vector3 pos, liftPos, targetPos;
		public Vector3 dir, liftDir, targetDir;
		public Vector3 normal, liftNormal;
		public float t, dur, lag, planted, curl, el, lastRel;
	}

	readonly Foot[] feet = new Foot[4];
	bool init;
	Vector3 fb = Vector3.forward, lastPosition;
	float leanDeg, leadDeg;
	float turnVel, yawRate, spd, bodyH, swingSig, swayS, pitchDeg, rollDeg, time;
	Vector3 hfw = Vector3.forward;
	float headVel, headPitch, headPitchVel, headRoll, headRollVel, listenSign = 1f;
	MohoState lastState;

	// ------------------------------------------------------------ sprint additions (gallop, leap)
	// Inputs for a leap: the monster logic sets Airborne while it flies the body, AirLift (height of the body above the ground point
	// below it), AirPhase (0 take-off .. 1 touch-down) and AirImpact (vertical speed at touch-down, m/s).
	public bool Airborne;
	public float AirLift, AirPhase, AirImpact;
	public float GallopFrom = 36f;           // m/s at which the walk turns into a gallop
	// position, strength 0..1 (from the vertical speed at touch-down)
	public event Action<Vector3, float> Landed;
	public bool Gallop { get { return gallop; } }
	public float GallopPhase { get { return gPhase; } }
	public float GallopCycleSeconds { get { return gCycle; } }
	public float BodyLift { get { return bodyLiftDiag; } }
	static readonly float[] GallopTouchdown = { 0.40f, 0.52f, 0.00f, 0.10f };   // cycle phase at which LF, RF, LH, RH land
	bool gallop, wasAir;
	float gPhase, gCycle = 1f, landDip, landHold, spineArch, bodyLiftDiag;

	public MohoCreature()
	{
		for (int i = 0; i < 4; i++)
		{
			feet[i] = new Foot { index = i, front = i < 2, side = (i % 2 == 0) ? -1f : 1f, pair = (i == 0 || i == 3) ? 0 : 1 };
		}
	}

	// A floating-origin shift moves the world under the creature: planted feet must move with it.
	public void ApplyWorldShift(Vector3 delta)
	{
		Position += delta;
		lastPosition += delta;
		for (int i = 0; i < 4; i++)
		{
			feet[i].pos += delta; feet[i].liftPos += delta; feet[i].targetPos += delta;
		}
	}

	public void ResetFeet() { init = false; }

	static Vector3 Flat(Vector3 v, Vector3 up) { return v - up * Vector3.Dot(v, up); }
	static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

	Vector3 Ground(Vector3 p, Vector3 up)
	{
		if (GroundSampler != null) return GroundSampler(p);
		return p - up * Vector3.Dot(p - Position, up);
	}

	Vector3 GroundNormal(Vector3 p, Vector3 f, Vector3 r, Vector3 up)
	{
		// the slope is read over the length of the toes, so that the sole follows the ground instead of one point of it
		Vector3 a = Ground(p + f * 9f, up) - p, b = Ground(p + r * 3.5f, up) - p;
		Vector3 n = Vector3.Cross(a, b);
		if (n.sqrMagnitude < 1e-6f) return up;
		n.Normalize();
		return Vector3.Dot(n, up) < 0f ? -n : n;
	}

	Vector3 Home(int i, Vector3 pos, Vector3 f, Vector3 r)
	{
		Foot ft = feet[i];
		float lat = ft.front ? homeLatFront : homeLatHind;
		return pos + f * (ft.front ? homeFront : homeHind) + r * (ft.side * lat);
	}

	void Init(Vector3 up)
	{
		init = true;
		Vector3 h = Flat(Heading, up);
		fb = h.sqrMagnitude > 1e-4f ? h.normalized : Flat(Vector3.forward, up).normalized;
		if (fb.sqrMagnitude < 0.5f) fb = Flat(Vector3.right, up).normalized;
		hfw = fb;
		lastPosition = Position;
		Vector3 r = Vector3.Cross(up, fb);
		float sum = 0f;
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			f.mode = 0; f.t = 0; f.planted = 1f; f.curl = 0.1f;
			f.pos = Ground(Home(i, Position, fb, r), up);
			f.normal = GroundNormal(f.pos, fb, r, up);
			f.dir = Quaternion.AngleAxis(f.side * (f.front ? toeOutFront : toeOutHind), up) * fb;
			sum += Vector3.Dot(f.pos - Position, up);
		}
		bodyH = sum * 0.25f + StandHeight;
		spd = 0f; turnVel = 0f; swingSig = 0f; swayS = 0f; pitchDeg = 0f; rollDeg = 0f;
	}

	public void Step(float dt)
	{
		if (dt <= 0f) return;
		dt = Mathf.Min(dt, 0.1f);
		Vector3 up = Up.sqrMagnitude > 0.5f ? Up.normalized : Vector3.up;
		if (!init) Init(up);
		if ((Position - lastPosition).magnitude > 250f) Init(up);
		lastPosition = Position;
		time += dt;
		if (State == MohoState.Listening && lastState != MohoState.Listening) listenSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
		lastState = State;

		// ---- heading and speed
		Vector3 want = Flat(Heading, up);
		if (want.sqrMagnitude < 1e-4f || State != MohoState.Moving && Speed < 0.05f && State == MohoState.Listening) want = fb;
		if (HeadingExact && Flat(Heading, up).sqrMagnitude > 1e-4f)
		{
			Vector3 nf = Flat(Heading, up).normalized;
			float dAng = Vector3.SignedAngle(fb, nf, up);
			fb = nf;
			turnVel = Mathf.Lerp(turnVel, dAng / dt, 1f - Mathf.Exp(-14f * dt));
		}
		else
		{
		float ang = Vector3.SignedAngle(fb, want.normalized, up);
		float tgtVel = Mathf.Clamp(ang * 1.1f, -MaxTurnRate, MaxTurnRate);
		turnVel = Mathf.MoveTowards(turnVel, tgtVel, 40f * dt);
		fb = Quaternion.AngleAxis(turnVel * dt, up) * fb;
		fb = Flat(fb, up).normalized;
		}
		yawRate = turnVel;
		Vector3 rb = Vector3.Cross(up, fb);
		float speedIn = State == MohoState.Moving ? Mathf.Max(0f, Speed) : 0f;
		spd = Mathf.MoveTowards(spd, speedIn, 40f * dt);
		float Ts = Mathf.Lerp(0.95f, 0.55f, Mathf.Clamp01(spd / 45f));
		float S = Mathf.Clamp(spd * Ts * 1.35f, StrideMin, StrideMax);
		float rMax = StrideMax * 0.5f + 7f;

		// ---- air, landing, gait selection
		bool air = Airborne;
		if (air && !wasAir) BeginAir();
		if (!air && wasAir) EndAir(up, fb, rb);
		wasAir = air;
		if (landHold > 0f) landHold -= dt;
		landDip = Mathf.MoveTowards(landDip, 0f, (1.5f + landDip * 0.9f) * dt);
		if (!air)
		{
			if (!gallop && spd > GallopFrom && speedIn > GallopFrom && landHold <= 0f) BeginGallop();
			else if (gallop && (spd < GallopFrom - 9f || speedIn < GallopFrom - 9f)) gallop = false;
		}

		// ---- feet
		float[] sw = new float[2];
		if (air) UpdateAirFeet(dt, up, fb, rb);
		else if (gallop) UpdateGallop(dt, up, fb, rb);
		else if (landHold > 0f) { for (int i = 0; i < 4; i++) if (feet[i].mode == 0) feet[i].planted += dt; }
		else
		{
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			if (f.mode == 0) f.planted += dt;
			else if (f.mode == 1)
			{
				f.lag -= dt;
				if (f.lag <= 0f) BeginSwing(f, Ts, up);
			}
			if (f.mode == 2) UpdateSwing(f, dt, S, up, fb, rb, Ts);
			if (f.mode == 2) sw[f.pair] = Mathf.Max(sw[f.pair], Mathf.Sin(Mathf.PI * f.t));
		}
		bool[] busy = new bool[2];
		float[] need = new float[2];
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			if (f.mode != 0) { busy[f.pair] = true; continue; }
			Vector3 home = Home(i, Position, fb, rb);
			Vector3 off = f.pos - home;
			float rel = Vector3.Dot(off, fb);
			float Sf = S * StrideFactor(f);
			float n = Mathf.Max(-rel - Sf * 0.5f, Mathf.Abs(rel) - Sf * 0.5f - 2.5f);
			n = Mathf.Max(n, Flat(off, up).magnitude - rMax);
			if (f.planted < 0.12f) n = -1f;
			need[f.pair] = Mathf.Max(need[f.pair], n);
			if (Flat(off, up).magnitude > rMax * 1.5f) need[f.pair] = Mathf.Max(need[f.pair], 100f);
		}
		for (int k = 0; k < 2; k++)
		{
			int p = need[0] >= need[1] ? k : 1 - k;
			if (need[p] > 0f && !busy[p] && (!busy[1 - p] || need[p] >= 100f))
			{
				StartPair(p, Ts, up);
				busy[p] = true;
			}
		}

		}

		// ---- body pose
		float hs0 = 0f, hs1 = 0f, hAvg = 0f;
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			Vector3 support = f.mode == 2 ? f.targetPos : f.pos;
			float h = Vector3.Dot(support - Position, up);
			hAvg += h * 0.25f;
			if (f.front) hs0 += h * 0.5f; else hs1 += h * 0.5f;
		}
		float breathe = Mathf.Sin(time * 0.55f) * 0.18f;
		float crouch = State == MohoState.Listening ? -1.2f : 0f;
		float bobT = -0.4f * (sw[0] + sw[1]) * Mathf.Clamp01(spd / 25f);
		float hTarget = hAvg + StandHeight + breathe + crouch + bobT - landDip;
		float bodyRate = 5f, pitchRate = 3f;
		float gal = gallop ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((spd - GallopFrom) / 80f)) : 0f;
		float cyc = gPhase * 2f * Mathf.PI;
		float pitchT = Mathf.Clamp(Mathf.Atan2(hs0 - hs1, 52f) * Mathf.Rad2Deg * 0.8f, -25f, 25f);
		if (air)
		{
			hTarget = StandHeight * 0.9f + AirLift - landDip; bodyRate = 30f;
			pitchT = Mathf.Lerp(24f, -22f, Smooth(AirPhase)); pitchRate = 7f;
		}
		else if (gallop)
		{
			// two bounces per cycle: low over the planted hind and fore feet, high in the two flights; the body pitches nose-up over the hind push, nose-down over the fore landing
			hTarget += gal * (5.5f * (0.5f - 0.5f * Mathf.Cos(2f * (cyc - 0.34f * Mathf.PI))) - 2.2f);
			bodyRate = 14f;
			pitchT = gal * 11f * Mathf.Cos(cyc - 0.4f * Mathf.PI) + pitchT * 0.3f; pitchRate = 14f;
		}
		bodyH = Mathf.Lerp(bodyH, hTarget, 1f - Mathf.Exp(-bodyRate * dt));
		bodyLiftDiag = bodyH - (hAvg + StandHeight);
		float sig = sw[1] - sw[0];
		swingSig = Mathf.Lerp(swingSig, sig, 1f - Mathf.Exp(-8f * dt));
		pitchDeg = Mathf.Lerp(pitchDeg, pitchT, 1f - Mathf.Exp(-pitchRate * dt));
		float gaitAmt = Mathf.Clamp01(spd / 12f);
		rollDeg = swingSig * 2.2f * gaitAmt;
		float twist = swingSig * 7f * gaitAmt;
		float leanT = 0f, leadT = 0f;
		if (TurnLean && !air)
		{
			// into the arc: the sideways acceleration is speed x yaw rate; the body leans towards the centre (positive roll leans left, positive yaw turns right),
			// the chest and shoulders lead the turn by about half a second of yaw and the pelvis follows
			float aC = spd * Mathf.Abs(yawRate) * Mathf.Deg2Rad;
			leanT = -Mathf.Sign(yawRate) * LeanMaxDeg * Mathf.Clamp01(aC / 8f);
			leadT = Mathf.Clamp(yawRate * LeadSeconds * Mathf.Clamp01(spd / 4f), -LeadMaxDeg, LeadMaxDeg);
		}
		leanDeg = Mathf.Lerp(leanDeg, leanT, 1f - Mathf.Exp(-4f * dt));
		leadDeg = Mathf.Lerp(leadDeg, leadT, 1f - Mathf.Exp(-5f * dt));
		rollDeg += leanDeg;
		twist += leadDeg;
		float sway = -swingSig * 1.9f * gaitAmt;
		float bend = swingSig * 0.9f * gaitAmt;
		float archT = 0f;
		if (gallop)
		{
			twist += gal * 9f * Mathf.Sin(cyc);
			rollDeg += gal * 3f * Mathf.Sin(cyc + 1f);
			archT = gal * 4f * Mathf.Cos(cyc - 1.7f * Mathf.PI);
		}
		spineArch = Mathf.Lerp(spineArch, archT, 1f - Mathf.Exp(-10f * dt));

		// ---- head state (springs)
		UpdateHead(dt, up, rb);

		// ---- joints with reach constraint (lower the body when a planted limb would be overstretched)
		float shift = BuildPose(bodyH, up, fb, rb, sway, bend, twist, S, true);
		if (shift > 0f)
		{
			bodyH -= shift;
			BuildPose(bodyH, up, fb, rb, sway, bend, twist, S, false);
		}

		Pose.Origin = Position;
		Pose.Frame = Quaternion.LookRotation(fb, up);
		Pose.Time = time;
		if (Skin != null) Skin.Apply(Pose);
	}

	// ---------------------------------------------------------------- gallop

	Vector3 HomeG(int i, Vector3 pos, Vector3 f, Vector3 r)
	{
		Foot ft = feet[i];
		float lat = ft.front ? homeLatFront : homeLatHind;
		return pos + f * (ft.front ? galFront : galHind) + r * (ft.side * lat);
	}

	static float Frac(float v) { return v - Mathf.Floor(v); }

	void BeginGallop()
	{
		gallop = true;
		gPhase = 0.76f;      // the start of the long flight: all four feet come off together and the first bound begins
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			if (f.mode == 1) f.mode = 0;
			if (f.mode == 2) f.el = f.t * f.dur;
			f.lastRel = Frac(gPhase - GallopTouchdown[i]);
		}
	}

	void UpdateGallop(float dt, Vector3 up, Vector3 fwd, Vector3 rgt)
	{
		float L = Mathf.Lerp(46f, 86f, Mathf.Clamp01((spd - GallopFrom) / 84f));      // metres per cycle
		gCycle = Mathf.Max(0.3f, L / Mathf.Max(spd, 1f));
		gPhase = Frac(gPhase + dt / gCycle);
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			float st = f.front ? 0.22f : 0.24f;
			float rel = Frac(gPhase - GallopTouchdown[i]);
			bool wrapped = rel < f.lastRel - 0.5f;      // went through the touch-down phase
			f.lastRel = rel;
			if (f.mode == 3 || f.mode == 1) f.mode = 0;
			if (f.mode == 2)
			{
				f.el += dt;
				float remT = (1f - rel) * gCycle;
				if (wrapped) { LandGallopFoot(f, up, fwd, rgt, L, st); continue; }
				float rem = Mathf.Max(remT, 0.02f);
				Quaternion q = Quaternion.AngleAxis(yawRate * rem, up);
				Vector3 fF = q * fwd, rF = q * rgt;
				Vector3 pPos = Position + fwd * (spd * rem);
				float reach = (f.front ? 7f : 2f) + st * L * 0.5f;
				f.targetPos = Ground(HomeG(f.index, pPos, fF, rF) + fF * reach, up);
				f.targetDir = Quaternion.AngleAxis(f.side * (f.front ? toeOutFront : toeOutHind), up) * fF;
				f.dur = f.el + rem;
				f.t = Mathf.Clamp01(f.el / f.dur);
				float e = Mathf.Lerp(f.t, Smooth(f.t), 0.5f);
				float arc = (6f + 0.16f * L) * Mathf.Sin(Mathf.PI * Mathf.Pow(f.t, 0.85f));
				f.pos = Vector3.Lerp(f.liftPos, f.targetPos, e) + up * arc;
				f.dir = Vector3.Slerp(f.liftDir, f.targetDir, e);
				f.normal = Vector3.Slerp(f.liftNormal, up, Mathf.Sin(Mathf.PI * f.t));
				f.curl = Mathf.Lerp(0.1f, 0.95f, Mathf.Sin(Mathf.PI * Mathf.Pow(f.t, 0.7f)));
			}
			else
			{
				f.planted += dt;
				if (rel >= st && !wrapped)
				{
					f.mode = 2; f.t = 0f; f.el = 0f; f.dur = 0.2f;
					f.liftPos = f.pos; f.liftDir = f.dir; f.liftNormal = f.normal;
					f.targetPos = f.pos; f.targetDir = f.dir;
				}
			}
		}
	}

	void LandGallopFoot(Foot f, Vector3 up, Vector3 fwd, Vector3 rgt, float L, float st)
	{
		f.pos = f.targetPos;
		f.dir = f.targetDir;
		f.normal = GroundNormal(f.pos, fwd, rgt, up);
		f.mode = 0; f.planted = 0f; f.curl = 0.1f; f.t = 1f;
		FootFallCount++;
		if (FootFall != null) FootFall(f.index, f.pos, Mathf.Clamp01(0.4f + spd / 160f));
	}

	// ---------------------------------------------------------------- leap

	void BeginAir()
	{
		gallop = false;
		for (int i = 0; i < 4; i++) feet[i].mode = 3;
	}

	// Feet trail behind and below on the way up, and reach forward and down on the way back; they never go below the ground.
	void UpdateAirFeet(float dt, Vector3 up, Vector3 fwd, Vector3 rgt)
	{
		float r = Smooth((AirPhase - 0.38f) / 0.4f);
		Vector3 C = Position + up * (StandHeight * 0.9f + AirLift);
		float k = 1f - Mathf.Exp(-9f * dt);
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			Vector3 trail = f.front ? new Vector3(-12f, -24f, 15f) : new Vector3(-50f, -30f, 9f);
			Vector3 reach = f.front ? new Vector3(44f, -22f, 14f) : new Vector3(16f, -36f, 8f);
			Vector3 l = Vector3.Lerp(trail, reach, r);
			float flutter = 1.5f * Mathf.Sin(time * 5f + i * 1.7f) * (1f - r);
			Vector3 target = C + fwd * l.x + up * (l.y + flutter) + rgt * (l.z * f.side);
			// the toes are long: keep the foot well above the ground, and bring it down only in the last tenth of the flight
			float minH = Mathf.Lerp(12f, 2.5f, Smooth((AirPhase - 0.9f) / 0.1f));
			Vector3 g = Ground(target, up);
			if (Vector3.Dot(target - g, up) < minH) target = g + up * minH;
			f.pos = Vector3.Lerp(f.pos, target, k);
			float tilt = f.front ? Mathf.Lerp(-45f, -6f, r) : Mathf.Lerp(-30f, -8f, r);   // positive points the toes down
			Vector3 nTarget = Quaternion.AngleAxis(tilt, rgt) * up;
			f.normal = Vector3.Slerp(f.normal, nTarget, k).normalized;
			f.dir = Vector3.Slerp(f.dir, Flat(fwd, up), k);
			f.curl = Mathf.Lerp(f.curl, Mathf.Lerp(0.85f, 0.08f, r), k);
			f.t = AirPhase;
		}
	}

	void EndAir(Vector3 up, Vector3 fwd, Vector3 rgt)
	{
		float strength = Mathf.Clamp01(AirImpact / 22f);
		float ahead = spd * 0.07f;
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			f.pos = Ground(HomeG(i, Position + fwd * ahead, fwd, rgt), up);
			f.normal = GroundNormal(f.pos, fwd, rgt, up);
			f.dir = Quaternion.AngleAxis(f.side * (f.front ? toeOutFront : toeOutHind), up) * fwd;
			f.mode = 0; f.planted = 0f; f.curl = 0.1f; f.t = 0f;
		}
		landDip = 4f + 9f * strength;
		bodyH -= landDip * 0.6f;
		landHold = 0.14f;
		if (Landed != null) Landed(Position, strength);
	}

	void StartPair(int pair, float Ts, Vector3 up)
	{
		bool first = true;
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			if (f.pair != pair || f.mode != 0) continue;
			if (first) { BeginSwing(f, Ts, up); first = false; }
			else { f.mode = 1; f.lag = 0.09f; }
		}
	}

	void BeginSwing(Foot f, float Ts, Vector3 up)
	{
		f.mode = 2; f.t = 0f; f.dur = Ts;
		f.liftPos = f.pos; f.liftDir = f.dir; f.liftNormal = f.normal;
		f.targetPos = f.pos; f.targetDir = f.dir;
	}

	// In an arc the outer feet travel further than the inner ones: stride length scales with the foot's distance from the arc's centre (1 +- curvature x lateral offset).
	float StrideFactor(Foot f)
	{
		if (!TurnLean || spd < 3f) return 1f;
		float kappa = yawRate * Mathf.Deg2Rad / Mathf.Max(spd, 3f);       // positive = turning right
		float lat = f.front ? homeLatFront : homeLatHind;
		return Mathf.Clamp(1f - f.side * kappa * lat, 0.45f, 1.6f);
	}

	void UpdateSwing(Foot f, float dt, float S, Vector3 up, Vector3 fwd, Vector3 rgt, float Ts)
	{
		S *= StrideFactor(f);
		// target: the home spot where the body will be when the foot lands, half a stride ahead
		float rem = Mathf.Max(f.dur * (1f - f.t), 0.02f);
		Quaternion q = Quaternion.AngleAxis(yawRate * rem, up);
		Vector3 fF = q * fwd, rF = q * rgt;
		Vector3 pPos = Position + fwd * (spd * rem);
		Vector3 home = Home(f.index, pPos, fF, rF);
		f.targetPos = Ground(home + fF * (S * 0.5f), up);
		if (TurnLean)
		{
			// the feet of the two sides must not cross: the landing spot stays on its own side of the body line
			float latHome = f.front ? homeLatFront : homeLatHind;
			float lt = Vector3.Dot(f.targetPos - pPos, rF) * f.side;
			float minLat = 0.45f * latHome;
			if (lt < minLat) f.targetPos = Ground(f.targetPos + rF * (f.side * (minLat - lt)), up);
		}
		f.targetDir = Quaternion.AngleAxis(f.side * (f.front ? toeOutFront : toeOutHind), up) * fF;
		f.t += dt / f.dur;
		if (f.t >= 1f)
		{
			f.t = 1f;
			f.pos = f.targetPos;
			f.dir = f.targetDir;
			f.normal = GroundNormal(f.pos, fF, rF, up);
			f.mode = 0; f.planted = 0f; f.curl = 0.1f;
			FootFallCount++;
			if (FootFall != null) FootFall(f.index, f.pos, Mathf.Clamp01(0.2f + spd / 40f));
			return;
		}
		float e = Smooth(f.t);
		float arc = (4f + 0.18f * S) * Mathf.Sin(Mathf.PI * Mathf.Pow(f.t, 0.85f));
		f.pos = Vector3.Lerp(f.liftPos, f.targetPos, e) + up * arc;
		f.dir = Vector3.Slerp(f.liftDir, f.targetDir, e);
		f.normal = Vector3.Slerp(f.liftNormal, up, Mathf.Sin(Mathf.PI * f.t));
		f.curl = Mathf.Lerp(0.1f, 0.95f, Mathf.Sin(Mathf.PI * Mathf.Pow(f.t, 0.7f)));
	}

	void UpdateHead(float dt, Vector3 up, Vector3 rb)
	{
		float yawRel, pitchT, rollT;
		float maxRate = 45f;
		Vector3 headPos = Pose.Position[(int)MohoJoint.Head];
		if (State == MohoState.Listening)
		{
			yawRel = Vector3.SignedAngle(fb, Flat(hfw, up).normalized, up);
			pitchT = 4f; rollT = 14f * listenSign; maxRate = 6f;
		}
		else if (LookTarget.HasValue)
		{
			Vector3 to = LookTarget.Value - headPos;
			Vector3 tf = Flat(to, up);
			yawRel = tf.sqrMagnitude > 1e-3f ? Mathf.Clamp(Vector3.SignedAngle(fb, tf.normalized, up), -115f, 115f) : 0f;
			pitchT = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(to, up), Mathf.Max(tf.magnitude, 1f)) * Mathf.Rad2Deg, -35f, 35f);
			rollT = 0f;
			if (State == MohoState.Idle) maxRate = 14f;
		}
		else if (State == MohoState.Idle)
		{
			yawRel = 38f * (Mathf.PerlinNoise(time * 0.05f, 3.1f) * 2f - 1f);
			pitchT = -4f + 8f * (Mathf.PerlinNoise(time * 0.04f, 7.7f) * 2f - 1f);
			rollT = 0f; maxRate = 10f;
		}
		else
		{
			yawRel = 6f * (Mathf.PerlinNoise(time * 0.15f, 1.3f) * 2f - 1f);
			pitchT = -6f; rollT = 0f; maxRate = 25f;
			if (TurnLean) yawRel += Mathf.Clamp(yawRate * 0.6f, -30f, 30f);   // the head leads the turn
		}
		yawRel *= headMotion; pitchT *= headMotion; rollT *= headMotion;
		Vector3 tw = Quaternion.AngleAxis(yawRel, up) * fb;
		float w = 1.5f;
		float delta = Vector3.SignedAngle(hfw, tw, up);
		headVel += (w * w * delta - 2f * w * headVel) * dt;
		headVel = Mathf.Clamp(headVel, -maxRate, maxRate);
		hfw = Flat(Quaternion.AngleAxis(headVel * dt, up) * hfw, up).normalized;
		float rel = Vector3.SignedAngle(fb, hfw, up);
		if (Mathf.Abs(rel) > 120f) { hfw = Quaternion.AngleAxis(Mathf.Sign(rel) * 120f, up) * fb; headVel = 0f; }
		float w2 = 1.8f;
		headPitchVel += (w2 * w2 * (pitchT - headPitch) - 2f * w2 * headPitchVel) * dt;
		headPitch += headPitchVel * dt;
		headRollVel += (w2 * w2 * (rollT - headRoll) - 2f * w2 * headRollVel) * dt;
		headRoll += headRollVel * dt;
	}

	// Writes Pose. Returns the amount (m) the body must come down to keep every planted limb within reach (only when check is true).
	float BuildPose(float h, Vector3 up, Vector3 fwd, Vector3 rgt, float sway, float bend, float twist, float S, bool check)
	{
		Quaternion baseRot = Quaternion.LookRotation(fwd, up) * Quaternion.AngleAxis(-(pitchDeg + restPitch), Vector3.right) * Quaternion.AngleAxis(rollDeg, Vector3.forward);
		Vector3 C = Position + up * h + rgt * sway;
		Quaternion pelvisRot = baseRot * Quaternion.AngleAxis(-twist * 0.5f, Vector3.up);
		Quaternion chestRot = baseRot * Quaternion.AngleAxis(twist * 0.5f, Vector3.up);
		Vector3 Pp = C + baseRot * ppOff;
		Vector3 Pc = C + baseRot * pcOff;
		Vector3 Pm = C + baseRot * (pmOff + new Vector3(bend, spineArch, 0f));
		Vector3 tail = Pp + pelvisRot * tailOff;
		Vector3 nb = Pc + chestRot * nbOff;
		// head frame
		Vector3 rh = Vector3.Cross(up, hfw);
		Quaternion pr = Quaternion.AngleAxis(-headPitch, rh);
		Vector3 hf = pr * hfw, hu0 = pr * up;
		Vector3 hu = Quaternion.AngleAxis(headRoll, hf) * hu0;
		Vector3 cf = chestRot * Vector3.forward;
		Vector3 n1 = nb + (cf + hf * 0.45f).normalized * 5.6f + up * 2.4f;
		Vector3 n2 = n1 + (cf * 0.35f + hf).normalized * 6.2f - up * 0.5f;
		if (modelNeck)
		{
			// the stump hangs from the chest; only its slight lag (a fraction of the head spring) turns it
			float psi = Vector3.SignedAngle(Flat(cf, up), hfw, up);
			n1 = nb + Quaternion.AngleAxis(psi * 0.5f, up) * (chestRot * neck1Off);
			n2 = n1 + Quaternion.AngleAxis(psi, up) * (chestRot * neck2Off);
		}

		Pose.Position[(int)MohoJoint.Tail] = tail; Pose.Rotation[(int)MohoJoint.Tail] = pelvisRot;
		Pose.Position[(int)MohoJoint.Pelvis] = Pp; Pose.Rotation[(int)MohoJoint.Pelvis] = pelvisRot;
		Pose.Position[(int)MohoJoint.SpineMid] = Pm; Pose.Rotation[(int)MohoJoint.SpineMid] = Quaternion.Slerp(pelvisRot, chestRot, 0.5f);
		Pose.Position[(int)MohoJoint.Chest] = Pc; Pose.Rotation[(int)MohoJoint.Chest] = chestRot;
		Pose.Position[(int)MohoJoint.NeckBase] = nb; Pose.Rotation[(int)MohoJoint.NeckBase] = Quaternion.LookRotation((n1 - nb).normalized, up);
		Pose.Position[(int)MohoJoint.NeckMid] = n1; Pose.Rotation[(int)MohoJoint.NeckMid] = Quaternion.LookRotation((n2 - n1).normalized, up);
		Pose.Position[(int)MohoJoint.Head] = n2; Pose.Rotation[(int)MohoJoint.Head] = Quaternion.LookRotation(hf, hu);

		float worst = 0f;
		for (int i = 0; i < 4; i++)
		{
			Foot f = feet[i];
			bool arm = f.front;
			float a = arm ? armUpper : legUpper;
			float b = arm ? armLower : legLower;
			Vector3 sock = arm
				? Pc + chestRot * new Vector3(f.side * shoulderOff.x, shoulderOff.y, shoulderOff.z)
				: Pp + pelvisRot * new Vector3(f.side * hipOff.x, hipOff.y, hipOff.z);
			Vector3 N = f.normal.sqrMagnitude > 0.5f ? f.normal.normalized : up;
			Vector3 D = f.dir - N * Vector3.Dot(f.dir, N);
			D = D.sqrMagnitude > 1e-4f ? D.normalized : fwd;
			if (reverseHind && !arm) D = -D;   // the model's hind toes point backward
			float palm = arm ? HandPalm : FootPalm;
			float wristUp = arm ? wristLift : ankleLift;
			Vector3 W = f.pos - D * (palm * 0.5f) + N * wristUp;
			Vector3 K = f.pos + D * (palm * 0.5f) + N * knuckleLift;
			Vector3 P = (K - W).normalized;
			Vector3 Np = (N - P * Vector3.Dot(N, P)).normalized;
			Vector3 pw = arm ? armPoleW : legPoleW;
			Vector3 pole = up * pw.x + rgt * (f.side * pw.y) + fwd * pw.z;
			float d = (W - sock).magnitude;
			float reach = (a + b) * 0.985f;
			if (check && f.mode == 0)
			{
				Vector3 diff = sock - W;
				float dz = Vector3.Dot(diff, up);
				float hor = Flat(diff, up).magnitude;
				float allowed = hor < reach ? Mathf.Sqrt(reach * reach - hor * hor) : 0f;
				worst = Mathf.Max(worst, dz - allowed);
			}
			if (f.mode == 0) MaxStretchRatio = Mathf.Max(MaxStretchRatio, d / (a + b));
			Vector3 elbow = SolveIK(sock, W, a, b, pole);
			int ju = MohoRigInfo.LimbJoint(i, 0), jl = ju + 1, jh = ju + 2;
			Vector3 poleN = (elbow - (sock + W) * 0.5f);
			if (poleN.sqrMagnitude < 1e-4f) poleN = pole;
			poleN.Normalize();
			Pose.Position[ju] = sock; Pose.Rotation[ju] = Quaternion.LookRotation((elbow - sock).normalized, poleN);
			Pose.Position[jl] = elbow; Pose.Rotation[jl] = Quaternion.LookRotation((W - elbow).normalized, poleN);
			Pose.Position[jh] = W; Pose.Rotation[jh] = Quaternion.LookRotation(P, Np);
			Pose.Planted[i] = f.mode == 0;
			Pose.Curl[i] = f.curl;
			Pose.Contact[i] = f.pos;
			Pose.HandForward[i] = P;
			Pose.HandNormal[i] = Np;
		}
		return worst;
	}

	static Vector3 SolveIK(Vector3 root, Vector3 target, float a, float b, Vector3 pole)
	{
		Vector3 v = target - root;
		float d = v.magnitude;
		Vector3 dir = d > 1e-4f ? v / d : Vector3.down;
		float dc = Mathf.Clamp(d, Mathf.Abs(a - b) + 0.05f, (a + b) * 0.999f);
		float x = (a * a - b * b + dc * dc) / (2f * dc);
		float hh = Mathf.Sqrt(Mathf.Max(a * a - x * x, 0f));
		Vector3 pp = pole - dir * Vector3.Dot(pole, dir);
		if (pp.sqrMagnitude < 1e-4f) pp = Vector3.Cross(dir, Vector3.right);
		pp.Normalize();
		return root + dir * x + pp * hh;
	}
}
