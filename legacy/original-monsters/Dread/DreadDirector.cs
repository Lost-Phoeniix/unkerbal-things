// Dread director: pure logic, no UnityEngine. Targets net40 / C# 12 (no Math.Clamp, no MathF).
// One hidden value (menace) drives every smoothed output and every discrete event.
// Deterministic for a given seed and input sequence; no allocation in Update.
using System;

public enum DreadEventKind
{
    None = 0,
    TearEvent,
    FeedFreeze,
    Dropout,
    CreakEvent,
    ThumpEvent,
    HullSilence,
    PortraitStatic,
}

public sealed class DreadSettings
{
    // Distances (metres) mapped to proximity 0..1.
    public double FarDistance = 1500.0;
    public double NearDistance = 150.0;

    // Master switches.
    public bool EventsEnabled = true;
    public bool FalseAlarmsEnabled = true;
    public double FalseAlarmRate = 1.0 / 240.0;   // per second while eligible
    public double FalseAlarmMinGap = 90.0;        // seconds between false alarms
    public double FalseAlarmMaxMenace = 0.14;     // below the lowest real event threshold

    // Peak and release.
    public double PeakMenace = 0.80;
    public double PeakHoldMin = 20.0, PeakHoldMax = 60.0;
    public double ReleaseHoldMin = 18.0, ReleaseHoldMax = 28.0;
    public double ReleaseRampIn = 3.0, ReleaseRampOut = 6.0;
    public double ReleaseDepth = 0.65;            // menace cap = 1 - depth * release

    // Final approach / docking blocking.
    public double FinalApproachHeight = 200.0;
    public double FinalApproachDescent = 2.0;     // m/s, positive = descending

    // Events.
    public double RepeatPenalty = 0.35;           // rate multiplier applied right after a kind fires
    public double PenaltyRecoveryTau = 90.0;
    public double GlobalGapMin = 1.5, GlobalGapMax = 4.0;
}

public struct DreadInput
{
    public float Dt;
    public float NearestMonsterDistance;  // metres, negative / NaN / infinity = no monster
    public bool Chasing;
    public bool LineOfSight;
    public bool AttackRunning;
    public float HeightFromTerrain;       // metres
    public float DescentRate;             // m/s, positive = descending
    public bool Docking;
    public float Throttle;                // 0..1
    public bool LightsOn;
    public bool InIVA;
    public bool SafeScene;
    // Hunt (ground monsters): Searching = walking to a noise or sweeping an area, without sight of the craft. HuntState is the DreadHunt state
    // (-1 none), BelievedDistance the distance from the monster to where it believes the craft is (-1 none). Defaults change nothing.
    public bool Searching;
    public int HuntState;
    public float BelievedDistance;
}

public struct DreadOutput
{
    public float Menace;
    public float Grade, Vignette, Grain, SignalNoise;
    public float DroneLevel, MusicDuck, Lowpass;
    public float InstrumentJitter, IvaLightDip;
    public float Release;            // 0..1 shape of the release window
    public bool RequestRelease;      // advisory: monsters should linger or withdraw
    public float Noise;              // 0..1 exposed to monsters
    public bool FinalApproach;

    // Event fired during this Update (None if none). At most one per Update.
    public DreadEventKind Fired;
    public float FiredDuration;
    public float FiredStrength;
    public bool FiredFalseAlarm;
    public bool FiredCorrelatedToThrust;

    // Event currently running.
    public DreadEventKind Active;
    public float ActiveRemaining;
    public bool FeedFrozen;
    public bool DropoutActive;
}

public sealed class DreadDirector
{
    public readonly DreadSettings Settings;
    public DreadOutput Out;

    const int KindCount = 8;
    const double MaxSub = 0.01;

    sealed class Rng
    {
        ulong s;
        public Rng(ulong seed)
        {
            // Hash the seed so neighbouring seeds do not produce shifted copies of one stream.
            ulong z = seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            s = z ^ (z >> 31);
            Next(); Next();
        }
        public ulong Next()
        {
            s += 0x9E3779B97F4A7C15UL;
            ulong z = s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double D() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
        public double Range(double a, double b) { return a + (b - a) * D(); }
    }

