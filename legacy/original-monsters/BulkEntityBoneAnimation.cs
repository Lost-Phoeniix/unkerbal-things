using System;
using System.Collections.Generic;
using UnityEngine;

public class BulkEntityBoneAnimation : MonoBehaviour
{
	private enum BoneKind
	{
		Other,
		UpperLeg,
		LowerLeg,
		Foot,
		UpperArm,
		LowerArm,
		Hand,
		Clavicle,
		Spine
	}

	private sealed class BonePose
	{
		public Transform transform;

		public Quaternion baseRotation;

		public Vector3 axis;

		public float phase;

		public float rate;

		public float amplitude;

		public BoneKind kind;

		public float side;
	}

	private readonly List<BonePose> poses = new List<BonePose>();

	private bool chasing;

	public void Configure(GameObject root)
	{
		if (root == null)
		{
			return;
		}
		HashSet<Transform> hashSet = new HashSet<Transform>();
		SkinnedMeshRenderer[] componentsInChildren = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			Transform[] bones = skinnedMeshRenderer.bones;
			foreach (Transform transform in bones)
			{
				if (transform != null && hashSet.Add(transform))
				{
					string text = transform.name.ToLowerInvariant();
					poses.Add(new BonePose
					{
						transform = transform,
						baseRotation = transform.localRotation,
						axis = UnityEngine.Random.onUnitSphere,
						phase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f),
						rate = UnityEngine.Random.Range(5.5f, 10f),
						amplitude = UnityEngine.Random.Range(2f, 5f),
						kind = GetBoneKind(text),
						side = ((text.Contains(".r") || text.Contains("right")) ? (-1f) : 1f)
					});
				}
			}
		}
	}

	public void SetChasing(bool value)
	{
		chasing = value;
	}

	private float gaitPhase;

	private float gaitAmount;

	private float gaitStamp = -100f;

	// Distance-driven leg cycle (set every frame by the motion code; falls back to the timed cycle when not refreshed).
	public void SetGait(float phase, float amount)
	{
		gaitPhase = phase;
		gaitAmount = amount;
		gaitStamp = Time.time;
	}

	private static BoneKind GetBoneKind(string name)
	{
		if (name.Contains("upper_leg"))
		{
			return BoneKind.UpperLeg;
		}
		if (name.Contains("low_leg") || name.Contains("lower_leg"))
		{
			return BoneKind.LowerLeg;
		}
		if (name.Contains("foot") || name.Contains("toe"))
		{
			return BoneKind.Foot;
		}
		if (name.Contains("upper_arm"))
		{
			return BoneKind.UpperArm;
		}
		if (name.Contains("bottom_arm") || name.Contains("lower_arm") || name.Contains("forearm"))
		{
			return BoneKind.LowerArm;
		}
		if (name.Contains("hand"))
		{
			return BoneKind.Hand;
		}
		if (name.Contains("clavicle") || name.Contains("shoulder"))
		{
			return BoneKind.Clavicle;
		}
		if (name.Contains("spine") || name.Contains("chest"))
		{
			return BoneKind.Spine;
		}
		return BoneKind.Other;
	}

	private void LateUpdate()
	{
		if (Time.timeScale <= 0f)
		{
			return;
		}
		float time = Time.time;
		bool gait = Time.time - gaitStamp < 0.25f;
		foreach (BonePose pose in poses)
		{
			if (pose.transform == null)
			{
				continue;
			}
			if (!chasing)
			{
				pose.transform.localRotation = Quaternion.Slerp(pose.transform.localRotation, pose.baseRotation, TimeWarp.deltaTime * 8f);
				continue;
			}
			float num = Mathf.Sin(gait ? gaitPhase : (time * 10f));
			float num2 = num * pose.side;
			Quaternion quaternion;
			switch (pose.kind)
			{
			case BoneKind.UpperLeg:
				quaternion = Quaternion.AngleAxis(31f * num2, Vector3.right);
				break;
			case BoneKind.LowerLeg:
				quaternion = Quaternion.AngleAxis(-25f * num2, Vector3.right);
				break;
			case BoneKind.Foot:
				quaternion = Quaternion.AngleAxis(12f * num2, Vector3.right);
				break;
			case BoneKind.Clavicle:
				quaternion = Quaternion.Euler(-22f, 0f, 7f * pose.side);
				break;
			case BoneKind.UpperArm:
				quaternion = Quaternion.Euler(-48f + 7f * num2, 0f, 11f * pose.side);
				break;
			case BoneKind.LowerArm:
				quaternion = Quaternion.Euler(-36f - 5f * num2, 0f, 4f * pose.side);
				break;
			case BoneKind.Hand:
				quaternion = Quaternion.Euler(-14f, 0f, 2f * pose.side);
				break;
			case BoneKind.Spine:
				quaternion = Quaternion.Euler(2.5f * num, 0f, 1.5f * num);
				break;
			default:
			{
				float angle = Mathf.Sin(time * pose.rate + pose.phase) * pose.amplitude;
				quaternion = Quaternion.AngleAxis(angle, pose.axis);
				break;
			}
			}
			pose.transform.localRotation = (gait ? Quaternion.Slerp(pose.baseRotation, pose.baseRotation * quaternion, gaitAmount) : (pose.baseRotation * quaternion));
		}
	}
}
