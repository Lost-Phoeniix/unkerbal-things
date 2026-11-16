using KrakensKreations.Dread;
using System;
using UnityEngine;

// The procedural Moho/Eve creature (MohoRig.cs, MohoCreature.cs, MohoSkinProcedural.cs) wired in as the Bulk monster's visual.
// mohoCreature=false keeps the imported model and its old animation. The tracker only feeds the creature position, heading, speed, look target
// and state; the creature owns body orientation, feet, head and mesh. Heading/look target come from measured motion until the ai unit
// exposes its own (see FeedMoho).
public partial class EntityTracker
{
	private static bool mohoCreature = true;

	private static bool mohoModel = true;
	private bool mohoModelLogged;
	private MohoCreature bulkMoho;
	private Vector3d mohoLastPos;
	private bool mohoHasLast;
	private float mohoSpeedSmooth;
	private Vector3 mohoHeading = Vector3.forward;
	private EventData<Vector3d, Vector3d>.OnEvent mohoShiftHandler;

	// Footfall hook for later step sounds: (foot 0 LF / 1 RF / 2 LH / 3 RH, world position, strength 0..1). DreadSteps is not changed.
	internal event Action<int, Vector3, float> MohoFootFall;

	private static bool mohoTurnLean = true;
	private static float mohoLeanMaxDeg = 14f;
	private bool bulkTurnHold;     // the Moho creature is running away under HeadingMotion: its heading is the brain's, not read back from the body

	private static void LoadMohoSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "mohoTurnLean", ref mohoTurnLean);
		ReadFloatSetting(node, "mohoTurnLeanMaxDeg", ref mohoLeanMaxDeg);
		mohoLeanMaxDeg = Mathf.Clamp(mohoLeanMaxDeg, 0f, 35f);
		if (node != null && node.HasValue("mohoCreature"))
		{
			bool.TryParse(node.GetValue("mohoCreature"), out mohoCreature);
		}
	}

	private GameObject CreateMohoEntityVisual()
	{
		GameObject root = new GameObject("EntityTracker_MohoEveEntity");
		root.layer = 0;
		MohoCreature creature = root.AddComponent<MohoCreature>();
		string modelFailure = null;
		MohoBoneSkin model = null;
		if (mohoModel)
		{
			string bundle = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Moho", "moho.bundle");
			model = MohoModelLoader.Create(creature, bundle, out modelFailure);
		}
		if (model != null)
		{
			creature.Skin = model;
			if (!mohoModelLogged)
			{
				mohoModelLogged = true;
				Debug.Log("[KK Moho] skinned model loaded: " + model.VertexCount + " vertices, " + model.TriangleCount + " triangles, shader " + model.ShaderName);
			}
		}
		else
		{
			if (mohoModel && !mohoModelLogged)
			{
				mohoModelLogged = true;
				Debug.Log("[KK Moho] model not used (" + modelFailure + "): procedural skin");
			}
			MohoSkinProcedural skin = new MohoSkinProcedural(root.transform, null);
			creature.Skin = skin;
		}
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
	}

	private void ReleaseMoho()
	{
		if (mohoShiftHandler != null)
		{
			GameEvents.onFloatingOriginShift.Remove(mohoShiftHandler);
			mohoShiftHandler = null;
		}
		bulkMoho = null;
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
		bool moving = mohoSpeedSmooth > 1f;
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
		bulkMoho.LookTarget = (!bulkMoho.ModelProfile && !moving && target != null && !bulkEntityStandingDown && (!huntAi || huntBulk.hunt.HasSight)) ? (Vector3?)target.GetWorldPos3D() : null;
		CelestialBody body = bulkEntityBody;
		if (bulkMoho.GroundSampler == null && body != null)
		{
			bulkMoho.GroundSampler = delegate (Vector3 p)
			{
				return (Vector3)ProjectToGround((Vector3d)p, body);
			};
		}
		MohoFeedAir(bulkMoho);
		bulkMoho.Step(dt);
	}
}
