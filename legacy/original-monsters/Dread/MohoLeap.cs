// Moho creature: the ballistic leap, planned against the terrain. Pure C# (no UnityEngine); tested offline.
//
// A leap is a parabola under the body's real gravity: launched at the ground height h0 with vertical speed vz0 = sqrt(2 g peak) and a
// constant horizontal speed along the heading. Nothing steers in the air. The plan is made at take-off: the path is sampled against the
// terrain (the same heights the monster is projected onto), the peak is raised (up to a cap) when the path would graze a rise, and a plan
// that clips terrain, lands in the sea or takes too long is refused. During the flight the height above ground is
// (h0 + vz0 t - g t2 / 2) minus the ground height under the monster, never below zero, so it cannot sink into terrain whatever the caller does.
using System;

namespace KrakensKreations.Dread
{
    public sealed class MohoLeapSettings
    {
        public float MinPeak = 40f;          // lowest peak above the launch ground (m)
        public float RunPeakMax = 70f;       // highest peak of a running leap
        public float CatchMaxHeight = 60f;   // highest craft (above terrain) a catch leap goes for
        public float AirSpeedCap = 70f;      // horizontal speed kept in the air (m/s)
        public float RunMeanGapSeconds = 14f;// average time between running leaps while sprinting
        public float Clearance = 6f;         // path stays this far above the ground
        public float MaxDuration = 30f;      // longer than this is refused
        public float GravityScale = 1f;      // of the body's surface gravity
        public float CatchRadius = 35f;      // craft this close to the body centre at the top is caught
        public float CatchRange = 160f;      // horizontal distance from which the catch leap starts
    }

    public sealed class MohoLeap
    {
        public bool Active;
        public bool Final;                   // catch leap (as opposed to a running leap)
        public float Elapsed, Duration, ApexTime, Peak, Vz0, G, Vh;
        public double H0, X0, Y0, Heading, LandX, LandY, LandGround;
        public float MinClearance = float.MaxValue;   // smallest height above ground seen while flying
        public float MaxLift;                          // highest height above ground seen while flying
        public float LandingSpeed;                     // vertical speed at touch-down (m/s)
        public bool JustLanded;                        // true for the one Advance in which it touched down
        public string Refusal = "";

        public float Range { get { return Vh * Duration; } }

        /// <summary>Height above the launch ground at time t.</summary>
        public float Altitude(float t) { return Vz0 * t - 0.5f * G * t * t; }

        /// <summary>Plans a leap from (x, y) along heading (radians) at horizontal speed vh. gravity in m/s2. Returns false (Refusal says why) when no safe plan exists.</summary>
        public bool Plan(IHuntTerrain terrain, double x, double y, double heading, float vh, float peak, float gravity, MohoLeapSettings s, float maxPeak, bool final)
        {
            Active = false; JustLanded = false; Refusal = "";
            if (terrain == null || gravity < 0.05f) { Refusal = "no gravity"; return false; }
            float cap = Math.Max(maxPeak, s.MinPeak);
            float p = Math.Max(peak, s.MinPeak);
            double h0 = terrain.Height(x, y);
            double cx = Math.Cos(heading), cy = Math.Sin(heading);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (p > cap + 0.01f) { Refusal = "path needs more than the peak cap"; return false; }
                float vz0 = (float)Math.Sqrt(2.0 * gravity * p);
                float apex = vz0 / gravity;
                bool clips = false, landed = false;
                float tLand = 0f;
                double lx = x, ly = y, lg = h0;
                for (float t = 0.25f; t <= s.MaxDuration; t += 0.25f)
                {
                    double px = x + cx * vh * t, py = y + cy * vh * t;
                    double alt = h0 + vz0 * t - 0.5 * gravity * t * t;
                    double ground = terrain.Height(px, py);
                    if (t < apex)
                    {
                        // rising: the path must clear the ground ahead (the required clearance builds up over the first two seconds)
                        if (alt - ground < s.Clearance * Math.Min(1.0, t / 2.0)) { clips = true; break; }
                    }
                    if (t >= apex && alt <= ground)
                    {
                        // refine inside this step
                        float lo = t - 0.25f, hi = t;
                        for (int k = 0; k < 10; k++)
                        {
                            float mid = 0.5f * (lo + hi);
                            double ax = x + cx * vh * mid, ay = y + cy * vh * mid;
                            double a2 = h0 + vz0 * mid - 0.5 * gravity * mid * mid;
                            if (a2 <= terrain.Height(ax, ay)) hi = mid; else lo = mid;
                        }
                        tLand = hi; landed = true;
                        lx = x + cx * vh * tLand; ly = y + cy * vh * tLand; lg = terrain.Height(lx, ly);
                        break;
                    }
                }
                if (clips) { p += 8f; continue; }
                if (!landed) { Refusal = "no landing within the duration"; return false; }
                if (!terrain.Walkable(lx, ly)) { Refusal = "lands in the sea or a forbidden zone"; return false; }
                Peak = p; Vz0 = vz0; G = gravity; Vh = vh; ApexTime = apex; Duration = tLand;
                H0 = h0; X0 = x; Y0 = y; Heading = heading; LandX = lx; LandY = ly; LandGround = lg;
                Elapsed = 0f; MinClearance = float.MaxValue; MaxLift = 0f; LandingSpeed = 0f;
                Final = final;
                Active = true;
                return true;
            }
            Refusal = "path needs more than the peak cap";
            return false;
        }

        /// <summary>Advances the flight. groundNow is the terrain height under the monster now (absolute). Returns the height above ground (0 once landed).</summary>
        public float Advance(float dt, double groundNow)
        {
            JustLanded = false;
            if (!Active) return 0f;
            Elapsed += dt;
            if (Elapsed >= Duration)
            {
                Elapsed = Duration;
                Active = false;
                JustLanded = true;
                LandingSpeed = Math.Abs(Vz0 - G * Duration);
                return 0f;
            }
            float lift = (float)(H0 + Altitude(Elapsed) - groundNow);
            if (lift < 0f) lift = 0f;
            if (lift < MinClearance) MinClearance = lift;
            if (lift > MaxLift) MaxLift = lift;
            return lift;
        }

        /// <summary>0 on take-off, 0.5 at the top, 1 on touch-down.</summary>
        public float Phase { get { return Duration > 0f ? Elapsed / Duration : 0f; } }
        public bool Rising { get { return Active && Elapsed < ApexTime; } }
    }
}
