using System;
using System.Collections;
using System.Collections.Generic;
using UnkerbalThings.Dread;
using UnityEngine;

// The Laythe leviathan, a hunter of the deep. The brain and the scare director are pure (Dread/BloopLogic.cs); this file is the game side: the model
// from the bundle, its swim animation, the senses, the scream and the catch. Nothing here moves the craft: the fish circles, dives and rises,
// and only a hit at speed destroys it. Sound and screen systems also get events: BloopCueFired.
// Model: "Alien Fish (animated)" by Jungle Jim, CC BY 4.0 (underwater/leviathan/LICENSE-model.txt).
public partial class EntityTracker
{
	private static float bloopMinDepthMetres = 400f;
	private static float bloopKillEarliestSeconds = 180f;
	private static float bloopKillSpreadSeconds = 90f;
	// where the water is black: with True Volumetrics and Scatterer on Laythe the picture darkens almost linearly to here, day and night alike
	private static float bloopLightDepthMetres = 1000f;
	private static float bloopStrikeSpeed = 48f;
	private static float bloopKillSpeed = 34f;
	private static float leviathanLengthMetres = 45f;
	private static string leviathanLook = "A";
	private static float leviathanScreamVolume = 1f;
	// how far the water lets the player see it (True Volumetrics, Laythe at 440 m: about 60 m)
	private static float leviathanSightMetres = 70f;
	// the clips of underwater/leviathan/anim_build.py (swim wave, backbone bent along the turn, jaws); false: only the model's own swim clip, as before
	private static bool leviathanAnimations = true;
	// tail beats per second = this x speed / tail swing (a Strouhal number; real swimmers sit at 0.2-0.4)
	private static float leviathanTailStrouhal = 0.22f;
	// a craft under way: it leads, paces, overtakes and dives ahead (Dread/BloopLogic.cs, ReactToMotion); false: every figure around where the craft is now
	private static bool bloopReactToMotion = true;
	private static float bloopChaseSpeed = 30f, bloopPaceMaxSpeed = 38f;

	// Cue, strength 0..1, world position of the monster. For the sound and screen systems.
	internal event Action<BloopCue, float, Vector3> BloopCueFired;

	private BloopBrain bloopBrain;
	private BloopCues bloopCues;
	private BloopFrame bloopFrame;
	private LeviathanRig bloopRig;
	private BloopFx bloopFx;
	private double bloopFloorZ = -3000.0, bloopCraftFloorZ = -3000.0;
	private float bloopFloorTimer;
	private readonly Queue<KeyValuePair<float, BloopVec>> bloopEchoTrail = new Queue<KeyValuePair<float, BloopVec>>();
	private BloopVec bloopOldPosition;
	private float bloopAmbienceTimer;
	private BloopState bloopLoggedState;
	internal BloopCue bloopLastCue;
	internal float bloopLastCueTime;
	private static AudioClip leviathanScreamClip, leviathanGrowlClip;
	private AudioSource leviathanScreamAudio, leviathanGrowlAudio;
	private bool bloopJumpscareDone;

	internal BloopBrain BloopBrainForTest { get { return bloopBrain; } }
	// test scenes only: speed, turn rate and jaws for the picture instead of the brain's (x, y, z)
	internal Vector3? bloopTestPose;
	internal string BloopAnimationForTest { get { return bloopRig != null ? bloopRig.AnimationSummary() : "no rig"; } }
	internal BloopCues BloopCuesForTest { get { return bloopCues; } }

	private static void LoadBloopSettings(ConfigNode node)
	{
		if (node == null)
		{
			return;
		}
		if (node.HasValue("leviathanLook"))
		{
			leviathanLook = node.GetValue("leviathanLook").Trim().ToUpperInvariant() == "B" ? "B" : "A";
		}
		ReadFloatSetting(node, "bloopMinDepthMetres", ref bloopMinDepthMetres);
		ReadFloatSetting(node, "bloopKillEarliestSeconds", ref bloopKillEarliestSeconds);
		ReadFloatSetting(node, "bloopKillSpreadSeconds", ref bloopKillSpreadSeconds);
		ReadFloatSetting(node, "bloopLightDepthMetres", ref bloopLightDepthMetres);
		ReadFloatSetting(node, "bloopStrikeSpeed", ref bloopStrikeSpeed);
		ReadFloatSetting(node, "bloopKillSpeed", ref bloopKillSpeed);
		ReadFloatSetting(node, "leviathanLengthMetres", ref leviathanLengthMetres);
		ReadFloatSetting(node, "leviathanScreamVolume", ref leviathanScreamVolume);
		ReadFloatSetting(node, "leviathanSightMetres", ref leviathanSightMetres);
		ReadBoolScareSetting(node, "leviathanAnimations", ref leviathanAnimations);
		ReadFloatSetting(node, "leviathanTailStrouhal", ref leviathanTailStrouhal);
		ReadBoolScareSetting(node, "bloopReactToMotion", ref bloopReactToMotion);
		ReadFloatSetting(node, "bloopChaseSpeed", ref bloopChaseSpeed);
		ReadFloatSetting(node, "bloopPaceMaxSpeed", ref bloopPaceMaxSpeed);
		// a fixed rule: never sooner than three minutes, whatever a settings file says
		bloopKillEarliestSeconds = Mathf.Max(180f, bloopKillEarliestSeconds);
		// a kill speed above what it can reach would make it harmless
		bloopKillSpeed = Mathf.Min(bloopKillSpeed, bloopStrikeSpeed * 0.9f);
		// outside the rise it is never as fast as in it: a craft that outruns the pace gets away
		bloopPaceMaxSpeed = Mathf.Min(bloopPaceMaxSpeed, bloopStrikeSpeed * 0.9f);
		bloopChaseSpeed = Mathf.Min(bloopChaseSpeed, bloopPaceMaxSpeed);
	}

	// Plane tangent at the place of the encounter; Z is the height over the sea.
	private sealed class BloopFrame
	{
		private readonly CelestialBody body;
		private readonly Vector3d o, e1, e2;

		public BloopFrame(CelestialBody body, Vector3d world)
		{
			this.body = body;
			o = (world - body.position).normalized;
			Vector3d north = Vector3d.Exclude(o, (Vector3d)body.transform.up);
			e1 = north.sqrMagnitude < 1e-9 ? Vector3d.Exclude(o, Vector3d.right).normalized : north.normalized;
			e2 = Vector3d.Cross(o, e1).normalized;
		}

