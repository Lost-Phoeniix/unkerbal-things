// Dread: pure integration logic (no UnityEngine): the per-frame screen driver, restore bookkeeping, scream rate limit
// and instrument jitter. Everything here is exercised by the offline integration tests.
// Depends on DreadDirector.cs (DreadOutput, DreadEventKind) and DreadMapping.cs.
using System;

public struct DreadScreenToggles
{
    public bool Grade, Vignette, Grain, Signal, Freeze;

    public static DreadScreenToggles AllOn()
    {
        DreadScreenToggles t = new DreadScreenToggles();
        t.Grade = true; t.Vignette = true; t.Grain = true; t.Signal = true; t.Freeze = true;
        return t;
    }
}

/// <summary>Turns the director's output into shader parameters, once per frame. Owns the event envelopes (tear row, chroma flash,
/// freeze capture). Parameters are exactly zero whenever the director's output is zero.</summary>
public sealed class DreadScreenDriver
{
    public DreadScreenParams Params;
    /// <summary>True for exactly one frame per feed freeze: the renderer must store the live frame before drawing.</summary>
    public bool CaptureFrame;

    DreadEventKind lastActive;
    float eventAge, eventStrength, tearY = 0.55f;
    float lossEnv, darkEnv, trackY = 0.3f;
    bool freezeCaptured;
    int tearHold;
    uint rng = 2463534242u;

    public void Reset()
    {
        Params = new DreadScreenParams();
        CaptureFrame = false;
        lastActive = DreadEventKind.None;
        eventAge = 0f; eventStrength = 0f; freezeCaptured = false; tearHold = 0;
        lossEnv = 0f; darkEnv = 0f;
    }

    float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }

    public void Update(float dt, in DreadOutput o, bool iva, DreadScreenToggles t, float strength, int frame)
    {
        DreadScreenParams p = DreadMapping.Continuous(o.Grade, o.Vignette, o.Grain, o.SignalNoise);
        CaptureFrame = false;
        DreadEventKind kind = o.Active;
        if (kind != lastActive)
        {
            lastActive = kind;
            eventAge = 0f;
            eventStrength = (o.Fired == kind) ? o.FiredStrength : 0.7f;
            freezeCaptured = false;
            tearHold = 0;
        }
        else if (o.Fired == kind && kind != DreadEventKind.None)
        {
            eventStrength = o.FiredStrength;
        }
        if (dt > 0f && dt < 1f) eventAge += dt;
        float lossTarget = 0f, darkTarget = 0f;

        if (!iva)
        {
            switch (kind)
            {
                case DreadEventKind.TearEvent:
                    if (tearHold <= 0) { tearY = 0.12f + 0.76f * Rand(); tearHold = 2; }
                    tearHold--;
                    DreadMapping.Tear(ref p, eventStrength, tearY);
                    lossTarget = DreadFeed.TearLoss * eventStrength; darkTarget = DreadFeed.TearDark * eventStrength;
                    if (eventAge < 0.1f) DreadMapping.Chroma(ref p, 1f);
                    break;
                case DreadEventKind.Dropout:
                    DreadMapping.Dropout(ref p, eventStrength);
                    lossTarget = DreadFeed.DropoutLoss(eventStrength); darkTarget = DreadFeed.DropoutDark;
                    if (eventAge < 0.1f) DreadMapping.Chroma(ref p, 1f);
                    break;
                case DreadEventKind.FeedFreeze:
                    if (!freezeCaptured) { freezeCaptured = true; CaptureFrame = t.Freeze; }
                    else
                    {
                        DreadMapping.Freeze(ref p, 1f);
                        if (t.Freeze) { lossTarget = DreadFeed.FreezeLoss; darkTarget = DreadFeed.FreezeDark; p.Sync = Math.Max(p.Sync, DreadFeed.FreezeSync); }
                    }
                    break;
            }
        }
        else
        {
            DreadMapping.IvaOnly(ref p);
        }
        lossEnv = DreadFeed.Envelope(lossEnv, iva ? 0f : lossTarget, dt);
        darkEnv = DreadFeed.Envelope(darkEnv, iva ? 0f : darkTarget, dt);
        trackY = DreadFeed.TrackStep(trackY, p.Sync, dt);
        p.Loss = iva ? 0f : lossEnv;
        p.Dark = iva ? 0f : darkEnv;
        p.TrackY = trackY;
        DreadMapping.Mask(ref p, t.Grade, t.Vignette, t.Grain, t.Signal, t.Freeze);
        p.Strength = DreadMapping.C01(strength);
        p.Seed = (float)(frame & 0xFFFFF);
        Params = p;
    }
}

