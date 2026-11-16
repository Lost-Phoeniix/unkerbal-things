using System;
using System.Collections.Generic;
using System.Globalization;
using KrakensKreations.Dread;
using UnityEngine;

// The Moho creature sprints (gallop), leaps (running bounds and the final catch leap), warns from a distance,
// never pops into view or out of view, and its steps follow its real footfalls. Moho/Bulk only. Every part has a settings.cfg key.
public partial class EntityTracker
{
	// ---- settings ----
	private static bool mohoSprint = true;
	private static float mohoSprintSpeed = 120f;
	private static float mohoSprintAccel = 20f;
	private static float mohoGallopFrom = 36f;
	private static bool mohoLeaps = true;
	private static readonly MohoLeapSettings mohoLeapSettings = new MohoLeapSettings();
	private static bool mohoWarnings = true;
	private static bool mohoWarnText = true;
	private static float mohoWarnRadius = 2500f;
	private static bool mohoSpawnHidden = true;
	private static bool mohoLeaveRunning = true;
	private static bool mohoStepsFromFeet = true;

	private static void LoadSprintSettings(ConfigNode node)
	{
		ReadBoolScareSetting(node, "mohoSprint", ref mohoSprint);
		ReadFloatSetting(node, "mohoSprintSpeed", ref mohoSprintSpeed);
		ReadFloatSetting(node, "mohoSprintAccel", ref mohoSprintAccel);
		ReadFloatSetting(node, "mohoGallopFromSpeed", ref mohoGallopFrom);
		ReadBoolScareSetting(node, "mohoLeaps", ref mohoLeaps);
		ReadFloatSetting(node, "mohoLeapMinHeight", ref mohoLeapSettings.MinPeak);
		ReadFloatSetting(node, "mohoLeapMaxHeight", ref mohoLeapSettings.CatchMaxHeight);
		ReadFloatSetting(node, "mohoRunLeapMaxHeight", ref mohoLeapSettings.RunPeakMax);
		ReadFloatSetting(node, "mohoRunLeapEverySeconds", ref mohoLeapSettings.RunMeanGapSeconds);
		ReadFloatSetting(node, "mohoLeapAirSpeed", ref mohoLeapSettings.AirSpeedCap);
		ReadFloatSetting(node, "mohoGravityScale", ref mohoLeapSettings.GravityScale);
		ReadBoolScareSetting(node, "mohoWarnings", ref mohoWarnings);
		ReadBoolScareSetting(node, "mohoWarnText", ref mohoWarnText);
		ReadFloatSetting(node, "mohoWarnRadiusMetres", ref mohoWarnRadius);
		ReadBoolScareSetting(node, "mohoSpawnHidden", ref mohoSpawnHidden);
		ReadBoolScareSetting(node, "mohoLeaveRunning", ref mohoLeaveRunning);
		ReadBoolScareSetting(node, "mohoStepsFromFeet", ref mohoStepsFromFeet);
		ReadBoolScareSetting(node, "mohoModel", ref mohoModel);
		mohoSprintSpeed = Mathf.Clamp(mohoSprintSpeed, 0f, 300f);
		mohoSprintAccel = Mathf.Clamp(mohoSprintAccel, 2f, 200f);
		mohoGallopFrom = Mathf.Clamp(mohoGallopFrom, 10f, 200f);
		mohoWarnRadius = Mathf.Clamp(mohoWarnRadius, 300f, 20000f);
		mohoLeapSettings.MinPeak = Mathf.Clamp(mohoLeapSettings.MinPeak, 10f, 400f);
		mohoLeapSettings.CatchMaxHeight = Mathf.Clamp(mohoLeapSettings.CatchMaxHeight, 5f, 300f);
		mohoLeapSettings.RunPeakMax = Mathf.Clamp(mohoLeapSettings.RunPeakMax, mohoLeapSettings.MinPeak, 400f);
		mohoLeapSettings.RunMeanGapSeconds = Mathf.Clamp(mohoLeapSettings.RunMeanGapSeconds, 3f, 600f);
		mohoLeapSettings.AirSpeedCap = Mathf.Clamp(mohoLeapSettings.AirSpeedCap, 5f, 300f);
		mohoLeapSettings.GravityScale = Mathf.Clamp(mohoLeapSettings.GravityScale, 0.1f, 5f);
	}

