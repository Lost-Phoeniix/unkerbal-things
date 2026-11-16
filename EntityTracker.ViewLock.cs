using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnkerbalThings.Dread;

// The view near the Moho swarm: rules in Dread/ViewLock.cs, the camera head in ImagerFeedPart.cs.
// The flight camera is taken over (FlightCamera.DeactivateUpdate, its pose set every frame from ViewLockCameraDriver, which runs
// before ScaledCamera copies it) instead of switching to IVA: part menus are hidden in IVA, Internal and Map modes
// (UIPartActionController.OnCameraChange), so the camera mode stays Flight and every open part menu keeps working.
// The Dread screen pass (Camera 00) then gives the feed its tube look; nothing is rendered twice.
public partial class EntityTracker
{
	private static readonly ViewLockSettings viewLockSettings = new ViewLockSettings();
	private static float viewFeedBlow = 0.5f, viewEvaFov = 70f;
	private ViewLock viewLock;
	private bool viewWantFeed;
	private ModuleUTImagerFeed viewImager;
	private Vessel viewVessel;
	// camera takeover
	private bool viewCamTaken;
	private Vessel viewCamVessel;
	private Vector3 viewCamLocalPos;
	private Quaternion viewCamLocalRot;
	private float viewCamFov = 60f;
	private GameObject viewDriverObject;
	// EVA first person: look direction in the surface frame (degrees), so walking where you look cannot spin the view
	private float viewEvaYaw, viewEvaPitch;
	private bool viewEvaYawSet;
	private ViewMode viewLastMode = ViewMode.Outside;
	private float viewInterference, viewFeedWear;
	private double viewSwarmDistance = double.PositiveInfinity;

	private static void LoadViewLockSettings(ConfigNode node)
	{
		ViewLockSettings s = viewLockSettings;
		ReadBoolScareSetting(node, "viewLock", ref s.Enabled);
		ReadFloatSetting(node, "viewLockPullMetres", ref s.PullMetres);
		ReadFloatSetting(node, "viewLockReleaseMetres", ref s.ReleaseMetres);
		ReadFloatSetting(node, "viewLockFadeSeconds", ref s.FadeSeconds);
		ReadFloatSetting(node, "viewLockRecoverSeconds", ref s.RecoverSeconds);
		ReadBoolScareSetting(node, "viewLockEvaFirstPerson", ref s.EvaFirstPerson);
		ReadBoolScareSetting(node, "viewLockIvaClear", ref s.IvaClear);
		ReadFloatSetting(node, "viewFeedBlow", ref viewFeedBlow);
		ReadBoolScareSetting(node, "viewFeedTufxOff", ref viewFeedTufxOff);
		s.PullMetres = Mathf.Clamp(s.PullMetres, 10f, 5000f);
		s.ReleaseMetres = Mathf.Clamp(s.ReleaseMetres, s.PullMetres + 5f, 10000f);
		s.FadeSeconds = Mathf.Clamp(s.FadeSeconds, 0.2f, 60f);
		s.RecoverSeconds = Mathf.Clamp(s.RecoverSeconds, 0.2f, 60f);
		viewFeedBlow = Mathf.Clamp01(viewFeedBlow);
	}

	// ---------------------------------------------------------------- called by the part module

	internal void ImagerFeedToggle(ModuleUTImagerFeed m)
	{
		if (m == null || m.vessel != FlightGlobals.ActiveVessel)
		{
			ScreenMessages.PostScreenMessage("Switch to this craft first.", 3f, ScreenMessageStyle.UPPER_CENTER);
			return;
		}
		bool live = ImagerFeedActive(m);
		if (live && viewLock != null && !viewLock.CanLeaveFeed)
		{
			ScreenMessages.PostScreenMessage("No outside picture. The camera is all there is.", 3f, ScreenMessageStyle.UPPER_CENTER);
			return;
		}
		viewWantFeed = !live;
		viewImager = m;
	}

	internal bool ImagerFeedActive(ModuleUTImagerFeed m)
	{
		return m != null && viewLock != null && viewLock.Mode == ViewMode.Feed && viewImager == m;
	}

	// ---------------------------------------------------------------- per frame (UpdateDreadCore, before the screen pass)

