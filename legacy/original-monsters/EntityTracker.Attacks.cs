using System;
using System.Collections.Generic;
using UnityEngine;

// Windup / Lunge / Tear-apart sequence that replaces the instant explosion on a catch.
public partial class EntityTracker
{
	private enum AttackKind
	{
		Mimic,
		Bulk,
		Laythe,
		Rake,
		Sea,
		Bloop
	}

	private enum AttackPhase
	{
		Windup,
		Lunge,
		Tear
	}

	private enum AttackStepResult
	{
		Continue,
		Cancel,
		Finish
	}

	// One instance per catch. Positions are body-relative so the floating origin cannot break them.
	private sealed class AttackSequence
	{
		public AttackKind kind;
		public AttackPhase phase;
		public Vessel vessel;
		public Guid vesselId;
		public GameObject visual;
		public CelestialBody body;
		public bool maim;
		public int partCount = -1;
		public float dodgeDistance;
		public float heightLimit;
		public float timer;
		public float windupDuration;
		public float shakeTimer;
		public float stopOffset;
		public Vector3d holdRel;
		public Vector3d tearOffset;
		public Vector3d lastVesselRel;
		public List<Part> order;
		public int nextPart;
		public float tearStep;
	}

	private const float AttackLungeSeconds = 0.3f;
	private const float AttackMaxStep = 0.1f;

	// Settings (see settings.cfg).
	private static bool attackSequences = true;
	private static float attackTearSeconds = 3f;

	private static bool attackCameraShake = true;

	private readonly List<AttackSequence> attackSequenceList = new List<AttackSequence>();
	private readonly Dictionary<Camera, Quaternion> attackCameraSaved = new Dictionary<Camera, Quaternion>();
	private float attackCameraAmplitude;
	private bool attackCameraHooked;
	private int attackCameraFrame = -1;
	private Quaternion attackCameraOffset = Quaternion.identity;

	private static void LoadAttackSettings(ConfigNode node)
	{
		if (node != null && node.HasValue("attackSequences"))
		{
			bool.TryParse(node.GetValue("attackSequences"), out attackSequences);
		}
		if (node != null && node.HasValue("attackCameraShake"))
		{
			bool.TryParse(node.GetValue("attackCameraShake"), out attackCameraShake);
		}
		ReadFloatSetting(node, "attackTearSeconds", ref attackTearSeconds);
		attackTearSeconds = Mathf.Clamp(attackTearSeconds, 0.5f, 20f);
	}

	private bool IsAttackSequenceRunning(AttackKind kind)
	{
		for (int i = 0; i < attackSequenceList.Count; i++)
		{
			if (attackSequenceList[i].kind == kind)
			{
				return true;
			}
		}
		return false;
	}

	private void CancelAllAttackSequences()
	{
		attackSequenceList.Clear();
		ShutdownAttackCameraShake();
	}

	// Mimic strike on a vessel over 10 parts: tear 3-5 parts, then the existing stand-down flow.
	private bool BeginMimicMaimSequence(Vessel vessel)
	{
		return BeginAttackSequence(AttackKind.Mimic, vessel, groundEntityObject, groundEntityBody, 2f, UnityEngine.Random.Range(3, 6), maim: true);
	}

	// Returns true if a sequence took over this catch. False means: use the old instant catch.
	private bool BeginAttackSequence(AttackKind kind, Vessel vessel, GameObject visual, CelestialBody body, float catchDistance, int partCount, bool maim = false)
	{
		if (!attackSequences || vessel == null || visual == null || body == null || !HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f)
		{
			return false;
		}
		if (IsAttackSequenceRunning(kind))
		{
			return true;
		}
		Vector3d visualPos = visual.transform.position;
		double startDistance = (vessel.GetWorldPos3D() - visualPos).magnitude;
		AttackSequence seq = new AttackSequence();
		seq.kind = kind;
		seq.phase = AttackPhase.Windup;
		seq.vessel = vessel;
		seq.vesselId = vessel.id;
		seq.visual = visual;
		seq.body = body;
		seq.maim = maim;
		seq.partCount = partCount;
		seq.dodgeDistance = 2f * Mathf.Max(catchDistance, (float)startDistance);
		seq.stopOffset = Mathf.Min(6f, (float)startDistance * 0.5f);
		seq.windupDuration = UnityEngine.Random.Range(0.6f, 1.0f);
		seq.holdRel = visualPos - body.position;
		if (kind != AttackKind.Sea && kind != AttackKind.Bloop)
		{
			float groundLimit = hoverAttacks ? Mathf.Max(hoverAttackMaxHeight, 10f) : 10f;
			seq.heightLimit = Mathf.Max(groundLimit, vessel.heightFromTerrain + 5f);
		}
		PlayAttackWindupAudio(kind);
		AttackShake();
		seq.shakeTimer = 0.45f;
		attackSequenceList.Add(seq);
		return true;
	}