	// ---- state ----
	private readonly MohoLeap mohoLeap = new MohoLeap();
	private float mohoLeapLift, mohoLeapCooldown, mohoRunLeapTimer = -1f, mohoRidgeTimer, mohoSprintSeconds;
	private int mohoFootPending;
	internal int MohoLeapCount, MohoLandCount, MohoLeapRefused;
	internal float MohoMaxLeapLift, MohoLastLandDistance, MohoLastLandScale;
	internal readonly List<string> MohoLog = new List<string>();
	private float dreadThumpScale = 1f, dreadThumpScaleTimer, dreadWarnJitter;
	private readonly DreadWarnings mohoWarn = new DreadWarnings();
	internal int[] MohoWarnCount = new int[4];
	private bool bulkSpawnPending;
	private float bulkSpawnRetryAt, bulkSpawnPendingSince;
	private bool bulkLeaving;
	private float bulkLeaveSpeed, bulkExposeTimer;
	private bool bulkExposeCached;
	internal int MohoSpawnCount, MohoSpawnDelayed, MohoSpawnExposed, MohoDespawnCount, MohoDespawnExposed;

	private void MohoNote(string what)
	{
		string line = Time.time.ToString("F1", CultureInfo.InvariantCulture) + " " + what;
		MohoLog.Add(line);
		if (MohoLog.Count > 400)
		{
			MohoLog.RemoveAt(0);
		}
		Debug.Log("[KK Moho] " + what);
	}

	private void MohoResetSprintState()
	{
		mohoLeap.Active = false;
		mohoLeapLift = 0f;
		mohoLeapCooldown = 4f;
		mohoRunLeapTimer = -1f;
		mohoSprintSeconds = 0f;
		mohoFootPending = 0;
		bulkLeaving = false;
		bulkLeaveSpeed = 0f;
		bulkTurnHold = false;
		bulkExposeTimer = 0f;
		bulkExposeCached = true;
		if (huntBulk.hunt != null)
		{
			huntBulk.hunt.Airborne = false;
		}
	}

	// ---------------------------------------------------------------- sprint and leaps (called from HuntGroundApproach)

	private void MohoPrepare(HuntMonster m)
	{
		DreadHunt h = m.hunt;
		h.SprintSpeed = mohoSprint ? mohoSprintSpeed : 0f;
		h.SprintAccel = mohoSprintAccel;
		h.Airborne = mohoLeap.Active;
		bulkMoho.GallopFrom = mohoGallopFrom;
	}

	private float MohoGravity(CelestialBody body)
	{
		return (float)(body.GeeASL * 9.80665) * mohoLeapSettings.GravityScale;
	}