/// <summary>Remembers the value a game system had before we scaled it, restores it exactly, and notices when the
/// owner changed it in the meantime (then the owner's new value becomes the one to scale and to restore).</summary>
public sealed class DreadScaleBook
{
    public bool Active;
    public float Original, Last;

    /// <summary>Returns true when the caller must write <paramref name="value"/>. factor 1 restores the original exactly.</summary>
    public bool Apply(float current, float factor, out float value)
    {
        value = current;
        bool identity = factor >= 0.99999f;
        if (!Active)
        {
            if (identity) return false;
            Active = true; Original = current;
        }
        else if (current != Last)
        {
            Original = current;
        }
        if (identity)
        {
            Active = false;
            value = Original;
            Last = Original;
            return value != current;
        }
        value = Original * factor;
        Last = value;
        return true;
    }

    /// <summary>Gives the original back unless the owner has changed the value since we wrote it.</summary>
    public bool Release(float current, out float value)
    {
        value = current;
        if (!Active) return false;
        Active = false;
        if (current != Last) return false;
        value = Original;
        return value != current;
    }
}

/// <summary>Limits a looping scream: at most maxSeconds of it per run, then silence for cooldownSeconds.
/// Call once per frame with whether the monster wants the sound and the elapsed step; returns whether it may play.</summary>
public sealed class DreadScreamGate
{
    float played, cooldownLeft;
    bool playing;

    public bool Allow(bool wanted, float dt, float maxSeconds, float cooldownSeconds)
    {
        if (!(dt > 0f)) dt = 0f;
        if (dt > 1f) dt = 1f;
        if (cooldownLeft > 0f) { cooldownLeft -= dt; if (cooldownLeft < 0f) cooldownLeft = 0f; }
        if (!wanted)
        {
            if (playing) { playing = false; cooldownLeft = Math.Max(cooldownLeft, cooldownSeconds * 0.5f); }
            return false;
        }
        if (playing)
        {
            played += dt;
            if (played >= maxSeconds) { playing = false; cooldownLeft = cooldownSeconds; return false; }
            return true;
        }
        if (cooldownLeft <= 0f) { playing = true; played = 0f; return true; }
        return false;
    }

    public void Reset() { playing = false; played = 0f; cooldownLeft = 0f; }
}

/// <summary>Small smoothed random jitter for the display-only navball and altimeter. Exactly zero at amount 0.</summary>
public sealed class DreadJitter
{
    public const float NavDegrees = 4f;
    public const float AltRelative = 0.02f;
    public const float AltMetres = 3f;

    public float NavX, NavY, NavZ, AltRel, AltAbs;
    float tx, ty, tz, ta, tb, timer;
    uint rng = 88172645u;

    float Rand2() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return ((rng & 0xFFFFFFu) / 16777216f) * 2f - 1f; }

    public void Update(float dt, float amount)
    {
        if (!(amount > 0f))
        {
            NavX = NavY = NavZ = AltRel = AltAbs = 0f;
            tx = ty = tz = ta = tb = 0f; timer = 0f;
            return;
        }
        if (amount > 1f) amount = 1f;
        timer -= dt;
        if (timer <= 0f)
        {
            timer = 0.06f + 0.10f * (0.5f + 0.5f * Rand2());
            tx = Rand2() * NavDegrees; ty = Rand2() * NavDegrees; tz = Rand2() * NavDegrees * 0.5f;
            ta = Rand2() * AltRelative; tb = Rand2() * AltMetres;
        }
        float k = 1f - (float)Math.Exp(-Math.Max(dt, 0f) / 0.04f);
        NavX += (tx * amount - NavX) * k; NavY += (ty * amount - NavY) * k; NavZ += (tz * amount - NavZ) * k;
        AltRel += (ta * amount - AltRel) * k; AltAbs += (tb * amount - AltAbs) * k;
    }

    /// <summary>Noise 0..1 for the IVA light flicker, smoothed slowly.</summary>
    public float FlickerState;
    public void UpdateFlicker(float dt, float dip)
    {
        if (!(dip > 0f)) { FlickerState = 0f; return; }
        float target = (Rand2() * 0.5f + 0.5f);
        FlickerState += (target - FlickerState) * (1f - (float)Math.Exp(-Math.Max(dt, 0f) / 0.12f));
    }
}
