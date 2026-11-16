using System;
using System.Collections.Generic;
using UnityEngine;

public class LaytheCreatureBoneAnimation : MonoBehaviour
{
	private sealed class Pose
	{
		public Transform bone;

		public Quaternion baseRotation;

		public int index;
	}

	private readonly List<Pose> poses = new List<Pose>();

	private bool chasing;

	private bool roaming;

	public void Configure(GameObject root)
	{
		HashSet<Transform> hashSet = new HashSet<Transform>();
		SkinnedMeshRenderer[] componentsInChildren = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			Transform[] bones = skinnedMeshRenderer.bones;
			foreach (Transform transform in bones)
			{
				if (transform != null && hashSet.Add(transform))
				{
					poses.Add(new Pose
					{
						bone = transform,
						baseRotation = transform.localRotation,
						index = poses.Count
					});
				}
			}
		}
	}

	public void SetState(bool isChasing, bool isRoaming)
	{
		chasing = isChasing;
		roaming = isRoaming;
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
		float t = (chasing ? 1f : (roaming ? 0.22f : 0f));
		float num = (chasing ? 11f : 2.2f);
		bool gait = chasing && Time.time - gaitStamp < 0.25f;
		if (gait)
		{
			t *= gaitAmount;
		}
		foreach (Pose pose in poses)
		{
			if (!(pose.bone == null))
			{
				if (pose.index < 4)
				{
					pose.bone.localRotation = pose.baseRotation;
					continue;
				}
				int num2 = (pose.index - 4) / 4;
				int num3 = (pose.index - 4) % 4;
				float num4 = (float)num2 * ((float)Math.PI / 2f);
				float cycle = (gait ? gaitPhase : (Time.time * num));
				float num5 = Mathf.Sin(cycle + num4);
				float num6 = Mathf.Sin(cycle + num4 + 0.8f);
				Quaternion quaternion = Quaternion.Euler(num3 switch
				{
					2 => 15f, 
					1 => -21f, 
					0 => 28f, 
					_ => -9f, 
				} * num5, 2.5f * num6, 1.5f * num5);
				pose.bone.localRotation = Quaternion.Slerp(pose.baseRotation, pose.baseRotation * quaternion, t);
			}
		}
	}
}
