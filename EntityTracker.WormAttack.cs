using System.Globalization;
using UnityEngine;

// The worm swarm's ambush:
//  1. about 20 m from the craft (horizontally) the whole swarm pours into the ground (dive, about 2 s);
//  2. silence: no steps, nothing visible; the hidden creature creeps under the craft;
//  3. after mohoAmbushSilence seconds it erupts straight up under the craft as a spire. A craft that flew more than
//     mohoAmbushReach metres away or above mohoSpireMaxHeight is missed: the spire whips up empty, collapses, and the hunt
//     goes on. That is the player's way out;
//  4. a craft within reach is seized: the spire coils round it and gathers into a ball that holds it while its parts burst,
//     then the ball is hauled down and bursts into a pile.
// Only with the GPU worm body (mohoWormBody = gpu) and mohoWormAmbush = true; otherwise the old catch runs unchanged.
public partial class EntityTracker
{
	private static bool mohoWormAmbush = true;
	private static float mohoAmbushDistance = 150f;
	private static float mohoAmbushSilence = 2.5f;
	private static float mohoAmbushReach = 25f;
	private static float mohoSpireMaxHeight = 45f;
	// still undecided, so easy to undo: the spire reaches mohoSpireMaxHeight times this (1 = the old 45 m) ...
	private static float mohoSpireHeightScale = 2f;
	// ... and a seized craft is slammed down (fast drop, bigger dust and shake); false = the old haul-down
	private static bool mohoSpireSmash = true;
	// worm hunt: the hidden creature creeps under the craft from the start of the dive, and the dive lasts at most this long (s);
	// worms still on top then are left behind (they go under once off the terrain grid). false = the old dive (up to 6 s, creep
	// only once nine tenths are under)
	private static bool mohoWormQuickStrike = true;
	private static float mohoAmbushDiveMax = 2.5f;
	/// <summary>Highest the spire reaches (m): above this at the eruption the craft escapes.</summary>
	private static float SpireMax { get { return Mathf.Clamp(mohoSpireMaxHeight * mohoSpireHeightScale, 8f, 150f); } }
	private static float mohoAmbushUnderSpeed = 200f;
	private static bool mohoAmbushQuietKill = true;   // parts taken by the swarm vanish (crushed, eaten) instead of exploding