	private void PlayAttackWindupAudio(AttackKind kind)
	{
		AudioSource audioSource = null;
		AudioSource audioSource2 = null;
		switch (kind)
		{
		case AttackKind.Mimic:
			audioSource = groundScreamAudio;
			audioSource2 = groundFootstepsAudio;
			break;
		case AttackKind.Bulk:
			audioSource2 = bulkFootstepsAudio;
			break;
		case AttackKind.Laythe:
			audioSource = laytheCreatureScreamAudio;
			audioSource2 = laytheCreatureFootstepsAudio;
			break;
		case AttackKind.Rake:
			audioSource = rakeChaseAudio;
			break;
		case AttackKind.Sea:
			audioSource = seaGrowlAudio;
			break;
		case AttackKind.Bloop:
			audioSource = bloopBurrowAudio;
			break;
		}
		if (audioSource2 != null && audioSource2.isPlaying)
		{
			audioSource2.Pause();
		}
		if (audioSource != null && audioSource.clip != null && !audioSource.isPlaying)
		{
			audioSource.Play();
		}
	}

	// Reuses the mod's window jolt (see UpdateProximityScreenShake); never retriggered while a jolt is running.
	private void AttackShake(float strength = 0.5f)
	{
		if (joltFramesRemaining <= 0)
		{
			TriggerWindowJolt();
		}
		if (attackCameraShake)
		{
			attackCameraAmplitude = Mathf.Max(attackCameraAmplitude, Mathf.Lerp(0.3f, 1.8f, Mathf.Clamp01(strength)));
			if (!attackCameraHooked)
			{
				Camera.onPreCull += OnAttackCameraPreCull;
				Camera.onPostRender += OnAttackCameraPostRender;
				attackCameraHooked = true;
			}
		}
	}

	// Camera shake: a small random rotation applied to the flight cameras only for the duration of their render
	// (onPreCull), then removed again (onPostRender). Nothing the game's own camera update writes is ever changed.
	private void UpdateAttackCameraShake()
	{
		if (attackCameraAmplitude <= 0f)
		{
			return;
		}
		attackCameraAmplitude = Mathf.MoveTowards(attackCameraAmplitude, 0f, 7f * Time.unscaledDeltaTime);
	}

	private bool IsAttackShakeCameraActive()
	{
		return attackCameraShake && attackCameraAmplitude > 0f && HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled && FlightCamera.fetch != null && CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight;
	}

	private void OnAttackCameraPreCull(Camera cam)
	{
		if (cam == null || attackCameraSaved.ContainsKey(cam) || !IsAttackShakeCameraActive())
		{
			return;
		}
		Transform root = FlightCamera.fetch.transform;
		if (cam.transform != root && !cam.transform.IsChildOf(root))
		{
			return;
		}
		if (attackCameraFrame != Time.frameCount)
		{
			attackCameraFrame = Time.frameCount;
			attackCameraOffset = Quaternion.Euler(UnityEngine.Random.Range(-1f, 1f) * attackCameraAmplitude, UnityEngine.Random.Range(-1f, 1f) * attackCameraAmplitude, UnityEngine.Random.Range(-1f, 1f) * attackCameraAmplitude * 0.5f);
		}
		attackCameraSaved[cam] = cam.transform.rotation;
		cam.transform.rotation = cam.transform.rotation * attackCameraOffset;
	}

	private void OnAttackCameraPostRender(Camera cam)
	{
		if (cam != null && attackCameraSaved.TryGetValue(cam, out var value))
		{
			cam.transform.rotation = value;
		}
		if (cam != null)
		{
			attackCameraSaved.Remove(cam);
		}
	}

