// Dread: the heavy channels of the failing feed (sync, signal loss, gain loss) and their envelopes.
// Pure C# like DreadMapping.cs; the game DLL, the offline tests and the GPU rig all compile this very file.
// Photosensitivity: light only ever goes away fast and comes back slowly. Envelope is the one place that guarantees that.
using System;

public static class DreadFeed
{
    public const float SyncStart = 0.55f;
    public const float AttackSeconds = 0.05f;
    public const float ReleaseSeconds = 0.55f;

    // what each event takes away, at event strength 1
    public const float TearDark = 0.2f, TearLoss = 0.2f;
    public const float DropoutDark = 0.3f, DropoutLossMin = 0.6f, DropoutLossMax = 0.95f;
    public const float FreezeDark = 0.4f, FreezeLoss = 0.15f, FreezeSync = 0.9f;

    /// <summary>Sync instability only starts when the threat is already high, then rises fast.</summary>
    public static float SyncCurve(float signal)
    {
        float s = DreadMapping.C01(signal);
        float t = DreadMapping.C01((s - SyncStart) / (1f - SyncStart));
        return (float)Math.Pow(t, 1.2);
    }

    public static float DropoutLoss(float strength)
    {
        return DropoutLossMin + (DropoutLossMax - DropoutLossMin) * DreadMapping.C01(strength);
    }

    /// <summary>One step of a darkening envelope: rises (picture darkens) in about 50 ms, falls (picture recovers) in about 550 ms, exactly zero at the end.</summary>
    public static float Envelope(float value, float target, float dt)
    {
        if (!(dt > 0f)) return value;
        float tau = target > value ? AttackSeconds : ReleaseSeconds;
        value += (target - value) * (1f - (float)Math.Exp(-dt / tau));
        if (target <= 0f && value < 0.002f) value = 0f;
        return value;
    }

    /// <summary>The tracking band creeps up the picture, faster the worse the sync is.</summary>
    public static float TrackStep(float trackY, float sync, float dt)
    {
        if (!(dt > 0f)) return trackY;
        float y = trackY + dt * (0.03f + 0.1f * DreadMapping.C01(sync));
        return y - (float)Math.Floor(y);
    }
}
