using System;
using System.Globalization;
using KSP.Localization;
using UnityEngine;
using UnkerbalThings.Dread;

// The worm swarm science loop. Placeholder parts (rescaled stock models, configs in
// Parts/UnkerbalThings-WormScience.cfg); experiments in Parts/UnkerbalThings-WormScienceDefs.cfg. The rules live in
// Dread/WormScience.cs, the swarm queries in EntityTracker.WormHunt.cs.
//
// No message or part text names the swarm's hearing ranges. The scanner shows its own working range and
// your distance, as designed; the spotter shows bearing and distance only. With wormReadoutNoise both are rough
// and garbled by the swarm's interference (Dread/WormScience.cs, WormReadout), less so on parts with more `shielding`.

/// <summary>Spotter (tech-tree unlock): points toward the swarm while the craft is low enough. Unlimited range. With
/// wormReadoutNoise the reading is rough and scrolls past as a ticker; the swarm's interference makes it worse up close, and this
/// small part has almost no shielding, so near the swarm it is next to useless (the trade-off for its weight).</summary>
public class ModuleUTWormSpotter : PartModule
{
	[KSPField(guiActive = true, guiName = "Swarm")]
	public string swarmReading = "-";
	/// <summary>0..1: how much of the swarm's interference the part's casing holds back (WormReadout.Corruption).</summary>
	[KSPField] public float shielding = 0.1f;

	const int TickerWidth = 24;
	float timer, scrollTimer, lastRead;
	int step;
	string message = "-";
	float corruption;
	WormReadout.Drift headingDrift, rangeDrift;
	System.Random rng;

	public override void OnStart(StartState state)
	{
		int seed = part != null ? (int)(part.flightID ^ 0x5eed) : 1;
		headingDrift = new WormReadout.Drift(seed);
		rangeDrift = new WormReadout.Drift(seed * 31 + 7);
		rng = new System.Random(seed * 17 + 3);
	}

	public override void OnUpdate()
	{
		if (headingDrift == null) OnStart(StartState.Flying);
		timer -= Time.deltaTime;
		if (timer <= 0f)
		{
			timer = 0.5f;
			message = Read();
		}
		if (!EntityTracker.WormReadoutNoise) { swarmReading = message; return; }
		scrollTimer -= Time.deltaTime;
		if (scrollTimer > 0f) return;
		scrollTimer = 0.16f;
		step++;
		swarmReading = WormReadout.Garble(WormReadout.Ticker(message, TickerWidth, step), corruption * 0.45f, rng);
	}

	string Read()
	{
		corruption = 0f;
		float dt = Mathf.Max(0.05f, Time.time - lastRead);
		lastRead = Time.time;
		Vessel v = vessel;
		EntityTracker t = EntityTracker.Instance;
		bool noisy = EntityTracker.WormReadoutNoise;
		if (v == null || t == null) return noisy ? "NO SIGNAL" : "no signal";
		double above = v.heightFromTerrain >= 0f ? v.heightFromTerrain : v.altitude - Math.Max(0.0, v.terrainAltitude);
		if (above > EntityTracker.WormSpotterMaxAltitude) return noisy ? "NO GROUND CONTACT" : "too high";
		Vector3d swarm; double dist; bool heap, visible;
		if (!t.WormSwarmFrom(v, part.transform.position, out swarm, out dist, out heap, out visible)) return noisy ? "GROUND QUIET" : "nothing";
		Vector3d here = part.transform.position;
		Vector3d up = (here - v.mainBody.position).normalized;
		Vector3d north = Vector3d.Exclude(up, v.mainBody.transform.up).normalized;
		Vector3d east = Vector3d.Cross(up, north).normalized;
		Vector3d d = swarm - here;
		double bearing = WormScience.Bearing(Vector3d.Dot(d, east), Vector3d.Dot(d, north));
		CultureInfo c = CultureInfo.InvariantCulture;
		if (!noisy)
		{
			string range = dist >= 1000.0 ? (dist / 1000.0).ToString("F1", c) + " km" : dist.ToString("F0", c) + " m";
			return bearing.ToString("F0", c) + "° " + WormScience.Compass(bearing) + ", " + range;
		}
		float interference = EntityTracker.WormReadoutInterference(dist);
		corruption = WormReadout.Corruption(interference, shielding);
		if (corruption >= 0.85f && rng.NextDouble() < 0.4) return "-- NO CARRIER --";
		double err = headingDrift.Step(dt, WormReadout.HeadingSpread(corruption));
		double rnoise = rangeDrift.Step(dt, 1.0);
		int bars = 1 + Mathf.RoundToInt(4f * interference);
		return "MOVEMENT " + WormReadout.Heading(bearing + err, corruption) + "  " + WormReadout.Range(dist, corruption, rnoise)
			+ "  SIG " + new string('|', bars) + new string('.', 5 - bars);
	}
}