	private void ViewLockStep(Vessel v, float dt)
	{
		if (viewLock == null) viewLock = new ViewLock(viewLockSettings);
		if (v != viewVessel)
		{
			viewVessel = v;
			viewWantFeed = false;
			viewImager = null;
			viewEvaYawSet = false;
		}
		if (viewImager == null || viewImager.vessel != v || !viewImager.Ready) viewImager = FindImager(v);
		Vector3d sw; double d = double.PositiveInfinity; bool heap, visible;
		if (v != null && !WormSwarmFrom(v, v.CoMD, out sw, out d, out heap, out visible)) d = double.PositiveInfinity;
		viewSwarmDistance = d;
		viewInterference = WormReadoutInterference(d);
		viewFeedWear = ViewLock.FeedWear(d, viewLockSettings.PullMetres, 60f);
		ViewLockInput input = new ViewLockInput();
		input.Dt = dt;
		input.SwarmDistance = d;
		input.HasImager = viewImager != null;
		input.Eva = v != null && v.isEVA;
		input.WantFeed = viewWantFeed;
		input.Attack = ViewBlackoutPhase();
		viewLock.Step(in input);
		if (viewLock.Mode != viewLastMode)
		{
			if (viewLock.Mode == ViewMode.Feed)
				ScreenMessages.PostScreenMessage("Camera feed. Hold right mouse to aim, wheel to zoom" + (viewLock.Locked ? ", C to look out of the windows." : "."), 6f, ScreenMessageStyle.UPPER_CENTER);
			if (viewLastMode == ViewMode.Feed && viewLock.Mode == ViewMode.Outside) viewWantFeed = false;
			viewLastMode = viewLock.Mode;
		}
		EnsureViewDriver();
	}

	/// <summary>The eruption is under way: the strike itself is never seen through a camera or a helmet.</summary>
	private bool ViewBlackoutPhase()
	{
		return wormAmbushPhase == WormAmbushPhase.Rise || wormAmbushPhase == WormAmbushPhase.Ball
			|| wormAmbushPhase == WormAmbushPhase.Drop || wormAmbushPhase == WormAmbushPhase.Collapse
			|| wormAmbushPhase == WormAmbushPhase.Settle || wormAmbushPhase == WormAmbushPhase.After;
	}

	private float viewFeedLockStart = -1f;
	private const float ViewFeedLockSeconds = 0.45f;

	private static ModuleUTImagerFeed FindImager(Vessel v)
	{
		if (v == null || v.parts == null) return null;
		for (int i = 0; i < v.parts.Count; i++)
		{
			Part p = v.parts[i];
			if (p == null) continue;
			ModuleUTImagerFeed m = p.FindModuleImplementing<ModuleUTImagerFeed>();
			if (m != null && m.Ready) return m;
		}
		return null;
	}

	// ---------------------------------------------------------------- screen (ApplyDreadScreen)