    struct KindDef
    {
        public double Min, Full, Rate, Cooldown, DurMin, DurMax;
        public bool Signal;     // needs the external camera feed (not in IVA)
        public bool NeedsRising;
    }

    readonly KindDef[] defs = new KindDef[KindCount];
    readonly double[] nextAllowed = new double[KindCount];
    readonly double[] penalty = new double[KindCount];
    readonly double[] cdScale = new double[KindCount];
    readonly double[] rateTmp = new double[KindCount];

    readonly Rng rngEvents;   // events and false alarms (draw count depends on dt, by design)
    readonly Rng rngRelease;  // release timing only: independent of frame rate

    // State.
    double time;
    double menace, exposure;
    double peakT, peakHold, releaseT, releaseHold, releaseShape;
    bool inRelease;
    double grade, vignette, grain, signal, drone, duck, lowpass, jitter, ivaDip, noise;
    double activeEnd, busyUntil, lastFalseAlarm = -1e9;
    DreadEventKind active;
    bool finalApproach;
    double prevThrottle, thrustAct;
    int corrCreaks;
    bool breakPending, breakDone;
    double calmTime;
    bool firstUpdate = true;
    bool firedThisUpdate;
    bool losNow, darkNow;

    public DreadDirector(int seed) : this(seed, new DreadSettings()) { }

    public DreadDirector(int seed, DreadSettings settings)
    {
        Settings = settings ?? new DreadSettings();
        rngEvents = new Rng((ulong)(uint)seed);
        rngRelease = new Rng((ulong)(uint)seed ^ 0xA5A5A5A5UL);
        // Min, Full, Rate(/s at full), Cooldown, DurMin, DurMax
        Def(DreadEventKind.TearEvent,      0.30, 0.90, 1.0 / 50, 8,  0.1, 0.5, true,  false);
        Def(DreadEventKind.FeedFreeze,     0.80, 0.95, 1.0 / 90, 25, 0.6, 1.8, true,  false);
        Def(DreadEventKind.Dropout,        0.50, 0.90, 1.0 / 70, 15, 0.5, 2.0, true,  false);
        Def(DreadEventKind.CreakEvent,     0.15, 0.60, 1.0 / 45, 20, 0.8, 1.6, false, false);
        Def(DreadEventKind.ThumpEvent,     0.30, 0.80, 1.0 / 70, 20, 0.3, 0.6, false, false);
        Def(DreadEventKind.HullSilence,    0.60, 0.85, 1.0 / 60, 60, 2.5, 5.0, false, true);
        Def(DreadEventKind.PortraitStatic, 0.55, 0.90, 1.0 / 70, 20, 1.0, 3.0, false, false);
        for (int i = 0; i < KindCount; i++) { penalty[i] = 1; cdScale[i] = 1; }
        peakHold = DrawPeakHold();
        releaseHold = DrawReleaseHold();
    }

    // Mixture of short, medium and long holds so peaks cannot be timed.
    double DrawPeakHold()
    {
        double u = rngRelease.D();
        if (u < 0.35) return rngRelease.Range(4, 14);
        if (u < 0.65) return rngRelease.Range(14, 40);
        return rngRelease.Range(40, 150);
    }
    double DrawReleaseHold()
    {
        double u = rngRelease.D();
        if (u < 0.3) return rngRelease.Range(10, 16);
        if (u < 0.7) return rngRelease.Range(16, 28);
        return rngRelease.Range(28, 50);
    }

    void Def(DreadEventKind k, double min, double full, double rate, double cd, double d0, double d1, bool signal, bool rising)
    {
        defs[(int)k] = new KindDef { Min = min, Full = full, Rate = rate, Cooldown = cd, DurMin = d0, DurMax = d1, Signal = signal, NeedsRising = rising };
    }

    /// <summary>Current repeat-penalty multiplier (1 = none) for a kind. For tests.</summary>
    public double GetPenalty(DreadEventKind k) { return penalty[(int)k]; }

    static double C01(double v) { if (!(v > 0)) return 0; return v > 1 ? 1 : v; }
    static double Sane(float v, double def) { return (float.IsNaN(v) || float.IsInfinity(v)) ? def : v; }

