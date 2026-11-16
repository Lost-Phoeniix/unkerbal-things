using System;
using System.Collections.Generic;
using System.Linq;
using UnkerbalThings.Dread;

static class P
{
    static int fails;
    static void Check(string name, bool ok, string info = "")
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (info.Length > 0 ? "  " + info : ""));
        if (!ok) fails++;
    }

    class Run
    {
        public double KillAt = -1, InspectClosest = 1e9, InspectSpeed, Closest, HitSpeed, MaxTurn, MaxTurnInSight, MaxLateral, MaxSpeed, MaxSideSlip, CircleSweep, MinStrikeStartBelow = 1e9, MinZ = 1e9, MaxZ = -1e9;
        public int Feints, Misses, Strikes, Screams, RealStrikes;
        public double NearShare, MaxMouthFar, MouthAtHit, MaxMouthCircle, FeintWorst, PassSeconds;
        public int FeintsSeen, FeintsRun, Passes, Cues;
        public double CircleSeconds, HardTurnSeconds, Swings;
        public bool Caught;
        public BloopState End;
        public List<double> CircleDistances = new List<double>();
        public List<double> CircleBelow = new List<double>();
    }

    // loudEvery: seconds between noises (0 = silent). dodge: the craft moves sideways at this speed after each scream. quietAfter: from then on silent, and it sneaks away.
    static Run Simulate(int seed, double loudEvery, bool lights, double seconds, double start = 2500, double quietAfter = 1e9, double dodge = 0, double floorZ = -3000, double craftZ = -450, Action<BloopBrain> setup = null, double cruise = 0, Func<double, BloopBrain, BloopVec, BloopVec> move = null)
    {
        var b = new BloopBrain(seed, new BloopVec(start, 0, craftZ - 100));
        b.CeilingDepth = 200;
        setup?.Invoke(b);
        var rng = new Random(seed * 7 + 1);
        var craft = new BloopVec(0, 0, craftZ);
        double t = 0, dt = 0.04, nextNoise = 0, dodgeLeft = 0;
        var r = new Run();
        var lastHeading = b.Heading;
        BloopState last = b.State;
        double lastAngle = double.NaN, circleTime = 0;
        BloopVec dodgeDir = new BloopVec(0, 1, 0);
        double known = 0, nearTime = 0, feintClosest = 1e9;
        bool passCounted = false;
        int swingSign = 0;
        var cues = new BloopCues(seed ^ 0x51ed);
        while (t < seconds)
        {
            if (loudEvery > 0 && t >= nextNoise && t < quietAfter)
            {
                b.Hear(0.7, 8000, craft);
                nextNoise = t + loudEvery * (0.5 + rng.NextDouble());
            }
            if (t >= quietAfter) craft.X += 2 * dt;
            craft.X += cruise * dt;   // a craft under way on a straight course
            BloopVec moved = move != null ? move(t, b, craft) : new BloopVec(cruise, 0, 0);
            if (move != null) craft = craft + moved * dt;
            if (dodgeLeft > 0) { craft = craft + dodgeDir * (dodge * dt); dodgeLeft -= dt; }
            b.InSight = (b.Position - craft).Length < 92;   // the camera is at the craft: 70 m of water plus half the body
            bool sightBefore = b.InSight;
            BloopVec before = b.Position;
            b.Update(dt, craft, lights, floorZ, true);
            BloopVec step = b.Position - before;
            if (step.Length > 1e-6)
            {
                // it moves only along where it points: the step's direction against the heading
                double slip = Math.Acos(Math.Max(-1, Math.Min(1, BloopVec.Dot(step.Unit, b.Heading)))) * 180 / Math.PI;
                if (b.Position.Z > -2.01 || b.Position.Z < floorZ + 2.01) slip = 0;   // the last-resort clamps, reported separately
                r.MaxSideSlip = Math.Max(r.MaxSideSlip, slip);
            }
            double turn = Math.Acos(Math.Max(-1, Math.Min(1, BloopVec.Dot(lastHeading, b.Heading)))) / dt;
            r.MaxTurn = Math.Max(r.MaxTurn, turn * 180 / Math.PI);
            if (sightBefore) r.MaxTurnInSight = Math.Max(r.MaxTurnInSight, turn * 180 / Math.PI);
            r.MaxLateral = Math.Max(r.MaxLateral, turn * b.Speed);
            r.MaxSpeed = Math.Max(r.MaxSpeed, b.Speed);
            r.MinZ = Math.Min(r.MinZ, b.Position.Z);
            r.MaxZ = Math.Max(r.MaxZ, b.Position.Z);
            lastHeading = b.Heading;
            double gap = (b.Position - craft).Length;
            if (b.Aware && b.Sees) { known += dt; if (gap < 150) nearTime += dt; }
            if (gap > 220) r.MaxMouthFar = Math.Max(r.MaxMouthFar, b.MouthOpen);
            if (b.State == BloopState.Circle || b.State == BloopState.Investigate || b.State == BloopState.Dive) r.MaxMouthCircle = Math.Max(r.MaxMouthCircle, b.MouthOpen);
            if (b.Caught) r.MouthAtHit = b.MouthOpen;
            // what the player gets to see: a feint that comes within sight, a pass that stays beside the craft, cues that keep coming
            if (b.State == BloopState.Strike && !b.RealStrike) feintClosest = Math.Min(feintClosest, gap);
            if (last == BloopState.Strike && b.State != BloopState.Strike && feintClosest < 1e8)
            {
                r.FeintsRun++;
                if (feintClosest < 70) r.FeintsSeen++;
                r.FeintWorst = Math.Max(r.FeintWorst, feintClosest);
                feintClosest = 1e9;
            }
            if (b.State == BloopState.Inspect && gap < 45 && Math.Abs(b.Position.Z - craft.Z) < 15) { r.PassSeconds += dt; if (!passCounted) { r.Passes++; passCounted = true; } }
            if (b.State != BloopState.Inspect) passCounted = false;
            // how it swims while it circles or weaves under the craft: hard turns, and how often it swings from one side to the other
            if (b.State == BloopState.Circle && b.Sees && gap < 150)
            {
                r.CircleSeconds += dt;
                double tr = b.TurnRate * 180 / Math.PI;
                if (Math.Abs(tr) > 45) r.HardTurnSeconds += dt;
                int sg = tr > 10 ? 1 : tr < -10 ? -1 : 0;
                if (sg != 0 && sg != swingSign) { if (swingSign != 0) r.Swings++; swingSign = sg; }
            }
            var ci = new BloopCueInput { Dt = dt, Depth = -craft.Z, SurfaceLight = 0.5, LightsOn = lights, Noise = 0.3, Distance = gap, MonsterAbove = b.Position.Z - craft.Z, State = b.State, CraftFloorClearance = craft.Z - floorZ, MonsterFloorClearance = b.Position.Z - floorZ, InFront = true, InLightCone = gap < 140, CraftSpeed = moved.Length };
            cues.Update(ci);
            if (cues.Fired != BloopCue.None && b.Aware) r.Cues++;
            if (b.Screamed) r.Screams++;
            // the late dodge a player would try: when the rising fish is 60 m away, a sideways sprint across its path
            if (dodge > 0 && dodgeLeft <= 0 && b.State == BloopState.Strike && b.RealStrike && (craft - b.Position).Length < 60)
            {
                BloopVec across = BloopVec.Cross(b.Heading, new BloopVec(0, 0, 1));
                dodgeDir = across.Length > 0.1 ? new BloopVec(across.X, across.Y, 0).Unit : new BloopVec(1, 0, 0);
                dodgeLeft = 4;
            }
            if (b.State == BloopState.Strike && last != BloopState.Strike)
            {
                r.MinStrikeStartBelow = Math.Min(r.MinStrikeStartBelow, craft.Z - b.Position.Z);
                if (b.RealStrike) r.RealStrikes++;
            }
            if (b.State != BloopState.Circle) circleTime = 0;
            if (b.State == BloopState.Inspect && Math.Abs(b.Position.Z - craft.Z) < 12 && (b.Position - craft).Length < r.InspectClosest) { r.InspectClosest = (b.Position - craft).Length; r.InspectSpeed = b.Speed; }
            if (b.State == BloopState.Circle)
            {
                BloopVec rel = b.Position - craft;
                double ang = Math.Atan2(rel.Y, rel.X);
                if (!double.IsNaN(lastAngle) && last == BloopState.Circle)
                {
                    double d = ang - lastAngle;
                    if (d > Math.PI) d -= 2 * Math.PI; else if (d < -Math.PI) d += 2 * Math.PI;
                    r.CircleSweep += Math.Abs(d);
                }
                lastAngle = ang;
                circleTime += dt;
                if (b.Sees && circleTime > 20) { r.CircleDistances.Add(rel.Flat); r.CircleBelow.Add(craft.Z - b.Position.Z); }   // once it has settled into the circle
            }
            last = b.State;
            t += dt;
            if (b.Caught) { r.Caught = true; r.KillAt = b.Clock; break; }
        }
        r.Closest = b.ClosestBeforeOpen;
        r.NearShare = known > 0 ? nearTime / known : 0;
        r.Feints = b.Feints;
        r.Misses = b.Misses;
        r.Strikes = b.Strikes;
        r.HitSpeed = b.HitSpeed;
        r.End = b.State;
        return r;
    }

    static double[] CueGaps(BloopCueInput i, double seconds, int seed, out int count, out int[] counts)
    {
        var c = new BloopCues(seed);
        var gaps = new List<double>();
        double last = -1, t = 0;
        while (t < seconds)
        {
            i.Dt = 0.05;
            c.Update(i);
            if (c.Fired != BloopCue.None)
            {
                if (last >= 0) gaps.Add(t - last);
                last = t;
            }
            t += 0.05;
        }
        count = c.Counts.Sum();
        counts = c.Counts;
        return gaps.ToArray();
    }

    static BloopCueInput Base() { return new BloopCueInput { Depth = 400, SurfaceLight = 0.25, Noise = 0.1, Distance = 300, MonsterAbove = 40, State = BloopState.Circle, CraftFloorClearance = 800, MonsterFloorClearance = 800, CraftSpeed = 1 }; }

    static string Pct(IEnumerable<double> xs, double p) { var a = xs.OrderBy(x => x).ToArray(); return a.Length == 0 ? "-" : a[Math.Min(a.Length - 1, (int)(a.Length * p))].ToString("F0"); }

    static void Main()
    {
        // ---- the kill gate
        var loud = Enumerable.Range(1, 200).Select(s => Simulate(s, 6, true, 1200)).ToArray();
        var kills = loud.Where(r => r.Caught).Select(r => r.KillAt).OrderBy(x => x).ToArray();
        Check("loud lit craft: kill never before 180 s (200 seeds)", kills.All(k => k >= 180), kills.Length + " kills, first " + (kills.Length > 0 ? kills[0].ToString("F0") : "-") + " s");
        Check("loud lit craft that sits still: it does kill in the end", kills.Length >= 180, "kills " + kills.Length + "/200");
        if (kills.Length > 0)
            Console.WriteLine("     kill time on the clock: min " + kills[0].ToString("F0") + " median " + Pct(kills, 0.5) + " p90 " + Pct(kills, 0.9) + " max " + kills[kills.Length - 1].ToString("F0") + " s");
        Check("kill times are spread, not one fixed moment", kills.Length > 2 && kills[kills.Length * 9 / 10] - kills[kills.Length / 10] > 45, "p10..p90 " + (kills[kills.Length * 9 / 10] - kills[kills.Length / 10]).ToString("F0") + " s");   // was 60 before the circle was tightened to stay in sight
        Check("feints before the kill", loud.Where(r => r.Caught).All(r => r.Feints >= 1), "feints min " + loud.Where(r => r.Caught).Select(r => r.Feints).DefaultIfEmpty(0).Min());
        Check("every kill was a hit at speed", loud.Where(r => r.Caught).All(r => r.HitSpeed >= 34), "slowest killing hit " + loud.Where(r => r.Caught).Select(r => r.HitSpeed).DefaultIfEmpty(0).Min().ToString("F1") + " m/s");
        Check("a scream for every real rise, none for a feint", loud.All(r => r.Screams == r.RealStrikes), "screams " + loud.Sum(r => r.Screams) + ", real rises " + loud.Sum(r => r.RealStrikes) + ", feints " + loud.Sum(r => r.Feints));
        var near = Enumerable.Range(1, 100).Select(s => Simulate(s, 3, true, 600, 60)).ToArray();
        Check("spawned at 60 m: no kill before 180 s", near.Where(r => r.Caught).All(r => r.KillAt >= 180), "first " + near.Where(r => r.Caught).Select(r => r.KillAt).DefaultIfEmpty(-1).Min().ToString("F0"));

        // ---- only fast enough kills
        var slow = Enumerable.Range(1, 60).Select(s => Simulate(s, 6, true, 1200, setup: b => { b.StrikeSpeed = 28; b.KillSpeed = 34; })).ToArray();
        Check("a rise slower than the kill speed never destroys the craft", slow.All(r => !r.Caught), slow.Count(r => r.Caught) + "/60 killed, " + slow.Sum(r => r.Misses) + " slow rises that touched or missed");

        // ---- the hunt pattern
        Check("it circles its prey", loud.Count(r => r.CircleSweep > 2 * Math.PI) > 180, loud.Count(r => r.CircleSweep > 2 * Math.PI) + "/200 went round at least once, median sweep " + Pct(loud.Select(r => r.CircleSweep * 180 / Math.PI), 0.5) + " deg");
        var cd = loud.SelectMany(r => r.CircleDistances).ToArray();
        foreach (var bad in loud.Where(r => r.Caught && !(r.InspectClosest < 35 && r.InspectSpeed < 14)).Take(5)) Console.WriteLine("     no close pass: closest " + bad.InspectClosest.ToString("F0") + " m at " + bad.InspectSpeed.ToString("F1") + " m/s, kill at " + bad.KillAt.ToString("F0"));
        Check("it comes up to the craft's depth and slowly passes close by", loud.Where(r => r.Caught).All(r => r.InspectClosest < 35 && r.InspectSpeed < 14), "closest pass p50 " + Pct(loud.Select(r => r.InspectClosest), 0.5) + " m, p95 " + Pct(loud.Select(r => r.InspectClosest), 0.95) + " m, at " + Pct(loud.Select(r => r.InspectSpeed), 0.5) + " m/s");
        var underneath = loud.SelectMany(r => r.CircleBelow).ToArray();
        Check("it circles underneath the craft", double.Parse(Pct(underneath, 0.1)) >= 15, "below by p10 " + Pct(underneath, 0.1) + " median " + Pct(underneath, 0.5) + " m");
        Check("circle distance: inside the 60 m the water shows, never on top", cd.Length > 0 && Pct(cd, 0.05) != "-" && double.Parse(Pct(cd, 0.05)) >= 20 && double.Parse(Pct(cd, 0.95)) <= 120, "p5 " + Pct(cd, 0.05) + " median " + Pct(cd, 0.5) + " p95 " + Pct(cd, 0.95) + " m");
        Check("every rise starts from well below the craft", loud.Where(r => r.Strikes > 0).All(r => r.MinStrikeStartBelow >= 70), "shallowest start " + loud.Where(r => r.Strikes > 0).Min(r => r.MinStrikeStartBelow).ToString("F0") + " m below");
        // how dodgeable it is: reported, not required (it is meant to be a real threat)
        foreach (double v in new[] { 6.0, 12.0, 20.0 })
        {
            var dodgers = Enumerable.Range(1, 100).Select(s => Simulate(s, 6, true, 1200, dodge: v)).ToArray();
            Console.WriteLine("     late sideways dodge at " + v + " m/s: " + dodgers.Sum(r => r.Misses) + " misses in 100 runs, " + dodgers.Count(r => r.Caught) + " kills, killing hits at " + Pct(dodgers.Where(r => r.Caught).Select(r => r.HitSpeed), 0.05) + ".." + Pct(dodgers.Where(r => r.Caught).Select(r => r.HitSpeed), 0.95) + " m/s");
            if (dodgers.Any(r => r.Misses > 0)) Check("after a miss at " + v + " m/s it comes round again", dodgers.Where(r => r.Misses > 0).All(r => r.Caught || r.RealStrikes > 1));
        }

        // ---- a craft under way on a straight course: it has to stay with it, pass close and still strike
        foreach (double v in new[] { 4.0, 10.0, 16.0, 24.0, 34.0 })
        {
            var on = Enumerable.Range(1, 60).Select(s => Simulate(s, 6, true, 1200, cruise: v)).ToArray();
            var off = Enumerable.Range(1, 60).Select(s => Simulate(s, 6, true, 1200, cruise: v, setup: b => b.ReactToMotion = false)).ToArray();
            Func<Run[], string> line = rs => "kills " + rs.Count(r => r.Caught) + "/60, close pass " + rs.Count(r => r.InspectClosest < 35) + "/60, within 150 m for " + (rs.Average(r => r.NearShare) * 100).ToString("F0") + " % of the time, misses " + rs.Sum(r => r.Misses);
            Console.WriteLine("     craft at " + v + " m/s: reacting: " + line(on));
            Console.WriteLine("     craft at " + v + " m/s: as before: " + line(off));
            Check("craft at " + v + " m/s: no kill before 180 s", on.Where(r => r.Caught).All(r => r.KillAt >= 180));
            Check("craft at " + v + " m/s: every kill a hit at speed, after a feint", on.Where(r => r.Caught).All(r => r.HitSpeed >= 34 && r.Feints >= 1));
            Check("craft at " + v + " m/s: it only moves where it points, turn limits hold", on.All(r => r.MaxSideSlip < 0.5 && r.MaxTurn <= 110.5 && r.MaxTurnInSight <= 30.5 && r.MaxLateral <= 32.5), "slip " + on.Max(r => r.MaxSideSlip).ToString("F2") + " deg, turn " + on.Max(r => r.MaxTurn).ToString("F0") + " deg/s, in sight " + on.Max(r => r.MaxTurnInSight).ToString("F1"));
            Check("craft at " + v + " m/s: never faster than the rise", on.All(r => r.MaxSpeed <= 48.01), "max " + on.Max(r => r.MaxSpeed).ToString("F1"));
            if (v <= 16)
            {
                Check("craft at " + v + " m/s: it stays with the craft", on.Average(r => r.NearShare) > 0.6, (on.Average(r => r.NearShare) * 100).ToString("F0") + " % (as before " + (off.Average(r => r.NearShare) * 100).ToString("F0") + " %)");
                Check("craft at " + v + " m/s: it gets its close pass", on.Count(r => r.InspectClosest < 35) >= 45, on.Count(r => r.InspectClosest < 35) + "/60 (as before " + off.Count(r => r.InspectClosest < 35) + ")");
                Check("craft at " + v + " m/s: it still kills", on.Count(r => r.Caught) >= 40, on.Count(r => r.Caught) + "/60 (as before " + off.Count(r => r.Caught) + ")");
            }
        }
        var outrun = Enumerable.Range(1, 60).Select(s => Simulate(s, 6, true, 1200, cruise: 42)).ToArray();
        Check("a craft faster than its pace gets away", outrun.Count(r => r.Caught) <= 3, outrun.Count(r => r.Caught) + "/60 killed at 42 m/s");

        // ---- a craft that does not hold its course: the hunt and what the player gets to see of it must survive every manoeuvre
        var moves = new (string name, Func<double, Func<double, BloopBrain, BloopVec, BloopVec>> make)[]
        {
            ("at rest", v => (t, b, c) => new BloopVec(0, 0, 0)),
            ("straight", v => (t, b, c) => new BloopVec(v, 0, 0)),
            ("zigzag, 50 deg either side every 12 s", v => (t, b, c) => { double a = ((int)(t / 12) % 2 == 0 ? 50 : -50) * Math.PI / 180; return new BloopVec(Math.Cos(a) * v, Math.Sin(a) * v, 0); }),
            ("in a circle of 120 m", v => (t, b, c) => { double a = t * v / 120; return new BloopVec(Math.Cos(a) * v, Math.Sin(a) * v, 0); }),
            ("turning back every 25 s", v => (t, b, c) => new BloopVec((int)(t / 25) % 2 == 0 ? v : -v, 0, 0)),
            ("stop and go every 20 s", v => (t, b, c) => new BloopVec((int)(t / 20) % 2 == 0 ? v : 0, 0, 0)),
            // a pilot who keeps turning away: every 3 s a new course at right angles to where the fish is (kept while the fish is straight underneath)
            ("always sideways to the fish", v => { double next = 0; BloopVec dir = new BloopVec(1, 0, 0); return (t, b, c) => { if (t >= next) { next = t + 3; BloopVec rel = b.Position - c; if (rel.Flat > 30) dir = new BloopVec(-rel.Y, rel.X, 0).Unit; } return dir * v; }; }),
            ("random new heading every 5-15 s", v => { var rr = new Random(77); double next = 0, a = 0; return (t, b, c) => { if (t >= next) { a = rr.NextDouble() * 2 * Math.PI; next = t + 5 + rr.NextDouble() * 10; } return new BloopVec(Math.Cos(a) * v, Math.Sin(a) * v, 0); }; }),
        };
        Run[] rest = null;
        foreach (double v in new[] { 4.0, 8.0, 16.0 })
        {
            foreach (var m in moves)
            {
                if (m.name == "at rest" && rest != null) continue;
                var rs = Enumerable.Range(1, 40).Select(s => Simulate(s, 6, true, 900, move: m.make(v))).ToArray();
                if (m.name == "at rest") rest = rs;
                string tag = m.name == "at rest" ? "craft at rest" : "craft at " + v + " m/s " + m.name;
                int feints = rs.Sum(r => r.FeintsRun), seen = rs.Sum(r => r.FeintsSeen);
                Console.WriteLine("     " + tag + ": near " + (rs.Average(r => r.NearShare) * 100).ToString("F0") + " %, passes " + rs.Average(r => r.Passes).ToString("F1") + " a run, " + rs.Average(r => r.PassSeconds).ToString("F1") + " s beside it, feints seen " + seen + "/" + feints + " (worst " + rs.Max(r => r.FeintWorst).ToString("F0") + " m), cues " + rs.Average(r => r.Cues).ToString("F1") + ", under the craft: hard turns " + (100 * rs.Sum(r => r.HardTurnSeconds) / Math.Max(1, rs.Sum(r => r.CircleSeconds))).ToString("F0") + " % of the time, " + (60 * rs.Sum(r => r.Swings) / Math.Max(1, rs.Sum(r => r.CircleSeconds))).ToString("F1") + " swings a minute, kills " + rs.Count(r => r.Caught) + "/40, misses " + rs.Sum(r => r.Misses));
                if (m.name == "at rest") continue;
                Check(tag + ": it stays with the craft", rs.Average(r => r.NearShare) > 0.75, (rs.Average(r => r.NearShare) * 100).ToString("F0") + " %");
                Check(tag + ": the close pass happens and lasts", rs.Count(r => r.Passes >= 1) >= (m.name.StartsWith("always") ? 30 : 36) && rs.Average(r => r.PassSeconds) >= (m.name.StartsWith("always") ? 0.45 : 0.6) * rest.Average(r => r.PassSeconds),   // a pilot who keeps turning away from it does cut the pass short
                    rs.Count(r => r.Passes >= 1) + "/40 runs, " + rs.Average(r => r.PassSeconds).ToString("F1") + " s (at rest " + rest.Average(r => r.PassSeconds).ToString("F1") + " s)");
                Check(tag + ": it swims calmly under the craft", rs.Sum(r => r.HardTurnSeconds) < 0.08 * rs.Sum(r => r.CircleSeconds) && 60 * rs.Sum(r => r.Swings) / Math.Max(1, rs.Sum(r => r.CircleSeconds)) < 14,   // its own weave is 6 a minute; a craft that changes course a few times a minute adds to that
                    (100 * rs.Sum(r => r.HardTurnSeconds) / Math.Max(1, rs.Sum(r => r.CircleSeconds))).ToString("F0") + " % hard turns, " + (60 * rs.Sum(r => r.Swings) / Math.Max(1, rs.Sum(r => r.CircleSeconds))).ToString("F1") + " swings a minute");
                Check(tag + ": feints pass within sight", feints > 0 && seen >= feints * 0.9, seen + "/" + feints);
                Check(tag + ": as many cues as at rest", rs.Average(r => r.Cues) >= 0.7 * rest.Average(r => r.Cues), rs.Average(r => r.Cues).ToString("F1") + " (at rest " + rest.Average(r => r.Cues).ToString("F1") + ")");
                Check(tag + ": no kill before 180 s, limits hold", rs.Where(r => r.Caught).All(r => r.KillAt >= 180 && r.HitSpeed >= 34) && rs.All(r => r.MaxSideSlip < 0.5 && r.MaxTurn <= 110.5 && r.MaxTurnInSight <= 30.5 && r.MaxSpeed <= 48.01));
            }
        }

        // ---- jaws
        Check("jaws shut while it circles, searches and dives, and whenever it is far off", loud.All(r => r.MaxMouthCircle == 0 && r.MaxMouthFar == 0));
        Check("jaws wide open at the hit", loud.Where(r => r.Caught).All(r => r.MouthAtHit > 0.95), "least " + loud.Where(r => r.Caught).Select(r => r.MouthAtHit).DefaultIfEmpty(0).Min().ToString("F2"));

        // ---- senses
        var silent = Enumerable.Range(1, 50).Select(s => Simulate(s, 0, false, 900, 4000)).ToArray();
        Check("silent dark craft far away: never noticed", silent.All(r => r.End == BloopState.Dormant && !r.Caught));
        var sneak = Enumerable.Range(1, 60).Select(s => Simulate(s, 6, false, 1200, 2500, 60)).ToArray();
        Check("craft that goes silent early: no kill before the gate", sneak.All(r => !r.Caught || r.KillAt >= 180));
        Check("it can give up", Enumerable.Range(1, 30).Select(s => { var b = new BloopBrain(s, new BloopVec(2000, 0, -500)); b.Hear(0.8, 8000, new BloopVec(0, 0, -450)); for (double t = 0; t < 600; t += 0.05) b.Update(0.05, new BloopVec(5000, 5000, -450), false, -3000, true); return b.State; }).Count(s => s == BloopState.Gone) > 25);

        // ---- motion: a fish
        Check("it only moves where it points", loud.All(r => r.MaxSideSlip < 0.5), "max " + loud.Max(r => r.MaxSideSlip).ToString("F2") + " deg off the heading");
        Check("turn rate limit 110 deg/s", loud.All(r => r.MaxTurn <= 110.5), "max " + loud.Max(r => r.MaxTurn).ToString("F1") + " deg/s");
        Check("in sight it never turns faster than 30 deg/s", loud.All(r => r.MaxTurnInSight <= 30.5), "max " + loud.Max(r => r.MaxTurnInSight).ToString("F1") + " deg/s");
        Check("sideways pull of a turn limited: wide arcs at speed", loud.All(r => r.MaxLateral <= 32.5), "max " + loud.Max(r => r.MaxLateral).ToString("F1") + " m/s2");
        Check("speed limit", loud.All(r => r.MaxSpeed <= 48.01), "max " + loud.Max(r => r.MaxSpeed).ToString("F1") + " m/s");
        Check("it is fast in the rise", loud.Count(r => r.MaxSpeed > 42) > 150, loud.Count(r => r.MaxSpeed > 42) + "/200 above 42 m/s");
        var shallow = Enumerable.Range(1, 30).Select(s => Simulate(s, 4, true, 600, 800, floorZ: -700, craftZ: -450)).ToArray();
        Check("stays between floor and ceiling (floor 250 m under the craft)", shallow.All(r => r.MinZ >= -698.01 && r.MaxZ <= -2), "z " + shallow.Min(r => r.MinZ).ToString("F0") + " .. " + shallow.Max(r => r.MaxZ).ToString("F0"));
        Check("ceiling: it stays deep while circling", loud.All(r => r.MaxZ <= -150), "highest " + loud.Max(r => r.MaxZ).ToString("F0") + " m (ceiling 200, a rise may overshoot a little)");

        // ---- cues: not periodic
        int count; int[] counts;
        var g = CueGaps(Base(), 3600, 5, out count, out counts);
        double mean = g.Average(), sd = Math.Sqrt(g.Select(x => (x - mean) * (x - mean)).Average());
        Check("cue gaps are irregular", sd / mean > 0.45, "n " + g.Length + " mean " + mean.ToString("F1") + " sd " + sd.ToString("F1"));
        var g2 = CueGaps(Base(), 3600, 6, out count, out counts);
        Check("two seeds differ", !g.Take(5).SequenceEqual(g2.Take(5)));

        // ---- cues: depend on the situation
        int[] Cues(BloopCueInput i) { int n; int[] c; CueGaps(i, 3600, 9, out n, out c); return c; }
        var far = Base(); far.Distance = 6000; far.Noise = 0.9;
        Check("nothing when the monster is far away and the craft is loud", Cues(far).Where((v, k) => k != (int)BloopCue.FalseEcho).Sum() == 0);
        var dark = Base(); dark.LightsOn = false; dark.SurfaceLight = 0.2;
        var c1 = Cues(dark);
        Check("dark: no daylight shadows, lure can show", c1[(int)BloopCue.Dimming] == 0 && c1[(int)BloopCue.Silhouette] == 0 && c1[(int)BloopCue.LureGlow] > 0);
        var day = Base(); day.SurfaceLight = 0.7; day.LookingUp = true; day.Distance = 150;
        var c2 = Cues(day);
        Check("daylight, monster above: shadows, no lure", c2[(int)BloopCue.Dimming] > 0 && c2[(int)BloopCue.Silhouette] > 0 && c2[(int)BloopCue.LureGlow] == 0);
        var lit = Base(); lit.LightsOn = true; lit.InLightCone = true; lit.InFront = true; lit.Distance = 90;
        var c3 = Cues(lit);
        Check("lights on, close: cone glimpse and flicker", c3[(int)BloopCue.ConeGlimpse] > 0 && c3[(int)BloopCue.LightFlicker] > 0);
        var below = Base(); below.MonsterAbove = -60; below.Distance = 200;
        Check("monster below: bubbles, not above: none", Cues(below)[(int)BloopCue.Bubbles] > 0 && Cues(Base())[(int)BloopCue.Bubbles] == 0);
        var floor = Base(); floor.MonsterFloorClearance = 20; floor.CraftFloorClearance = 100;
        Check("near the floor: sediment", Cues(floor)[(int)BloopCue.Sediment] > 0 && Cues(Base())[(int)BloopCue.Sediment] == 0);
        Check("the scream is never a random cue", Cues(lit)[(int)BloopCue.Scream] == 0 && Cues(below)[(int)BloopCue.Scream] == 0);

        // ---- exposure budget: reveals thin out under constant conditions
        var eb = new BloopCues(4); var i2 = lit; i2.Dt = 0.05;
        for (double t = 0; t < 1200; t += 0.05) eb.Update(i2);
        var free = new BloopCues(4) { ExposureWeight = 0 }; int freeN = 0;
        for (double t = 0; t < 1200; t += 0.05) { free.Update(i2); if (free.Fired != BloopCue.None) freeN++; }
        Check("exposure budget lowers the rate", eb.Counts.Sum() < freeN, eb.Counts.Sum() + " with, " + freeN + " without");

        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