	/// <summary>Turns the Dread pass into the view's picture: clear in IVA and EVA, the tube feed through the imager, static then
	/// black outside while the swarm is close. Blackout cuts the feed and the helmet view during an eruption.</summary>
	private void ViewLockScreen(ref DreadScreenParams p, bool iva)
	{
		if (viewLock == null || !viewLockSettings.Enabled) return;
		float seed = p.Seed, strength = p.Strength;
		if (iva)
		{
			if (viewLockSettings.IvaClear) { p = new DreadScreenParams(); p.Seed = seed; p.Strength = strength; }
			return;
		}
		bool cam = viewCamTaken;
		ViewMode mode = viewLock.Mode;
		// a receiver that has just been switched to a feed needs a moment to lock on: a short burst of static and rolling sync
		// instead of a perfectly clean picture in the first frame
		bool fed = cam && (mode == ViewMode.Feed || mode == ViewMode.EvaEye);
		if (!fed) viewFeedLockStart = -1f;
		else if (viewFeedLockStart < 0f) viewFeedLockStart = Time.unscaledTime;
		float lockIn = fed ? 1f - Mathf.Clamp01((Time.unscaledTime - viewFeedLockStart) / ViewFeedLockSeconds) : 0f;
		lockIn *= lockIn;
		if (mode == ViewMode.EvaEye && cam)
		{
			p = new DreadScreenParams(); p.Seed = seed; p.Strength = 1f;
			p.Dark = Mathf.Max(p.Dark, 0.6f * lockIn);   // the eyes adjust: a moment of dark, no static (no electronics)
		}
		else if (mode == ViewMode.Feed && cam)
		{
			if (lockIn > 0f)
			{
				p.Snow = Mathf.Max(p.Snow, lockIn);
				p.Sync = Mathf.Max(p.Sync, 0.8f * lockIn);
			}
			// 1980s colour TV that wears down as the swarm closes in: clean at the pull radius, worn out at 60 m
			float wear = viewFeedWear;
			float blur = viewImager != null && viewImager.Zoom != null ? viewImager.Zoom.Blur : 0f;
			p.Strength = 1f;
			p.Tube = 1f;
			p.Blow = viewFeedBlow;
			p.Wear = wear;
			p.Seq = 1f;   // field-sequential colour: always on in the feed (Apollo colour camera)
			p.Grade = 0f;
			p.Chroma = Mathf.Max(p.Chroma, 0.35f * wear);
			p.Vignette = Mathf.Max(p.Vignette, 0.12f + 0.3f * wear);
			p.Grain = Mathf.Max(p.Grain, 0.5f + 0.4f * wear);   // base: about 1 % rms, inside the documented > 32 dB signal-to-noise
			p.SignalNoise = Mathf.Max(p.SignalNoise, 0.02f + 0.45f * wear);
			p.Sync = Mathf.Max(p.Sync, 0.02f + 0.35f * wear + 0.35f * blur);
		}
		else
		{
			float loss, snow, dark;
			ViewLock.OutsideScreen(viewLock.OutsideFade, out loss, out snow, out dark);
			if (loss > 0f || snow > 0f || dark > 0f)
			{
				p.Strength = 1f;
				p.Loss = Mathf.Max(p.Loss, loss);
				p.Snow = Mathf.Max(p.Snow, snow);
				p.Dark = Mathf.Max(p.Dark, dark);
			}
			return;
		}
		float b = viewLock.Blackout;
		if (b > 0f)
		{
			p.Dark = Mathf.Max(p.Dark, b);
			p.Snow = Mathf.Max(p.Snow, b);   // the picture is replaced, not dimmed: nothing of the strike may show through
		}
	}

	// ---------------------------------------------------------------- camera (ViewLockCameraDriver.LateUpdate)

	private void EnsureViewDriver()
	{
		if (viewDriverObject != null) return;
		viewDriverObject = new GameObject("UT ViewLock camera");
		viewDriverObject.AddComponent<ViewLockCameraDriver>();
	}

	internal void ViewLockLate()
	{
		try
		{
			ViewLockCamera();
		}
		catch (Exception ex)
		{
			Debug.Log("[UT ViewLock] camera takeover failed, view released: " + ex);
			ViewLockRelease();
			viewLockSettings.Enabled = false;
		}
	}

	private void ViewLockCamera()
	{
		FlightCamera fc = FlightCamera.fetch;
		Vessel v = FlightGlobals.ActiveVessel;
		bool flightMode = HighLogic.LoadedSceneIsFlight && fc != null && v != null && !MapView.MapIsEnabled
			&& CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight;
		ViewMode mode = viewLock != null && viewLockSettings.Enabled ? viewLock.Mode : ViewMode.Outside;
		Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity; float fov = 60f;
		bool want = flightMode && v == viewVessel;
		if (want && mode == ViewMode.Feed)
		{
			want = viewImager != null && viewImager.vessel == v;
			if (want)
			{
				if (!EventSystem.current || !EventSystem.current.IsPointerOverGameObject())
				{
					float wheel = GameSettings.AXIS_MOUSEWHEEL.GetAxis();
					bool drag = Input.GetMouseButton(1);
					viewImager.Steer(drag ? Input.GetAxis("Mouse X") : 0f, drag ? Input.GetAxis("Mouse Y") : 0f, wheel);
				}
				want = viewImager.Eye(out pos, out rot);
				fov = viewImager.Zoom.Fov;
			}
		}
		else if (want && mode == ViewMode.EvaEye)
		{
			want = EvaEye(v, out pos, out rot);
			fov = viewEvaFov;
		}
		else want = false;

		if (want && viewCamTaken && viewCamVessel != v) ViewLockRelease();
		if (!want) { if (viewCamTaken) ViewLockRelease(); return; }
		if (!viewCamTaken)
		{
			viewCamLocalPos = fc.transform.localPosition;
			viewCamLocalRot = fc.transform.localRotation;
			viewCamFov = fc.FieldOfView;
			fc.DeactivateUpdate();
			viewCamTaken = true;
			viewCamVessel = v;
		}
		fc.transform.SetPositionAndRotation(pos, rot);
		if (Mathf.Abs(fc.FieldOfView - fov) > 0.01f) fc.SetFoV(fov);
		ViewTufxHold(mode == ViewMode.Feed);
	}

