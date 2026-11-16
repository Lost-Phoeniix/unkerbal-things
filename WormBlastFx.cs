using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Our own vacuum blast for the Moho kill when Volumetric Explosion FX is not installed (setting mohoKillBlast = auto).
/// Plain Unity, shared by the game (EntityTracker.WormAttack.cs) and the offline rig
/// (its run script copies it). No air: no smoke, no mushroom, no haze. A short flash and a point light, a hot ball that flies apart and
/// fades in 1-2 s (additive glowing puffs, KK/WormDust with KK_HOT), sparks and the ejecta through the lit dust (WormDustFx).
/// The light's colour ramp (bluish white flash, then yellow-white cooling to orange) and its range are VEFX's
/// (Core/ExplosionLight.cs, MIT, Copyright (c) 2026 Warnix; see Dread/KillBlast.cs for the licence text).
/// </summary>
public sealed class WormBlastFx
{
	readonly GameObject root;
	readonly ParticleSystem hot;
	readonly Material hotMat;
	readonly Light light;
	readonly WormDustFx dust;
	float age = -1f, fireball, dome, fire, range;
	public bool ManualStep;

	public WormBlastFx(Transform frame, Shader dustShader, WormDustFx dust)
	{
		this.dust = dust;
		root = new GameObject("WormBlastFx");
		root.layer = 0;
		root.transform.SetParent(frame, false);
		Texture2D a, b;
		SixWayBake.Get(out a, out b);
		hotMat = new Material(dustShader);
		hotMat.mainTexture = a;
		hotMat.EnableKeyword("KK_HOT");
		hotMat.SetFloat("_SrcBlend", (float)BlendMode.One);
		hotMat.SetFloat("_DstBlend", (float)BlendMode.One);
		hotMat.renderQueue = 3010;

		var go = new GameObject("hot");
		go.layer = 0;
		go.transform.SetParent(root.transform, false);
		hot = go.AddComponent<ParticleSystem>();
		hot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		var main = hot.main;
		main.playOnAwake = false; main.loop = true; main.duration = 1f; main.maxParticles = 400;
		main.simulationSpace = ParticleSystemSimulationSpace.Local;
		main.gravityModifier = 0f;
		var em = hot.emission; em.enabled = false;
		var sh = hot.shape; sh.enabled = false;
		var tsa = hot.textureSheetAnimation;
		tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 2;
		tsa.animation = ParticleSystemAnimationType.WholeSheet;
		tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
		tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 7.999f);
		// it cools as it thins: white-hot, yellow, orange, deep red, gone
		var col = hot.colorOverLifetime; col.enabled = true;
		var g = new Gradient();
		g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.86f, 0.62f), 0f), new GradientColorKey(new Color(1f, 0.82f, 0.42f), 0.2f),
				new GradientColorKey(new Color(1f, 0.55f, 0.18f), 0.55f), new GradientColorKey(new Color(0.6f, 0.18f, 0.06f), 1f) },
			new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0.35f, 0.6f), new GradientAlphaKey(0f, 1f) });
		col.color = g;
		var sol = hot.sizeOverLifetime; sol.enabled = true;
		sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.5f)));
		var r = go.GetComponent<ParticleSystemRenderer>();
		r.sharedMaterial = hotMat;
		r.renderMode = ParticleSystemRenderMode.Billboard;
		r.shadowCastingMode = ShadowCastingMode.Off;
		r.receiveShadows = false;
		r.maxParticleSize = 4f;
		hot.Play();

		var lgo = new GameObject("flash");
		lgo.layer = 0;
		lgo.transform.SetParent(root.transform, false);
		light = lgo.AddComponent<Light>();
		light.type = LightType.Point;
		light.shadows = LightShadows.None;
		light.intensity = 0f;
		light.enabled = false;
	}

	/// <summary>Soft edges (the depth texture), as for the dust.</summary>
	public void SetSoft(bool on, float range)
	{
		if (on) hotMat.EnableKeyword("KK_SOFT"); else hotMat.DisableKeyword("KK_SOFT");
		hotMat.SetFloat("_Soft", range);
	}

	static float R(float a, float b) { return Random.Range(a, b); }

	/// <summary>
	/// The blast at `at` (local frame). fireballRadius, domeRadius and fireFraction from Dread/KillBlast.cs (a craft with no
	/// propellant still flashes, with a small hot core); wreckage: how fast its sparks fly (m/s).
	/// </summary>
	public void Fire(Vector3 at, float fireballRadius, float domeRadius, float fireFraction, float wreckage)
	{
		fireball = Mathf.Max(fireballRadius, 1.5f);
		dome = Mathf.Max(domeRadius, fireball);
		fire = Mathf.Clamp01(Mathf.Max(fireFraction, 0.25f));
		age = 0f;
		// VEFX's light range: several fireball radii, the flash reaches farther
		range = Mathf.Clamp((Mathf.Max(Mathf.Max(fireball * 8f, dome * 2f), 6f) + 15f) * 1.8f, 15f, 400f);
		light.transform.localPosition = at + Vector3.up * fireball * 0.4f;
		light.range = range;
		light.enabled = true;
		// the hot ball: lobes of glowing gas flying apart at ~2.5 radii per second (vacuum), thinning out
		float R0 = Mathf.Min(fireball, 30f);
		int n = Mathf.Clamp((int)(40 + R0 * 6f), 40, 260);
		int lobes = Random.Range(3, 6);
		for (int l = 0; l < lobes; l++)
		{
			Vector3 ld = (Random.onUnitSphere + Vector3.up * 0.6f).normalized;
			for (int i = 0; i < n / lobes; i++)
			{
				Vector3 d = (ld * 0.7f + Random.onUnitSphere * 0.55f).normalized;
				if (d.y < 0.05f) { d.y = 0.05f - d.y * 0.3f; d.Normalize(); }   // the ground stops the gas: it rolls out sideways, not into the soil
				var ep = new ParticleSystem.EmitParams();
				ep.position = at + Vector3.up * R0 * 0.3f + d * R0 * R(0f, 0.3f);
				ep.velocity = d * R0 * R(0.8f, 2.5f);
				ep.startSize = R0 * R(0.45f, 0.9f);
				ep.startLifetime = R(0.7f, 1.6f) * Mathf.Lerp(1f, 1.4f, fire);
				ep.startColor = new Color(1f, 1f, 1f, 0.45f);   // additive: hundreds overlap, so each glows faintly (no white-out)
				hot.Emit(ep, 1);
			}
		}
		if (dust != null)
		{
			// sparks and torn wreckage on ballistic arcs, and the soil the blast throws up around it
			float sp = Mathf.Clamp(wreckage, 6f, 40f);
			dust.Grit(at + Vector3.up, Vector3.up, 220f, sp, 0.9f, Vector3.zero);
			dust.Clods(at + Vector3.up, Vector3.up, 40f, sp * 0.7f, 0.9f, Vector3.zero);
			dust.Puffs(at, Vector3.up, 20f, sp * 0.4f, 0.8f, Vector3.zero, 4f);
			if (dust.Ground != null) dust.Curtain(at, Mathf.Clamp(dome * 0.35f, 4f, 10f), 1.3f, 1.2f, dust.Ground);
		}
	}

	/// <summary>Every frame: the light's flash and glow (and, in the rig, the particles by hand).</summary>
	public void Step(float dt)
	{
		if (ManualStep) hot.Simulate(dt, true, false, false);
		if (age < 0f) return;
		age += age < 0.5f ? Mathf.Min(dt, 0.033f) : dt;   // the kill frame hitches: the flash must still be seen
		// VEFX: a 0.18 s flash (bluish white in vacuum) over a fireball glow that cools from yellow-white to orange
		float flash = 3f * fire * Mathf.Pow(Mathf.Clamp01(1f - age / 0.18f), 2f);
		float heat = Mathf.Exp(-age / 0.7f);
		float glow = 2.2f * fire * heat;
		float fl = Mathf.Clamp01(flash / 3f);
		Color fireCol = new Color(1f, 0.42f + 0.36f * heat, 0.12f + 0.33f * heat * heat);
		Color flashCol = new Color(1f, 0.95f, 0.96f);
		light.color = Color.Lerp(fireCol, flashCol, fl);
		light.intensity = (flash + glow) * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(age / 0.05f));
		light.range = range * (0.6f + 0.4f * Mathf.Max(fl, heat));
		if (age > 3f) { light.enabled = false; age = -1f; }
	}

	public void Destroy() { if (root != null) Object.Destroy(root); }
}