	// a strike that catches nothing forms no ball: every worm goes straight to the limp heap, then the hunt
	// goes on (Scramble). false = the old miss (ball or mound, drop, settle)
	private static bool mohoAmbushMissHeap = true;
	// the spire sways, leans and rises less regularly (0 = the old regular motion, 1 = full), the ball is lumpy and uneven (0 = the old round ball)
	private static float mohoSpireIrregular = 1f;
	private static float mohoBallIrregular = 1f;
	private bool wormMissHeap;
	private Vector3 wormLeanShadow, wormLeanVisible;
	// the grab and the kill: the spire's worms writhe and break ranks
	// (mohoGrabChaos, 0 = the old exact column); after the grab the arm collapses onto the craft into a writhing mound
	// (mohoGrabCollapse, false = the old ball); the craft then blows up inside it (mohoKillBlast: auto = Volumetric Explosion FX
	// if installed, else our own vacuum blast; stock = the stock explosions; off = the old quiet kill) and the blast throws the
	// worms (mohoKillFling, at most mohoKillFlingMax m/s, by the craft's propellant and the distance from the blast)
	private static float mohoGrabChaos = 1f;
	private static bool mohoGrabCollapse = true;
	private static string mohoKillBlast = "auto";
	private static bool mohoKillFling = true;
	private static float mohoKillFlingMax = 15f;
	// the explosion's look and the worms' throw are tuned apart (a smaller fireball looked goofy; a weaker throw did not)
	private static float mohoKillBlastSize = 0.35f, mohoKillFlingStrength = 0.6f;
	private const float CollapseSeconds = 1.8f, CollapseRadius = 8f, SquashSeconds = 1.4f, SquashEndHeight = 3.5f;
	private Vector3d wormCollapseFrom;
	private float wormCollapseH0;
	private Vector3 wormCollapseLean0;
	private bool wormBlasted;
	private static int vefxInstalled = -1;
	/// <summary>Volumetric Explosion FX is loaded (a soft dependency: never required, never bundled).</summary>
	private static bool VefxInstalled
	{
		get
		{
			if (vefxInstalled < 0)
			{
				vefxInstalled = 0;
				try { foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies) if (a != null && a.name == "VolumetricExplosionFX") { vefxInstalled = 1; break; } }
				catch (System.Exception) { }
			}
			return vefxInstalled == 1;
		}
	}

	/// <summary>Smooth noise in -1..1: three sines at incommensurate rates (never repeats within a strike).</summary>
	private static float WormNoise(float t, float seed)
	{
		return 0.5f * Mathf.Sin(t * 2.17f + seed) + 0.32f * Mathf.Sin(t * 3.71f + seed * 1.7f + 1.3f) + 0.18f * Mathf.Sin(t * 6.13f + seed * 2.9f + 0.4f);
	}

	private enum WormAmbushPhase { None, Dive, Silent, Rise, Ball, Drop, Collapse, Settle, After }

	private WormAmbushPhase wormAmbushPhase;
	private float wormAmbushTimer, wormAmbushCooldown, wormSpireHeight, wormCraftHeight, wormBallTop;
	private Vessel wormAmbushVessel;
	private System.Guid wormAmbushVesselId;
	private Vector3d wormSpireBaseRel, wormGrabRel;
	private bool wormGrabbed, wormReachable, wormKilled, wormGroundStrike, wormSated;
	private AttackSequence wormTear;
	internal string WormAmbushLog = "";

	internal bool WormAmbushActive { get { return wormAmbushPhase != WormAmbushPhase.None; } }
	internal string WormAmbushPhaseName { get { return wormAmbushPhase.ToString(); } }
	internal bool WormAmbushGrabbed { get { return wormGrabbed; } }
	internal bool WormAmbushKilled { get { return wormKilled; } }
	internal bool WormSated { get { return wormSated; } }

	private static void LoadWormAttackSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "mohoWormAmbush", ref mohoWormAmbush);
		ReadFloatSetting(node, "mohoAmbushDistance", ref mohoAmbushDistance);
		ReadFloatSetting(node, "mohoAmbushSilence", ref mohoAmbushSilence);
		ReadFloatSetting(node, "mohoAmbushReach", ref mohoAmbushReach);
		ReadFloatSetting(node, "mohoSpireMaxHeight", ref mohoSpireMaxHeight);
		ReadFloatSetting(node, "mohoSpireHeightScale", ref mohoSpireHeightScale);
		ReadBoolScareSetting(node, "mohoSpireSmash", ref mohoSpireSmash);
		ReadBoolScareSetting(node, "mohoWormQuickStrike", ref mohoWormQuickStrike);
		ReadBoolScareSetting(node, "mohoAmbushMissHeap", ref mohoAmbushMissHeap);
		ReadFloatSetting(node, "mohoSpireIrregular", ref mohoSpireIrregular);
		ReadFloatSetting(node, "mohoBallIrregular", ref mohoBallIrregular);
		mohoSpireIrregular = Mathf.Clamp01(mohoSpireIrregular);
		mohoBallIrregular = Mathf.Clamp01(mohoBallIrregular);
		ReadFloatSetting(node, "mohoGrabChaos", ref mohoGrabChaos);
		mohoGrabChaos = Mathf.Clamp01(mohoGrabChaos);
		ReadBoolScareSetting(node, "mohoGrabCollapse", ref mohoGrabCollapse);
		ReadStringDreadSetting(node, "mohoKillBlast", ref mohoKillBlast);
		mohoKillBlast = (mohoKillBlast ?? "auto").Trim().ToLowerInvariant();
		if (mohoKillBlast != "auto" && mohoKillBlast != "stock" && mohoKillBlast != "off") mohoKillBlast = "auto";
		ReadBoolScareSetting(node, "mohoKillFling", ref mohoKillFling);
		ReadFloatSetting(node, "mohoKillFlingMax", ref mohoKillFlingMax);
		mohoKillFlingMax = Mathf.Clamp(mohoKillFlingMax, 0f, 40f);
		ReadFloatSetting(node, "mohoKillBlastSize", ref mohoKillBlastSize);
		ReadFloatSetting(node, "mohoKillFlingStrength", ref mohoKillFlingStrength);
		mohoKillBlastSize = Mathf.Clamp(mohoKillBlastSize, 0f, 2f);
		mohoKillFlingStrength = Mathf.Clamp(mohoKillFlingStrength, 0f, 2f);
		ReadFloatSetting(node, "mohoAmbushDiveMax", ref mohoAmbushDiveMax);
		mohoAmbushDiveMax = Mathf.Clamp(mohoAmbushDiveMax, 0.5f, 10f);
		mohoSpireHeightScale = Mathf.Clamp(mohoSpireHeightScale, 0.2f, 3f);
		ReadFloatSetting(node, "mohoAmbushUnderSpeed", ref mohoAmbushUnderSpeed);
		ReadBoolScareSetting(node, "mohoAmbushQuietKill", ref mohoAmbushQuietKill);
		ReadBoolScareSetting(node, "mohoWormLeaps", ref MohoWormGpuSkin.Leaps);
		ReadBoolScareSetting(node, "mohoWormGroundFix", ref MohoWormGpuSkin.GroundFix);
		ReadBoolScareSetting(node, "mohoWormTunnels", ref MohoWormGpuSkin.Tunnels);
		ReadBoolScareSetting(node, "mohoWormDustLit", ref MohoWormDust.Lit);
		ReadBoolScareSetting(node, "mohoWormDustSoft", ref MohoWormDust.Soft);
		ReadFloatSetting(node, "mohoWormDustDensity", ref MohoWormDust.DensityScale);
		ReadBoolScareSetting(node, "mohoWormDustFromWorms", ref MohoWormDust.FromWormsOn);
		ReadFloatSetting(node, "mohoWormDustSun", ref MohoWormDust.SunScale);
		MohoWormDust.DensityScale = Mathf.Clamp(MohoWormDust.DensityScale, 0f, 3f);
		MohoWormDust.SunScale = Mathf.Clamp(MohoWormDust.SunScale, 0.2f, 4f);
		ReadBoolScareSetting(node, "mohoWormSerpent", ref MohoWormGpuSkin.Serpent);
		ReadFloatSetting(node, "mohoWormSerpentLength", ref MohoWormGpuSkin.SerpentLength);
		ReadFloatSetting(node, "mohoWormSerpentRadius", ref MohoWormGpuSkin.SerpentRadius);
		ReadFloatSetting(node, "mohoWormSerpentWave", ref MohoWormGpuSkin.SerpentWave);
		ReadFloatSetting(node, "mohoWormSerpentMaxSpeed", ref mohoWormSerpentMaxSpeed);
		ReadFloatSetting(node, "mohoWormSerpentSubmerged", ref MohoWormGpuSkin.SerpentSubmerged);
		ReadFloatSetting(node, "mohoWormBendStiffness", ref MohoWormGpuSkin.BendStiffness);
		ReadBoolScareSetting(node, "mohoWormCalmHeap", ref MohoWormGpuSkin.CalmHeap);
		ReadFloatSetting(node, "mohoWormSpurtShare", ref MohoWormGpuSkin.SpurtShare);
		ReadFloatSetting(node, "mohoWormSpurtSeconds", ref MohoWormGpuSkin.SpurtSeconds);
		ReadFloatSetting(node, "mohoWormSpurtFast", ref MohoWormGpuSkin.SpurtFast);
		ReadFloatSetting(node, "mohoWormSpurtSlow", ref MohoWormGpuSkin.SpurtSlow);
		MohoWormGpuSkin.SpurtShare = Mathf.Clamp01(MohoWormGpuSkin.SpurtShare);
		MohoWormGpuSkin.SpurtSeconds = Mathf.Clamp(MohoWormGpuSkin.SpurtSeconds, 0.1f, 5f);
		MohoWormGpuSkin.SpurtFast = Mathf.Clamp(MohoWormGpuSkin.SpurtFast, 1f, 3f);
		MohoWormGpuSkin.SpurtSlow = Mathf.Clamp(MohoWormGpuSkin.SpurtSlow, 0.1f, 1f);
		MohoWormGpuSkin.BendStiffness = Mathf.Clamp(MohoWormGpuSkin.BendStiffness, 0.02f, 1f);
		ReadBoolScareSetting(node, "mohoWormSerpentSink", ref MohoWormGpuSkin.SerpentSink);
		MohoWormGpuSkin.SerpentSubmerged = Mathf.Clamp(MohoWormGpuSkin.SerpentSubmerged, 0f, 150f);
		MohoWormGpuSkin.SerpentLength = Mathf.Clamp(MohoWormGpuSkin.SerpentLength, 20f, 180f);
		MohoWormGpuSkin.SerpentRadius = Mathf.Clamp(MohoWormGpuSkin.SerpentRadius, 1f, 8f);
		MohoWormGpuSkin.SerpentWave = Mathf.Clamp(MohoWormGpuSkin.SerpentWave, 0f, 3f);
		mohoWormSerpentMaxSpeed = Mathf.Clamp(mohoWormSerpentMaxSpeed, 5f, 300f);
		float lift = MohoWormGpuSkin.GroundLift;
		ReadFloatSetting(node, "mohoWormGroundLift", ref lift);
		MohoWormGpuSkin.GroundLift = Mathf.Clamp(lift, 0f, 5f);
		mohoAmbushDistance = Mathf.Clamp(mohoAmbushDistance, 8f, 400f);
		mohoAmbushSilence = Mathf.Clamp(mohoAmbushSilence, 1.5f, 30f);
		mohoAmbushReach = Mathf.Clamp(mohoAmbushReach, 3f, 200f);
		mohoSpireMaxHeight = Mathf.Clamp(mohoSpireMaxHeight, 8f, 60f);
		mohoAmbushUnderSpeed = Mathf.Clamp(mohoAmbushUnderSpeed, 2f, 400f);
	}

	private MohoWormGpuSkin WormGpu { get { return bulkWormSwarm as MohoWormGpuSkin; } }

	/// <summary>gpu body with the serpent: its travelling body is always visible, so the sprint is capped.</summary>
	private static float mohoWormSerpentMaxSpeed = 55f;
	/// <summary>gpu body: false while the creature must wait for its heap to dig in before it sets off (the serpent forms).</summary>
	private bool WormMayMove(Vector3d to)
	{
		MohoWormGpuSkin g = WormGpu;
		if (g == null || bulkEntityBody == null) return true;
		Vector3d from = bulkEntityBody.position + bulkEntityRelativePosition;
		bool ok = g.RequestDeparture((Vector3)(to - from), TimeWarp.deltaTime);
		if (!ok) bulkEntityStatus = "BURROWING";
		return ok;
	}

	private float MohoSprintSpeedNow
	{
		get { return (MohoWormGpuSkin.Serpent && WormGpu != null) ? Mathf.Min(mohoSprintSpeed, mohoWormSerpentMaxSpeed) : mohoSprintSpeed; }
	}

	/// <summary>The GPU worm body is on and handles every catch (the old windup/lunge/tear and the leaps stay off).</summary>
	private bool WormGpuCatches { get { return mohoWormAmbush && WormGpu != null && WormGpu.BodyName == "gpu"; } }

	private static float SmoothStep01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

	private void WormNote(string what)
	{
		string line = Time.time.ToString("F1", CultureInfo.InvariantCulture) + " " + what;
		WormAmbushLog += line + "\n";
		Debug.Log("[UT Moho] ambush: " + line);
	}

	// Called while chasing, before the old catch check. True: the ambush started (or runs) and handled this frame.
	private bool TryStartWormAmbush(Vessel vessel, Vector3d creatureGround, bool force = false)
	{
		if (!WormGpuCatches || vessel == null || bulkEntityBody == null) return false;
		wormAmbushCooldown -= TimeWarp.deltaTime;
		// the worm hunt decides itself when to strike: the old cooldown only ran down while a strike was tried, so an arriving
		// serpent stood waiting, sank and rose as a heap before it struck
		if (wormAmbushCooldown > 0f && !force) return false;
		if (mohoMaxKillParts > 0f && vessel.parts != null && vessel.parts.Count > mohoMaxKillParts) return false;   // the old path handles it
		Vector3d up = (creatureGround - bulkEntityBody.position).normalized;
		Vector3d d = VesselCentre(vessel) - creatureGround;
		double horizontal = (d - up * Vector3d.Dot(d, up)).magnitude;
		if (horizontal > mohoAmbushDistance && !force) return false;   // force: the worm hunt decided (it may aim where the craft was)
		wormAmbushPhase = WormAmbushPhase.Dive;
		wormAmbushTimer = 0f;
		wormAmbushVessel = vessel;
		wormAmbushVesselId = vessel.id;
		wormGrabbed = false; wormKilled = false; wormTear = null;
		wormHaveCraft = false; wormCraftVel = Vector3d.zero;
		WormGpu.AmbushControl = true;
		WormGpu.Core.ResetSpire();
		bulkEntityStatus = "BURROWING";
		WormNote("dive at " + horizontal.ToString("F0", CultureInfo.InvariantCulture) + " m from the craft, craft " + vessel.heightFromTerrain.ToString("F0", CultureInfo.InvariantCulture) + " m up");
		return true;
	}

	private void EndWormAmbush(string why)
	{
		WormNote("end: " + why);
		WormHuntAmbushEnded(why == "kill");
		wormAmbushPhase = WormAmbushPhase.None;
		wormAmbushVessel = null;
		wormTear = null;
		wormAmbushCooldown = 6f;
		MohoWormGpuSkin g = WormGpu;
		if (g != null)
		{
			g.AmbushControl = false; g.Core.ResetSpire(); g.Core.diveMode = 0;
			if (wormMissHeap) { g.Core.activity = 1f; g.Core.bendK = MohoWormGpuSkin.BendStiffness; }   // the hunt goes on: the worms get their muscles back
		}
		wormMissHeap = false;
	}

	// the attack aims at the craft's centre of mass, not its root part (the cockpit)
	private static Vector3d VesselCentre(Vessel v)
	{
		// a craft on rails (packed) does not update its centre of mass: use its live position then
		if (v.packed) return v.GetWorldPos3D();
		Vector3d c = v.CoMD;
		return (c.sqrMagnitude > 1.0 && !double.IsNaN(c.x)) ? c : v.GetWorldPos3D();
	}

	// holds the craft with its centre of mass at `centre`
	private void PinVessel(Vessel v, Vector3d centre)
	{
		if (v == null || debugImmortality) return;
		try { v.SetPosition(centre + (v.GetWorldPos3D() - VesselCentre(v))); v.SetWorldVelocity(Vector3d.zero); }
		catch (System.Exception ex) { WormNote("pin failed: " + ex.Message); }
	}

	// Runs the ambush while it is active. True: it handled this frame (UpdateBulkEntity returns).
	private bool UpdateWormAmbush()
	{
		if (wormAmbushPhase == WormAmbushPhase.None) return false;
		MohoWormGpuSkin g = WormGpu;
		if (g == null || bulkEntityBody == null) { wormAmbushPhase = WormAmbushPhase.None; return false; }
		float dt = TimeWarp.deltaTime;
		if (Time.timeScale <= 0f || dt <= 0f) return true;
		dt = Mathf.Min(dt, 0.1f);
		wormAmbushTimer += dt;
		WormSwarmGpu core = g.Core;
		CelestialBody body = bulkEntityBody;
		Vessel v = wormAmbushVessel;
		bool alive = v != null && v.state != Vessel.State.DEAD && v.mainBody == body && v.parts != null && v.parts.Count > 0;
		if (alive)
		{
			// the craft's velocity over the ground, measured (a held or packed craft reports none of its own)
			Vector3d rel = VesselCentre(v) - body.position;
			if (wormHaveCraft) wormCraftVel = Vector3d.Lerp(wormCraftVel, (rel - wormCraftLastRel) / dt, 1.0 - System.Math.Exp(-6.0 * dt));
			wormCraftLastRel = rel;
			wormHaveCraft = true;
		}
		Vector3d pos = body.position + bulkEntityRelativePosition;
		Vector3d up = (pos - body.position).normalized;
		core.leapRate = 0f;

		switch (wormAmbushPhase)
		{
		case WormAmbushPhase.Dive:
			core.diveMode = 2; core.riseRate = 0f;
			if (wormAmbushTimer < 2f && !g.Dust.WormDriven && Random.value < dt * 40f)   // the old random puffs (the worms make their own)
			{
				Vector2 r = Random.insideUnitCircle * core.restRadius;
				Vector3 at = g.ToLocal((Vector3)pos) + new Vector3(r.x, 0f, r.y);
				at.y = core.GroundAt(at.x, at.z);
				g.Dust.Spray(at, Vector3.up, 6, 2.5f, 0.6f, Vector3.zero);
			}
			// the hidden part already moves while the rest pours into the ground; a heap digging in head first keeps the creature
			// (and so the terrain grid) over it until most of it is down, else the worms left behind would sit on unknown ground
			bool quick = mohoWormQuickStrike && WormHuntOn;
			if (quick) core.diveMode = 3;   // all at once, three times the digging speed
			bool allDown = g.VisibleWorms == 0;
			if (alive && (quick ? (allDown || wormAmbushTimer > mohoAmbushDiveMax) : (g.SerpentActive || !MohoWormGpuSkin.Tunnels || g.VisibleWorms < g.WormTotal / 10))) CreepUnder(v, ref pos, dt);
			float diveMax = quick ? mohoAmbushDiveMax : (MohoWormGpuSkin.Tunnels ? 6f : 3f);   // head-first digging takes longer
			if ((wormAmbushTimer > Mathf.Min(1.2f, diveMax) && g.VisibleWorms == 0) || wormAmbushTimer > diveMax + (quick ? 2f : 0f))
			{
				wormAmbushPhase = WormAmbushPhase.Silent;
				bulkEntityStatus = "SILENT";
				WormNote("all under after " + wormAmbushTimer.ToString("F1", CultureInfo.InvariantCulture) + " s, visible " + g.VisibleWorms);
				wormAmbushTimer = 0f;   // the silence has its own clock: before, it kept the dive's and a long dive erupted at once, uncrept
			}
			break;

		case WormAmbushPhase.Silent:
			core.diveMode = mohoWormQuickStrike && WormHuntOn ? 3 : 2; core.riseRate = 0f;
			if (alive) CreepUnder(v, ref pos, dt);
			// the worm hunt strikes only once it is under its target (up to 3 s more); a craft that broke the lock gets the last aim
			if (wormAmbushTimer >= mohoAmbushSilence && (!WormHuntOn || !alive || WormUnderAim(v, pos) || wormAmbushTimer >= mohoAmbushSilence + 3f)) BeginSpire(g, alive, ref pos);
			break;

		case WormAmbushPhase.Rise:
		{
			float te = wormAmbushTimer;
			float hOld = wormSpireHeight * (1f - Mathf.Exp(-te / 0.18f));   // the regular rise: it alone decides the seizure
			core.spireHeight = hOld * (1f - mohoSpireIrregular * 0.09f * (0.5f + 0.5f * WormNoise(te * 1.3f, 5.1f)));   // visible rise: small surges and sags
			core.spireCoil = SmoothStep01((te - 0.5f) / 0.6f) * (wormGroundStrike ? 0.4f : 1f);   // a landed craft: the top only half curls (a rearing head, nothing up there to wrap)
			// the arm comes straight up under the craft, then only leans: the top follows the craft with small smooth corrections,
			// at most about a third of the height to the side and no faster than 10 m/s (a lean, never a stretch)
			Vector3 baseL = core.spireBase;
			float maxLean = Mathf.Clamp(0.3f * wormSpireHeight, 3f, 8f);
			Vector3 craftL = alive ? g.ToLocal(wormGrabbed ? (Vector3)(body.position + wormGrabRel) : (Vector3)VesselCentre(v)) : baseL + Vector3.up * wormSpireHeight;
			Vector3 want = craftL - baseL;
			want.y = 0f;
			want = Vector3.ClampMagnitude(want, maxLean);
			// the shadow lean is the old regular one (core.spireLean): it alone decides the seizure, so catch and
			// miss do not change. The visible lean (mohoSpireIrregular > 0) wanders around it: its direction, size and speed vary
			// with layered noise at incommensurate rates, with hesitations and surges; still at most maxLean and 10 m/s.
			Vector3 lean = Vector3.MoveTowards(wormLeanShadow, want, 10f * dt);
			wormLeanShadow = Vector3.Lerp(wormLeanShadow, lean, 1f - Mathf.Exp(-8f * dt));
			// a landed craft: the column leaps to its full height (it is not cut short at the craft), the craft is taken at its foot
			float topY = wormGroundStrike ? hOld : Mathf.Min(hOld, Mathf.Max(craftL.y - baseL.y, 2f));
			Vector3 tipL = baseL + Vector3.up * topY + wormLeanShadow;
			if (mohoSpireIrregular > 0f)
			{
				float irr = mohoSpireIrregular;
				float ang = irr * (0.38f * WormNoise(te, 1.3f) + 0.2f * WormNoise(te * 1.7f, 7.7f));        // radians off the direct line
				Quaternion rot = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up);
				Vector3 wantV = rot * want * (1f + irr * 0.25f * WormNoise(te * 0.8f, 3.3f));
				Vector3 perp = new Vector3(-want.z, 0f, want.x).normalized;
				wantV += perp * (irr * 1.2f * WormNoise(te * 1.1f, 9.4f));                                 // sway across the line
				wantV = Vector3.ClampMagnitude(wantV, maxLean);
				float surge = Mathf.Clamp01(0.55f + 0.45f * WormNoise(te * 1.4f, 2.2f));
				if (WormNoise(te * 0.9f, 6.6f) < -0.55f) surge *= 0.2f;                                    // a hesitation
				Vector3 stepV = Vector3.MoveTowards(wormLeanVisible, wantV, 10f * Mathf.Lerp(1f, surge, irr) * dt);
				wormLeanVisible = Vector3.ClampMagnitude(Vector3.Lerp(wormLeanVisible, stepV, 1f - Mathf.Exp(-8f * dt)), maxLean);
				core.spireLean = wormLeanVisible;
			}
			else { wormLeanVisible = wormLeanShadow; core.spireLean = wormLeanShadow; }
			float topV = wormGroundStrike ? core.spireHeight : Mathf.Min(core.spireHeight, Mathf.Max(craftL.y - baseL.y, 2f));
			Vector3 tipV = baseL + Vector3.up * topV + core.spireLean;
			core.spireCraft = (wormGrabbed && !wormGroundStrike) ? craftL : tipV;   // a landed craft does not pull the top down to it
			if (alive && wormReachable && !wormGrabbed && te > 0.12f && hOld >= wormCraftHeight - 1.5f)
			{
				// the arm's top reached the craft: seized. A craft that slipped out of the lean's reach is missed.
				Vector3d d = VesselCentre(v) - (body.position + wormSpireBaseRel);
				Vector3 footL = baseL + wormLeanShadow * 0.1f;
				if (wormGroundStrike ? new Vector2(craftL.x - footL.x, craftL.z - footL.z).magnitude < 6f : (craftL - tipL).magnitude < 6f)
				{
					wormGrabbed = true;
					wormGrabRel = VesselCentre(v) - body.position;
					wormTear = new AttackSequence { kind = AttackKind.Bulk, vessel = v, vesselId = v.id, body = body, partCount = -1 };
					BuildAttackTearOrder(wormTear);
					AttackShake(0.9f);
					bulkEntityStatus = "SEIZED";
					WormNote("seized the craft " + Vector3d.Dot(d, up).ToString("F1", CultureInfo.InvariantCulture) + " m up after " + te.ToString("F2", CultureInfo.InvariantCulture) + " s, " + wormTear.order.Count + " parts");
				}
			}
			if (wormGrabbed && alive) PinVessel(v, body.position + wormGrabRel);
			if (te >= 1.3f)
			{
				wormAmbushTimer = 0f;
				if (!wormGrabbed && mohoAmbushMissHeap)
				{
					// a miss: no ball. Every worm of the strike lets go at once and goes to heap mode (limp, dormant), then the
					// hunt goes on as after any miss (After -> EndWormAmbush -> WormHuntAmbushEnded(false)).
					core.ballBlend = 0f; core.burst = 0f; core.spireHold = 0f;
					core.spireRelease = true;
					wormMissHeap = true;
					core.riseRate = 0f;
					wormAmbushPhase = WormAmbushPhase.After;
					WormNote("missed: craft out of reach (" + (alive ? v.heightFromTerrain.ToString("F0", CultureInfo.InvariantCulture) + " m up" : "gone") + "), no ball, straight to the heap");
				}
				else if (mohoGrabCollapse)
				{
					// the arm loses its shape and pours down over the craft into a writhing mound (no ball)
					wormAmbushPhase = WormAmbushPhase.Collapse;
					wormCollapseFrom = wormGrabbed ? body.position + wormGrabRel : body.position + wormSpireBaseRel;
					core.mound = true;
					core.ballRadius = CollapseRadius;
					wormCollapseH0 = Mathf.Max(core.spireHeight, SquashEndHeight + 0.5f);
					wormCollapseLean0 = core.spireLean;
					if (!wormGrabbed) WormNote("missed: craft out of reach (" + (alive ? v.heightFromTerrain.ToString("F0", CultureInfo.InvariantCulture) + " m up" : "gone") + "), collapsing");
				}
				else
				{
					wormAmbushPhase = WormAmbushPhase.Ball;
					if (!wormGrabbed) WormNote("missed: craft out of reach (" + (alive ? v.heightFromTerrain.ToString("F0", CultureInfo.InvariantCulture) + " m up" : "gone") + ")");
				}
			}
			break;
		}

		case WormAmbushPhase.Ball:
		{
			Vector3d baseW = body.position + wormSpireBaseRel;
			Vector3d centreW;
			if (wormGroundStrike)
			{
				// a landed craft: the swirl pours over it into a low mound that crushes it
				Vector3d craftW = wormGrabbed ? body.position + wormGrabRel : baseW;
				centreW = craftW + up * (core.ballRadius * 0.15f);
			}
			else centreW = wormGrabbed ? body.position + wormGrabRel : baseW + up * (wormSpireHeight * 0.8f);
			core.ballCenter = g.ToLocal((Vector3)centreW);
			core.spireCraft = core.ballCenter;
			core.ballBlend = SmoothStep01(wormAmbushTimer / 0.8f);
			if (wormGrabbed && alive && !wormGroundStrike) PinVessel(v, centreW);
			TearSome(wormAmbushTimer / 1.1f);
			if (wormAmbushTimer >= 1.2f)
			{
				if (wormGroundStrike)
				{
					if (wormGrabbed) FinishWormKill();
					StartSettle(g, centreW, ref pos);
				}
				else
				{
					wormAmbushPhase = WormAmbushPhase.Drop;
					wormAmbushTimer = 0f;
					wormBallTop = core.ballCenter.y;
				}
			}
			break;
		}

		case WormAmbushPhase.Drop:
		{
			// the ball is hauled down to the ground, faster and faster (mohoSpireSmash: slammed down in half the time)
			float u = Mathf.Clamp01(wormAmbushTimer / (mohoSpireSmash ? 0.45f : 0.9f));
			Vector3 c = core.ballCenter;
			float floor = core.GroundAt(c.x, c.z) + core.ballRadius * 0.5f;
			c.y = Mathf.Lerp(wormBallTop, floor, u * u);
			core.ballCenter = c;
			core.spireCraft = c;
			if (wormGrabbed && alive) PinVessel(v, (Vector3d)g.ToWorld(c));
			TearSome(1f);
			if (u >= 1f)
			{
				Vector3 ground = new Vector3(c.x, core.GroundAt(c.x, c.z), c.z);
				if (mohoSpireSmash) { g.Dust.Ring(ground, core.ballRadius * 1.4f, 2600, 7f, 0.9f, core.GroundAt); AttackShake(1f); }
				else { g.Dust.Ring(ground, core.ballRadius, 1200, 4f, 0.6f, core.GroundAt); AttackShake(0.7f); }
				if (wormGrabbed) FinishWormKill();
				StartSettle(g, (Vector3d)g.ToWorld(c), ref pos);
			}
			break;
		}

		case WormAmbushPhase.Collapse:
		{
			// the column squashes down onto the craft: it keeps its grip at the top and shortens from the top down (slow, then
			// faster), pulling a seized craft down inside its top; every worm keeps its share of the height, so the whole column
			// compresses evenly and nothing jumps, and it widens as it shortens (the same mass), so near the ground it is already
			// mound-shaped. Only the last ~0.5 s blends into the tangled mound. Outer parts crunch. Then the blast.
			float u = Mathf.Clamp01(wormAmbushTimer / CollapseSeconds);
			float us = Mathf.Clamp01(wormAmbushTimer / SquashSeconds);
			float H = Mathf.Lerp(wormCollapseH0, SquashEndHeight, us * us * (3f - 2f * us));   // slow, then fast, then easing out onto the mound
			core.spireHeight = H;
			// grab chaos: the lower column gives way to physics (bottom first, over 0.5 s) and is pushed out by the mass coming down;
			// no widening by targets. Without chaos the old guided squash widens the column.
			core.squash = mohoGrabChaos > 0f ? 1f : Mathf.Clamp(Mathf.Sqrt(wormCollapseH0 / H), 1f, 1.5f);
			core.releaseShare = mohoGrabChaos > 0f ? 0.65f * SmoothStep01(wormAmbushTimer / 0.5f) : 0f;
			core.squashFrom = wormCollapseH0;
			core.spireLean = wormCollapseLean0 * (H / wormCollapseH0);   // the foot stays where it is
			Vector3 baseL = core.spireBase;
			Vector3 topL = baseL + Vector3.up * H + core.spireLean;
			Vector3d groundW = ProjectToGround((Vector3d)g.ToWorld(baseL + core.spireLean), body);
			Vector3 gL = g.ToLocal((Vector3)groundW);
			core.ballCenter = gL;
			bool carried = wormGrabbed && alive && !wormGroundStrike;
			Vector3 gripL = topL - Vector3.up * 1.5f;
			gripL.y = Mathf.Max(gripL.y, gL.y + 1f);
			core.spireCraft = carried ? gripL : topL;
			core.ballBlend = SmoothStep01((u - 0.55f) / 0.45f);
			core.spireHold = Mathf.Lerp(1f, 0.5f, SmoothStep01((u - 0.75f) / 0.25f));
			if (carried)
			{
				// the craft rides down in the column's top (eased in from where it was seized, so it does not snap)
				Vector3d gripW = (Vector3d)g.ToWorld(gripL);
				PinVessel(v, Vector3d.Lerp(wormCollapseFrom, gripW, SmoothStep01(wormAmbushTimer / 0.3f)));
			}
			TearSome(0.3f * u);
			if (u >= 1f)
			{
				if (wormGrabbed) WormKillBlast(g, groundW, ref pos);
				else
				{
					wormBallTop = core.ballCenter.y;
					StartSettle(g, groundW, ref pos);
				}
			}
			break;
		}

		case WormAmbushPhase.Settle:
		{
			// the guide lets go: its pull fades while the mass sinks onto the ground, so the worms land and the pile takes over
			float u = Mathf.Clamp01(wormAmbushTimer / 1.6f);
			core.spireHold = 1f - SmoothStep01(u);
			Vector3 c = core.ballCenter;
			c.y = Mathf.Lerp(wormBallTop, core.GroundAt(c.x, c.z) + core.ballRadius * 0.2f, SmoothStep01(u));
			core.ballCenter = c;
			core.spireCraft = c;
			if (u >= 1f)
			{
				core.burst = 0f;
				core.spireRelease = true;   // spire worms become ordinary worms (surface, or crawling out of the soil)
				core.riseRate = 0f;         // only the strike's worms make the pile; the rest stay under (no worms popping up around it)
				wormAmbushPhase = WormAmbushPhase.After;
				wormAmbushTimer = 0f;
				WormNote("handed over to the pile" + (wormKilled ? ", craft destroyed" : ""));
			}
			break;
		}

		case WormAmbushPhase.After:
			if (wormMissHeap) { core.activity = 0.5f; core.bendK = Mathf.Min(0.1f, MohoWormGpuSkin.BendStiffness); }   // a miss: the heap lies limp at once
			core.diveMode = 0;
			core.riseRate = 0f;
			if (wormAmbushTimer >= 1f)   // the pile has formed; nothing left to watch (was 2.5 s)
			{
				// after a kill the swarm stays where it fed, as a living pile, until the flight scene ends
				bulkEntityStatus = wormKilled ? "FEEDING" : "CHASING";
				if (wormKilled && !WormHuntOn) wormSated = true;   // the worm hunt: a fed heap that still hunts (WormHunt, Fed)
				EndWormAmbush(wormKilled ? "kill" : "miss");
			}
			break;
		}

		bulkEntityRelativePosition = pos - body.position;
		bulkEntityObject.transform.position = (Vector3)pos;
		FeedMoho(pos, (pos - body.position).normalized, v, false);
		return true;
	}

	private void BeginSpire(MohoWormGpuSkin g, bool alive, ref Vector3d pos)
	{
		CelestialBody body = bulkEntityBody;
		WormSwarmGpu core = g.Core;
		Vector3d baseW = pos;
		wormReachable = false;
		wormCraftHeight = 0f;
		if (alive)
		{
			Vessel v = wormAmbushVessel;
			Vector3d under = ProjectToGround(CraftAim(v), body);   // where the craft will be when the spire arrives
			Vector3d up = (under - body.position).normalized;
			Vector3d d = under - pos;
			double horizontal = (d - up * Vector3d.Dot(d, up)).magnitude;
			wormCraftHeight = (float)Vector3d.Dot(VesselCentre(v) - under, up);
			if (horizontal <= mohoAmbushReach)
			{
				baseW = under;
				wormReachable = wormCraftHeight <= SpireMax - 1f;
			}
			WormNote("erupt: craft " + horizontal.ToString("F0", CultureInfo.InvariantCulture) + " m from the hidden creature, " + wormCraftHeight.ToString("F1", CultureInfo.InvariantCulture) + " m up, reachable " + wormReachable);
		}
		else WormNote("erupt: the craft is gone");
		pos = baseW;
		wormSpireBaseRel = baseW - body.position;
		// a landed craft: the swarm still leaps high (22 m) and comes down on it, not a short hop up to the craft
		wormSpireHeight = wormCraftHeight < 4f ? Mathf.Min(22f, SpireMax) : Mathf.Clamp(wormCraftHeight + 5f, 14f, SpireMax);
		wormGroundStrike = wormCraftHeight < 4f;
		core.ballRadius = wormGroundStrike ? 8f : 12f;
		core.ResetSpire();
		core.spireOn = true;
		core.spireIrreg = mohoSpireIrregular; core.ballIrreg = mohoBallIrregular; core.grabChaos = mohoGrabChaos; core.mound = false; wormBlasted = false;
		wormLeanShadow = Vector3.zero; wormLeanVisible = Vector3.zero; wormMissHeap = false;
		core.diveMode = 0;
		Vector3 baseL = g.ToLocal((Vector3)baseW);
		core.spireBase = baseL;
		core.spireCraft = baseL + Vector3.up * wormSpireHeight;
		g.Dust.Curtain(baseL, 6f, 1.1f, 1f, core.GroundAt);   // the ejecta curtain (lit dust); the old dust gets its ring
		AttackShake(1f);
		bulkEntityStatus = "ERUPTING";
		DreadSoundWormErupt(wormGroundStrike ? 1f : 0.6f);
		wormAmbushPhase = WormAmbushPhase.Rise;
		wormAmbushTimer = 0f;
	}

	// parts burst one after another while the ball holds the craft (outermost first, crew last), by fraction 0..1
	private void TearSome(float fraction)
	{
		if (wormTear == null || wormTear.order == null || debugImmortality) return;
		int want = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(fraction) * wormTear.order.Count), 0, wormTear.order.Count);
		while (wormTear.nextPart < want)
		{
			Part part = wormTear.order[wormTear.nextPart++];
			if (part != null) { if (mohoAmbushQuietKill) part.Die(); else part.explode(); }
		}
	}

	private void FinishWormKill()
	{
		Vessel v = wormAmbushVessel;
		TearSome(1f);
		if (!debugImmortality && v != null && v.state != Vessel.State.DEAD && v.parts != null && v.parts.Count > 0)
		{
			if (mohoAmbushQuietKill) { var left = new System.Collections.Generic.List<Part>(v.parts); foreach (Part p in left) if (p != null) p.Die(); if (v.state != Vessel.State.DEAD) v.Die(); }
			else DestroyCaughtVessel(v);
		}
		bulkSeenVesselIds.Remove(wormAmbushVesselId);
		wormKilled = !debugImmortality;
		if (wormKilled && !WormKillTailRunning) DreadSoundWormKill();   // the quiet kill (no blast) must not be silent
		if (debugImmortality) { bulkEntityStandingDown = true; bulkWithdrawalTimer = 3f; }
	}
	// The end of the collapse: the craft blows up inside the mound and the blast throws the worms (study: one frame for every
	// remaining part, so Volumetric Explosion FX merges them into one vacuum blast; without it our own blast, WormBlastFx).
	private void WormKillBlast(MohoWormGpuSkin g, Vector3d groundW, ref Vector3d pos)
	{
		WormSwarmGpu core = g.Core;
		Vessel v = wormAmbushVessel;
		UnkerbalThings.Dread.KillBlastSize size = null;
		try
		{
			if (v != null && v.parts != null)
			{
				var res = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();
				double mass = 0;
				foreach (Part p in v.parts)
				{
					if (p == null) continue;
					mass += (p.mass + p.GetResourceMass()) * 1000.0;
					for (int i = 0; i < p.Resources.Count; i++)
					{
						PartResource r = p.Resources[i];
						if (r != null && r.info != null) res.Add(new System.Collections.Generic.KeyValuePair<string, double>(r.resourceName, r.amount * r.info.density * 1000.0));
					}
				}
				Vector3 vs = v.vesselSize;
				size = UnkerbalThings.Dread.KillBlast.Size(res, mass, Mathf.Max(vs.x, Mathf.Max(vs.y, vs.z)));
			}
		}
		catch (System.Exception ex) { WormNote("blast sizing failed: " + ex.Message); }
		if (size == null) size = UnkerbalThings.Dread.KillBlast.Size(null, 1000, 2);

		bool alive = v != null && v.state != Vessel.State.DEAD && v.parts != null && v.parts.Count > 0;
		string how;
		if (mohoKillBlast == "off" || debugImmortality || !alive)
		{
			FinishWormKill();
			how = debugImmortality ? "immortal" : (alive ? "off (quiet)" : "craft already gone");
		}
		else
		{
			TearSome(0f);
			var left = new System.Collections.Generic.List<Part>(v.parts);
			bool stock = mohoKillBlast == "stock";
			foreach (Part p in left) if (p != null) { if (stock) p.explode(); else p.Die(); }   // one frame: one merged blast
			if (v.state != Vessel.State.DEAD) v.Die();
			bulkSeenVesselIds.Remove(wormAmbushVesselId);
			wormKilled = true;
			DreadSoundWormKill();
			if (stock) how = "stock explosions";
			else if (VefxInstalled) how = "Volumetric Explosion FX";
			else
			{
				how = "own vacuum blast";
				if (mohoKillBlastSize > 0f)
					g.Dust.Blast(g.ToLocal((Vector3)groundW) + Vector3.up, (float)size.FireballRadius * mohoKillBlastSize, (float)size.DomeRadius * mohoKillBlastSize, (float)size.Fire, (float)size.LaunchSpeed * mohoKillBlastSize, core.GroundAt);
			}
		}
		// the fling: every worm of the strike lets go at once, thrown out and up from the blast (closest fastest)
		float fling = mohoKillFling ? UnkerbalThings.Dread.KillBlast.FlingSpeed(size, mohoKillFlingMax) * mohoKillFlingStrength : 0f;
		float reach = UnkerbalThings.Dread.KillBlast.FlingReach(size, CollapseRadius);
		core.ballCenter = g.ToLocal((Vector3)groundW) + Vector3.up * 1.5f;
		core.burst = fling;
		core.blastRadius = fling > 0f ? reach : 0f;
		core.blastStart = core.spireTime;
		core.spireHold = 0f;
		core.spireRelease = true;
		core.riseRate = 0f;
		wormBlasted = true;
		AttackShake(1f);
		WormNote("blast: " + how + ", " + size + ", fling " + fling.ToString("F1", CultureInfo.InvariantCulture) + " m/s to " + reach.ToString("F0", CultureInfo.InvariantCulture) + " m");
		pos = groundW;
		wormAmbushPhase = WormAmbushPhase.After;
		wormAmbushTimer = 0f;
		WormNote("handed over to the pile" + (wormKilled ? ", craft destroyed" : ""));
	}

	private void StartSettle(MohoWormGpuSkin g, Vector3d centreW, ref Vector3d pos)
	{
		wormBallTop = g.Core.ballCenter.y;
		wormAmbushPhase = WormAmbushPhase.Settle;
		wormAmbushTimer = 0f;
		pos = ProjectToGround(centreW, bulkEntityBody);   // the creature is where the pile will be
	}

	// After a kill: the swarm stays where it fed, a living pile (no hunting, no leaving), until the flight scene ends.
	private bool UpdateWormSated()
	{
		if (!wormSated || WormGpu == null || bulkEntityBody == null) return false;
		if (Time.timeScale <= 0f || TimeWarp.deltaTime <= 0f) return true;
		Vector3d pos = bulkEntityBody.position + bulkEntityRelativePosition;
		bulkEntityStatus = "FEEDING";
		bulkEntityChasing = false;
		// a dormant heap: the ambush keeps the swarm (no diving, rising or leaping), the worms slump and only writhe slowly
		MohoWormGpuSkin g = WormGpu;
		g.AllowRise = false;
		g.AmbushControl = true;
		g.Core.diveMode = 0; g.Core.riseRate = 0f; g.Core.leapRate = 0f; g.Core.reachShare = 0f;
		g.Core.activity = Mathf.MoveTowards(g.Core.activity, 0.5f, 0.2f * TimeWarp.deltaTime);
		g.Core.bendK = Mathf.MoveTowards(g.Core.bendK, Mathf.Min(0.1f, MohoWormGpuSkin.BendStiffness), 0.1f * TimeWarp.deltaTime);   // limp: they drape over each other instead of lying like sticks
		bulkEntityObject.transform.position = (Vector3)pos;
		FeedMoho(pos, (pos - bulkEntityBody.position).normalized, null, false);
		return true;
	}
	private Vector3d wormCraftVel, wormCraftLastRel;
	private bool wormHaveCraft;

	// where to strike a moving craft: its centre of mass 0.35 s ahead along its measured ground velocity
	private Vector3d CraftAim(Vessel v) { return VesselCentre(v) + wormCraftVel * 0.35; }

	// true when the hidden creature is under where it means to strike (the craft, led; or the last aim of a broken lock)
	private bool WormUnderAim(Vessel v, Vector3d pos)
	{
		Vector3d aim;
		if (!WormLockedAim(v, out aim)) aim = CraftAim(v);
		Vector3d under = ProjectToGround(aim, bulkEntityBody);
		Vector3d up = (under - bulkEntityBody.position).normalized;
		Vector3d d = under - pos;
		return (d - up * Vector3d.Dot(d, up)).magnitude <= mohoAmbushReach * 0.5;
	}

	// the hidden creature creeps under the craft (nobody can see or hear it)
	private void CreepUnder(Vessel v, ref Vector3d pos, float dt)
	{
		Vector3d aim;
		if (!WormLockedAim(v, out aim)) aim = CraftAim(v);   // locked on: it follows the craft unless it got far enough sideways
		Vector3d under = ProjectToGround(aim, bulkEntityBody);
		Vector3d to = under - pos;
		double dist = to.magnitude, stepLen = mohoAmbushUnderSpeed * dt;
		pos = dist <= stepLen ? under : ProjectToGround(pos + to / dist * stepLen, bulkEntityBody);
	}
}