	// ---------------------------------------------------------------- TUFX off while the imager feed is on
	// TUFX post-processing (a PostProcessLayer on each camera) runs before the Dread pass, so profiles with auto exposure
	// washed out the feed's tube picture. While the feed is on, the layers of the flight cameras are
	// switched off and HDR goes back to the game's own setting; the exact earlier state is put back when the feed ends.
	// Nothing is written to the save or to TUFX; without TUFX there is no PostProcessLayer and this does nothing.

	private static bool viewFeedTufxOff = true;
	private struct ViewTufxHeld { public Behaviour Layer; public Camera Cam; public bool Enabled, Hdr; }
	private readonly System.Collections.Generic.List<ViewTufxHeld> viewTufxHeld = new System.Collections.Generic.List<ViewTufxHeld>();
	private bool viewTufxHolding;

	private void ViewTufxHold(bool hold)
	{
		hold = hold && viewFeedTufxOff;
		if (hold == viewTufxHolding)
		{
			// TUFX sets HDR again whenever it applies a profile (camera mode change, its own window): keep both off
			if (hold)
				foreach (ViewTufxHeld h in viewTufxHeld)
				{
					if (h.Layer != null && h.Layer.enabled) h.Layer.enabled = false;
					if (h.Cam != null && h.Cam.allowHDR) h.Cam.allowHDR = false;
				}
			return;
		}
		viewTufxHolding = hold;
		if (hold)
		{
			viewTufxHeld.Clear();
			System.Collections.Generic.List<Camera> cams = new System.Collections.Generic.List<Camera>();
			FlightCamera fc = FlightCamera.fetch;
			if (fc != null && fc.cameras != null) cams.AddRange(fc.cameras);
			if (fc != null && fc.mainCamera != null && !cams.Contains(fc.mainCamera)) cams.Add(fc.mainCamera);
			if (ScaledCamera.Instance != null)
			{
				if (ScaledCamera.Instance.cam != null) cams.Add(ScaledCamera.Instance.cam);
				if (ScaledCamera.Instance.galaxyCamera != null) cams.Add(ScaledCamera.Instance.galaxyCamera);
			}
			foreach (Camera c in cams)
			{
				if (c == null) continue;
				Behaviour layer = c.GetComponent("PostProcessLayer") as Behaviour;
				if (layer == null) continue;
				viewTufxHeld.Add(new ViewTufxHeld { Layer = layer, Cam = c, Enabled = layer.enabled, Hdr = c.allowHDR });
				layer.enabled = false;
				c.allowHDR = false;
			}
			if (viewTufxHeld.Count > 0) Debug.Log("[UT ViewLock] TUFX post-processing off on " + viewTufxHeld.Count + " cameras while the feed is on");
		}
		else
		{
			foreach (ViewTufxHeld h in viewTufxHeld)
			{
				if (h.Layer != null) h.Layer.enabled = h.Enabled;
				if (h.Cam != null) h.Cam.allowHDR = h.Hdr;
			}
			viewTufxHeld.Clear();
		}
	}

	/// <summary>Gives the flight camera back exactly as it was (orbit distance, rotation, FoV). Safe to call any time.</summary>
	private void ViewLockRelease()
	{
		ViewTufxHold(false);
		if (!viewCamTaken) return;
		viewCamTaken = false;
		viewCamVessel = null;
		FlightCamera fc = FlightCamera.fetch;
		if (fc == null) return;
		fc.transform.localPosition = viewCamLocalPos;
		fc.transform.localRotation = viewCamLocalRot;
		fc.SetFoV(viewCamFov);
		fc.ActivateUpdate();
	}