	// Returns a status text when it handled the monster's leap logic this frame, null to leave the status as it is.
	private string MohoLeapStep(HuntMonster m, ref Vector3d position, Vessel vessel, CelestialBody body, float dt)
	{
		DreadHunt h = m.hunt;
		Vector3d up = (position - body.position).normalized;
		if (mohoLeap.Active)
		{
			double ground = m.terrain.Height(h.X, h.Y);
			mohoLeapLift = mohoLeap.Advance(dt, ground);
			MohoMaxLeapLift = Mathf.Max(MohoMaxLeapLift, mohoLeapLift);
			if (mohoLeap.JustLanded)
			{
				mohoLeapLift = 0f;
				h.Airborne = false;
				mohoLeapCooldown = UnityEngine.Random.Range(5f, 9f);
				MohoLandCount++;
				MohoNote("landed after " + mohoLeap.Duration.ToString("F1", CultureInfo.InvariantCulture) + " s, peak lift " + mohoLeap.MaxLift.ToString("F1", CultureInfo.InvariantCulture) + " m, lowest lift in flight " + mohoLeap.MinClearance.ToString("F1", CultureInfo.InvariantCulture) + " m, landing speed " + mohoLeap.LandingSpeed.ToString("F1", CultureInfo.InvariantCulture) + " m/s, ground under the landing " + (ground - mohoLeap.LandGround).ToString("F2", CultureInfo.InvariantCulture) + " m off the plan");
				return null;
			}
			if (mohoLeap.Final && !behaviorBulk.catchPending)
			{
				Vector3d centre = position + up * (double)(mohoLeapLift + 30f);
				if ((vessel.GetWorldPos3D() - centre).magnitude <= (double)mohoLeapSettings.CatchRadius)
				{
					behaviorBulk.catchPending = true;
					behaviorBulk.catchTime = Time.time;
					MohoNote("catch leap reached the craft at lift " + mohoLeapLift.ToString("F1", CultureInfo.InvariantCulture) + " m");
					return "POUNCING";
				}
			}
			return mohoLeap.Final ? "LEAPING AT THE CRAFT" : "BOUNDING";
		}
		mohoLeapLift = 0f;
		mohoLeapCooldown -= dt;
		float sprintSpeed = mohoSprintSpeed;
		if (h.Sprinting)
		{
			mohoSprintSeconds += dt;
		}
		else
		{
			mohoSprintSeconds = 0f;
			mohoRunLeapTimer = -1f;
		}
		bool hunting = h.State == HuntState.Hunt || h.State == HuntState.Attack;
		if (!hunting)
		{
			return null;
		}
		float height = vessel.heightFromTerrain >= 0f ? vessel.heightFromTerrain : 0f;
		float g = MohoGravity(body);
		if (h.HasSight && height >= 10f)
		{
			// the craft is off the ground (just taken off, or hovering)
			if (height > mohoLeapSettings.CatchMaxHeight)
			{
				return "WAITING BENEATH";
			}
			double dx = h.BeliefX - h.X, dy = h.BeliefY - h.Y;
			double dist = Math.Sqrt(dx * dx + dy * dy);
			// the leap goes along the heading: it waits until the body points at the craft (the arc brings it round)
			bool pointing = !huntSettings.NaturalTurn || Math.Abs(Math.Atan2(Math.Sin(Math.Atan2(dy, dx) - h.Heading), Math.Cos(Math.Atan2(dy, dx) - h.Heading))) < 8.0 * Math.PI / 180.0;
			if (mohoLeapCooldown <= 0f && dist <= mohoLeapSettings.CatchRange && pointing)
			{
				float peak = Mathf.Clamp(height + 10f, mohoLeapSettings.MinPeak, mohoLeapSettings.CatchMaxHeight + 15f);
				float apex = Mathf.Sqrt(2f * peak / g);
				float vh = Mathf.Min((float)dist / apex, mohoLeapSettings.AirSpeedCap);
				double heading = Math.Atan2(dy, dx);
				if (mohoLeap.Plan(m.terrain, h.X, h.Y, heading, vh, peak, g, mohoLeapSettings, mohoLeapSettings.CatchMaxHeight + 25f, true))
				{
					StartMohoLeap(h, heading, vh);
					MohoNote("catch leap: craft " + height.ToString("F0", CultureInfo.InvariantCulture) + " m up, " + dist.ToString("F0", CultureInfo.InvariantCulture) + " m away, peak " + mohoLeap.Peak.ToString("F0", CultureInfo.InvariantCulture) + " m, flight " + mohoLeap.Duration.ToString("F1", CultureInfo.InvariantCulture) + " s");
					return "LEAPING AT THE CRAFT";
				}
				MohoLeapRefused++;
				mohoLeapCooldown = 2f;
			}
			return null;
		}
		if (!h.Sprinting || h.Speed < 0.7f * sprintSpeed || mohoSprintSeconds < 5f || mohoLeapCooldown > 0f)
		{
			return null;
		}
		double bx = h.BeliefX - h.X, by = h.BeliefY - h.Y;
		double d = Math.Sqrt(bx * bx + by * by);
		if (mohoRunLeapTimer < 0f)
		{
			mohoRunLeapTimer = Mathf.Clamp(-Mathf.Log(Mathf.Max(UnityEngine.Random.value, 0.02f)) * mohoLeapSettings.RunMeanGapSeconds, 4f, 3f * mohoLeapSettings.RunMeanGapSeconds);
		}
		mohoRunLeapTimer -= dt;
		mohoRidgeTimer -= dt;
		bool ridge = false;
		if (mohoRidgeTimer <= 0f)
		{
			mohoRidgeTimer = 0.4f;
			double here = m.terrain.Height(h.X, h.Y);
			double cx = Math.Cos(h.Heading), cy = Math.Sin(h.Heading);
			double a1 = m.terrain.Height(h.X + cx * 120.0, h.Y + cy * 120.0), a2 = m.terrain.Height(h.X + cx * 260.0, h.Y + cy * 260.0);
			ridge = a1 - here > 30.0 || a2 - here > 45.0;
		}
		if (mohoRunLeapTimer > 0f && !ridge)
		{
			return null;
		}
		float rpeak = ridge ? mohoLeapSettings.MinPeak : UnityEngine.Random.Range(mohoLeapSettings.MinPeak, mohoLeapSettings.RunPeakMax);
		float rvh = Mathf.Min(h.Speed, mohoLeapSettings.AirSpeedCap);
		bool ok = mohoLeap.Plan(m.terrain, h.X, h.Y, h.Heading, rvh, rpeak, g, mohoLeapSettings, mohoLeapSettings.RunPeakMax, false);
		if (ok && mohoLeap.Range > d - 80.0)
		{
			// it would land past the craft: not now
			mohoLeap.Active = false;
			ok = false;
		}
		if (!ok)
		{
			MohoLeapRefused++;
			mohoRunLeapTimer = 2.5f;
			return null;
		}
		StartMohoLeap(h, h.Heading, rvh);
		mohoRunLeapTimer = -1f;
		MohoNote((ridge ? "running leap over a rise" : "running leap") + ": peak " + mohoLeap.Peak.ToString("F0", CultureInfo.InvariantCulture) + " m, flight " + mohoLeap.Duration.ToString("F1", CultureInfo.InvariantCulture) + " s, range " + mohoLeap.Range.ToString("F0", CultureInfo.InvariantCulture) + " m, craft " + d.ToString("F0", CultureInfo.InvariantCulture) + " m away");
		return "BOUNDING";
	}