    static double Smooth(double v, double target, double a)
    {
        v += (target - v) * a;
        if (target <= 0 && v < 1e-4) v = 0;
        return v;
    }

    public void Update(in DreadInput inp)
    {
        double dt = inp.Dt;
        if (double.IsNaN(dt) || dt <= 0) return;
        if (dt > 0.5) dt = 0.5;

        var S = Settings;
        double d = inp.NearestMonsterDistance;
        double p = 0;
        if (!(double.IsNaN(d) || double.IsInfinity(d) || d < 0))
        {
            p = C01((S.FarDistance - d) / (S.FarDistance - S.NearDistance));
            p = p * p * (3 - 2 * p);
        }
        bool safe = inp.SafeScene;
        bool chase = inp.Chasing && !safe, los = inp.LineOfSight && !safe, attack = inp.AttackRunning && !safe;
        bool search = inp.Searching && !safe && !chase;
        if (safe) p = 0;
        double height = Sane(inp.HeightFromTerrain, 1e9), descent = Sane(inp.DescentRate, 0);
        double throttle = C01(Sane(inp.Throttle, 0));
        if (firstUpdate) { prevThrottle = throttle; firstUpdate = false; }

        // Final approach with hysteresis.
        if (!finalApproach)
        {
            if (height < S.FinalApproachHeight && descent > S.FinalApproachDescent) finalApproach = true;
        }
        else if (height > S.FinalApproachHeight * 1.2 || descent < 0.5) finalApproach = false;
        bool blockFreeze = finalApproach || inp.Docking;
        bool iva = inp.InIVA;

        // Noise (instant-ish, fast tau; not an "effect", exposed to monsters).
        double noiseTarget = C01(0.08 + 0.65 * throttle + (inp.LightsOn ? 0.27 : 0.0));
        noise += (noiseTarget - noise) * (1 - Math.Exp(-dt / 0.4));

        // Thrust activity for creak correlation (per update, from throttle slope).
        double slope = Math.Abs(throttle - prevThrottle) / dt;
        prevThrottle = throttle;
        thrustAct = C01(Math.Max(thrustAct * Math.Exp(-dt / 3.0), Math.Min(1.0, slope * 2.0)));

        // Menace target (before release cap).
        double rawT = 0.45 * p + (chase ? 0.30 * (0.5 + 0.5 * p) : 0) + (los ? 0.15 * p : 0)
                    + (attack ? 0.10 : 0) + (search ? 0.12 * (0.5 + 0.5 * p) : 0) + 0.20 * exposure * p;
        rawT = C01(rawT);
        if (p <= 0 && !chase && !attack) rawT = 0;

        int n = (int)Math.Ceiling(dt / MaxSub);
        if (n < 1) n = 1;
        double h = dt / n;
        firedThisUpdate = false;
        losNow = los;
        darkNow = noiseTarget < 0.3;
        Out.Fired = DreadEventKind.None;
        Out.FiredDuration = 0; Out.FiredStrength = 0; Out.FiredFalseAlarm = false; Out.FiredCorrelatedToThrust = false;

        for (int i = 0; i < n; i++)
        {
            time += h;
            Step(h, p, rawT, safe, chase, iva, blockFreeze, throttle);
        }

        // Publish.
        Out.Menace = (float)C01(menace);
        Out.Grade = (float)C01(grade);
        Out.Vignette = (float)C01(vignette);
        Out.Grain = (float)C01(grain);
        Out.SignalNoise = (float)C01(signal);
        Out.DroneLevel = (float)C01(drone);
        Out.MusicDuck = (float)C01(duck);
        Out.Lowpass = (float)C01(lowpass);
        Out.InstrumentJitter = (float)C01(jitter);
        Out.IvaLightDip = (float)C01(ivaDip);
        Out.Release = (float)C01(releaseShape);
        Out.RequestRelease = inRelease;
        Out.Noise = (float)C01(noise);
        Out.FinalApproach = finalApproach;
        Out.Active = active;
        Out.ActiveRemaining = active == DreadEventKind.None ? 0f : (float)Math.Max(0, activeEnd - time);
        Out.FeedFrozen = active == DreadEventKind.FeedFreeze;
        Out.DropoutActive = active == DreadEventKind.Dropout;
    }