/// <summary>Seismic signal generator: one pulse into the ground and the swarm spots you at once, wherever you are. Enables the
/// behaviour study (ModuleUTWormExperiment wormTask = study) for a while.</summary>
public class ModuleUTSeismicGenerator : PartModule
{
	[KSPField] public float cooldownSeconds = 30f;
	[KSPField(guiActive = true, guiName = "Generator")]
	public string generatorStatus = "ready";

	float cooldown;

	[KSPEvent(guiActive = true, guiName = "Fire seismic pulse")]
	public void FirePulse()
	{
		if (vessel == null) return;
		if (!vessel.Landed) { ScreenMessages.PostScreenMessage("The generator must stand on the ground.", 4f, ScreenMessageStyle.UPPER_CENTER); return; }
		if (cooldown > 0f) { ScreenMessages.PostScreenMessage("The generator is recharging.", 3f, ScreenMessageStyle.UPPER_CENTER); return; }
		cooldown = cooldownSeconds;
		EntityTracker t = EntityTracker.Instance;
		if (t != null) t.WormSeismicPulse(vessel);
		// the thumper foot strikes the ground: clip "thump" on the part's model (Parts/utSeismicGenerator.mu); a model without it plays nothing
		Animation[] thumpers = part != null ? part.FindModelAnimators("thump") : null;
		if (thumpers != null)
			for (int i = 0; i < thumpers.Length; i++)
				if (thumpers[i] != null) { thumpers[i].Stop(); thumpers[i].Play("thump"); }
		ScreenMessages.PostScreenMessage("Seismic pulse sent into the ground.", 4f, ScreenMessageStyle.UPPER_CENTER);
	}

	[KSPAction("Fire seismic pulse")]
	public void FirePulseAction(KSPActionParam param) { FirePulse(); }

	public override void OnUpdate()
	{
		if (cooldown > 0f) cooldown -= TimeWarp.deltaTime;
		generatorStatus = cooldown > 0f ? "recharging " + Mathf.CeilToInt(cooldown) + " s" : "ready";
	}
}

/// <summary>The worm experiments on top of the stock experiment module (data storage, transmit, review come from it). Every one
/// takes time (idea from the Time For Science mod, MIT, no code from it): the button starts a run while the
/// condition holds, the button then counts down, the condition must hold the whole time (a lapse aborts it), and when the time is
/// up the stock deploy completes it. wormTask: scan (within wormScanMetres of the heap), photo / energy (not landed, the swarm in
/// view and near), seismic (landed while the swarm is about: the risky one), study (after the seismic signal generator brought it).
/// Time runs with the game clock (TimeWarp.deltaTime, at most 0.25 s a frame, like the swarm's own brain), so warping does not
/// skip the danger.</summary>
public class ModuleUTWormExperiment : ModuleScienceExperiment
{
	[KSPField] public string wormTask = "scan";
	[KSPField(guiActive = true, guiName = "Swarm experiment")]
	public string wormStatus = "";

