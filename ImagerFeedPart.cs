using System;
using System.Globalization;
using UnityEngine;
using UnkerbalThings.Dread;

/// <summary>The Swarm Imager's camera as a view (rules in Dread/ViewLock.cs).
/// Holds the camera head: pan about the drum axis, tilt (elevation of the view) on the fork, a few zoom steps, all slow and with hard
/// stops. Turns the model's "pan" and "tilt" meshes so the head is seen to move from outside.
/// EntityTracker.ViewLock.cs decides when the player looks through it and moves the flight camera to Eye().</summary>
public class ModuleUTImagerFeed : PartModule
{
	[KSPField] public float panLimit = 0f;           // degrees either side of the front; 0 = turns all the way round
	[KSPField] public float elevationMin = -40f;     // view elevation, degrees (negative: down)
	[KSPField] public float elevationMax = 35f;
	[KSPField] public float elevationRest = -20f;    // the head as built (the model's camera looks 20 deg down)
	[KSPField] public float panRate = 45f;           // degrees per second
	[KSPField] public float tiltRate = 30f;
	[KSPField] public string fovSteps = "50, 30, 15";
	[KSPField] public float zoomSeconds = 1.5f;
	[KSPField] public Vector3 eyeOffset = new Vector3(0.150f, -0.333f, 0.858f);   // tilt-local, model scale: at the main lens glass (science_build.py prints it)
	[KSPField] public float mouseDegrees = 4f;      // head target degrees per mouse unit while the right button is held

	[KSPField(guiActive = true, guiName = "Camera")]
	public string feedStatus = "standby";

	public GimbalAxis Pan { get; private set; }
	public GimbalAxis Tilt { get; private set; }
	public ZoomSteps Zoom { get; private set; }
	Transform panT, tiltT;
	Quaternion panRest, tiltRest;
	static readonly Vector3 OpticalAxisRest = new Vector3(0f, -0.3420f, 0.9397f);   // tilt-local, the camera as built

	/// <summary>The head is there and works (both moving meshes found).</summary>
	public bool Ready { get { return panT != null && tiltT != null && Pan != null; } }

	public override void OnStart(StartState state)
	{
		Pan = panLimit > 0f ? new GimbalAxis(-panLimit, panLimit, panRate, 0f) : new GimbalAxis(0f, 0f, panRate, 0f);
		Tilt = new GimbalAxis(elevationMin, elevationMax, tiltRate, elevationRest);
		Zoom = new ZoomSteps(ParseFovs(fovSteps), zoomSeconds);
		panT = part.FindModelTransform("pan");
		tiltT = part.FindModelTransform("tilt");
		if (panT != null) panRest = panT.localRotation;
		if (tiltT != null) tiltRest = tiltT.localRotation;
		if (!Ready && HighLogic.LoadedSceneIsFlight) Debug.Log("[UT ImagerFeed] " + part.partInfo.name + ": model has no pan/tilt transforms; no camera feed");
	}

	static float[] ParseFovs(string s)
	{
		try
		{
			string[] parts = (s ?? "").Split(',');
			var list = new System.Collections.Generic.List<float>();
			for (int i = 0; i < parts.Length; i++)
			{
				float f;
				if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f > 1f && f < 170f) list.Add(f);
			}
			if (list.Count > 0) return list.ToArray();
		}
		catch (Exception) { }
		return new[] { 50f };
	}

	[KSPEvent(guiActive = true, guiName = "View camera feed")]
	public void ToggleFeed()
	{
		EntityTracker t = EntityTracker.Instance;
		if (t == null || !Ready) { ScreenMessages.PostScreenMessage("The camera does not respond.", 3f, ScreenMessageStyle.UPPER_CENTER); return; }
		t.ImagerFeedToggle(this);
	}

	[KSPAction("Toggle camera feed")]
	public void ToggleFeedAction(KSPActionParam param) { ToggleFeed(); }

	/// <summary>Player input while looking through the camera: right mouse drag aims the head, the wheel steps the zoom.</summary>
	public void Steer(float mouseX, float mouseY, float wheel)
	{
		if (!Ready) return;
		Pan.Push(mouseX * mouseDegrees);
		Tilt.Push(mouseY * mouseDegrees);
		if (wheel > 0f) Zoom.In();
		else if (wheel < 0f) Zoom.Out();
	}

	public void Update()
	{
		if (!HighLogic.LoadedSceneIsFlight || !Ready) return;
		float dt = Time.deltaTime;   // 0 while paused: the head stops
		Pan.Step(dt);
		Tilt.Step(dt);
		Zoom.Step(dt);
		// the meshes: pan turns about the part's up axis; the camera turns with it and tilts about its bearing axis (the pivot is on
		// the pan axis, so only rotations change). Unity: +X rotation pitches the view down, so elevation up is a negative angle.
		Quaternion panQ = Quaternion.AngleAxis(Pan.Value, Vector3.up);
		Quaternion tiltQ = Quaternion.AngleAxis(-(Tilt.Value - elevationRest), Vector3.right);
		panT.localRotation = panQ * panRest;
		tiltT.localRotation = panQ * tiltQ * tiltRest;
		EntityTracker t = EntityTracker.Instance;
		feedStatus = t != null && t.ImagerFeedActive(this) ? "live" : "standby";
	}

	/// <summary>Where the feed looks from: in front of the main lens, along the optical axis, the craft's up as the picture's top.</summary>
	public bool Eye(out Vector3 position, out Quaternion rotation)
	{
		position = Vector3.zero; rotation = Quaternion.identity;
		if (!Ready) return false;
		position = tiltT.TransformPoint(eyeOffset);
		Vector3 fwd = tiltT.TransformDirection(OpticalAxisRest);
		Vector3 up = part.transform.up;
		if (Vector3.Cross(fwd, up).sqrMagnitude < 1e-6f) up = tiltT.up;
		rotation = Quaternion.LookRotation(fwd, up);
		return true;
	}
}
