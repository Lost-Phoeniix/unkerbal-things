using UnityEngine;

public class MimicImportedAnimation : MonoBehaviour
{
	private enum BakedState
	{
		Idle,
		Aggro,
		Chase
	}

	private Animation animationComponent;

	private AnimationState idle;

	private AnimationState aggro;

	private AnimationState chase;

	private bool aggroStarted;

	private bool fallbackAggro;

	private float fallbackAggroEndsAt;

	private bool chasing;

	private GameObject[] bakedIdleFrames;

	private GameObject[] bakedAggroFrames;

	private GameObject[] bakedChaseFrames;

	private Renderer[] baseRenderers;

	private int bakedFrame;

	private float nextBakedFrameAt;

	private BakedState bakedState;

	public bool HasAggroClip => (bakedAggroFrames != null && bakedAggroFrames.Length > 0) || aggro != null || fallbackAggro;

	public bool IsAggroComplete
	{
		get
		{
			if (!aggroStarted)
			{
				return true;
			}
			if (fallbackAggro)
			{
				return Time.time >= fallbackAggroEndsAt;
			}
			return animationComponent == null || aggro == null || !animationComponent.IsPlaying(aggro.name);
		}
	}

	public void Configure(Animation source)
	{
		animationComponent = source;
		if (animationComponent == null)
		{
			fallbackAggro = true;
			return;
		}
		animationComponent.enabled = true;
		idle = animationComponent["Mimic_Idle"];
		aggro = animationComponent["Mimic_Aggro"];
		chase = animationComponent["Mimic_Chase"];
		if (idle != null)
		{
			idle.wrapMode = WrapMode.Loop;
			animationComponent.Play(idle.name);
		}
		if (aggro != null)
		{
			aggro.wrapMode = WrapMode.Once;
		}
		if (chase != null)
		{
			chase.wrapMode = WrapMode.Loop;
		}
	}

	public void ConfigureBakedAnimations(GameObject[] idleFrames, GameObject[] aggroFrames, GameObject[] chaseFrames, Renderer[] originalRenderers)
	{
		bakedIdleFrames = idleFrames;
		bakedAggroFrames = aggroFrames;
		bakedChaseFrames = chaseFrames;
		baseRenderers = originalRenderers;
		if (baseRenderers != null)
		{
			Renderer[] array = baseRenderers;
			foreach (Renderer renderer in array)
			{
				if (renderer != null)
				{
					renderer.enabled = false;
				}
			}
		}
		fallbackAggro = bakedAggroFrames != null && bakedAggroFrames.Length > 0;
		bakedState = BakedState.Idle;
		bakedFrame = 0;
		ShowBakedFrame(bakedIdleFrames, 0);
		nextBakedFrameAt = Time.time + 0.25f;
	}

	private void HideAllBakedFrames()
	{
		HideFrames(bakedIdleFrames);
		HideFrames(bakedAggroFrames);
		HideFrames(bakedChaseFrames);
	}

	private static void HideFrames(GameObject[] frames)
	{
		if (frames == null)
		{
			return;
		}
		foreach (GameObject gameObject in frames)
		{
			if (gameObject != null)
			{
				gameObject.SetActive(value: false);
			}
		}
	}

	private void ShowBakedFrame(GameObject[] frames, int index)
	{
		HideAllBakedFrames();
		if (frames != null && frames.Length != 0)
		{
			int num = Mathf.Clamp(index, 0, frames.Length - 1);
			if (frames[num] != null)
			{
				frames[num].SetActive(value: true);
			}
		}
	}

	public void BeginAggro()
	{
		aggroStarted = true;
		chasing = false;
		if (fallbackAggro)
		{
			fallbackAggroEndsAt = Time.time + 3.5f;
			bakedState = BakedState.Aggro;
			bakedFrame = 0;
			ShowBakedFrame(bakedAggroFrames, bakedFrame);
			nextBakedFrameAt = Time.time + 3.5f / (float)Mathf.Max(1, bakedAggroFrames.Length);
		}
		else if (!(animationComponent == null) && !(aggro == null))
		{
			animationComponent.Stop();
			aggro.time = 0f;
			aggro.speed = 1f;
			animationComponent.Play(aggro.name);
		}
	}

	private float motionSpeedFactor = 1f;

	private float motionStamp = -100f;

	// Chase speed relative to the monster's nominal speed; scales the baked chase frame interval.
	public void SetMotionSpeedFactor(float factor)
	{
		motionSpeedFactor = factor;
		motionStamp = Time.time;
	}

	private float ChaseFrameIntervalScale()
	{
		return (Time.time - motionStamp < 0.25f) ? Mathf.Clamp(motionSpeedFactor, 0.4f, 1.6f) : 1f;
	}

	private void Update()
	{
		if (Time.timeScale <= 0f || Time.time < nextBakedFrameAt)
		{
			return;
		}
		GameObject[] array = ((bakedState == BakedState.Aggro) ? bakedAggroFrames : ((bakedState == BakedState.Chase) ? bakedChaseFrames : bakedIdleFrames));
		if (array != null && array.Length != 0)
		{
			if (bakedState == BakedState.Aggro)
			{
				bakedFrame = Mathf.Min(bakedFrame + 1, array.Length - 1);
			}
			else
			{
				bakedFrame = (bakedFrame + 1) % array.Length;
			}
			ShowBakedFrame(array, bakedFrame);
			float num = ((bakedState == BakedState.Aggro) ? (3.5f / (float)Mathf.Max(1, array.Length)) : ((bakedState == BakedState.Chase) ? (0.1f / ChaseFrameIntervalScale()) : 0.3f));
			nextBakedFrameAt = Time.time + num;
		}
	}

	public void SetChasing(bool value)
	{
		bool flag = chasing != value;
		chasing = value;
		if (bakedChaseFrames != null && bakedChaseFrames.Length > 0)
		{
			BakedState bakedState = (chasing ? BakedState.Chase : ((aggroStarted && !IsAggroComplete) ? BakedState.Aggro : BakedState.Idle));
			if (flag || bakedState != this.bakedState)
			{
				this.bakedState = bakedState;
				bakedFrame = 0;
				ShowBakedFrame(bakedState switch
				{
					BakedState.Aggro => bakedAggroFrames, 
					BakedState.Chase => bakedChaseFrames, 
					_ => bakedIdleFrames, 
				}, bakedFrame);
				nextBakedFrameAt = Time.time + ((bakedState == BakedState.Chase) ? 0.1f : 0.3f);
			}
		}
		if (!(animationComponent == null) && (!aggroStarted || !(aggro != null) || !animationComponent.IsPlaying(aggro.name)))
		{
			string text = ((chasing && chase != null) ? chase.name : ((idle != null) ? idle.name : null));
			if (!string.IsNullOrEmpty(text) && !animationComponent.IsPlaying(text))
			{
				animationComponent.CrossFade(text, 0.12f);
			}
		}
	}
}