	WormRun run;
	/// <summary>A timed run is under way (the feed overlay shows REC).</summary>
	public bool IsRunning { get { return run != null && run.Running; } }
	float uiTimer, uiStep = 0.2f;
	/// <summary>0..1: how much of the swarm's interference the part's casing holds back (WormReadout.Corruption). Big parts: mostly shielding.</summary>
	[KSPField] public float shielding = 0.5f;
	System.Random rng = new System.Random();
	WormReadout.Drift rangeDrift = new WormReadout.Drift(Environment.TickCount);
	string actionName = "Run";

	public override void OnStart(StartState state)
	{
		DeployEventDisabled = true;   // the stock deploy button and action stay off; UTRun runs the timer and the swarm checks first
		base.OnStart(state);
		experiment = ResearchAndDevelopment.GetExperiment(experimentID);   // the base skips this while the deploy event is off
		actionName = Localizer.Format(experimentActionName);
		Fields["wormStatus"].guiName = wormTask == "scan" ? "Scanner" : actionName;
		Actions["UTRunAction"].guiName = actionName;
		Events["UTCancel"].guiName = "Cancel " + TaskNoun.ToLowerInvariant();
		RefreshUI();
	}

	/// <summary>What the run is called in messages ("Swarm scan started", not "Start Swarm Scan: started").</summary>
	string TaskNoun
	{
		get
		{
			switch (wormTask)
			{
			case "scan": return "Swarm scan";
			case "photo": return "Photography";
			case "energy": return "Energy recording";
			case "seismic": return "Seismic recording";
			case "study": return "Behaviour study";
			default: return "Experiment";
			}
		}
	}

	float RunSeconds
	{
		get
		{
			switch (wormTask)
			{
			case "scan": return EntityTracker.WormScanSeconds;
			case "photo": return EntityTracker.WormPhotoSeconds;
			case "energy": return EntityTracker.WormEnergySeconds;
			case "seismic": return EntityTracker.WormSeismicSeconds;
			default: return EntityTracker.WormStudyRunSeconds;
			}
		}
	}

	[KSPEvent(guiActive = true, guiName = "Run")]
	public void UTRun()
	{
		if (run != null && run.Running) return;   // the button is the countdown while running
		string why;
		bool ok = Condition(out why);
		run = new WormRun(RunSeconds);
		if (!run.Start(ok)) { Say(why); run = null; return; }
		Say(TaskNoun + " started, " + WormScience.Clock(run.Seconds) + (wormTask == "scan" ? ". Hold within " + EntityTracker.WormScanMetres.ToString("F0", CultureInfo.InvariantCulture) + " m." : "."));
		if (run.Done) Finish();
		RefreshUI();
	}

	[KSPAction("Run")]
	public void UTRunAction(KSPActionParam param) { UTRun(); }