	// A leap that is still flying when the monster is taken off the hunt (it struck the craft and leaves): it finishes its fall and lands, it is never left hanging.
	private void MohoAdvanceLeapFree(float dt, Vector3d groundPos, CelestialBody body)
	{
		if (!mohoLeap.Active)
		{
			mohoLeapLift = 0f;
			return;
		}
		double ground = body.TerrainAltitude(body.GetLatitude(groundPos), body.GetLongitude(groundPos));
		mohoLeapLift = mohoLeap.Advance(dt, ground);
		if (mohoLeap.JustLanded)
		{
			mohoLeapLift = 0f;
			MohoLandCount++;
			if (huntBulk.hunt != null)
			{
				huntBulk.hunt.Airborne = false;
			}
			MohoNote("landed while leaving, landing speed " + mohoLeap.LandingSpeed.ToString("F1", CultureInfo.InvariantCulture) + " m/s");
		}
	}

	private void StartMohoLeap(DreadHunt h, double heading, float vh)
	{
		h.Heading = heading;
		h.Speed = vh;
		h.Airborne = true;
		MohoLeapCount++;
		mohoLeapLift = 0f;
	}

	// Per frame, from FeedMoho: what the rig has to know about the leap.
	private void MohoFeedAir(MohoCreature c)
	{
		c.Airborne = mohoLeap.Active;
		c.AirLift = mohoLeapLift;
		c.AirPhase = mohoLeap.Active ? mohoLeap.Phase : 0f;
		c.AirImpact = mohoLeap.LandingSpeed;
	}

	// The creature touched down. Heavy: a thump and a shake that fall off with distance, through the ground only; in flight the instruments jitter.
	private void OnMohoLanded(Vector3 position, float strength)
	{
		Vessel v = FlightGlobals.ActiveVessel;
		if (v == null)
		{
			return;
		}
		float d = (float)((Vector3d)position - v.GetWorldPos3D()).magnitude;
		float scale = Mathf.Clamp01(1.15f - d / 3000f);
		scale *= scale;
		MohoLastLandDistance = d;
		MohoLastLandScale = scale;
		bool ground = v.Landed && !dreadStepTestNotLanded;
		MohoNote("touch-down " + d.ToString("F0", CultureInfo.InvariantCulture) + " m from the craft, impact " + strength.ToString("F2", CultureInfo.InvariantCulture) + ", reach " + scale.ToString("F2", CultureInfo.InvariantCulture) + (ground ? ", through the ground" : ", craft in flight: instruments only"));
		if (scale < 0.02f)
		{
			return;
		}
		float s = scale * (0.4f + 0.6f * strength);
		dreadWarnJitter = Mathf.Max(dreadWarnJitter, 0.3f + 0.6f * s);
		if (ground)
		{
			DreadSynth synth = dreadAudioHost != null ? dreadAudioHost.Synth : null;
			if (synth != null)
			{
				dreadThumpScale = Mathf.Clamp(0.25f + 0.75f * s * 1.5f, 0.25f, 1f);
				dreadThumpScaleTimer = 0.6f;
				synth.ThumpLevel = dreadHullSounds && !DreadSoundOwns ? dreadThumpScale : 0f;
				synth.TriggerThump();
			}
			DreadSoundOnLanding(s);
			if (s > 0.05f)
			{
				AttackShake(Mathf.Clamp01(s * 1.4f));
			}
		}
	}