	private void ViewLockShutdown()
	{
		ViewLockRelease();
		if (viewDriverObject != null) { UnityEngine.Object.Destroy(viewDriverObject); viewDriverObject = null; }
		if (viewLock != null) viewLock.Reset();
		viewLastMode = ViewMode.Outside;
		viewWantFeed = false;
		viewImager = null;
		viewVessel = null;
	}

	/// <summary>An EVA kerbal's own eyes: in front of the helmet, mouse-look (right button) in the surface frame.</summary>
	private bool EvaEye(Vessel v, out Vector3 pos, out Quaternion rot)
	{
		pos = Vector3.zero; rot = Quaternion.identity;
		KerbalEVA eva = v.evaController;
		if (eva == null || v.mainBody == null) return false;
		Transform head = eva.helmetTransform != null ? eva.helmetTransform : eva.transform;
		Vector3 up = (v.transform.position - v.mainBody.position).normalized;
		Vector3 north = Vector3.ProjectOnPlane(v.mainBody.transform.up, up).normalized;
		if (north.sqrMagnitude < 1e-6f) north = Vector3.ProjectOnPlane(eva.transform.forward, up).normalized;
		if (!viewEvaYawSet)
		{
			Vector3 f = Vector3.ProjectOnPlane(eva.transform.forward, up);
			viewEvaYaw = f.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(north, f, up) : 0f;
			viewEvaPitch = -5f;
			viewEvaYawSet = true;
		}
		if (Input.GetMouseButton(1) && (!EventSystem.current || !EventSystem.current.IsPointerOverGameObject()))
		{
			viewEvaYaw += Input.GetAxis("Mouse X") * 3f;
			viewEvaPitch = Mathf.Clamp(viewEvaPitch + Input.GetAxis("Mouse Y") * 3f, -75f, 70f);
		}
		Quaternion frame = Quaternion.LookRotation(north, up);
		rot = frame * Quaternion.Euler(-viewEvaPitch, viewEvaYaw, 0f);
		pos = head.position + up * 0.05f + rot * Vector3.forward * 0.22f;
		return true;
	}

	// ---------------------------------------------------------------- overlay burned into the feed (OnGUI)

	private GUIStyle viewHudStyle;