		public BloopVec ToPlane(Vector3d world)
		{
			Vector3d rel = world - body.position;
			double r = rel.magnitude;
			Vector3d dir = rel / r;
			double c = Vector3d.Dot(dir, o);
			double k = body.Radius / Math.Max(0.1, c);
			return new BloopVec(Vector3d.Dot(dir, e1) * k, Vector3d.Dot(dir, e2) * k, r - body.Radius);
		}

		public Vector3d ToWorld(BloopVec p)
		{
			Vector3d dir = (o * body.Radius + e1 * p.X + e2 * p.Y).normalized;
			return body.position + dir * (body.Radius + p.Z);
		}

		public Vector3 DirToWorld(BloopVec d)
		{
			return (Vector3)(e1 * d.X + e2 * d.Y + o * d.Z);
		}

		public BloopVec DirToPlane(Vector3d d)
		{
			return new BloopVec(Vector3d.Dot(d, e1), Vector3d.Dot(d, e2), Vector3d.Dot(d, o));
		}
	}

	// Called from SpawnBloopEntity. Null: the bundle is missing, and there is no leviathan.
	private GameObject CreateBloopVisual()
	{
		string why;
		GameObject root = LeviathanRig.Create(System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "UnkerbalThings", "Leviathan", "leviathan.bundle"), leviathanLook, leviathanLengthMetres, leviathanAnimations, leviathanTailStrouhal, out bloopRig, out why);
		if (root == null)
		{
			Debug.Log("[UT Bloop] leviathan model not used: " + why);
		}
		return root;
	}

	private void BloopAiStart(Vessel vessel)
	{
		if (bloopRig == null || bloopEntityObject == null)
		{
			return;
		}
		bloopEntityObject.transform.localScale = Vector3.one;
		bloopRig.ApplyMaterial();
		Vector3d craft = vessel.GetWorldPos3D();
		bloopFrame = new BloopFrame(bloopEntityBody, craft);
		BloopVec start = bloopFrame.ToPlane(bloopEntityBody.position + bloopEntityRelativePosition);
		// near enough to come by, far enough never to be seen arriving: it starts below the craft and roams towards the rough area
		BloopVec craftPlane = bloopFrame.ToPlane(craft);
		start = craftPlane + (start - craftPlane).Unit * UnityEngine.Random.Range(1200f, 3500f);
		start.Z = craftPlane.Z - UnityEngine.Random.Range(40f, 220f);
		int seed = UnityEngine.Random.Range(1, int.MaxValue);
		bloopBrain = new BloopBrain(seed, start);
		bloopBrain.KillEarliestSeconds = bloopKillEarliestSeconds;
		bloopBrain.KillSpreadSeconds = bloopKillSpreadSeconds;
		bloopBrain.StrikeSpeed = bloopStrikeSpeed;
		bloopBrain.KillSpeed = bloopKillSpeed;
		bloopBrain.CatchMetres = Mathf.Max(10f, leviathanLengthMetres * 0.3f);
		bloopBrain.CeilingDepth = Mathf.Max(25f, bloopMinDepthMetres * 0.5f);
		bloopBrain.ReactToMotion = bloopReactToMotion;
		bloopBrain.ChaseSpeed = bloopChaseSpeed;
		bloopBrain.PaceMaxSpeed = bloopPaceMaxSpeed;
		bloopBrain.Rumor(craftPlane + new BloopVec(UnityEngine.Random.Range(-500f, 500f), UnityEngine.Random.Range(-500f, 500f), 0), 500.0);
		bloopCues = new BloopCues(seed ^ 0x51ed);
		bloopEchoTrail.Clear();
		bloopFx = bloopEntityObject.AddComponent<BloopFx>();
		bloopFx.Setup(bloopRig);
		bloopFloorTimer = 0f;
		bloopAmbienceTimer = UnityEngine.Random.Range(20f, 45f);
		bloopLoggedState = BloopState.Dormant;
		LeviathanAudioSetup();
		Debug.Log("[UT Bloop] " + LeviathanDiagnostics());
		Debug.Log("[UT Bloop] leviathan spawned at " + (bloopFrame.ToPlane(craft) - start).Length.ToString("F0") + " m, kill not before " + bloopKillEarliestSeconds + " s, only at " + bloopKillSpeed + " m/s or more, " + bloopRig.AnimationSummary() + ", reacts to a moving craft " + bloopReactToMotion);
	}

	private void BloopAiStop()
	{
		bloopBrain = null;
		bloopCues = null;
		bloopFrame = null;
		bloopRig = null;
		bloopFx = null;
		// the jumpscare rings out over the wreck; only the growl goes, fading
		if (leviathanGrowlAudio != null && leviathanGrowlAudio.isPlaying)
		{
			leviathanGrowlId++;
			if (isActiveAndEnabled) StartCoroutine(LeviathanGrowlEnvelope(leviathanGrowlId, leviathanGrowlAudio.volume, 0f, 0f, 0.4f));
			else leviathanGrowlAudio.Stop();
		}
	}

	private void UpdateBloopAi(Vessel vessel)
	{
		// the damage bridge's hitbox spans the whole model. A solid box this size around a craft flings it away at millions of m/s
		foreach (Collider c in bloopEntityObject.GetComponentsInChildren<Collider>(true))
		{
			c.isTrigger = true;
		}
		float dt = TimeWarp.deltaTime;
		if (dt <= 0f || Time.timeScale <= 0f)
		{
			return;
		}
		Vector3d craftWorld = vessel.GetWorldPos3D();
		BloopVec craft = bloopFrame.ToPlane(craftWorld);
		bool reach = CanBloopTarget(vessel);
		bool lights = vessel.ActionGroups[KSPActionGroup.Light];
		double noise = 0.0;
		try
		{
			HuntUpdateNoise(vessel);
			for (int i = 0; i < huntEvents.Count; i++)
			{
				HuntNoiseEvent e = huntEvents[i];
				if (e.kind == HuntNoise.Lights)
				{
					continue; // lights are seen, not heard
				}
				double range = e.kind == HuntNoise.Throttle ? 9000.0 : e.kind == HuntNoise.Rcs ? 3000.0 : e.kind == HuntNoise.Impact ? 12000.0 : 6000.0;
				if (reach)
				{
					bloopBrain.Hear(e.loudness, range, bloopFrame.ToPlane(e.position));
				}
				noise = Math.Max(noise, e.loudness);
			}
		}
		catch (Exception ex)
		{
			Debug.Log("[UT Bloop] noise sensing failed: " + ex.Message);
		}
		if (vessel.ctrlState != null)
		{
			noise = Math.Max(noise, vessel.ctrlState.mainThrottle);
		}
		bloopFloorTimer -= dt;
		if (bloopFloorTimer <= 0f && bloopEntityBody.pqsController != null)
		{
			bloopFloorTimer = 1f;
			bloopFloorZ = Math.Min(-5.0, bloopEntityBody.pqsController.GetSurfaceHeight((bloopFrame.ToWorld(bloopBrain.Position) - bloopEntityBody.position).normalized) - bloopEntityBody.Radius);
			bloopCraftFloorZ = Math.Min(-5.0, bloopEntityBody.pqsController.GetSurfaceHeight((craftWorld - bloopEntityBody.position).normalized) - bloopEntityBody.Radius);
		}
		bloopBrain.InSight = LeviathanInSight(lights);
		// time warp must not let it skip through the craft: small steps, each with its own contact test
		int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.04f), 1, 50);
		bool screamed = false;
		// how the craft moves through the water, in the plane of the encounter; within a frame of many steps the craft is where it was at that moment
		BloopVec craftVelocity = bloopFrame.DirToPlane(vessel.srf_velocity);
		for (int i = 0; i < steps && !bloopBrain.Caught; i++)
		{
			if (bloopReactToMotion)
			{
				bloopBrain.Update(dt / steps, craft - craftVelocity * (dt - (i + 1) * dt / steps), craftVelocity, lights, bloopFloorZ, reach);
			}
			else
			{
				bloopBrain.Update(dt / steps, craft, lights, bloopFloorZ, reach);
			}
			screamed |= bloopBrain.Screamed;
		}
		bloopEntityChasing = bloopBrain.State != BloopState.Dormant && bloopBrain.State != BloopState.Gone;
		bloopEntityStatus = bloopBrain.State.ToString().ToUpperInvariant();
		bloopEntityRelativePosition = bloopFrame.ToWorld(bloopBrain.Position) - bloopEntityBody.position;
		if (bloopBrain.State != bloopLoggedState)
		{
			Debug.Log("[UT Bloop] " + bloopLoggedState + " -> " + bloopBrain.State + " at " + (craft - bloopBrain.Position).Length.ToString("F0") + " m, " + bloopBrain.Speed.ToString("F0") + " m/s, clock " + bloopBrain.Clock.ToString("F0") + " s" + (bloopBrain.State == BloopState.Recover && bloopBrain.HitSpeed > 0 ? ", last contact at " + bloopBrain.HitSpeed.ToString("F0") + " m/s" : ""));
			bloopLoggedState = bloopBrain.State;
		}

		// where it was a while ago: what an echo reports
		bloopEchoTrail.Enqueue(new KeyValuePair<float, BloopVec>(Time.time, bloopBrain.Position));
		while (bloopEchoTrail.Count > 0 && Time.time - bloopEchoTrail.Peek().Key > 10f)
		{
			bloopOldPosition = bloopEchoTrail.Dequeue().Value;
		}

		BloopPlaceVisual();

		// a real rise starts in silence, deep below; the jumpscare comes when it is near enough to be seen, about 1.5 s before the hit, wherever the camera looks
		if (screamed || bloopBrain.State != BloopState.Strike)
		{
			bloopJumpscareDone = false;
		}
		if (!bloopJumpscareDone && bloopBrain.State == BloopState.Strike && bloopBrain.RealStrike && (LeviathanNear(lights) || bloopBrain.Caught))
		{
			bloopJumpscareDone = true;
			LeviathanScream();
			BloopApplyCue(BloopCue.Scream, 1f, vessel, craft);
		}
		if (bloopBrain.Caught)
		{
			Debug.Log("[UT Bloop] hit the craft at " + bloopBrain.HitSpeed.ToString("F0") + " m/s, " + bloopBrain.Clock.ToString("F0") + " s on the clock, after " + bloopBrain.Feints + " feints and " + bloopBrain.Misses + " misses");
			DespawnBloopEntity();
			HandleEntityCatch(vessel);
			return;
		}
		if (bloopBrain.State == BloopState.Gone)
		{
			Debug.Log("[UT Bloop] gave up and left");
			DespawnBloopEntity();
			return;
		}

		BloopCueInput input = BloopBuildCueInput(vessel, craft, lights, noise, dt);
		bloopCues.Update(input);
		if (bloopCues.Fired != BloopCue.None)
		{
			BloopApplyCue(bloopCues.Fired, (float)bloopCues.FiredStrength, vessel, craft);
		}
		bloopFx.Follow(FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : (Vector3)craftWorld);
		bloopFx.Stream(bloopReactToMotion ? (Vector3)vessel.srf_velocity : Vector3.zero);
		double dist = (craft - bloopBrain.Position).Length;
		LeviathanAmbience(dist, dt);
	}

	private BloopCueInput BloopBuildCueInput(Vessel vessel, BloopVec craft, bool lights, double noise, float dt)
	{
		BloopCueInput i = new BloopCueInput();
		i.Dt = dt;
		i.Depth = -craft.Z;
		Vector3 upV = bloopFrame.DirToWorld(new BloopVec(0, 0, 1));
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
		i.SurfaceLight = Math.Max(0.0, 1.0 + craft.Z / Math.Max(1f, bloopLightDepthMetres));
		i.LightsOn = lights;
		i.Noise = noise;
		BloopVec rel = bloopBrain.Position - craft;
		i.Distance = rel.Length;
		i.MonsterAbove = rel.Z;
		i.State = bloopBrain.State;
		i.CraftFloorClearance = craft.Z - bloopCraftFloorZ;
		i.MonsterFloorClearance = bloopBrain.Position.Z - bloopFloorZ;
		i.CraftSpeed = vessel.srfSpeed;
		if (cam != null)
		{
			Vector3 toMonster = bloopFrame.DirToWorld(rel.Unit);
			i.InFront = Vector3.Dot(cam.transform.forward, toMonster) > 0.55f;
			i.LookingUp = Vector3.Dot(cam.transform.forward, upV) > 0.25f;
			i.InLightCone = lights && Vector3.Dot(cam.transform.forward, toMonster) > 0.8f;
		}
		return i;
	}

	// The picture is always where the fish is, pointing where it swims, banked into its turn.
	private void BloopPlaceVisual()
	{
		Vector3 pos = (Vector3)(bloopEntityBody.position + bloopEntityRelativePosition);
		Vector3 up = bloopFrame.DirToWorld(new BloopVec(0, 0, 1));
		Vector3 fwd = bloopFrame.DirToWorld(bloopBrain.Heading);
		bloopEntityObject.transform.position = pos;
		if (fwd.sqrMagnitude > 0.01f)
		{
			// sideways pull of the turn against a felt "gravity" of 9.8: it leans in, up to 55 degrees
			float bank = Mathf.Clamp(Mathf.Atan2((float)(bloopBrain.Speed * bloopBrain.TurnRate), 9.8f) * Mathf.Rad2Deg, -55f, 55f);
			Quaternion want = Quaternion.AngleAxis(-bank, fwd) * Quaternion.LookRotation(fwd, up);
			// only smooths the frame-to-frame jitter: the heading itself turns at a limited rate in the brain
			bloopEntityObject.transform.rotation = Quaternion.Slerp(bloopEntityObject.transform.rotation, want, Mathf.Clamp01(Time.deltaTime * 10f));
		}
		if (bloopRig != null)
		{
			Vector3 pose = bloopTestPose ?? new Vector3((float)bloopBrain.Speed, (float)bloopBrain.TurnRate, (float)bloopBrain.MouthOpen);
			bloopRig.Animate(pose.x, (float)bloopBrain.StrikeSpeed, pose.y, pose.z, TimeWarp.deltaTime);
		}
	}

	internal void BloopApplyCue(BloopCue cue, float strength, Vessel vessel, BloopVec craft)
	{
		bloopLastCue = cue;
		bloopLastCueTime = Time.time;
		Vector3 monster = (Vector3)(bloopEntityBody.position + bloopEntityRelativePosition);
		Vector3 up = bloopFrame.DirToWorld(new BloopVec(0, 0, 1));
		Debug.Log("[UT Bloop] cue " + cue + " at " + (bloopBrain.Position - craft).Length.ToString("F0") + " m, state " + bloopBrain.State);
		DreadSynth synth = dreadAudioHost != null ? dreadAudioHost.Synth : null;
		switch (cue)
		{
			case BloopCue.WaterDrift:
				bloopFx.Drift(bloopFrame.DirToWorld((bloopBrain.Position - craft).Unit) * -1f, UnityEngine.Random.Range(3f, 7f), strength);
				break;
			case BloopCue.Dimming:
			case BloopCue.Silhouette:
				// its shadow crosses the light from above
				bloopFx.Dim(UnityEngine.Random.Range(3f, 7f), (cue == BloopCue.Silhouette ? 0.45f : 0.3f) + 0.35f * strength);
				break;
			case BloopCue.Bubbles:
				// bubbles need about four seconds to rise to the craft's depth: for a craft under way the column starts ahead on its course, so the craft meets it instead of leaving it behind
				bloopFx.Bubbles(monster, (Vector3)vessel.GetWorldPos3D() + (bloopReactToMotion ? Vector3.ClampMagnitude((Vector3)vessel.srf_velocity * 4f, 150f) : Vector3.zero), up, strength);
				break;
			case BloopCue.Sediment:
				bloopFx.Sediment((Vector3)bloopFrame.ToWorld(new BloopVec(bloopBrain.Position.X, bloopBrain.Position.Y, bloopFloorZ + 4.0)), strength);
				break;
			case BloopCue.Echo:
				BloopEchoLine(bloopOldPosition, craft);
				break;
			case BloopCue.FalseEcho:
			{
				float a = UnityEngine.Random.Range(0f, 6.28f), r = UnityEngine.Random.Range(300f, 2000f);
				BloopEchoLine(craft + new BloopVec(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0), craft);
				break;
			}
			case BloopCue.ConeGlimpse:
			case BloopCue.EyeGlint:
				bloopFx.Eyes(UnityEngine.Random.Range(0.25f, 0.8f));
				break;
			case BloopCue.LureGlow:
				bloopFx.Lure(UnityEngine.Random.Range(5f, 14f), strength);
				break;
			case BloopCue.Silence:
				if (synth != null) synth.TriggerHullSilence(UnityEngine.Random.Range(2f, 5f));
				break;
			case BloopCue.LightFlicker:
				StartScare(ScareKind.Lights, forced: true);
				break;
		}
		try
		{
			if (BloopCueFired != null) BloopCueFired(cue, strength, monster);
		}
		catch (Exception ex)
		{
			Debug.Log("[UT Bloop] cue listener failed: " + ex.Message);
		}
	}

	// Could the player see it now: near enough for the water (lights carry a little further) and anywhere near the camera's view.
	// The brain turns slowly while this is true, so a sharp turn never happens on screen.
	private bool LeviathanInSight(bool lights)
	{
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
		if (cam == null)
		{
			return true;
		}
		if (!LeviathanNear(lights))
		{
			return false;
		}
		Vector3 vp = cam.WorldToViewportPoint((Vector3)(bloopEntityBody.position + bloopEntityRelativePosition));
		return vp.z > -leviathanLengthMetres * 0.5f && vp.x > -0.4f && vp.x < 1.4f && vp.y > -0.4f && vp.y < 1.4f;
	}

	// Near enough for the water to show it, wherever the camera looks. The jumpscare goes by this: from straight below the fish enters the view only a moment before the hit.
	private bool LeviathanNear(bool lights)
	{
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
		Vector3 fish = (Vector3)(bloopEntityBody.position + bloopEntityRelativePosition);
		Vector3 eye = cam != null ? cam.transform.position : (Vector3)FlightGlobals.ActiveVessel.GetWorldPos3D();
		// the body reaches half its length beyond its centre
		float reach = leviathanSightMetres * (lights ? 1.3f : 1f) + leviathanLengthMetres * 0.5f;
		return (fish - eye).sqrMagnitude <= reach * reach;
	}

	// what the camera should be seeing of the model, for the logs and the test scenes
	internal string LeviathanDiagnostics()
	{
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
		SkinnedMeshRenderer r = bloopEntityObject != null ? bloopEntityObject.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
		if (r == null || cam == null)
		{
			return "no renderer or camera";
		}
		Vector3 vp = cam.WorldToViewportPoint(r.bounds.center);
		return "shader " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none") + ", enabled " + r.enabled + ", active " + r.gameObject.activeInHierarchy + ", visible " + r.isVisible
			+ ", layer " + r.gameObject.layer + ", bounds size " + r.bounds.size.ToString("F1") + ", camera distance " + (r.bounds.center - cam.transform.position).magnitude.ToString("F0")
			+ ", viewport " + vp.ToString("F2") + ", camera far " + cam.farClipPlane.ToString("F0") + ", culling mask has layer " + ((cam.cullingMask & (1 << r.gameObject.layer)) != 0)
			+ ", root scale " + bloopEntityObject.transform.lossyScale.ToString("F2");
	}

	private void BloopEchoLine(BloopVec where, BloopVec craft)
	{
		BloopVec rel = where - craft;
		double bearing = Math.Atan2(rel.Y, rel.X) * 180.0 / Math.PI;
		if (bearing < 0.0) bearing += 360.0;
		ScreenMessages.PostScreenMessage("ECHO  " + bearing.ToString("000") + "  " + (Math.Round(rel.Length / 50.0) * 50.0).ToString("0") + " m", UnityEngine.Random.Range(2.5f, 5f), ScreenMessageStyle.UPPER_LEFT);
	}

	// Two sounds of its own, CC0 from Freesound (underwater/leviathan/SOUNDS.txt): the jumpscare when a real rise bursts into sight, a distant growl now and then.
	// Both play flat (2D). The growl comes through a low-pass, heard through the hull; the jumpscare is a finished, limited mix (underwater/leviathan/jumpscare.py)
	// and plays unfiltered at full volume: it is right there.
	private void LeviathanAudioSetup()
	{
		if (leviathanScreamAudio == null)
		{
			leviathanScreamAudio = MakeLeviathanSource("LeviathanScream", 0f);
			leviathanGrowlAudio = MakeLeviathanSource("LeviathanGrowl", 900f);
		}
		if (leviathanScreamClip == null) StartCoroutine(LoadLeviathanClip("leviathan-jumpscare.wav", c => leviathanScreamClip = c));
		if (leviathanGrowlClip == null) StartCoroutine(LoadLeviathanClip("leviathan-growl.mp3", c => leviathanGrowlClip = c));
	}

	// cutoff 0: no filter
	private AudioSource MakeLeviathanSource(string name, float cutoff)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(transform, false);
		AudioSource s = go.AddComponent<AudioSource>();
		s.spatialBlend = 0f;
		s.playOnAwake = false;
		s.loop = false;
		s.dopplerLevel = 0f;
		s.priority = 0;
		if (cutoff > 0f)
		{
			AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
			lp.cutoffFrequency = cutoff;
		}
		return s;
	}

	// file: a name in Sounds, or a full path
	private IEnumerator LoadLeviathanClip(string file, Action<AudioClip> done)
	{
		string path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "UnkerbalThings", "Sounds", file);
		if (!System.IO.File.Exists(path))
		{
			Debug.Log("[UT Bloop] no sound at " + path);
			yield break;
		}
		WWW request = new WWW(new Uri(path).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error))
		{
			done(request.GetAudioClip(false, false, path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.MPEG));
		}
		else
		{
			Debug.Log("[UT Bloop] sound " + file + " failed: " + request.error);
		}
	}

	// The jumpscare: the rise is silent until the fish bursts into sight, then the mix hits at once at full strength.
	// Not scaled by the game's ship volume (0.5 by default made it far too quiet in the first runs); only the master volume and leviathanScreamVolume apply.
	private void LeviathanScream()
	{
		if (leviathanGrowlAudio != null && leviathanGrowlAudio.isPlaying) { leviathanGrowlId++; StartCoroutine(LeviathanGrowlEnvelope(leviathanGrowlId, leviathanGrowlAudio.volume, 0f, 0f, 0.12f)); }
		if (leviathanScreamAudio == null || leviathanScreamClip == null)
		{
			Debug.Log("[UT Bloop] jumpscare sound not loaded");
			return;
		}
		leviathanScreamAudio.clip = leviathanScreamClip;
		leviathanScreamAudio.pitch = 1f;
		leviathanScreamAudio.volume = Mathf.Clamp01(leviathanScreamVolume);
		leviathanScreamAudio.Play();
	}

	// the growl from somewhere in the dark while it hunts: rarer and quieter the further away it is
	private void LeviathanAmbience(double distance, float dt)
	{
		if (leviathanGrowlAudio == null || leviathanGrowlClip == null || !bloopBrain.Aware || bloopBrain.State == BloopState.Strike)
		{
			return;
		}
		bloopAmbienceTimer -= dt;
		if (bloopAmbienceTimer > 0f || leviathanGrowlAudio.isPlaying || (leviathanScreamAudio != null && leviathanScreamAudio.isPlaying))
		{
			return;
		}
		bloopAmbienceTimer = UnityEngine.Random.Range(35f, 80f);
		float near = Mathf.Clamp01(1f - (float)distance / 2500f);
		if (near <= 0.05f)
		{
			return;
		}
		leviathanGrowlAudio.clip = leviathanGrowlClip;
		leviathanGrowlAudio.pitch = UnityEngine.Random.Range(0.7f, 0.85f);
		float growlVolume = (0.25f + 0.5f * near) * GameSettings.SHIP_VOLUME;
		leviathanGrowlAudio.volume = 0f;   // it swells out of the dark: starting mid-recording at full level clicks
		// the recording is long: start somewhere inside it and let it run a while
		leviathanGrowlAudio.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, leviathanGrowlClip.length - 8f));
		leviathanGrowlAudio.Play();
		leviathanGrowlId++;
		StartCoroutine(LeviathanGrowlEnvelope(leviathanGrowlId, growlVolume, 1.2f, UnityEngine.Random.Range(4f, 7f), 1.5f));
	}

	private int leviathanGrowlId;

	// one growl: fade in, hold, fade out, stop. A newer growl or the scream (new id) takes over and this one quits.
	private IEnumerator LeviathanGrowlEnvelope(int id, float volume, float fadeIn, float hold, float fadeOut)
	{
		AudioSource s = leviathanGrowlAudio;
		float from = s != null ? s.volume : 0f;
		for (float t = 0f; fadeIn > 0f && t < fadeIn; t += Time.deltaTime)
		{
			if (s == null || !s.isPlaying || id != leviathanGrowlId) yield break;
			s.volume = Mathf.Lerp(from, volume, t / fadeIn);
			yield return null;
		}
		if (s != null && fadeIn > 0f) s.volume = volume;
		for (float t = 0f; t < hold; t += Time.deltaTime)
		{
			if (s == null || !s.isPlaying || id != leviathanGrowlId) yield break;
			yield return null;
		}
		float v0 = s != null ? s.volume : 0f;
		for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
		{
			if (s == null || !s.isPlaying || id != leviathanGrowlId) yield break;
			s.volume = v0 * (1f - t / fadeOut);
			yield return null;
		}
		if (s != null && id == leviathanGrowlId) s.Stop();
	}
}

