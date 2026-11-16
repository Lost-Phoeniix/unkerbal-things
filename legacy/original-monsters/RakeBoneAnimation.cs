using System;
using System.Collections.Generic;
using UnityEngine;

public class RakeBoneAnimation : MonoBehaviour
{
	private enum BoneKind
	{
		Other,
		Thigh,
		Shin,
		Foot,
		UpperArm,
		Forearm,
		Hand,
		Spine,
		Head
	}

	private sealed class BonePose
	{
		public Transform transform;

		public Quaternion baseRotation;

		public BoneKind kind;

		public float side;

		public float phase;
	}

	private readonly List<BonePose> poses = new List<BonePose>();

	private bool chasing;

	public void Configure(GameObject root)
	{
		HashSet<Transform> hashSet = new HashSet<Transform>();
		SkinnedMeshRenderer[] componentsInChildren = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			Transform[] bones = skinnedMeshRenderer.bones;
			foreach (Transform transform in bones)
			{
				if (!(transform == null) && hashSet.Add(transform))
				{
					string text = transform.name.ToLowerInvariant();
					poses.Add(new BonePose
					{
						transform = transform,
						baseRotation = transform.localRotation,
						kind = Classify(text),
						side = ((text.Contains(".r") || text.Contains("right")) ? (-1f) : 1f),
						phase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f)
					});
				}
			}
		}
	}

	public void SetChasing(bool value)
	{
		chasing = value;
	}

	private static BoneKind Classify(string name)
	{
		if (name.Contains("thigh"))
		{
			return BoneKind.Thigh;
		}
		if (name.Contains("shin"))
		{
			return BoneKind.Shin;
		}
		if (name.Contains("foot") || name.Contains("toe"))
		{
			return BoneKind.Foot;
		}
		if (name.Contains("upper_arm"))
		{
			return BoneKind.UpperArm;
		}
		if (name.Contains("forearm"))
		{
			return BoneKind.Forearm;
		}
		if (name.Contains("hand"))
		{
			return BoneKind.Hand;
		}
		if (name.Contains("spine"))
		{
			return BoneKind.Spine;
		}
		if (name.Contains("head"))
		{
			return BoneKind.Head;
		}
		return BoneKind.Other;
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

	private void LateUpdate()
	{
		float t = ((chasing && Time.timeScale > 0f) ? 1f : 0f);
		bool gait = Time.time - gaitStamp < 0.25f;
		if (gait)
		{
			t *= gaitAmount;
		}
		float num = Mathf.Sin(gait ? gaitPhase : (Time.time * 10f));
		foreach (BonePose pose in poses)
		{
			if (!(pose.transform == null))
			{
				float num2 = num * pose.side;
				Quaternion quaternion = pose.kind switch
				{
					BoneKind.Thigh => Quaternion.AngleAxis(35f * num2, Vector3.right), 
					BoneKind.Shin => Quaternion.AngleAxis(-30f * num2, Vector3.right), 
					BoneKind.Foot => Quaternion.AngleAxis(14f * num2, Vector3.right), 
					BoneKind.UpperArm => Quaternion.Euler(-52f + 8f * num2, 0f, 10f * pose.side), 
					BoneKind.Forearm => Quaternion.Euler(-36f - 5f * num2, 0f, 4f * pose.side), 
					BoneKind.Hand => Quaternion.Euler(-16f, 0f, 0f), 
					BoneKind.Spine => Quaternion.Euler(5f * num, 0f, 2f * num), 
					BoneKind.Head => Quaternion.Euler(-8f, 0f, 0f), 
					_ => Quaternion.AngleAxis(2.5f * Mathf.Sin(Time.time * 7f + pose.phase), Vector3.up), 
				};
				pose.transform.localRotation = Quaternion.Slerp(pose.baseRotation, pose.baseRotation * quaternion, t);
			}
		}
	}
}