    void Step(double h, double p, double rawT, bool safe, bool chase, bool iva, bool blockFreeze, double throttle)
    {
        var S = Settings;

        // Exposure: slow rise while near.
        if (p > 0.3 && !safe) exposure += h / 60.0; else exposure -= h / 30.0;
        exposure = C01(exposure);
        if (p <= 0 && exposure < 1e-4) exposure = 0;

        // Release window state machine (driven by menace only).
        if (safe) { inRelease = false; releaseT = 0; peakT = 0; }
        else if (!inRelease)
        {
            if (menace > S.PeakMenace) peakT += h * (1.0 + (losNow ? 0.0 : 2.0) + (darkNow ? 2.0 : 0.0));
            else if (menace < 0.6) peakT = Math.Max(0, peakT - 0.5 * h);
            if (peakT >= peakHold)
            {
                inRelease = true; releaseT = 0;
                releaseHold = DrawReleaseHold();
            }
        }
        else
        {
            releaseT += h;
            if (releaseT >= S.ReleaseRampIn + releaseHold)
            {
                inRelease = false; peakT = 0;
                peakHold = DrawPeakHold();
            }
        }
        double shape;
        if (inRelease) shape = C01(releaseT / S.ReleaseRampIn);
        else shape = 0;
        // Ramp out after the window: release shape decays smoothly via its own smoother.
        double shapeTarget = inRelease ? shape : 0;
        double shapeTau = inRelease ? 0.5 : S.ReleaseRampOut;
        releaseShape += (shapeTarget - releaseShape) * (1 - Math.Exp(-h / shapeTau));
        if (releaseShape < 1e-4 && !inRelease) releaseShape = 0;

        double T = rawT;
        double cap = 1.0 - S.ReleaseDepth * releaseShape;
        if (T > cap) T = cap;
        if (safe) T = 0;

        double tau = T >= menace ? 4.0 : (releaseShape > 0.05 ? 6.0 : (safe ? 1.5 : 12.0));
        if (releaseShape > 0.05 && T < menace) tau = 6.0;
        menace = Smooth(menace, T, 1 - Math.Exp(-h / tau));
        menace = C01(menace);

        // Calm tracking resets the chase-local creak state.
        if (menace == 0) calmTime += h; else calmTime = 0;
        if (calmTime > 30.0) { corrCreaks = 0; breakPending = false; breakDone = false; }

        // Continuous outputs.
        double m = menace;
        double sigT = iva ? 0 : m * (0.4 + 0.6 * p);
        double hh = safe ? h * 4.0 : h;   // leaving to a safe scene clears effects four times faster
        grade = Smooth(grade, m, 1 - Math.Exp(-hh / 4.0));
        vignette = Smooth(vignette, m, 1 - Math.Exp(-hh / 2.5));
        grain = Smooth(grain, m, 1 - Math.Exp(-hh / 1.5));
        signal = Smooth(signal, sigT, 1 - Math.Exp(-hh / 0.8));
        drone = Smooth(drone, m, 1 - Math.Exp(-hh / 3.0));
        duck = Smooth(duck, m, 1 - Math.Exp(-hh / 2.0));
        lowpass = Smooth(lowpass, m * m, 1 - Math.Exp(-hh / 4.0));
        ivaDip = Smooth(ivaDip, iva ? m : 0, 1 - Math.Exp(-hh / 1.5));

        // Active event end / cancel.
        if (active != DreadEventKind.None)
        {
            bool cancel = ((active == DreadEventKind.FeedFreeze || active == DreadEventKind.Dropout) && blockFreeze) || safe
                          || (iva && defs[(int)active].Signal);
            if (cancel || time >= activeEnd)
            {
                active = DreadEventKind.None;
                if (cancel) busyUntil = Math.Max(busyUntil, time + 1.0);
            }
        }

        // Jitter: only during a dropout, never in final approach / docking.
        if (blockFreeze || safe || iva) jitter = 0;
        else
        {
            bool dropping = active == DreadEventKind.Dropout;
            double jT = dropping ? 0.35 + 0.65 * m : 0;
            jitter = Smooth(jitter, jT, 1 - Math.Exp(-h / (dropping ? 0.15 : 0.4)));
        }

        // Penalty recovery.
        double rec = 1 - Math.Exp(-h / S.PenaltyRecoveryTau);
        for (int k = 1; k < KindCount; k++) penalty[k] += (1 - penalty[k]) * rec;

        if (!S.EventsEnabled || safe || firedThisUpdate) return;
        if (active != DreadEventKind.None || time < busyUntil) return;

        // Real events (single hazard draw over all eligible kinds).
        bool rising = rawT > menace + 0.01;
        double total = 0;
        for (int k = 1; k < KindCount; k++)
        {
            double r = 0;
            var df = defs[k];
            if (m >= df.Min && time >= nextAllowed[k] && !(df.Signal && iva)
                && !(((DreadEventKind)k == DreadEventKind.FeedFreeze || (DreadEventKind)k == DreadEventKind.Dropout) && blockFreeze)
                && !(df.NeedsRising && !(rising && !inRelease)))
            {
                double f = C01((m - df.Min) / (df.Full - df.Min));
                r = df.Rate * f * penalty[k];
                if ((DreadEventKind)k == DreadEventKind.CreakEvent) r *= CreakFactor();
                if (inRelease && (DreadEventKind)k != DreadEventKind.CreakEvent) r *= 0.3;
            }
            rateTmp[k] = r; total += r;
        }
        if (total > 0 && rngEvents.D() < 1 - Math.Exp(-total * h))
        {
            double pick = rngEvents.D() * total, acc = 0;
            int chosen = 0;
            for (int k = 1; k < KindCount; k++)
            {
                if (rateTmp[k] <= 0) continue;
                chosen = k; acc += rateTmp[k];
                if (pick <= acc) break;
            }
            if (chosen != 0) { Fire((DreadEventKind)chosen, m, false, iva); return; }
        }

        // False alarm: rare, low menace only, rate limited.
        if (S.FalseAlarmsEnabled && m > 0.005 && m <= S.FalseAlarmMaxMenace && time - lastFalseAlarm >= S.FalseAlarmMinGap
            && rngEvents.D() < 1 - Math.Exp(-S.FalseAlarmRate * h))
        {
            int pick = (int)(rngEvents.D() * (iva ? 2 : 3));
            DreadEventKind kind = pick == 0 ? DreadEventKind.CreakEvent : pick == 1 ? DreadEventKind.ThumpEvent : DreadEventKind.TearEvent;
            lastFalseAlarm = time;
            Fire(kind, m, true, iva);
        }
    }