	private void ShutdownAttackCameraShake()
	{
		if (attackCameraHooked)
		{
			Camera.onPreCull -= OnAttackCameraPreCull;
			Camera.onPostRender -= OnAttackCameraPostRender;
			attackCameraHooked = false;
		}
		foreach (KeyValuePair<Camera, Quaternion> item in attackCameraSaved)
		{
			if (item.Key != null)
			{
				item.Key.transform.rotation = item.Value;
			}
		}
		attackCameraSaved.Clear();
		attackCameraAmplitude = 0f;
	}

	private void UpdateAttackSequences()
	{
		UpdateAttackCameraShake();
		if (attackSequenceList.Count == 0)
		{
			return;
		}
		if (!HighLogic.LoadedSceneIsFlight)
		{
			attackSequenceList.Clear();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			return;
		}
		float dt = Mathf.Clamp(TimeWarp.deltaTime, 0f, AttackMaxStep);
		for (int num = attackSequenceList.Count - 1; num >= 0; num--)
		{
			if (num >= attackSequenceList.Count)
			{
				continue;
			}
			AttackSequence seq = attackSequenceList[num];
			AttackStepResult attackStepResult = StepAttackSequence(seq, dt);
			if (attackStepResult != AttackStepResult.Continue)
			{
				attackSequenceList.Remove(seq);
				if (attackStepResult == AttackStepResult.Finish)
				{
					FinishAttackSequence(seq);
				}
			}
		}
	}

	private bool IsAttackTargetStillValid(AttackSequence seq)
	{
		Vessel vessel = seq.vessel;
		if (seq.heightLimit > 0f && vessel.heightFromTerrain > seq.heightLimit)
		{
			return false;
		}
		if (seq.kind == AttackKind.Sea)
		{
			return CanSeaEntityTarget(vessel);
		}
		if (seq.kind == AttackKind.Bloop)
		{
			return CanBloopTarget(vessel);
		}
		return true;
	}

