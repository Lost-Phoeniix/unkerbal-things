using System;
using System.Collections.Generic;
using UnityEngine;

public class RakeEntityAnimation : MonoBehaviour
{
	private sealed class PiecePose
	{
		public Transform transform;

		public Vector3 basePosition;

		public Quaternion baseRotation;

		public string name;

		public float phase;
	}

	private readonly List<PiecePose> pieces = new List<PiecePose>();

	private bool chasing;

	public void Configure(GameObject root)
	{
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			Transform transform = renderer.transform;
			pieces.Add(new PiecePose
			{
				transform = transform,
				basePosition = transform.localPosition,
				baseRotation = transform.localRotation,
				name = transform.name.ToLowerInvariant(),
				phase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f)
			});
		}
	}

	public void SetChasing(bool value)
	{
		chasing = value;
	}

	private void LateUpdate()
	{
		float num = ((chasing && Time.timeScale > 0f) ? 1f : 0f);
		float time = Time.time;
		foreach (PiecePose piece in pieces)
		{
			if (!(piece.transform == null))
			{
				float num2 = Mathf.Sin(time * 10f + piece.phase);
				Vector3 basePosition = piece.basePosition;
				Quaternion baseRotation = piece.baseRotation;
				if (piece.name.Contains("lowerteeth"))
				{
					basePosition += Vector3.down * (0.1f + 0.06f * Mathf.Abs(num2)) * num;
					baseRotation *= Quaternion.Euler(8f * Mathf.Abs(num2) * num, 0f, 0f);
				}
				else if (piece.name.Contains("upperteeth"))
				{
					baseRotation *= Quaternion.Euler(-3f * Mathf.Abs(num2) * num, 0f, 0f);
				}
				else if (piece.name.Contains("eyes"))
				{
					basePosition += new Vector3(0.012f * num2, 0.018f * Mathf.Sin(time * 17f + piece.phase), 0f) * num;
				}
				else
				{
					basePosition += new Vector3(0f, -0.045f * Mathf.Abs(num2), 0.055f * num2) * num;
					baseRotation *= Quaternion.Euler(2f * num2 * num, 0f, 0f);
				}
				piece.transform.localPosition = basePosition;
				piece.transform.localRotation = baseRotation;
			}
		}
	}
}