	private void DrawViewLockHud()
	{
		if (viewLock == null || !viewLockSettings.Enabled || Event.current.type != EventType.Repaint || MapView.MapIsEnabled) return;
		if (CameraManager.Instance == null || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) return;
		bool feed = viewCamTaken && viewLock.Mode == ViewMode.Feed && viewImager != null;
		bool blind = !viewCamTaken && viewLock.Mode == ViewMode.Outside && viewLock.OutsideFade > 0.45f;
		if (!feed && !blind) return;
		float h = Screen.height, w = Screen.width;
		float pw = w, x0 = 0f;
		if (viewHudStyle == null) viewHudStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
		viewHudStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(h / 1080f * 24f));
		float pad = h * 0.03f, lh = viewHudStyle.fontSize * 1.4f;
		Color old = GUI.color;
		// the blind caption fades in with the static instead of appearing at one threshold
		viewHudAlpha = 0.85f * (feed ? 1f : Mathf.Clamp01((viewLock.OutsideFade - 0.45f) / 0.3f));
		viewHudStyle.alignment = blind ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
		CultureInfo c = CultureInfo.InvariantCulture;
		ModuleUTGroundRadar radar = FindRadar(FlightGlobals.ActiveVessel);
		string radarLine = radar != null ? radar.HudLine() : null;
		string call = radar != null ? ProximityWarn.Callout(radar.WarnLevel) : "";
		bool blink = (Time.unscaledTime % 0.8f) < 0.5f;
		if (feed && viewLock.Blackout <= 0.5f)
		{
			ModuleUTImagerFeed m = viewImager;
			string clock = KSPUtil.PrintDateCompact(Planetarium.GetUniversalTime(), true, true);
			string head = "AZ " + m.Pan.Wrapped.ToString("+000;-000", c) + "  EL " + m.Tilt.Value.ToString("+00;-00", c) + "  " + (m.Zoom.Fovs[0] / Mathf.Max(1f, m.Zoom.Fov)).ToString("0.0", c) + "x"
				+ (m.Pan.AtStop || m.Tilt.AtStop ? "  STOP" : "");
			HudLabel(new Rect(x0 + pad, pad, pw * 0.6f, lh), "CAM 1   " + clock, viewHudStyle);
			HudLabel(new Rect(x0 + pad, h - pad - lh, pw * 0.8f, lh), head, viewHudStyle);
			if (ViewAnyExperimentRunning(m.vessel) && (Time.unscaledTime % 1.0f) < 0.6f)
				HudLabel(new Rect(x0 + pw - pad - lh * 3f, pad, lh * 3f, lh), "REC", viewHudStyle);
			if (viewLock.Locked && viewFeedWear > 0.6f && (Time.unscaledTime % 2.4f) < 1.2f)
				HudLabel(new Rect(x0 + pad, h - pad - 2f * lh, pw * 0.8f, lh), "SIGNAL INTERFERENCE", viewHudStyle);
			if (radarLine != null) HudLabel(new Rect(x0 + pad, pad + lh, pw * 0.8f, lh), radarLine, viewHudStyle);
			if (call.Length > 0 && blink) HudLabel(new Rect(x0 + pw * 0.5f - lh * 3f, h * 0.5f + lh * 2f, lh * 6f, lh), call, viewHudStyle);
		}
		else if (blind)
		{
			// no picture: only the instruments speak (centred, plain)
			float y = h * 0.5f - lh;
			if (radarLine != null) HudLabel(new Rect(w * 0.5f - lh * 7f, y, lh * 14f, lh), radarLine, viewHudStyle);
			else HudLabel(new Rect(w * 0.5f - lh * 4f, y, lh * 8f, lh), "NO PICTURE", viewHudStyle);
			if (call.Length > 0 && blink) HudLabel(new Rect(w * 0.5f - lh * 3f, y + lh * 1.5f, lh * 6f, lh), call, viewHudStyle);
		}
		GUI.color = old;
	}

	private float viewHudAlpha = 0.85f;

	/// <summary>Overlay text with a dark drop shadow, so it stays readable over blown-out feed highlights.</summary>
	private void HudLabel(Rect r, string text, GUIStyle style)
	{
		float off = Mathf.Max(1f, style.fontSize / 12f);
		GUI.color = new Color(0f, 0f, 0f, viewHudAlpha * 0.8f);
		GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, style);
		GUI.color = new Color(0.92f, 0.95f, 0.93f, viewHudAlpha);
		GUI.Label(r, text, style);
	}

	private ModuleUTGroundRadar viewRadarCache;
	private ModuleUTGroundRadar FindRadar(Vessel v)
	{
		if (v == null) return null;
		if (viewRadarCache != null && viewRadarCache.vessel == v && viewRadarCache.radarOn) return viewRadarCache;
		viewRadarCache = null;
		for (int i = 0; i < v.parts.Count; i++)
		{
			ModuleUTGroundRadar r = v.parts[i] != null ? v.parts[i].FindModuleImplementing<ModuleUTGroundRadar>() : null;
			if (r != null && r.radarOn) { viewRadarCache = r; break; }
		}
		return viewRadarCache;
	}

	private static bool ViewAnyExperimentRunning(Vessel v)
	{
		if (v == null || v.parts == null) return false;
		for (int i = 0; i < v.parts.Count; i++)
		{
			Part p = v.parts[i];
			if (p == null) continue;
			var ms = p.FindModulesImplementing<ModuleUTWormExperiment>();
			for (int k = 0; k < ms.Count; k++) if (ms[k].IsRunning) return true;
		}
		return false;
	}
}

/// <summary>Sets the taken-over flight camera's pose late in the frame but before ScaledCamera copies it (no one-frame lag of the
/// sky and planets while the head turns).</summary>
[DefaultExecutionOrder(-500)]
public sealed class ViewLockCameraDriver : MonoBehaviour
{
	void LateUpdate()
	{
		EntityTracker t = EntityTracker.Instance;
		if (t != null) t.ViewLockLate();
	}
}