// The Alien Fish model from leviathan.bundle: one skinned mesh, its clips (legacy Animation) and its own textures.
// Clips (underwater/leviathan/anim_build.py): the loops "swim" and "sprint", the poses "turn_left", "turn_right" and "attack" laid over them, and
// "swim_classic", the model's own clip, which is all that plays with leviathanAnimations = false or an older bundle.
// The model faces +Z with its back up +Y as imported; the root holds it centred and scaled to the wanted length.
internal sealed class LeviathanRig
{
	private static AssetBundle cachedBundle;
	// measured in Blender on the bind pose (underwater/leviathan/tex.py): length along the fish 1.173, centre of the body in model space
	private const float ModelLength = 1.173f;
	private static readonly Vector3 ModelCentre = new Vector3(0f, 0.27f, -0.215f);
	internal readonly GameObject Root;
	internal readonly Transform LureTip;
	private readonly SkinnedMeshRenderer renderer;
	private readonly Animation anim;
	private readonly AnimationState swim, sprint, turnLeft, turnRight, attack;
	private readonly bool posed;
	private readonly float length, strouhal;
	private float phase, bend, gape;
	private readonly string look;
	// what the clips show, measured in Unity (KKLeviathanBuild.PoseCheck): the tail tip swings this share of the body length from side to side
	private const float SwimTailSwing = 0.168f, SprintTailSwing = 0.207f;
	// the turn clips bend the backbone by this much from nose to tail
	private const float TurnClipRadians = 100f * Mathf.Deg2Rad;