	private AttackStepResult StepAttackSequence(AttackSequence seq, float dt)
	{
		if (seq.visual == null || seq.body == null)
		{
			return AttackStepResult.Cancel;
		}
		bool vesselGone = seq.vessel == null || seq.vessel.state == Vessel.State.DEAD || seq.vessel.mainBody != seq.body;
		bool keepTearing = vesselGone && seq.phase == AttackPhase.Tear && seq.partCount < 0 && seq.order != null && seq.nextPart < seq.order.Count;
		if (vesselGone && !keepTearing)
		{
			return (seq.phase == AttackPhase.Windup) ? AttackStepResult.Cancel : AttackStepResult.Finish;
		}
		Transform transform = seq.visual.transform;
		Vector3d vesselRel = keepTearing ? seq.lastVesselRel : (seq.vessel.GetWorldPos3D() - seq.body.position);
		seq.lastVesselRel = vesselRel;
		Vector3d vesselPos = seq.body.position + vesselRel;
		seq.timer += dt;
		if (seq.phase == AttackPhase.Windup)
		{
			transform.position = (Vector3)(seq.body.position + seq.holdRel);
			Vector3d monsterPos = transform.position;
			if ((vesselPos - monsterPos).magnitude > (double)seq.dodgeDistance || !IsAttackTargetStillValid(seq))
			{
				return AttackStepResult.Cancel;
			}
			AimAttackVisual(seq, vesselPos, 540f * dt);
			float t = Mathf.Clamp01(seq.timer / seq.windupDuration);
			seq.shakeTimer -= dt;
			if (seq.shakeTimer <= 0f)
			{
				AttackShake(0.1f + 0.5f * t);
				seq.shakeTimer = Mathf.Lerp(0.45f, 0.12f, t);
			}
			if (seq.timer >= seq.windupDuration)
			{
				seq.phase = AttackPhase.Lunge;
				seq.timer = 0f;
			}
			return AttackStepResult.Continue;
		}
		if (seq.phase == AttackPhase.Lunge)
		{
			Vector3d toMonster = seq.holdRel - vesselRel;
			Vector3d dir = (toMonster.sqrMagnitude > 0.01) ? toMonster.normalized : (seq.holdRel.sqrMagnitude > 0.01 ? seq.holdRel.normalized : Vector3d.up);
			Vector3d target = vesselRel + dir * (double)seq.stopOffset;
			float num = Mathf.Clamp01(seq.timer / AttackLungeSeconds);
			double e = (double)(num * num);
			Vector3d rel = seq.holdRel + (target - seq.holdRel) * e;
			transform.position = (Vector3)(seq.body.position + rel);
			AimAttackVisual(seq, vesselPos, 1440f * Mathf.Max(dt, 0.02f));
			if (seq.timer >= AttackLungeSeconds)
			{
				seq.phase = AttackPhase.Tear;
				seq.timer = 0f;
				seq.tearOffset = target - vesselRel;
				BuildAttackTearOrder(seq);
				seq.shakeTimer = 0.3f;
				AttackShake(0.9f);
			}
			return AttackStepResult.Continue;
		}
		// Tear-apart.
		Vector3 jitter = UnityEngine.Random.insideUnitSphere * 0.25f;
		transform.position = (Vector3)(seq.body.position + vesselRel + seq.tearOffset) + jitter;
		AimAttackVisual(seq, vesselPos, 720f * Mathf.Max(dt, 0.02f));
		seq.shakeTimer -= dt;
		if (seq.shakeTimer <= 0f)
		{
			AttackShake(0.5f);
			seq.shakeTimer = 0.3f;
		}
		int count = seq.order.Count;
		int guard = 0;
		while (seq.nextPart < count && seq.timer >= (float)(seq.nextPart + 1) * seq.tearStep && guard++ < 25)
		{
			Part part = seq.order[seq.nextPart++];
			// Full catch: every captured part is destroyed even if an explosion separated it from the vessel.
			if (!debugImmortality && part != null && (seq.partCount < 0 || part.vessel == seq.vessel))
			{
				part.explode();
			}
			AttackShake(0.6f + 0.4f * (float)seq.nextPart / (float)Mathf.Max(1, count));
			seq.shakeTimer = 0.3f;
			if (seq.partCount >= 0 && (seq.vessel == null || seq.vessel.state == Vessel.State.DEAD))
			{
				break;
			}
		}
		if (seq.nextPart >= count && seq.timer >= ((count > 0) ? attackTearSeconds : 0.5f))
		{
			return AttackStepResult.Finish;
		}
		return AttackStepResult.Continue;
	}

