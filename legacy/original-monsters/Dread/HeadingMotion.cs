// Dread: motion that follows the body. Pure C# (no UnityEngine); tested offline.
//
// The walker has a heading and a speed, and its velocity is always along the heading: it cannot move backwards or sideways, whatever the goal does.
// The goal bearing only steers the heading, and only at a limited rate: the yaw rate can never exceed speed / turn radius, and the radius grows
// with speed (a sprint turn is a huge sweep). So at a standstill it cannot rotate at all: to turn, it has to step forward into the turn.
//
// Speed follows the misalignment: a big difference between heading and goal eases the speed off (never below a creep speed while there is
// somewhere to go, so it keeps moving), the speed also drops where the goal could not be reached on the arc the current speed allows, and it
// climbs again only as the heading comes onto the new line. Speed changes are limited by an acceleration and a braking rate; the yaw rate
// changes are limited by a yaw acceleration, so neither jumps.
//
// Angles in radians, heading = atan2(y, x) on a flat plane, speeds in m/s.
using System;

namespace KrakensKreations.Dread
{
    public sealed class TurnSettings
    {
        /// <summary>false: the old behaviour (turn on the spot, speed gated by alignment) is kept by the callers.</summary>
        public bool Enabled = true;
        public float RadiusMin = 12f;          // metres, turn radius at a standstill
        public float RadiusPerSpeed = 2.0f;    // metres of radius per m/s of speed
        public float MaxYawDegPerSecond = 100f;
        public float YawAccelDegPerSecond2 = 30f;
        public float CreepSpeed = 5f;          // lowest speed while it still has somewhere to go and has to turn
        public float EaseFloor = 0.45f;        // fraction of the wanted speed kept at a large misalignment
        public float EaseFromDeg = 20f;        // no easing below this misalignment
        public float EaseToDeg = 110f;         // full easing from here
        /// <summary>Turn radius and creep speed multiplier for smaller bodies.</summary>
        public float SizeScale = 1f;

        public void Clamp()
        {
            RadiusMin = Math.Max(0.5f, Math.Min(RadiusMin, 500f));
            RadiusPerSpeed = Math.Max(0f, Math.Min(RadiusPerSpeed, 20f));
            MaxYawDegPerSecond = Math.Max(5f, Math.Min(MaxYawDegPerSecond, 360f));
            YawAccelDegPerSecond2 = Math.Max(2f, Math.Min(YawAccelDegPerSecond2, 720f));
            CreepSpeed = Math.Max(0.5f, Math.Min(CreepSpeed, 50f));
            EaseFloor = Math.Max(0f, Math.Min(EaseFloor, 1f));
            EaseFromDeg = Math.Max(1f, Math.Min(EaseFromDeg, 90f));
            EaseToDeg = Math.Max(EaseFromDeg + 5f, Math.Min(EaseToDeg, 180f));
        }
    }

    public sealed class HeadingMotion
    {
        readonly TurnSettings S;

        public double Heading;                 // radians
        public float Speed;                    // m/s, along the heading, never negative
        public double YawRate;                 // radians per second, signed (positive = counter-clockwise on the plane)
        /// <summary>Displacement of the last step along the arc (metres, plane axes).</summary>
        public double LastDX, LastDY;
        /// <summary>True while the heading is more than EaseFromDeg off the goal bearing (a declared turn).</summary>
        public bool Turning { get; private set; }
        /// <summary>Last misalignment, radians (signed, goal minus heading).</summary>
        public double Error { get; private set; }
        /// <summary>Radius of the arc being flown, metres (infinite when straight).</summary>
        public double ArcRadius { get { return Speed > 0.05f && Math.Abs(YawRate) > 1e-6 ? Speed / Math.Abs(YawRate) : double.PositiveInfinity; } }

        public HeadingMotion(TurnSettings settings)
        {
            S = settings ?? new TurnSettings();
        }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        /// <summary>Turn radius at this speed, metres.</summary>
        public double Radius(double speed)
        {
            return S.SizeScale * (S.RadiusMin + S.RadiusPerSpeed * Math.Max(0.0, speed));
        }

        /// <summary>Largest yaw rate (rad/s) possible at this speed: speed over radius, and at most the cap. Zero at zero speed.</summary>
        public double YawCap(double speed)
        {
            if (speed <= 0.0) return 0.0;
            return Math.Min(S.MaxYawDegPerSecond * Math.PI / 180.0, speed / Radius(speed));
        }