	private LeviathanRig(GameObject root, GameObject model, string look, float length, bool newClips, float strouhal)
	{
		Root = root;
		this.look = look;
		this.length = length;
		this.strouhal = strouhal;
		float scale = length / ModelLength;
		model.transform.localScale = Vector3.one * scale;
		model.transform.localRotation = Quaternion.identity;
		model.transform.localPosition = -ModelCentre * scale;
		renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
		if (renderer != null)
		{
			renderer.updateWhenOffscreen = true;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}
		Transform tip = null;
		foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
		{
			if (t.name == "Bone.007_end") tip = t;
		}
		LureTip = tip ?? model.transform;
		anim = model.GetComponent<Animation>();
		if (anim == null || anim.GetClip("swim") == null)
		{
			return;
		}
		anim.cullingType = AnimationCullingType.AlwaysAnimate;
		anim.playAutomatically = false;
		anim.Stop();
		bool all = true;
		foreach (string name in new[] { "sprint", "turn_left", "turn_right", "attack" })
		{
			all &= anim.GetClip(name) != null;
		}
		posed = newClips && all;
		if (!posed)
		{
			swim = anim.GetClip("swim_classic") != null ? anim["swim_classic"] : anim["swim"];
			swim.wrapMode = WrapMode.Loop;
			swim.time = UnityEngine.Random.Range(0f, swim.length);
			anim.Play(swim.name);
			return;
		}
		swim = anim["swim"];
		sprint = anim["sprint"];
		turnLeft = anim["turn_left"];
		turnRight = anim["turn_right"];
		attack = anim["attack"];
		foreach (AnimationState s in new[] { swim, sprint })
		{
			s.layer = 0;
			s.wrapMode = WrapMode.Loop;
		}
		foreach (AnimationState s in new[] { turnLeft, turnRight, attack })
		{
			// additive: what the pose differs from its own first frame (the rest pose) is added to the swim loop
			s.blendMode = AnimationBlendMode.Additive;
			s.layer = 2;
			s.wrapMode = WrapMode.ClampForever;
		}
		phase = UnityEngine.Random.value;
		if (renderer != null)
		{
			// the bends run over six joints: with fewer than four bones per vertex the skin creases at each
			renderer.quality = SkinQuality.Bone4;
		}
		Animate(0f, 1f, 0f, 0f, 0f);
	}

