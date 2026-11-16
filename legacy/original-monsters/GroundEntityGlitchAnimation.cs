using System;
using System.Collections.Generic;
using UnityEngine;

public class GroundEntityGlitchAnimation : MonoBehaviour
{
	private sealed class BonePose
	{
		public Transform transform;

		public Quaternion baseRotation;

		public Vector3 baseScale;

		public Vector3 basePosition;

		public Vector3 positionOffset;

		public Vector3 rotationAxis;

		public float phase;

		public float rate;
	}

	private readonly List<BonePose> poses = new List<BonePose>();

	private bool chasing;

	private bool roaming;

	public void SetChasing(bool value)
	{
		chasing = value;
	}

	public void SetRoaming(bool value)
	{
		roaming = value;
	}

	public void Configure(List<Transform> bones)
	{
		foreach (Transform bone in bones)
		{
			BonePose bonePose = new BonePose();
			bonePose.transform = bone;
			bonePose.baseRotation = bone.localRotation;
			bonePose.baseScale = bone.localScale;
			bonePose.basePosition = bone.localPosition;
			bonePose.rotationAxis = UnityEngine.Random.onUnitSphere;
			bonePose.phase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
			bonePose.rate = UnityEngine.Random.Range(2f, 5f);
			string text = bone.name.ToLowerInvariant();
			Vector3 vector = ((bonePose.basePosition.sqrMagnitude > 0.0001f) ? bonePose.basePosition.normalized : Vector3.right);
			if (text.Contains("head"))
			{
				bonePose.positionOffset = new Vector3(0f, 1.35f, 0f);
			}
			else if (text.Contains("shoulder") || text.Contains("clavicle"))
			{
				bonePose.positionOffset = vector * 0.65f;
			}
			else if (text.Contains("upperarm") || text.Contains("upper_arm") || text.Contains("forearm") || text.Contains("lowerarm") || text.Contains("lower_arm") || text.Contains("bottom_arm"))
			{
				bonePose.positionOffset = vector * 0.65f;
			}
			else if (text.Contains("thigh") || text.Contains("upperleg") || text.Contains("upper_leg") || text.Contains("calf") || text.Contains("lowerleg") || text.Contains("lower_leg"))
			{
				bonePose.positionOffset = vector * 0.65f;
			}
			else if (text.Contains("hand") || text.Contains("foot") || text.Contains("toe"))
			{
				bonePose.positionOffset = vector * 0.45f;
			}
			else if (text.Contains("spine") || text.Contains("neck"))
			{
				bonePose.positionOffset = new Vector3(0f, 0.18f, 0f);
			}
			bone.localScale = bonePose.baseScale;
			poses.Add(bonePose);
		}
	}

	private void Update()
	{
		if (Time.timeScale <= 0f)
		{
			return;
		}
		float time = Time.time;
		foreach (BonePose pose in poses)
		{
			if (!(pose.transform == null))
			{
				float num = (chasing ? 4.5f : (roaming ? 1.35f : 1f));
				float num2 = (chasing ? 5f : (roaming ? 1.8f : 1f));
				Vector3 vector = (chasing ? (UnityEngine.Random.insideUnitSphere * 0.12f) : (roaming ? (UnityEngine.Random.insideUnitSphere * 0.015f) : Vector3.zero));
				pose.transform.localPosition = pose.basePosition + pose.positionOffset + vector;
				float angle = (Mathf.Sin(time * pose.rate * num + pose.phase) * 14f + Mathf.Sin(time * (pose.rate * 0.37f) * num + pose.phase * 2f) * 8f) * num2;
				pose.transform.localRotation = pose.baseRotation * Quaternion.AngleAxis(angle, pose.rotationAxis);
			}
		}
	}
}