	// ---------------------------------------------------------------- warnings

	private void MohoWarningsStep(Vessel vessel, float dt)
	{
		dreadWarnJitter = Mathf.MoveTowards(dreadWarnJitter, 0f, dt / 1.6f);
		if (dreadThumpScaleTimer > 0f)
		{
			dreadThumpScaleTimer -= dt;
			if (dreadThumpScaleTimer <= 0f)
			{
				dreadThumpScale = 1f;
			}
		}
		if (!mohoWarnings || !mohoCreature || bulkMoho == null || !bulkEntityActive || !huntAi || huntBulk.hunt == null || !huntBulk.info.Valid || bulkEntityBody != vessel.mainBody)
		{
			if (mohoWarn.InChase)
			{
				mohoWarn.Reset();
			}
			return;
		}
		mohoWarn.RadiusMetres = mohoWarnRadius;
		DreadWarning w;
		bool aware = huntBulk.hunt.Aware && !bulkEntityRetreating && !bulkEntityStandingDown;
		bool landed = vessel.Landed && !dreadStepTestNotLanded;
		if (mohoWarn.Update((float)huntBulk.info.TrueDistance, aware, landed, out w))
		{
			ApplyMohoWarning(w);
		}
	}

	private void ApplyMohoWarning(DreadWarning w)
	{
		MohoWarnCount[w.Stage]++;
		MohoNote("WARN stage " + w.Stage + " at " + w.Distance.ToString("F0", CultureInfo.InvariantCulture) + " m, " + (w.Ground ? "ground" : "flight") + ": " + w.Text);
		dreadWarnJitter = Mathf.Max(dreadWarnJitter, 0.35f + 0.45f * w.Strength);
		dreadStaticPulse = 0.8f + 0.9f * w.Strength;
		dreadStaticPulseLevel = 0.2f + 0.35f * w.Strength;
		if (w.Ground)
		{
			DreadSynth synth = dreadAudioHost != null ? dreadAudioHost.Synth : null;
			if (synth != null)
			{
				dreadThumpScale = 0.35f + 0.65f * w.Strength;
				dreadThumpScaleTimer = 0.6f;
				synth.ThumpLevel = dreadHullSounds && !DreadSoundOwns ? dreadThumpScale : 0f;
				synth.TriggerThump();
			}
			DreadSoundOnWarning(w.Stage, w.Strength);
			AttackShake(0.12f + 0.4f * w.Strength);
		}
		if (mohoWarnText)
		{
			ScreenMessages.PostScreenMessage(w.Text, 3.5f, ScreenMessageStyle.UPPER_LEFT);
		}
	}

	// ---------------------------------------------------------------- footfalls

	private void OnMohoFootFallForSteps()
	{
		if (mohoFootPending < 8)
		{
			mohoFootPending++;
		}
	}

	// ---------------------------------------------------------------- spawn and despawn out of sight

	private static bool BuildSpawnView(out SpawnView view)
	{
		view = new SpawnView();
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
		if (cam == null)
		{
			return false;
		}
		Vector3 p = cam.transform.position, f = cam.transform.forward, u = cam.transform.up;
		view = SpawnView.Make(p.x, p.y, p.z, f.x, f.y, f.z, u.x, u.y, u.z, cam.fieldOfView, cam.aspect);
		return true;
	}