	internal static GameObject Create(string path, string look, float length, bool newClips, float strouhal, out LeviathanRig rig, out string why)
	{
		rig = null;
		why = null;
		try
		{
			if (!System.IO.File.Exists(path))
			{
				why = "no bundle at " + path;
				return null;
			}
			if (cachedBundle == null)
			{
				cachedBundle = AssetBundle.LoadFromFile(path);
			}
			GameObject prefab = cachedBundle != null ? cachedBundle.LoadAsset<GameObject>("leviathan") : null;
			if (prefab == null)
			{
				why = "no leviathan in the bundle";
				return null;
			}
			GameObject root = new GameObject("EntityTracker_LaytheBloop");
			GameObject model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
			model.name = "LeviathanModel";
			rig = new LeviathanRig(root, model, look, length, newClips, strouhal);
			return root;
		}
		catch (Exception ex)
		{
			why = ex.ToString();
			rig = null;
			return null;
		}
	}

	// after the tracker's own material setup: the darkened skin with its normal map; the brightest spots glow a little on their own in the dark
	internal void ApplyMaterial()
	{
		if (renderer == null || cachedBundle == null)
		{
			return;
		}
		Texture2D albedo = cachedBundle.LoadAsset<Texture2D>("albedo_" + look);
		Texture2D glow = cachedBundle.LoadAsset<Texture2D>("glow_" + look);
		Texture2D normal = cachedBundle.LoadAsset<Texture2D>("normal");
		Shader shader = Shader.Find("KSP/Emissive/Bumped Specular") ?? Shader.Find("KSP/Bumped Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse");
		Material m = new Material(shader);
		m.mainTexture = albedo;
		if (m.HasProperty("_Color")) m.color = Color.white;
		if (m.HasProperty("_BumpMap") && normal != null) m.SetTexture("_BumpMap", normal);
		if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.55f, 0.6f, 0.6f));
		if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.6f);
		if (m.HasProperty("_Emissive") && glow != null)
		{
			m.SetTexture("_Emissive", glow);
			m.SetColor("_EmissiveColor", new Color(0.35f, 0.35f, 0.35f));
		}
		renderer.sharedMaterial = m;
		renderer.gameObject.layer = 0;
	}

	// One rule for each part of the picture (speed m/s, turnRate rad/s as the brain gives it, mouth 0..1):
	//  tail beat  beats per second = Strouhal number x speed / tail swing; cruising and sprinting loops are mixed by speed and kept in step
	//  bend       the backbone lies on the arc it swims: angle from nose to tail = body length x turn rate / speed
	//  jaws       open in under half a second, close slower
	// Without the new clips: the model's own clip, faster the faster the fish goes, as before.
	internal void Animate(float speed, float strikeSpeed, float turnRate, float mouth, float dt)
	{
		if (swim == null)
		{
			return;
		}
		if (!posed)
		{
			swim.speed = Mathf.Lerp(0.55f, 3.2f, Mathf.Clamp01(speed / Mathf.Max(1f, strikeSpeed)));
			return;
		}
		float fast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 40f, speed));
		float swing = Mathf.Lerp(SwimTailSwing, SprintTailSwing, fast) * length;
		float beats = Mathf.Clamp(strouhal * speed / Mathf.Max(0.1f, swing), 0.1f, 1.8f);
		// the loops hold two beats
		phase = Mathf.Repeat(phase + beats * dt * 0.5f, 1f);
		// a positive turn rate of the brain is a turn to the right on screen (it banks the same way, BloopPlaceVisual)
		float want = speed > 2f ? length * turnRate / speed / TurnClipRadians : 0f;
		bend = Mathf.Lerp(bend, Mathf.Clamp(want, -1f, 1f), 1f - Mathf.Exp(-dt / 0.35f));
		gape = Mathf.MoveTowards(gape, Mathf.Clamp01(mouth), dt * (mouth > gape ? 2.4f : 1.1f));
		Pose(swim, 1f - fast, phase);
		Pose(sprint, fast, phase);
		Pose(turnRight, Mathf.Max(0f, bend), 1f);
		Pose(turnLeft, Mathf.Max(0f, -bend), 1f);
		Pose(attack, Mathf.SmoothStep(0f, 1f, gape), 1f);
	}

	private static void Pose(AnimationState s, float weight, float normalizedTime)
	{
		s.enabled = true;
		s.speed = 0f;
		s.weight = weight;
		s.normalizedTime = normalizedTime;
	}

	internal string AnimationSummary()
	{
		return posed ? "clips: swim/sprint, turn, attack; bend " + bend.ToString("F2") + ", jaws " + gape.ToString("F2") : "clip: " + (swim != null ? swim.name : "none");
	}
}