        /// <summary>Another system set the heading or speed (spawn, leap, hand-over): follows it and drops the yaw rate when the heading was changed.</summary>
        public void Sync(double heading, float speed)
        {
            if (Math.Abs(Wrap(heading - Heading)) > 0.02) YawRate = 0.0;      // a real jump (spawn, leap): the old curve no longer applies; float round-off is not
            Heading = heading;
            Speed = speed < 0f ? 0f : speed;
        }

        /// <summary>One step with somewhere to go. desiredSpeed: where it would like to be (m/s) when aligned; goalDistance: metres to the goal,
        /// negative when unknown; speedCap: an extra ceiling (for example to stop at the goal); creepOnly: the way ahead is not clear, creep.</summary>
        public void Step(float dt, double goalBearing, float desiredSpeed, float goalDistance, float speedCap, float accel, float brake, bool creepOnly)
        {
            if (dt <= 0f) return;
            double e = Wrap(goalBearing - Heading);
            Error = e;
            double ae = Math.Abs(e);
            Turning = ae > S.EaseFromDeg * Math.PI / 180.0;
            float creep = Math.Min(S.CreepSpeed * S.SizeScale, Math.Max(desiredSpeed, 0f));

            // speed: eased off by the misalignment, never below the creep speed while it has somewhere to go
            double a1 = S.EaseFromDeg * Math.PI / 180.0, a2 = S.EaseToDeg * Math.PI / 180.0;
            double q = ae <= a1 ? 0.0 : (ae >= a2 ? 1.0 : (ae - a1) / (a2 - a1));
            q = q * q * (3 - 2 * q);
            double vt = desiredSpeed * (1.0 - (1.0 - S.EaseFloor) * q);
            if (vt < creep) vt = creep;
            // the goal must be reachable on the arc: a goal off to the side needs radius dist / (2 sin err), the speed is lowered to fit
            if (goalDistance >= 0f && ae > 0.17)
            {
                double rReq = goalDistance / (2.0 * Math.Sin(Math.Min(ae, Math.PI / 2)));
                double vReach = (rReq / S.SizeScale - S.RadiusMin) / Math.Max(0.01f, S.RadiusPerSpeed);
                if (vt > vReach) vt = Math.Max(creep, vReach);
            }
            if (speedCap < vt) vt = Math.Max(0.0, speedCap);
            if (creepOnly && vt > creep) vt = creep;
            if (vt > Speed) Speed = (float)Math.Min(vt, Speed + accel * dt);
            else Speed = (float)Math.Max(vt, Speed - brake * dt);
            Turn(dt, e);
        }

        /// <summary>One step without a goal (arrived, listening, braking): the speed falls at the braking rate and the heading keeps its curve, which relaxes.</summary>
        public void Coast(float dt, float brake)
        {
            if (dt <= 0f) return;
            Turning = false;
            Error = 0.0;
            Speed = Math.Max(0f, Speed - brake * dt);
            Turn(dt, 0.0);
        }

        /// <summary>The one exception to "no turning without moving": pinned against terrain it may not enter. Speed is zero; turns toward the bearing at the given rate.</summary>
        public void Pivot(float dt, double goalBearing, float degPerSecond)
        {
            Speed = 0f;
            double e = Wrap(goalBearing - Heading);
            double r = degPerSecond * Math.PI / 180.0 * dt;
            double d = Math.Max(-r, Math.Min(r, e));
            Heading = Wrap(Heading + d);
            YawRate = dt > 0f ? d / dt : 0.0;
            LastDX = 0.0; LastDY = 0.0;
        }

        void Turn(float dt, double e)
        {
            double cap = YawCap(Speed);
            double alpha = S.YawAccelDegPerSecond2 * Math.PI / 180.0;
            double des = 0.0;
            if (e != 0.0 && cap > 0.0)
            {
                double ae = Math.Abs(e);
                double m = Math.Min(cap, Math.Min(Math.Sqrt(1.6 * alpha * ae), ae / 0.5));
                des = e > 0 ? m : -m;
            }
            double dw = des - YawRate;
            double lim = alpha * dt;
            YawRate += Math.Max(-lim, Math.Min(lim, dw));
            YawRate = Math.Max(-cap, Math.Min(cap, YawRate));
            double h0 = Heading;
            Heading = Wrap(Heading + YawRate * dt);
            double hm = h0 + YawRate * dt * 0.5;       // the arc is integrated along its mid heading
            double step = Speed * dt;
            LastDX = Math.Cos(hm) * step;
            LastDY = Math.Sin(hm) * step;
        }
    }
}