	[KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 1.5f, guiName = "Cancel", active = false)]
	public void UTCancel()
	{
		if (run == null || !run.Running) return;
		run = null;
		Say(TaskNoun + " cancelled.");
		RefreshUI();
	}

	static void Say(string s) { if (!string.IsNullOrEmpty(s)) ScreenMessages.PostScreenMessage(s, 4f, ScreenMessageStyle.UPPER_CENTER); }

	/// <summary>Straight-line distance from this part to the heap; infinity when there is no heap.</summary>
	double HeapDistance()
	{
		EntityTracker t = EntityTracker.Instance;
		Vector3d sw; double d; bool heap, visible;
		if (t == null || vessel == null || !t.WormSwarmFrom(vessel, part.transform.position, out sw, out d, out heap, out visible) || !heap) return double.PositiveInfinity;
		return d;
	}

	/// <summary>Whether the experiment can run right now; why not (never naming the swarm's hearing ranges).</summary>
	bool Condition(out string why)
	{
		why = null;
		if (vessel == null) { why = "No vessel."; return false; }
		EntityTracker t = EntityTracker.Instance;
		Vector3d sw; double d = double.PositiveInfinity; bool heap, visible = false;
		bool there = t != null && t.WormSwarmFrom(vessel, part.transform.position, out sw, out d, out heap, out visible);
		switch (wormTask)
		{
		case "scan":
		{
			double hd = HeapDistance();
			if (double.IsInfinity(hd)) { why = "The scanner finds no swarm heap."; return false; }
			if (hd > EntityTracker.WormScanMetres) { why = "Out of the scanner's range."; return false; }
			return true;
		}
		case "photo":
		case "energy":
			if (vessel.Landed) { why = "This needs to be taken from the air."; return false; }
			if (!there || !visible || d > (wormTask == "photo" ? EntityTracker.WormPhotoMetres : EntityTracker.WormEnergyMetres)) { why = "Nothing to record here."; return false; }
			return true;
		case "seismic":
			if (!vessel.Landed) { why = "The geophones must be on the ground."; return false; }
			if (!there) { why = "The ground is silent."; return false; }
			return true;
		case "study":
			if (t == null || !t.WormSeismicRecent) { why = "Fire the seismic signal generator first."; return false; }
			if (!t.WormStudyReady()) { why = "Nothing has answered the pulse yet."; return false; }
			return true;
		}
		why = "Unknown experiment.";
		return false;
	}

	void Finish()
	{
		run.Consume();
		run = null;
		DeployExperiment();   // the stock completion: animation, data, results dialog, transmit
		RefreshUI();
	}

	public override void OnUpdate()
	{
		if (run != null && run.Running)
		{
			string why;
			bool ok = Condition(out why);
			run.Step(Mathf.Min(TimeWarp.deltaTime, 0.25f), ok);
			if (run.Aborted) { Say(TaskNoun + " stopped: " + why); run = null; RefreshUI(); }
			else if (run.Done) { Finish(); }
		}
		uiTimer -= Time.deltaTime;
		if (uiTimer > 0f) return;
		uiTimer = 0.2f;
		RefreshUI();
	}

	void RefreshUI()
	{
		bool running = run != null && run.Running;
		CultureInfo c = CultureInfo.InvariantCulture;
		BaseEvent go = Events["UTRun"];
		go.guiName = running ? actionName + ": " + WormScience.Clock(run.Remaining) + " left"
			: actionName + " (" + WormScience.Clock(RunSeconds) + (wormTask == "scan" ? ", within " + EntityTracker.WormScanMetres.ToString("F0", c) + " m" : "") + ")";
		go.active = true;
		Events["UTCancel"].active = running;
		bool noisy = EntityTracker.WormReadoutNoise;
		float corruption = noisy ? Corruption() : 0f;
		string scanRange = null;
		if (wormTask == "scan")
		{
			double d = HeapDistance();
			string heap;
			if (!noisy) heap = double.IsInfinity(d) ? "-" : d.ToString("F0", c) + " m";
			else heap = double.IsInfinity(d) ? "--" : WormReadout.ShortRange(d, corruption, rangeDrift.Step(Mathf.Max(0.05f, uiStep), 1.0));
			scanRange = "range " + EntityTracker.WormScanMetres.ToString("F0", c) + " m, heap " + heap;
		}
		Fields["wormStatus"].guiActive = running || scanRange != null;
		string status = running ? "running " + run.Elapsed.ToString("F0", c) + "/" + run.Seconds.ToString("F0", c) + " s" + (scanRange != null ? ", " + scanRange : "") : (scanRange ?? "");
		// the swarm's interference drops letters; the countdown on the button stays clean so the run can still be judged
		wormStatus = noisy ? WormReadout.Garble(status, corruption * 0.4f, rng) : status;
	}

	/// <summary>How corrupt this part's readout is now: the swarm's interference at its distance less the part's shielding.</summary>
	float Corruption()
	{
		EntityTracker t = EntityTracker.Instance;
		Vector3d sw; double d; bool heap, visible;
		if (t == null || vessel == null || !t.WormSwarmFrom(vessel, part.transform.position, out sw, out d, out heap, out visible)) return 0f;
		return WormReadout.Corruption(EntityTracker.WormReadoutInterference(d), shielding);
	}
}