// What the water shows: drifting specks, bubbles, sediment, the lure, the eye glint, the dimming of the light from above. Everything is made here, no asset.
internal sealed class BloopFx : MonoBehaviour
{
	private ParticleSystem snow, bubbles, sediment;
	private Material lureMat;
	private Light lureLight;
	private Transform lureBulb;
	private GameObject eyeA, eyeB;
	private Vector3 driftDir, stream;
	private ParticleSystem.Particle[] specks;
	private const float SnowRadius = 45f;
	private float driftTime, driftStrength, lureTime, lureStrength, dimTime, dimTotal, dimDepth, eyeTime, eyeOpen;
	private Texture2D blackTex;
	private LeviathanRig rig;
	private GameObject holder;

	internal void Setup(LeviathanRig rig)
	{
		this.rig = rig;
		holder = new GameObject("BloopFx");
		Texture2D dot = SoftDot();
		snow = Make("BloopSnow", dot, 400, 0.05f, 0.14f, 40f, new Color(0.75f, 0.85f, 0.9f, 0.22f), false);
		ParticleSystem.ShapeModule s = snow.shape;
		s.enabled = true;
		s.shapeType = ParticleSystemShapeType.Sphere;
		s.radius = 45f;
		ParticleSystem.EmissionModule e = snow.emission;
		e.rateOverTime = 28f;
		bubbles = Make("BloopBubbles", dot, 500, 0.12f, 0.75f, 16f, new Color(0.85f, 0.95f, 1f, 0.5f), true);
		sediment = Make("BloopSediment", dot, 400, 6f, 22f, 26f, new Color(0.24f, 0.22f, 0.18f, 0.20f), true);
		blackTex = new Texture2D(1, 1);
		blackTex.SetPixel(0, 0, Color.black);
		blackTex.Apply();
		Shader glow = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
		lureMat = new Material(glow);
		lureMat.mainTexture = dot;
		lureBulb = Quad("BloopLure", holder.transform, 1.6f, lureMat).transform;
		GameObject l = new GameObject("BloopLureLight");
		l.transform.SetParent(lureBulb, false);
		lureLight = l.AddComponent<Light>();
		lureLight.type = LightType.Point;
		lureLight.color = new Color(0.45f, 1f, 0.8f);
		lureLight.range = 36f;
		lureLight.intensity = 0f;
		eyeA = Quad("BloopEyeA", holder.transform, 1.4f, new Material(glow) { mainTexture = dot });
		eyeB = Quad("BloopEyeB", holder.transform, 1.4f, new Material(glow) { mainTexture = dot });
		eyeA.SetActive(false);
		eyeB.SetActive(false);
	}