    double CreakFactor()
    {
        if (corrCreaks < 3) return 0.25 + 4.0 * thrustAct;          // follows thrust at first
        if (!breakDone) { breakPending = true; return thrustAct < 0.1 ? 2.5 : 0.2; } // then break it once
        return 1.0;
    }

    void Fire(DreadEventKind kind, double m, bool falseAlarm, bool iva)
    {
        int k = (int)kind;
        var df = defs[k];
        double dur = rngEvents.Range(df.DurMin, df.DurMax);
        double strength = falseAlarm ? 0.4 : 0.4 + 0.6 * m;
        activeEnd = time + dur;
        active = kind;
        busyUntil = activeEnd + rngEvents.Range(Settings.GlobalGapMin, Settings.GlobalGapMax);
        nextAllowed[k] = time + df.Cooldown * rngEvents.Range(0.8, 1.5);
        penalty[k] *= Settings.RepeatPenalty;
        bool corr = false;
        if (kind == DreadEventKind.CreakEvent)
        {
            corr = thrustAct > 0.3;
            if (!falseAlarm)
            {
                if (corrCreaks < 3) { if (corr) corrCreaks++; }
                else if (!breakDone && thrustAct < 0.1) { breakDone = true; breakPending = false; }
            }
        }
        firedThisUpdate = true;
        Out.Fired = kind; Out.FiredDuration = (float)dur; Out.FiredStrength = (float)strength;
        Out.FiredFalseAlarm = falseAlarm; Out.FiredCorrelatedToThrust = corr;
    }
}