	private void AimAttackVisual(AttackSequence seq, Vector3d vesselPos, float maxDegrees)
	{
		Transform transform = seq.visual.transform;
		Vector3d visualPos = transform.position;
		Vector3 up = (Vector3)(visualPos - seq.body.position).normalized;
		Vector3 to = (Vector3)(vesselPos - visualPos);
		to -= up * Vector3.Dot(to, up);
		if (to.sqrMagnitude < 0.01f)
		{
			return;
		}
		Quaternion quaternion = Quaternion.LookRotation(to.normalized, up);
		Quaternion b;
		switch (seq.kind)
		{
		case AttackKind.Bulk:
			b = quaternion * Quaternion.Euler(0f, 180f, 0f);
			break;
		case AttackKind.Laythe:
			b = quaternion * LaytheCreatureModelAxisCorrection;
			break;
		case AttackKind.Rake:
			b = quaternion * Quaternion.Euler(0f, 180f, 0f) * RakeModelAxisCorrection;
			break;
		case AttackKind.Sea:
			b = quaternion * Quaternion.Euler(0f, 180f, 0f) * SeaModelAxisCorrection;
			break;
		case AttackKind.Bloop:
			b = quaternion * Quaternion.Euler(0f, 180f, 0f) * BloopModelAxisCorrection * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, -90f);
			break;
		default:
			b = quaternion;
			break;
		}
		transform.rotation = Quaternion.RotateTowards(transform.rotation, b, maxDegrees);
	}

	// Outermost first (children before parents), crew parts and their ancestors kept for last, root last.
	private void BuildAttackTearOrder(AttackSequence seq)
	{
		List<Part> list = new List<Part>();
		Vessel vessel = seq.vessel;
		if (vessel != null && vessel.parts != null)
		{
			list.AddRange(vessel.parts);
		}
		list.RemoveAll((Part p) => p == null);
		Dictionary<Part, int> depth = new Dictionary<Part, int>();
		HashSet<Part> protectedParts = new HashSet<Part>();
		foreach (Part item in list)
		{
			int d = 0;
			Part parent = item.parent;
			while (parent != null && d < 2000)
			{
				d++;
				parent = parent.parent;
			}
			depth[item] = d;
			if (item.protoModuleCrew != null && item.protoModuleCrew.Count > 0)
			{
				Part cursor = item;
				int guard = 0;
				while (cursor != null && guard++ < 2000)
				{
					protectedParts.Add(cursor);
					cursor = cursor.parent;
				}
			}
		}
		List<Part> first = new List<Part>();
		List<Part> second = new List<Part>();
		foreach (Part item2 in list)
		{
			if (protectedParts.Contains(item2))
			{
				second.Add(item2);
			}
			else
			{
				first.Add(item2);
			}
		}
		Comparison<Part> comparison = (Part a, Part b) => depth[b].CompareTo(depth[a]);
		first.Sort(comparison);
		second.Sort(comparison);
		first.AddRange(second);
		if (seq.partCount >= 0 && seq.partCount < first.Count)
		{
			first.RemoveRange(seq.partCount, first.Count - seq.partCount);
		}
		seq.order = first;
		seq.nextPart = 0;
		seq.tearStep = attackTearSeconds / (float)Mathf.Max(1, first.Count);
	}

	// Runs the same end-of-catch flow as the old instant catch, minus the destruction already done by the tear-apart.
	private void FinishAttackSequence(AttackSequence seq)
	{
		Vessel vessel = seq.vessel;
		if (seq.partCount < 0 && !debugImmortality && seq.order != null)
		{
			while (seq.nextPart < seq.order.Count)
			{
				Part part = seq.order[seq.nextPart++];
				if (part != null)
				{
					part.explode();
				}
			}
		}
		if (seq.partCount < 0 && !debugImmortality && vessel != null && vessel.state != Vessel.State.DEAD && vessel.parts != null && vessel.parts.Count > 0)
		{
			DestroyCaughtVessel(vessel);
		}
		switch (seq.kind)
		{
		case AttackKind.Mimic:
			if (!groundEntityActive || groundEntityObject != seq.visual)
			{
				return;
			}
			if (seq.maim)
			{
				if (!debugImmortality)
				{
					ScreenMessages.PostScreenMessage("The Mimic tears into your vessel and falls back.", 5f, ScreenMessageStyle.UPPER_CENTER);
				}
				groundEntityStandingDown = true;
				groundEntityWithdrawalTimer = 3f;
				groundEntityStatus = string.Concat(groundVariant, ": STANDING STILL");
				return;
			}
			groundSeenVesselIds.Remove(seq.vesselId);
			if (debugImmortality)
			{
				groundEntityStandingDown = true;
				groundEntityWithdrawalTimer = 3f;
			}
			groundEntityStatus = "SEEKING NEXT TARGET";
			break;
		case AttackKind.Bulk:
			if (!bulkEntityActive || bulkEntityObject != seq.visual)
			{
				return;
			}
			bulkSeenVesselIds.Remove(seq.vesselId);
			if (debugImmortality)
			{
				bulkEntityStandingDown = true;
				bulkWithdrawalTimer = 3f;
			}
			bulkEntityStatus = "SEEKING NEXT TARGET";
			break;
		case AttackKind.Laythe:
			if (!laytheCreatureActive || laytheCreatureObject != seq.visual)
			{
				return;
			}
			laytheSeenVesselIds.Remove(seq.vesselId);
			if (debugImmortality)
			{
				DespawnLaytheCreature();
				return;
			}
			laytheCreatureStatus = "SEEKING NEXT TARGET";
			break;
		case AttackKind.Rake:
			if (rakeEntityActive && rakeEntityObject == seq.visual)
			{
				DespawnRakeEntity();
			}
			break;
		case AttackKind.Sea:
			if (seaEntityActive && seaEntityObject == seq.visual)
			{
				DespawnSeaEntity();
			}
			break;
		case AttackKind.Bloop:
			if (bloopEntityActive && bloopEntityObject == seq.visual)
			{
				DespawnBloopEntity();
			}
			break;
		}
	}
}