	private bool MohoBodyInView(Vector3d groundPos, CelestialBody body, double margin)
	{
		SpawnView view;
		if (!BuildSpawnView(out view))
		{
			return true;
		}
		Vector3d up = (groundPos - body.position).normalized;
		return DreadSpawnRule.BodyInView(in view, groundPos.x, groundPos.y, groundPos.z, up.x, up.y, up.z, margin);
	}

	// A body standing at groundPos (world): inside the camera frustum, with line of sight to the camera or the craft?
	private bool MohoBodyExposed(Vector3d groundPos, CelestialBody body, Vessel vessel, double margin)
	{
		SpawnView view;
		if (!BuildSpawnView(out view))
		{
			return true;
		}
		Vector3d up = (groundPos - body.position).normalized;
		bool inView = DreadSpawnRule.BodyInView(in view, groundPos.x, groundPos.y, groundPos.z, up.x, up.y, up.z, margin);
		if (!inView)
		{
			return false;
		}
		return MohoBodySeen(groundPos, body, vessel);
	}

	// Line of sight from the camera, or from the craft, to any point of the body.
	private bool MohoBodySeen(Vector3d groundPos, CelestialBody body, Vessel vessel)
	{
		Vector3d craft = vessel.GetWorldPos3D();
		if (MohoSeenFrom(craft, 3.0, groundPos, body))
		{
			return true;
		}
		Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
		return cam != null && MohoSeenFrom((Vector3d)cam.transform.position, 0.0, groundPos, body);
	}

	private bool MohoSeenFrom(Vector3d eye, double eyeLift, Vector3d groundPos, CelestialBody body)
	{
		HuntFrame frame = new HuntFrame(body, eye);
		HuntTerrain terrain = new HuntTerrain(frame, body);
		double x, y;
		frame.ToPlane(groundPos, out x, out y);
		double eyeAlt = body.GetAltitude(eye) + eyeLift;
		return DreadSpawnRule.BodySeenFromCraft(terrain, body.Radius, 0.0, 0.0, eyeAlt, x, y, 20.0);
	}

	private bool TryFindHiddenSpawnPosition(Vessel vessel, CelestialBody body, float minimumDistance, float maximumDistance, out Vector3d groundPosition)
	{
		groundPosition = Vector3d.zero;
		Vector3d craft = vessel.GetWorldPos3D();
		Vector3d up = (craft - body.position).normalized;
		Vector3d east = Vector3d.Exclude(up, body.transform.up).normalized;
		for (int i = 0; i < 64; i++)
		{
			Vector3d dir = (Vector3d)(Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), (Vector3)up) * (Vector3)east);
			Vector3d world = craft + dir * (double)UnityEngine.Random.Range(minimumDistance, maximumDistance);
			Vector3d ground = ProjectToGround(world, body);
			if (!IsLandAt(ground, body) || (ground - craft).magnitude < minimumDistance)
			{
				continue;
			}
			if (MohoBodyInView(ground, body, 0.35) || MohoBodySeen(ground, body, vessel))
			{
				continue;
			}
			groundPosition = ground;
			return true;
		}
		return false;
	}

	private bool IsBulkExposedNow()
	{
		Vessel v = FlightGlobals.ActiveVessel;
		if (v == null || bulkEntityBody == null || bulkEntityObject == null)
		{
			return false;
		}
		Vector3d ground = bulkEntityBody.position + bulkEntityRelativePosition;
		return MohoBodyExposed(ground, bulkEntityBody, v, 0.0);
	}

	// Instead of vanishing in view: it runs away until nobody can see it, then it goes. Hidden already: it goes at once.
	private void LeaveBulkEntity(string why)
	{
		if (!bulkEntityActive)
		{
			return;
		}
		if (!mohoCreature || !mohoLeaveRunning || bulkMoho == null || !IsBulkExposedNow())
		{
			DespawnBulkEntity();
			return;
		}
		if (!bulkLeaving)
		{
			MohoNote("leaving on foot (" + why + ")");
		}
		bulkLeaving = true;
		bulkExposeTimer = 0f;
		bulkExposeCached = true;
		bulkEntityChasing = false;
		bulkEntityStandingDown = false;
		bulkEntityRetreating = true;
		bulkWithdrawalTimer = 120f;
		bulkEntityStatus = "RUNNING OFF";
	}
}