	private ParticleSystem Make(string name, Texture2D tex, int max, float size0, float size1, float life, Color color, bool burst)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(holder.transform, false);
		ParticleSystem ps = go.AddComponent<ParticleSystem>();
		ps.Stop();
		ParticleSystem.MainModule m = ps.main;
		m.maxParticles = max;
		m.startLifetime = life;
		m.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
		m.startSpeed = 0f;
		m.startColor = color;
		m.simulationSpace = ParticleSystemSimulationSpace.Local;
		ParticleSystem.EmissionModule e = ps.emission;
		e.rateOverTime = 0f;
		ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
		Shader sh = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
		r.material = new Material(sh) { mainTexture = tex };
		r.renderMode = ParticleSystemRenderMode.Billboard;
		go.layer = 0;
		if (!burst) ps.Play();
		return ps;
	}

	private static GameObject Quad(string name, Transform parent, float size, Material mat)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
		go.name = name;
		Collider c = go.GetComponent<Collider>();
		if (c != null) UnityEngine.Object.Destroy(c);
		go.transform.SetParent(parent, false);
		go.transform.localScale = Vector3.one * size;
		go.GetComponent<Renderer>().sharedMaterial = mat;
		go.layer = 0;
		return go;
	}

	private static Texture2D SoftDot()
	{
		const int N = 32;
		Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
		for (int y = 0; y < N; y++)
		{
			for (int x = 0; x < N; x++)
			{
				float d = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
				float a = Mathf.Clamp01(1f - d);
				t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
			}
		}
		t.Apply();
		return t;
	}

	internal void Follow(Vector3 camera)
	{
		if (snow != null) snow.transform.position = camera;
	}

	// How the craft moves through the water. The specks hang in the water, so they stream past a craft under way, and the drift of a scare
	// (the water pushed by something big) shows as a change of that stream, whichever way the craft is going.
	internal void Stream(Vector3 craftVelocity)
	{
		stream = craftVelocity.sqrMagnitude > 0.25f ? Vector3.ClampMagnitude(craftVelocity, 40f) : Vector3.zero;
	}

	internal void Drift(Vector3 direction, float seconds, float strength)
	{
		driftDir = direction.normalized;
		driftTime = seconds;
		driftStrength = strength;
	}

	internal void Dim(float seconds, float depth)
	{
		dimTime = dimTotal = seconds;
		dimDepth = depth;
	}

	internal void Bubbles(Vector3 source, Vector3 craft, Vector3 up, float strength)
	{
		// the column comes from below, beside the craft: the camera must be able to see it
		Vector3 flat = Vector3.ProjectOnPlane(source - craft, up);
		Vector3 at = craft + Vector3.ClampMagnitude(flat, 35f) - up * 25f;
		bubbles.transform.position = at;
		ParticleSystem.MainModule m = bubbles.main;
		m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
		ParticleSystem.ShapeModule s = bubbles.shape;
		s.enabled = true;
		s.shapeType = ParticleSystemShapeType.Cone;
		s.angle = 6f;
		s.radius = 3f;
		bubbles.transform.rotation = Quaternion.LookRotation(up);
		bubbles.Emit(Mathf.RoundToInt(180f + 260f * strength));
	}

	internal void Sediment(Vector3 at, float strength)
	{
		sediment.transform.position = at;
		ParticleSystem.MainModule m = sediment.main;
		m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 3f);
		ParticleSystem.ShapeModule s = sediment.shape;
		s.enabled = true;
		s.shapeType = ParticleSystemShapeType.Sphere;
		s.radius = 30f;
		sediment.Emit(Mathf.RoundToInt(100f + 200f * strength));
	}

	internal void Lure(float seconds, float strength)
	{
		lureTime = seconds;
		lureStrength = 0.5f + 0.5f * strength;
	}

	internal void Eyes(float seconds)
	{
		eyeTime = seconds;
	}

	private void LateUpdate()
	{
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
		if (cam == null || snow == null)
		{
			return;
		}
		float dt = Time.deltaTime;
		// the specks: at rest they barely move; when the water moves they all go one way
		driftTime -= dt;
		float want = driftTime > 0f ? 11f * driftStrength : 0f;
		ParticleSystem.VelocityOverLifetimeModule vel = snow.velocityOverLifetime;
		vel.enabled = true;
		vel.space = ParticleSystemSimulationSpace.World;
		float k = Mathf.Clamp01(driftTime > 0f ? 1f : 0f);
		vel.x = new ParticleSystem.MinMaxCurve(driftDir.x * want * k - stream.x);
		vel.y = new ParticleSystem.MinMaxCurve(driftDir.y * want * k - stream.y);
		vel.z = new ParticleSystem.MinMaxCurve(driftDir.z * want * k - stream.z);
		if (stream != Vector3.zero)
		{
			// a speck that has streamed out of the ball of water around the camera comes back in on the far side, so the water never empties
			if (specks == null) specks = new ParticleSystem.Particle[snow.main.maxParticles];
			int n = snow.GetParticles(specks);
			bool moved = false;
			for (int i = 0; i < n; i++)
			{
				Vector3 p = specks[i].position;
				if (p.sqrMagnitude > SnowRadius * SnowRadius)
				{
					specks[i].position = -p.normalized * (SnowRadius * 0.97f);
					moved = true;
				}
			}
			if (moved) snow.SetParticles(specks, n);
		}

		lureTime -= dt;
		float glow = lureTime > 0f ? lureStrength * Mathf.Clamp01(lureTime / 1.5f) * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.3f) * Mathf.Sin(Time.time * 0.7f)) : 0f;
		lureLight.intensity = glow * 3f;
		lureMat.SetColor("_TintColor", new Color(0.4f, 1f, 0.75f, glow));
		lureBulb.gameObject.SetActive(glow > 0.01f);
		if (glow > 0.01f)
		{
			// the bones carry the import's scale, so the bulb is not their child: it follows the tip
			if (rig != null) lureBulb.position = rig.LureTip.position;
			lureBulb.rotation = Quaternion.LookRotation(lureBulb.position - cam.transform.position, cam.transform.up);
			// it must read from far away: keep its size on screen, a small point of light and no more
			float dist = (lureBulb.position - cam.transform.position).magnitude;
			lureBulb.localScale = Vector3.one * Mathf.Clamp(dist * 0.012f, 0.9f, 5f);
		}

		eyeTime -= dt;
		bool eyes = eyeTime > 0f;
		// the eyes open and close over a fraction of a second, like the lure's glow, instead of switching on and off
		eyeOpen = eyes ? Mathf.Min(1f, eyeOpen + dt / 0.5f) : 0f;
		float eyeAlpha = 0.8f * eyeOpen * Mathf.Clamp01(eyeTime / 0.6f);
		eyeA.SetActive(eyes);
		eyeB.SetActive(eyes);
		if (eyes)
		{
			// two points at the edge of what is visible, along the line to the monster
			Vector3 to = (transform.position - cam.transform.position).normalized;
			Vector3 at = cam.transform.position + to * 70f;
			Vector3 side = Vector3.Cross(to, cam.transform.up).normalized;
			eyeA.transform.position = at - side * 1.3f;
			eyeB.transform.position = at + side * 1.3f;
			eyeA.transform.rotation = eyeB.transform.rotation = Quaternion.LookRotation(to, cam.transform.up);
			eyeA.GetComponent<Renderer>().material.SetColor("_TintColor", new Color(0.8f, 0.9f, 0.75f, eyeAlpha));
			eyeB.GetComponent<Renderer>().material.SetColor("_TintColor", new Color(0.8f, 0.9f, 0.75f, eyeAlpha));
		}
		dimTime -= dt;
	}

	private void OnGUI()
	{
		if (dimTime <= 0f || Event.current.type != EventType.Repaint)
		{
			return;
		}
		float t = dimTime / Mathf.Max(0.1f, dimTotal);
		float a = dimDepth * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
		Color old = GUI.color;
		GUI.color = new Color(0f, 0f, 0f, a);
		GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), blackTex);
		GUI.color = old;
	}

	private void OnDestroy()
	{
		if (holder != null) UnityEngine.Object.Destroy(holder);
	}
}
