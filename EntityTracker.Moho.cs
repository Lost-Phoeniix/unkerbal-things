using UnkerbalThings.Dread;
using System;
using UnityEngine;

// The Moho/Eve encounter keeps its existing hunt, warning, leap and footfall logic; its visible skin is a collider-free worm swarm.
// The tracker only feeds the creature position, heading, speed, look target
// and state; the creature owns body orientation, feet, head and mesh. Heading/look target come from measured motion until the ai unit
// exposes its own (see FeedMoho).
public partial class EntityTracker
{
	private static bool mohoCreature = true;

	private MohoCreature bulkMoho;
	private IWormSwarmSkin bulkWormSwarm;
	// which worm body: "gpu" (MohoWormGpuSkin: GPU worms that burrow and ambush; falls back to "heap" without compute shaders or the bundle),
	// or "heap" (MohoWormHeapSkin). KK_TEST_WORMBODY overrides it.
	private static string mohoWormBody = "gpu";
	private Vector3d mohoLastPos;
	private bool mohoHasLast;
	private float mohoSpeedSmooth;
	private Vector3 mohoHeading = Vector3.forward;
	private EventData<Vector3d, Vector3d>.OnEvent mohoShiftHandler;

	// Footfall hook for later step sounds: (foot 0 LF / 1 RF / 2 LH / 3 RH, world position, strength 0..1). DreadSteps is not changed.
	internal event Action<int, Vector3, float> MohoFootFall;

	// A craft with more parts than this survives the strike and the creature leaves. 0: no limit, every craft it reaches is torn apart.
	private static float mohoMaxKillParts = 0f;

	private static bool mohoTurnLean = true;
	private static float mohoLeanMaxDeg = 14f;
	private bool bulkTurnHold;     // the Moho creature is running away under HeadingMotion: its heading is the brain's, not read back from the body

