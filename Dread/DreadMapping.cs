// Dread: the one place that maps director values to KK/Dread shader uniforms and to audio filter values.
// Pure C# (no UnityEngine, no director types) and C# 7.3 compatible, so the very same file is compiled by the game DLL,
// by the offline integration tests and by the Unity GPU rig.
// The curves are the ones the rig measured; if you change one, the rig and the tests judge the shipping look.
using System;

public struct DreadScreenParams
{
    public float Strength;
    public float Grade, Vignette, Grain, Chroma, SignalNoise;
    public float Tear, TearY, TearH, Block, Freeze;
    public float Sync, TrackY, Loss, Dark;
    public float Seed;
    public float Tube, Blow, Snow, Wear;   // imager feed look, its degradation, and static (EntityTracker.ViewLock.cs); 0 = off
    public float Seq;                       // colour-sequential fringes on motion (needs the frame history, DreadCamera); 0 = off

    /// <summary>True when every effect is zero (the shader would return the source texel untouched).</summary>
    public bool IsClean(float eps)
    {
        return Grade <= eps && Vignette <= eps && Grain <= eps && Chroma <= eps && SignalNoise <= eps
            && Tear <= eps && Block <= eps && Freeze <= eps && Sync <= eps && Loss <= eps && Dark <= eps
            && Tube <= eps && Blow <= eps && Snow <= eps && Seq <= eps;
    }
}

public static class DreadMapping
{
    public const float TearMax = 0.9f;
    public const float BlockMax = 0.8f;
    public const float DropoutNoiseFloor = 0.5f;
    public const float SignalPeak = 0.8f;
    public const float SignalStart = 0.35f;
    public const float ListenerClosedHz = 22000f;

    public static float C01(float v)
    {
        if (!(v > 0f)) return 0f;   // also catches NaN
        return v > 1f ? 1f : v;
    }

    // ---- continuous channels (curves measured in the GPU rig) ----

    public static float GradeCurve(float g) { return (float)Math.Pow(C01(g), 1.6); }
    public static float VignetteCurve(float v) { return (float)Math.Pow(C01(v), 1.6); }
    public static float GrainCurve(float g) { return (float)Math.Pow(C01(g), 1.5); }

    /// <summary>Signal noise ramps in from 0.35 of the director's signal value and peaks at 0.8 uniform.</summary>
    public static float SignalCurve(float s)
    {
        float t = C01((C01(s) - SignalStart) / (1f - SignalStart));
        return SignalPeak * (float)Math.Pow(t, 1.5);
    }

    /// <summary>The continuous part of the look. All inputs zero gives all uniforms exactly zero.</summary>
    public static DreadScreenParams Continuous(float grade, float vignette, float grain, float signal)
    {
        DreadScreenParams p = new DreadScreenParams();
        p.Strength = 1f;
        p.Grade = GradeCurve(grade);
        p.Vignette = VignetteCurve(vignette);
        p.Grain = GrainCurve(grain);
        p.SignalNoise = SignalCurve(signal);
        p.Sync = DreadFeed.SyncCurve(signal);
        p.TearY = 0.55f;
        p.TearH = 0.07f;
        return p;
    }

    // ---- discrete events (strength 0..1 is the director's FiredStrength) ----

    public static void Tear(ref DreadScreenParams p, float strength, float tearY)
    {
        p.Tear = TearMax * (0.5f + 0.5f * C01(strength));
        p.TearY = C01(tearY);
        p.TearH = 0.07f;
    }

    public static void Dropout(ref DreadScreenParams p, float strength)
    {
        p.Block = BlockMax * (0.5f + 0.5f * C01(strength));
        if (p.SignalNoise < DropoutNoiseFloor) p.SignalNoise = DropoutNoiseFloor;
    }

    public static void Chroma(ref DreadScreenParams p, float amount) { p.Chroma = C01(amount); }

    public static void Freeze(ref DreadScreenParams p, float amount) { p.Freeze = C01(amount); }

    /// <summary>Inside the vessel the view is glass: only grade, vignette and grain remain.</summary>
    public static void IvaOnly(ref DreadScreenParams p)
    {
        p.SignalNoise = 0f; p.Chroma = 0f; p.Tear = 0f; p.Block = 0f; p.Freeze = 0f; p.Sync = 0f; p.Loss = 0f; p.Dark = 0f;
    }

    public static void Mask(ref DreadScreenParams p, bool grade, bool vignette, bool grain, bool signal, bool freeze)
    {
        if (!grade) p.Grade = 0f;
        if (!vignette) p.Vignette = 0f;
        if (!grain) p.Grain = 0f;
        if (!signal) { p.SignalNoise = 0f; p.Chroma = 0f; p.Tear = 0f; p.Block = 0f; p.Sync = 0f; p.Loss = 0f; p.Dark = 0f; }
        if (!freeze) p.Freeze = 0f;
    }

    // ---- audio ----

    /// <summary>Listener low-pass cutoff. lowpass 0 gives exactly the open value (the filter is then removed).</summary>
    public static float LowpassCutoffHz(float lowpass, float closedHz)
    {
        float l = C01(lowpass);
        if (l <= 0f) return ListenerClosedHz;
        if (closedHz < 500f) closedHz = 500f;
        if (closedHz > ListenerClosedHz) closedHz = ListenerClosedHz;
        return ListenerClosedHz * (float)Math.Pow(closedHz / ListenerClosedHz, l);
    }

    /// <summary>Linear gain for a music duck of 0..1 reaching maxDb decibels. duck 0 gives exactly 1.</summary>
    public static float DuckGain(float duck, float maxDb)
    {
        float d = C01(duck);
        if (d <= 0f) return 1f;
        if (maxDb < 0f) maxDb = 0f;
        return (float)Math.Pow(10.0, -(maxDb * d) / 20.0);
    }

    /// <summary>IVA light factor. dip 0 gives exactly 1; noise01 (0..1) is the flicker.</summary>
    public static float IvaLightFactor(float dip, float noise01)
    {
        float d = C01(dip);
        if (d <= 0f) return 1f;
        float f = 1f - d * (0.45f + 0.35f * C01(noise01));
        return f < 0.05f ? 0.05f : f;
    }
}
