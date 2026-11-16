// The Laythe leviathan: brain and scare director. Pure C# (no UnityEngine), tested offline by underwater/tests.
//
// Space: metres in a plane tangent at the encounter, X and Y horizontal, Z up, sea level at Z = 0.
// BloopBrain swims like a fish: it only ever moves along its heading, and it turns that heading at a limited rate (fast when slow, wide arcs when fast).
// Nothing moves it sideways. It knows the craft only from what it hears and senses (a belief with an error).
// The hunt: it closes in, circles the prey a little below it, tightening, then dives deep under it and rises straight up. A real rise starts with a
// scream; it kills only if it reaches the craft fast enough (KillSpeed), otherwise it overshoots, coasts on and circles again.
// It may not kill before its encounter clock passes KillEarliestSeconds plus a random spread, and not before it has made its feints: until then a rise
// is aimed to miss, without the scream.
// BloopCues picks the small scares: no timers, a Poisson rate per cue from depth, light, noise, distance and state, with cooldowns, a repeat penalty
// and an exposure budget that makes every reveal lower the chance of the next. None of them pushes the craft.
using System;

namespace KrakensKreations.Dread
{
    public struct BloopVec
    {
        public double X, Y, Z;
        public BloopVec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static BloopVec operator +(BloopVec a, BloopVec b) { return new BloopVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static BloopVec operator -(BloopVec a, BloopVec b) { return new BloopVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static BloopVec operator *(BloopVec a, double k) { return new BloopVec(a.X * k, a.Y * k, a.Z * k); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public double Flat { get { return Math.Sqrt(X * X + Y * Y); } }
        public BloopVec Unit { get { double l = Length; return l > 1e-9 ? this * (1.0 / l) : new BloopVec(0, 1, 0); } }
        public static double Dot(BloopVec a, BloopVec b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static BloopVec Cross(BloopVec a, BloopVec b) { return new BloopVec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    }

    public enum BloopState { Dormant, Investigate, Circle, Inspect, Dive, Strike, Recover, Gone }

    public sealed class BloopBrain
    {
        public double CruiseSpeed = 16, CircleSpeed = 18, InspectSpeed = 11, DiveSpeed = 26, StrikeSpeed = 48;
        public double InspectMiss = 20;         // how far beside the craft the slow pass at its depth goes: well inside what the water shows
        public double KillSpeed = 34;           // a rise slower than this at the craft does no harm
        public double Accel = 6, StrikeAccel = 22, Decel = 9;
        public double MaxTurnDegPerSecond = 110, MaxLateralAccel = 32, MaxPitchDeg = 55;
        public double SightTurnDegPerSecond = 30;   // while the craft can see it (InSight)
        public double TurnAccelDeg = 400, SightTurnAccelDeg = 45;   // how fast a turn may build up, deg/s per second
        public bool InSight;                        // set by the game each frame: near enough to be seen and in the camera's view
        public double KillEarliestSeconds = 180, KillSpreadSeconds = 90;
        public int FeintsBeforeKill = 1;
        public double StrikeTurnDrag = 1.6;     // share of its speed lost per radian it turns while rising
        public double FeintMiss = 30;           // how far a feint rise passes beside the craft: close enough to be seen
        public double CatchMetres = 14;
        public double SenseDark = 260, SenseLit = 480;
        public double GiveUpSeconds = 170;
        public double CeilingDepth = 25;        // never shallower than this
        public double FloorMargin = 14;

        public BloopVec Position, Heading = new BloopVec(0, 1, 0);
        public double Speed;
        public double TurnRate;                 // signed yaw rate, rad/s, positive to the left: for the bank of the body
        public BloopState State = BloopState.Dormant;
        public BloopVec Belief;
        public double BeliefError = 1e9;
        public double Clock;                    // seconds since it first noticed the craft
        public double KillTime;                 // the moment on the clock when a kill opens
        public int Feints, Strikes, Misses, Inspections;
        public bool Aware, Sees, Caught;
        public bool Screamed;                   // true for the one update in which a real rise begins
        public bool RealStrike;                 // the current rise may kill
        public double HitSpeed;                 // speed at the last contact with the craft
        public double LastInfoAge;
        public double ClosestBeforeOpen = 1e9;
        public bool KillOpen { get { return Aware && Clock >= KillTime && Feints >= FeintsBeforeKill; } }

        readonly Random rng;
        double stateTime, stateLimit, circleRadius, circleEnd, circleDir, circleDrop, diveDepth, strikeClosest, turnSpeed;
        bool inspected, inspectRun;
        BloopVec inspectStart, inspectThrough, inspectDir;
        BloopVec target, diveSide, craftVel, lastCraft;
        bool haveLastCraft, contactThisRise;

        public BloopBrain(int seed, BloopVec start)
        {
            rng = new Random(seed);
            Position = start;
            Heading = new BloopVec(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 0).Unit;
            KillTime = 0;
        }

        double R(double a, double b) { return a + (b - a) * rng.NextDouble(); }

        void Notice()
        {
            if (!Aware)
            {
                Aware = true;
                KillTime = KillEarliestSeconds + KillSpreadSeconds * rng.NextDouble();
            }
        }

        // A noise from the craft. Loudness 0..1, range: how far this kind of noise carries in water.
        public bool Hear(double loudness, double range, BloopVec source)
        {
            double d = (source - Position).Length;
            if (d > range * Math.Max(0.05, loudness)) return false;
            double err = 12 + 0.10 * d * (1.7 - loudness);
            BloopVec guess = source + new BloopVec(R(-1, 1), R(-1, 1), R(-0.4, 0.4)) * err;
            if (err < BeliefError || LastInfoAge > 10)
            {
                Belief = guess;
                BeliefError = err;
            }
            else
            {
                Belief = Belief + (guess - Belief) * 0.3;
                BeliefError = Math.Min(BeliefError, err * 1.1);
            }
            LastInfoAge = 0;
            Notice();
            if (State == BloopState.Dormant) Enter(BloopState.Investigate);
            return true;
        }

        // It roams towards a rough place without knowing of the craft: no encounter clock, no kill until it really hears or senses something.
        public void Rumor(BloopVec place, double error)
        {
            if (Aware) return;
            Belief = place;
            BeliefError = error;
            LastInfoAge = 0;
            Enter(BloopState.Investigate);
        }

        void Enter(BloopState s)
        {
            State = s;
            stateTime = 0;
            stateLimit = 1e9;
            switch (s)
            {
                case BloopState.Circle:
                {
                    // the circle starts wide and tightens; each round of circling is a little closer than the last
                    double tighten = Math.Min(1, (Feints + Misses) * 0.25);
                    // the water at depth shows about 60 m (True Volumetrics, Laythe): the circle ends well inside that
                    circleRadius = R(75, 100) * (1 - 0.3 * tighten);
                    circleEnd = R(28, 40);
                    BloopVec rel = Position - Belief;
                    circleDir = (rel.X * Heading.Y - rel.Y * Heading.X) > 0 ? 1 : -1;   // keep going the way it already turns
                    circleDrop = R(25, 40);   // well below the craft: it is seen going round underneath, the scariest view
                    double laps = R(0.6, 2.0);
                    stateLimit = laps * 2 * Math.PI * (circleRadius + circleEnd) * 0.5 / CircleSpeed;
                    break;
                }
                case BloopState.Inspect:
                {
                    // a slow pass at the craft's own depth, beside it: first out of sight to a start point, then straight past
                    // the start lies on its own side of the craft, so the way there never crosses the craft
                    BloopVec from = Position - Belief;
                    double a = Math.Atan2(-from.Y, -from.X) + R(-1.0, 1.0);
                    inspectDir = new BloopVec(Math.Cos(a), Math.Sin(a), 0);
                    BloopVec side = new BloopVec(-inspectDir.Y, inspectDir.X, 0) * (rng.Next(2) == 0 ? -1 : 1);
                    inspectThrough = Belief + side * R(InspectMiss * 0.75, InspectMiss * 1.25) + new BloopVec(0, 0, R(-6, 4));
                    inspectStart = inspectThrough - inspectDir * 150;
                    inspectRun = false;
                    Inspections++;
                    stateLimit = 90;
                    break;
                }
                case BloopState.Dive:
                {
                    inspected = false;
                    diveDepth = R(120, 180);
                    double a = R(0, Math.PI * 2);
                    diveSide = new BloopVec(Math.Cos(a), Math.Sin(a), 0);
                    stateLimit = 45;
                    break;
                }
                case BloopState.Strike:
                    contactThisRise = false;
                    strikeClosest = 1e9;
                    Strikes++;
                    stateLimit = 18;
                    break;
                case BloopState.Recover:
                    stateLimit = R(2.5, 5);
                    break;
            }
        }

        // craft: the true position, used for the senses and the contact. floorZ: sea floor under the monster (negative).
        public void Update(double dt, BloopVec craft, bool lightsOn, double floorZ, bool craftInReach)
        {
            Screamed = false;
            if (State == BloopState.Gone || Caught) return;
            if (haveLastCraft && dt > 0)
            {
                BloopVec v = (craft - lastCraft) * (1.0 / dt);
                craftVel = craftVel + (v - craftVel) * Math.Min(1, dt * 2);
            }
            lastCraft = craft;
            haveLastCraft = true;
            if (craftInReach && Aware) Clock += dt;
            stateTime += dt;
            LastInfoAge += dt;
            BeliefError += 1.4 * dt;

            double trueDist = (craft - Position).Length;
            // a fish feels the water: in the dark it still senses a craft this near, lights carry further
            Sees = craftInReach && trueDist <= (lightsOn ? SenseLit : SenseDark);
            if (Sees)
            {
                Belief = craft;
                BeliefError = 6;
                LastInfoAge = 0;
                Notice();
                if (State == BloopState.Dormant) Enter(BloopState.Investigate);
            }

            Think(craft, craftInReach, floorZ);
            BloopVec before = Position;
            Steer(dt, floorZ);
            Contact(before, craft);
            if (!KillOpen) ClosestBeforeOpen = Math.Min(ClosestBeforeOpen, (craft - Position).Length);
        }

        void Think(BloopVec craft, bool craftInReach, double floorZ)
        {
            if (State != BloopState.Dormant && LastInfoAge > GiveUpSeconds) { Enter(BloopState.Gone); return; }
            switch (State)
            {
                case BloopState.Investigate:
                    target = Belief;
                    if ((Belief - Position).Flat < Math.Max(150, BeliefError)) Enter(BloopState.Circle);
                    break;
                case BloopState.Circle:
                {
                    double k = Math.Min(1, stateTime / Math.Max(1, stateLimit));
                    double r = circleRadius + (circleEnd - circleRadius) * k;
                    BloopVec rel = Position - Belief;
                    double a = Math.Atan2(rel.Y, rel.X) + circleDir * Math.Min(0.9, 45 / r);
                    double z = Math.Max(Belief.Z - circleDrop, floorZ + FloorMargin + 10);
                    target = new BloopVec(Belief.X + Math.Cos(a) * r, Belief.Y + Math.Sin(a) * r, z);
                    // every round: circle below, come up and look at it from close by, circle again, then the rise
                    if (stateTime > stateLimit && craftInReach && !inspected) Enter(BloopState.Inspect);
                    // now and then one more round underneath before the rise: no rhythm to learn
                    else if (stateTime > stateLimit && craftInReach && BeliefError < 120 && rng.NextDouble() < 0.3) Enter(BloopState.Circle);
                    else if (stateTime > stateLimit && craftInReach && BeliefError < 120) Enter(BloopState.Dive);
                    else if (stateTime > stateLimit * 1.5) Enter(BloopState.Circle);
                    break;
                }
                case BloopState.Inspect:
                {
                    if (!inspectRun)
                    {
                        target = inspectStart;
                        // close to the start, and pointing roughly along the pass, so the run past the craft needs no turn worth seeing
                        if ((inspectStart - Position).Flat < 30 && Math.Abs(Position.Z - inspectStart.Z) < 15) inspectRun = true;
                    }
                    if (inspectRun)
                    {
                        target = inspectThrough + inspectDir * 250;
                        target.Z = inspectThrough.Z;
                        if (BloopVec.Dot(Position - inspectThrough, inspectDir) > 90)
                        {
                            inspected = true;
                            Enter(BloopState.Circle);
                            break;
                        }
                    }
                    if (stateTime > stateLimit) { inspected = true; Enter(BloopState.Circle); }
                    break;
                }
                case BloopState.Dive:
                {
                    // down and a little to the side, so that the rise comes from straight below
                    double z = Math.Max(Belief.Z - diveDepth, floorZ + FloorMargin + 6);
                    target = Belief + diveSide * 35;
                    target.Z = z;
                    BloopVec rel = Position - Belief;
                    bool deep = Position.Z <= Belief.Z - Math.Min(diveDepth, Belief.Z - z) * 0.85;
                    if ((deep && rel.Flat < 90) || stateTime > stateLimit)
                    {
                        RealStrike = KillOpen;
                        if (RealStrike) Screamed = true;
                        Enter(BloopState.Strike);
                    }
                    break;
                }
                case BloopState.Strike:
                {
                    // committed: it rises at the craft itself, leading it; a feint aims beside it
                    double tti = Math.Min(4, (craft - Position).Length / Math.Max(10, Speed));
                    BloopVec aim = craft + craftVel * tti;
                    if (!RealStrike)
                    {
                        BloopVec side = new BloopVec(diveSide.X, diveSide.Y, 0).Unit;
                        aim = aim + side * FeintMiss + new BloopVec(0, 0, 80);   // beside and past: it carries on upward instead of circling the aim point
                    }
                    target = aim;
                    // past: it came closest and is now pulling away again (it starts pointing down from the dive, so the direction alone says nothing)
                    double d = (craft - Position).Length;
                    strikeClosest = Math.Min(strikeClosest, d);
                    bool past = d > strikeClosest + 25 && Position.Z > craft.Z - 20;
                    if (past || stateTime > stateLimit)
                    {
                        if (!RealStrike) Feints++;
                        else if (!Caught) Misses++;
                        Enter(BloopState.Recover);
                    }
                    break;
                }
                case BloopState.Recover:
                    // momentum: it carries on where it points, then comes round again
                    target = Position + Heading * 200;
                    if (stateTime > stateLimit) Enter(craftInReach ? BloopState.Circle : BloopState.Investigate);
                    break;
            }
        }

        double WantedSpeed()
        {
            switch (State)
            {
                case BloopState.Investigate: return CruiseSpeed;
                case BloopState.Circle: return CircleSpeed;
                case BloopState.Inspect: return inspectRun ? InspectSpeed : CruiseSpeed;
                case BloopState.Dive: return DiveSpeed;
                case BloopState.Strike: return RealStrike ? StrikeSpeed : KillSpeed * 0.8;
                case BloopState.Recover: return CruiseSpeed;
                default: return 0;
            }
        }

        void Steer(double dt, double floorZ)
        {
            if (State == BloopState.Dormant) { Speed = Math.Max(0, Speed - Decel * dt); TurnRate = 0; return; }
            BloopVec want = (target - Position).Unit;
            bool rising = State == BloopState.Strike || State == BloopState.Dive;
            double maxPitch = Math.Sin((rising ? 88 : MaxPitchDeg) * Math.PI / 180);
            if (want.Z > maxPitch || want.Z < -maxPitch)
            {
                double flat = Math.Sqrt(Math.Max(1e-9, 1 - maxPitch * maxPitch));
                BloopVec h = new BloopVec(want.X, want.Y, 0).Unit;
                want = new BloopVec(h.X * flat, h.Y * flat, Math.Sign(want.Z) * maxPitch);
            }
            // the surface and the floor: it pulls up or levels out in time, it is never pushed
            double ceiling = -CeilingDepth, floor = floorZ + FloorMargin;
            double room = Math.Max(30, Speed * 1.5);
            if (Position.Z > ceiling - room && want.Z > -0.2) want = new BloopVec(want.X, want.Y, -0.2 - 0.6 * Math.Min(1, (Position.Z - (ceiling - room)) / room)).Unit;
            if (Position.Z < floor + room && want.Z < 0.2) want = new BloopVec(want.X, want.Y, 0.2 + 0.6 * Math.Min(1, ((floor + room) - Position.Z) / room)).Unit;

            double dot = Math.Max(-1, Math.Min(1, BloopVec.Dot(Heading, want)));
            double wanted = WantedSpeed();
            // it slows for a hard turn, except in a committed rise
            if (State != BloopState.Strike) wanted *= 0.55 + 0.45 * Math.Max(0, dot);
            double up = State == BloopState.Strike ? StrikeAccel : Accel;
            Speed += Math.Max(-Decel * dt, Math.Min(up * dt, wanted - Speed));

            // turn the heading towards want, limited in rate: a big body at speed can only make wide arcs
            // in sight it only turns slowly and eases into a turn: a sharp change of direction is done where nobody sees it
            double cap = (InSight ? SightTurnDegPerSecond : MaxTurnDegPerSecond) * Math.PI / 180;
            double ease = (InSight ? SightTurnAccelDeg : TurnAccelDeg) * Math.PI / 180 * dt;
            double rate = Math.Min(Math.Min(cap, MaxLateralAccel / Math.Max(4, Speed)), turnSpeed + ease);
            double angle = Math.Acos(dot);
            double step = Math.Min(angle, rate * dt);
            turnSpeed = dt > 0 ? step / dt : 0;
            BloopVec old = Heading;
            if (angle > 1e-6)
            {
                BloopVec axis = BloopVec.Cross(Heading, want);
                if (axis.Length < 1e-9) axis = new BloopVec(0, 0, 1);
                axis = axis.Unit;
                // Rodrigues: rotate Heading about axis by step
                Heading = (Heading * Math.Cos(step) + BloopVec.Cross(axis, Heading) * Math.Sin(step) + axis * (BloopVec.Dot(axis, Heading) * (1 - Math.Cos(step)))).Unit;
                // a hard turn in the rise costs speed: a craft that dodges well makes it arrive too slow to kill
                if (State == BloopState.Strike) Speed *= 1 - StrikeTurnDrag * step;
            }
            double yawOld = Math.Atan2(old.Y, old.X), yawNew = Math.Atan2(Heading.Y, Heading.X);
            double dy = yawNew - yawOld;
            if (dy > Math.PI) dy -= 2 * Math.PI; else if (dy < -Math.PI) dy += 2 * Math.PI;
            TurnRate = dt > 0 ? dy / dt : 0;
            Position = Position + Heading * (Speed * dt);
            // last resort only, for a floor that rises faster than it can climb: it never leaves the water
            if (Position.Z > -2) Position.Z = -2;
            if (Position.Z < floorZ + 2) Position.Z = floorZ + 2;
        }

        // the closest point of this step's path to the craft decides a contact
        void Contact(BloopVec from, BloopVec craft)
        {
            if (State != BloopState.Strike || contactThisRise) return;
            BloopVec seg = Position - from;
            double len2 = BloopVec.Dot(seg, seg);
            double t = len2 > 1e-9 ? Math.Max(0, Math.Min(1, BloopVec.Dot(craft - from, seg) / len2)) : 0;
            double d = (from + seg * t - craft).Length;
            if (d > CatchMetres) return;
            contactThisRise = true;
            HitSpeed = Speed;
            if (RealStrike && KillOpen && Speed >= KillSpeed) Caught = true;
        }
    }

    public enum BloopCue
    {
        None, WaterDrift, Dimming, Silhouette, Bubbles, Sediment, Echo, FalseEcho, ConeGlimpse, EyeGlint, LureGlow, Silence, LightFlicker, Scream, Count
    }

    public struct BloopCueInput
    {
        public double Dt;
        public double Depth;            // of the craft, metres
        public double SurfaceLight;     // 0..1 how much daylight reaches the craft at this depth
        public bool LightsOn;
        public double Noise;            // 0..1
        public double Distance;
        public double MonsterAbove;     // metres, negative when below the craft
        public BloopState State;
        public double CraftFloorClearance, MonsterFloorClearance;
        public bool LookingUp;          // the camera looks towards the surface
        public bool InLightCone;        // the monster is inside the beam of the craft's lights
        public bool InFront;            // the monster is in the camera's view
        public double CraftSpeed;
    }

    public sealed class BloopCues
    {
        public double RateScale = 1;
        public double ExposureTau = 110, ExposureWeight = 1.1;
        public BloopCue Fired;
        public double FiredStrength;
        public double Exposure;
        public int[] Counts = new int[(int)BloopCue.Count];

        readonly Random rng;
        readonly double[] cooldown = new double[(int)BloopCue.Count];
        readonly double[] penalty = new double[(int)BloopCue.Count];
        double busy;
        double time;

        static readonly double[] Cost = new double[(int)BloopCue.Count];
        static BloopCues()
        {
            Cost[(int)BloopCue.WaterDrift] = 0.2; Cost[(int)BloopCue.Dimming] = 0.5; Cost[(int)BloopCue.Silhouette] = 1.2; Cost[(int)BloopCue.Bubbles] = 0.3;
            Cost[(int)BloopCue.Sediment] = 0.3; Cost[(int)BloopCue.Echo] = 0.25; Cost[(int)BloopCue.FalseEcho] = 0.1; Cost[(int)BloopCue.ConeGlimpse] = 1.5;
            Cost[(int)BloopCue.EyeGlint] = 0.7; Cost[(int)BloopCue.LureGlow] = 0.8; Cost[(int)BloopCue.Silence] = 0.3; Cost[(int)BloopCue.LightFlicker] = 0.5;
        }

        public BloopCues(int seed) { rng = new Random(seed); }

        static double Near(double d, double far, double near) { return d >= far ? 0 : d <= near ? 1 : (far - d) / (far - near); }

        // Events per second for one cue in this situation; 0 when the situation does not allow it. The scream is not random: the brain fires it.
        public static double Rate(BloopCue c, in BloopCueInput i)
        {
            bool closing = i.State == BloopState.Dive || i.State == BloopState.Strike;
            bool quiet = i.Noise < 0.25;
            bool dark = i.SurfaceLight < 0.55;
            switch (c)
            {
                case BloopCue.WaterDrift: return i.Distance > 60 ? 0.03 * Near(i.Distance, 900, 200) * (i.CraftSpeed < 3 ? 1.5 : 1) : 0;
                case BloopCue.Dimming: return i.SurfaceLight > 0.3 && i.MonsterAbove > 15 ? 0.05 * Near(i.Distance, 500, 80) : 0;
                case BloopCue.Silhouette: return i.SurfaceLight > 0.35 && i.MonsterAbove > 12 && i.LookingUp ? 0.09 * Near(i.Distance, 300, 60) : 0;
                case BloopCue.Bubbles: return i.MonsterAbove < -8 ? 0.035 * Near(i.Distance, 450, 70) : 0;
                case BloopCue.Sediment: return i.MonsterFloorClearance < 60 && i.CraftFloorClearance < 260 ? 0.04 * Near(i.Distance, 800, 150) : 0;
                case BloopCue.Echo: return quiet ? 0.035 * Near(i.Distance, 2600, 300) : 0;
                case BloopCue.FalseEcho: return i.State == BloopState.Recover || i.Distance > 900 ? 0.005 : 0;
                case BloopCue.ConeGlimpse: return i.LightsOn && i.InLightCone ? 0.3 * Near(i.Distance, 140, 50) : 0;
                case BloopCue.EyeGlint: return i.LightsOn && i.InFront && i.Distance > 110 ? 0.04 * Near(i.Distance, 360, 140) : 0;
                case BloopCue.LureGlow: return !i.LightsOn && dark && i.State == BloopState.Circle && i.Distance > 70 ? 0.03 * Near(i.Distance, 420, 120) : 0;
                case BloopCue.Silence: return quiet && closing ? 0.05 * Near(i.Distance, 650, 100) : 0;
                case BloopCue.LightFlicker: return i.LightsOn ? 0.05 * Near(i.Distance, 260, 80) : 0;
                default: return 0;
            }
        }

        public void Update(in BloopCueInput i)
        {
            Fired = BloopCue.None;
            time += i.Dt;
            Exposure *= Math.Exp(-i.Dt / ExposureTau);
            busy -= i.Dt;
            double total = 0;
            int n = (int)BloopCue.Count;
            double[] rates = new double[n];
            for (int k = 1; k < n; k++)
            {
                cooldown[k] -= i.Dt;
                penalty[k] *= Math.Exp(-i.Dt / 90);
                if (cooldown[k] > 0) continue;
                double r = Rate((BloopCue)k, i) * RateScale * Math.Exp(-penalty[k] * 1.2) * Math.Exp(-Exposure * ExposureWeight * (Cost[k] > 0.4 ? 1 : 0.35));
                rates[k] = r;
                total += r;
            }
            if (busy > 0 || total <= 0) return;
            if (rng.NextDouble() >= 1 - Math.Exp(-total * i.Dt)) return;
            double pick = rng.NextDouble() * total;
            for (int k = 1; k < n; k++)
            {
                pick -= rates[k];
                if (rates[k] > 0 && pick <= 0)
                {
                    Fired = (BloopCue)k;
                    FiredStrength = 0.4 + 0.6 * rng.NextDouble();
                    Counts[k]++;
                    cooldown[k] = 12 + rng.NextDouble() * 40;
                    penalty[k] += 1;
                    Exposure += Cost[k];
                    busy = 2 + rng.NextDouble() * 7;
                    return;
                }
            }
        }
    }
}