	private static void LoadMohoSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "mohoTurnLean", ref mohoTurnLean);
		ReadFloatSetting(node, "mohoMaxKillParts", ref mohoMaxKillParts);
		ReadFloatSetting(node, "mohoTurnLeanMaxDeg", ref mohoLeanMaxDeg);
		mohoLeanMaxDeg = Mathf.Clamp(mohoLeanMaxDeg, 0f, 35f);
		string body = node.GetValue("mohoWormBody");
		if (!string.IsNullOrEmpty(body)) mohoWormBody = body.Trim().ToLowerInvariant();
		LoadWormAttackSettings(node);
	}

	private static IWormSwarmSkin CreateWormBody(Transform parent)
	{
		string choice = Environment.GetEnvironmentVariable("KK_TEST_WORMBODY");
		choice = string.IsNullOrEmpty(choice) ? mohoWormBody : choice.Trim().ToLowerInvariant();
		if (choice == "gpu")
		{
			ComputeShader sim; Shader tube;
			if (MohoWormGpuSkin.Available(out sim, out tube)) return new MohoWormGpuSkin(parent, sim, tube);
			Debug.Log("[UT Moho] gpu worm body unavailable (" + SystemInfo.graphicsDeviceType + ", compute " + SystemInfo.supportsComputeShaders + ", bundle " + (KKShaderBundle.Failure ?? "ok") + "): using the heap");
		}
		return new MohoWormHeapSkin(parent);
	}

	private GameObject CreateMohoEntityVisual()
	{
		GameObject root = new GameObject("EntityTracker_MohoEveEntity");
		root.layer = 0;
		MohoCreature creature = root.AddComponent<MohoCreature>();
		bulkWormSwarm = CreateWormBody(root.transform);
		creature.Skin = bulkWormSwarm;
		bulkMoho = creature;
		creature.FootFall += OnMohoFootFall;
		creature.Landed += OnMohoLanded;
		MohoResetSprintState();
		mohoShiftHandler = OnMohoOriginShift;
		GameEvents.onFloatingOriginShift.Add(mohoShiftHandler);
		mohoHasLast = false;
		mohoSpeedSmooth = 0f;
		return root;
	}

	private void OnMohoFootFall(int foot, Vector3 position, float strength)
	{
		OnMohoFootFallForSteps();
		if (MohoFootFall != null)
		{
			MohoFootFall(foot, position, strength);
		}
	}

	private void OnMohoOriginShift(Vector3d offset, Vector3d nonFrame)
	{
		if (bulkMoho != null)
		{
			bulkMoho.ApplyWorldShift(-(Vector3)offset);
			mohoLastPos -= offset;
		}
		if (bulkWormSwarm != null) bulkWormSwarm.ApplyWorldShift(-(Vector3)offset);
	}

	private void ReleaseMoho()
	{
		if (mohoShiftHandler != null)
		{
			GameEvents.onFloatingOriginShift.Remove(mohoShiftHandler);
			mohoShiftHandler = null;
		}
		wormAmbushPhase = WormAmbushPhase.None;
		wormAmbushVessel = null;
		wormKilled = false;
		wormGrabbed = false;
		wormSated = false;
		bulkMoho = null;
		if (bulkWormSwarm != null) bulkWormSwarm.Dispose();
		bulkWormSwarm = null;
		bulkTurnHold = false;
		mohoHasLast = false;
		mohoLeap.Active = false;
		mohoLeapLift = 0f;
		bulkLeaving = false;
	}

	// Called from UpdateBulkEntity each frame with the logical ground position.
	private void FeedMoho(Vector3d groundPos, Vector3d radialUp, Vessel target, bool chasing)
	{
		if (bulkMoho == null)
		{
			return;
		}
		float dt = TimeWarp.deltaTime;
		if (dt <= 0f || Time.timeScale <= 0f)
		{
			return;
		}
		Vector3 up = (Vector3)radialUp;
		if (!mohoHasLast)
		{
			mohoLastPos = groundPos;
			mohoHasLast = true;
			mohoHeading = Vector3.ProjectOnPlane(bulkEntityObject.transform.rotation * Vector3.back, up);
		}
		Vector3 delta = Vector3.ProjectOnPlane((Vector3)(groundPos - mohoLastPos), up);
		mohoLastPos = groundPos;
		float raw = Mathf.Min(delta.magnitude / dt, 400f);
		mohoSpeedSmooth = Mathf.Lerp(mohoSpeedSmooth, raw, 1f - Mathf.Exp(-6f * dt));
		bool exact = false;
		DreadHunt hb = huntBulk.hunt;
		if (huntAi && huntSettings.NaturalTurn && hb != null && huntBulk.frame != null && (hb.Aware || bulkTurnHold || hb.Speed > 0.3f))
		{
			// the body takes the monster's own heading (HeadingMotion): the one it moves along, not one guessed from the last position change
			Vector3d hd;
			if (HuntHeadingDirection(huntBulk, groundPos, bulkEntityBody, hb.Heading, out hd))
			{
				mohoHeading = (Vector3)hd;
				exact = true;
			}
		}
		if (!exact && raw > 0.5f)
		{
			mohoHeading = delta.normalized;
		}
		// during the ambush the creature is under the ground: no steps, no motion state (the silence)
		bool moving = mohoSpeedSmooth > 1f && !WormAmbushActive;
		MohoState state = moving ? MohoState.Moving : ((Time.time % 16f) > 11f ? MohoState.Listening : MohoState.Idle);
		bulkMoho.Position = (Vector3)groundPos;
		bulkMoho.Up = up;
		bulkMoho.Heading = mohoHeading;
		bulkMoho.HeadingExact = exact;
		bulkMoho.TurnLean = mohoTurnLean;
		bulkMoho.LeanMaxDeg = mohoLeanMaxDeg;
		bulkMoho.Speed = moving ? mohoSpeedSmooth : 0f;
		bulkMoho.State = state;
		// It looks where it walks. Only a standing creature lets its head follow the craft, and slowly.
		bulkMoho.LookTarget = (!bulkMoho.ModelProfile && !moving && target != null && !bulkEntityStandingDown && (!huntAi || (huntBulk.hunt != null && huntBulk.hunt.HasSight))) ? (Vector3?)target.GetWorldPos3D() : null;
		CelestialBody body = bulkEntityBody;
		if (bulkMoho.GroundSampler == null && body != null)
		{
			bulkMoho.GroundSampler = delegate (Vector3 p)
			{
				return (Vector3)ProjectToGround((Vector3d)p, body);
			};
		}
		if (bulkWormSwarm != null)
		{
			bulkWormSwarm.SetGroundSampler(bulkMoho.GroundSampler);
			bulkWormSwarm.SetTarget(target != null ? target.GetWorldPos3D() : groundPos, chasing && !bulkEntityStandingDown && !bulkEntityRetreating);
		}
		MohoFeedAir(bulkMoho);
		bulkMoho.Step(dt);
	}
}
