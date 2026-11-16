// Dread: the Moho worm swarm's hunt. Pure C# (no UnityEngine);
// tested offline.
//
// The one rule: the swarm is blind and Moho has no air. It feels only what travels through the ground. An airborne craft is
// invisible to it (hovering high, engines in flight, lights); touching or shaking the ground gives you away. The caller turns
// what the craft does into ground signals (Feel) and reports landed craft near the heap (Presence); the brain never reads the
// craft's position any other way, except while it is locked on after spotting (TrackCraft).
//
// Life cycle (one creature): Dormant (a heap wandering at ~1 m/s) -> Alerted by a touchdown anywhere (serpent to a random point
// 500-1500 m from the craft, then unburrows) -> Search (a heap closing in, then wandering inside a circle round the craft) ->
// Attack once spotted (within 100 m: dive and strike at once; farther: serpent to the source, plunge, strike; locked on, the
// strike follows the craft unless it moved 100 m sideways from where the lock began) -> kill: Fed (a dormant heap that still
// hunts what it spots) / miss: Scramble (heap 10 s, then serpent searching 20 s) -> Wander (heap, random, 1 min) -> Dormant.
//
// Positions are metres on a flat plane (the caller maps the planet to it). Every number is a tuning value.
using System;

namespace UnkerbalThings.Dread
{
    public enum WormState { Dormant, Alerted, Search, Attack, Scramble, Wander, Fed }

    /// <summary>What reaches the swarm through the ground. Presence: a landed craft (or a Kerbal on the ground) near the heap.</summary>
    public enum WormSignal { Presence, Touchdown, Hover, Rcs, Impact, Drill, Explosion, Seismic, Wheels }

    /// <summary>How the body should travel this frame: a crawling heap, the serpent, or the ambush (the caller runs it).</summary>
    public enum WormBodyMode { Heap, Serpent, Ambush }

    /// <summary>What a felt signal did.</summary>
    public enum WormFelt { Nothing, Alerted, Spotted, Ignored }

    public sealed class WormHuntSettings
    {
        // speeds, m/s
        public float DormantSpeed = 2f;
        public float SearchSpeed = 8f;
        public float WanderSpeed = 4f;
        public float SerpentSpeed = 55f;          // travel as the serpent (alerted, attack)
        public float ScrambleSerpentSpeed = 25f;  // the serpent searching after a missed grab
        // alert
        public float AlertMinMetres = 500f;       // it surfaces this far from the craft at least ...
        public float AlertMaxMetres = 1500f;      // ... and at most
        // search
        public float SearchCircleMetres = 500f;   // diameter of the circle round the craft it wanders in
        public float SearchGiveUpSeconds = 240f;  // searching that long inside the circle with nothing: wander off (0 = never)
        // senses (metres); Touchdown alerts at any range
        public float PresenceMetres = 100f;       // a landed craft this close is spotted at once; also the direct-attack range
        public float TouchdownMetres = 1000f;     // a soft touchdown is spotted within this
        public float TouchdownSoftSpeed = 10f;    // m/s at touchdown; up to here it counts as soft
        public float TouchdownHardSpeed = 40f;    // m/s at touchdown where the spotting range reaches ExplosionMetres
        public float HoverMetres = 500f;          // engines on less than HoverHeight above the ground
        public float HoverHeight = 15f;           // (used by the caller) m above the actual ground
        public float RcsMetres = 500f;            // RCS fired while landed
        public float ImpactMetres = 500f;         // something hits the ground (debris, a Kerbal jumping)
        public float DrillMetres = 1750f;
        public float ExplosionMetres = 3000f;
        public float WheelsMetres = 500f;         // driving on the ground (not in the design's table; a landed craft's system)
        // attack
        public float LockEscapeMetres = 100f;     // the strike follows the craft unless it moved this far sideways from the lock
        public float AmbushMetres = 150f;         // the serpent's final burrow goes into the ground this far from the target
        public float PlungeLeadSeconds = 0.85f;   // the plunge's rising arc carries the head on for about this long at travel
                                                  // speed (~45 m at 55 m/s), so the attack starts that much earlier
        public float ArriveMetres = 20f;
        // after a miss
        public float ScrambleHeapSeconds = 10f;
        public float ScrambleSerpentSeconds = 20f;
        public float ScrambleRadius = 300f;
        public float WanderSeconds = 60f;
        // idle wandering: a new direction every so often
        public float DormantTurnMin = 20f, DormantTurnMax = 60f;
        // a dormant or fed heap stays where it settled: it roams only inside a circle of this radius round that spot
        // (spread out a lot, long lines, but generally stay in place). 0 = the old free drift.
        public float DormantLeash = 60f;
        public float WanderTurnMin = 8f, WanderTurnMax = 20f;

        public void Clamp()
        {
            DormantSpeed = Clamp(DormantSpeed, 0f, 20f);
            SearchSpeed = Clamp(SearchSpeed, 0.5f, 60f);
            WanderSpeed = Clamp(WanderSpeed, 0.5f, 60f);
            SerpentSpeed = Clamp(SerpentSpeed, 5f, 300f);
            ScrambleSerpentSpeed = Clamp(ScrambleSerpentSpeed, 5f, 300f);
            AlertMinMetres = Clamp(AlertMinMetres, 0f, 10000f);
            AlertMaxMetres = Clamp(AlertMaxMetres, AlertMinMetres, 10000f);
            SearchCircleMetres = Clamp(SearchCircleMetres, 20f, 10000f);
            SearchGiveUpSeconds = Clamp(SearchGiveUpSeconds, 0f, 3600f);
            PresenceMetres = Clamp(PresenceMetres, 0f, 2000f);
            TouchdownMetres = Clamp(TouchdownMetres, 0f, 20000f);
            TouchdownSoftSpeed = Clamp(TouchdownSoftSpeed, 0.1f, 200f);
            TouchdownHardSpeed = Clamp(TouchdownHardSpeed, TouchdownSoftSpeed + 0.1f, 500f);
            HoverMetres = Clamp(HoverMetres, 0f, 20000f);
            HoverHeight = Clamp(HoverHeight, 0f, 500f);
            RcsMetres = Clamp(RcsMetres, 0f, 20000f);
            ImpactMetres = Clamp(ImpactMetres, 0f, 20000f);
            DrillMetres = Clamp(DrillMetres, 0f, 20000f);
            ExplosionMetres = Clamp(ExplosionMetres, 0f, 20000f);
            WheelsMetres = Clamp(WheelsMetres, 0f, 20000f);
            LockEscapeMetres = Clamp(LockEscapeMetres, 5f, 5000f);
            AmbushMetres = Clamp(AmbushMetres, 8f, 1000f);
            PlungeLeadSeconds = Clamp(PlungeLeadSeconds, 0f, 5f);
            ArriveMetres = Clamp(ArriveMetres, 2f, 200f);
            ScrambleHeapSeconds = Clamp(ScrambleHeapSeconds, 0f, 300f);
            ScrambleSerpentSeconds = Clamp(ScrambleSerpentSeconds, 0f, 300f);
            ScrambleRadius = Clamp(ScrambleRadius, 20f, 5000f);
            WanderSeconds = Clamp(WanderSeconds, 0f, 3600f);
            DormantTurnMin = Clamp(DormantTurnMin, 1f, 600f); DormantTurnMax = Clamp(DormantTurnMax, DormantTurnMin, 600f);
            DormantLeash = Clamp(DormantLeash, 0f, 2000f);
            WanderTurnMin = Clamp(WanderTurnMin, 1f, 600f); WanderTurnMax = Clamp(WanderTurnMax, WanderTurnMin, 600f);
        }

        static float Clamp(float v, float a, float b) { return float.IsNaN(v) ? a : (v < a ? a : (v > b ? b : v)); }

        /// <summary>Range at which a signal spots the craft. Touchdown: by impact speed (soft: TouchdownMetres, up to ExplosionMetres
        /// for a hard landing). Seismic: unlimited. Presence: PresenceMetres.</summary>
        public double SpotRange(WormSignal kind, float strength)
        {
            switch (kind)
            {
                case WormSignal.Presence: return PresenceMetres;
                case WormSignal.Touchdown:
                {
                    if (strength <= TouchdownSoftSpeed) return TouchdownMetres;
                    double u = Math.Min(1.0, (strength - TouchdownSoftSpeed) / (TouchdownHardSpeed - TouchdownSoftSpeed));
                    return TouchdownMetres + (Math.Max(ExplosionMetres, TouchdownMetres) - TouchdownMetres) * u;
                }
                case WormSignal.Hover: return HoverMetres;
                case WormSignal.Rcs: return RcsMetres;
                case WormSignal.Impact: return ImpactMetres;
                case WormSignal.Drill: return DrillMetres;
                case WormSignal.Explosion: return ExplosionMetres;
                case WormSignal.Seismic: return double.PositiveInfinity;
                default: return WheelsMetres;
            }
        }
    }

    public sealed class WormHunt
    {
        readonly WormHuntSettings S;
        uint rng;

        // ---- own body: the caller places it and reads it back after Update (it may hold it still: the heap digs in first)
        public double X, Y;
        public double Heading;          // radians, atan2(y, x)
        public float Speed;             // m/s wanted this frame

        public WormState State { get; private set; }
        public WormBodyMode Body { get; private set; }
        public float StateSeconds { get; private set; }
        /// <summary>Seconds spent searching inside the circle (the give-up clock).</summary>
        public float SearchInsideSeconds { get; private set; }
        public double GoalX { get; private set; }
        public double GoalY { get; private set; }
        public bool HasGoal { get; private set; }

        // belief: where it thinks the craft is (the circle it searches)
        public bool HasBelief { get; private set; }
        public double BeliefX { get; private set; }
        public double BeliefY { get; private set; }
        public double BeliefRadius { get; private set; }

        // lock-on (Attack)
        public bool Locked { get; private set; }
        public double LockX { get; private set; }
        public double LockY { get; private set; }
        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        /// <summary>The craft moved LockEscapeMetres sideways from where the lock began: the strike keeps its last aim.</summary>
        public bool LockBroken { get; private set; }
        /// <summary>Spotted within PresenceMetres: dive and strike on the spot, no serpent.</summary>
        public bool Direct { get; private set; }
        /// <summary>The caller should start the ambush now (Attack and close enough, or direct).</summary>
        public bool WantsAmbush { get; private set; }
        public bool AmbushRunning { get; private set; }

        // the last thing it felt, for the debug readout
        public WormSignal LastSignal { get; private set; }
        public double LastSignalDistance { get; private set; }
        public double LastSignalRange { get; private set; }
        public WormFelt LastFelt { get; private set; }
        public float LastSignalAge { get; private set; }
        public bool HasFelt { get; private set; }
        public int Kills { get; private set; }
        public int Misses { get; private set; }

        float turnTimer, scrambleSerpentLeft;
        bool scrambleSerpent;
        double alertX, alertY;

        public WormHunt(WormHuntSettings settings, int seed)
        {
            S = settings ?? new WormHuntSettings();
            rng = (uint)seed * 2654435761u + 12345u;
            if (rng == 0) rng = 1;
            Reset();
        }

        public WormHuntSettings Settings { get { return S; } }

        public void Reset()
        {
            Enter(WormState.Dormant);
            HasBelief = false;
            HasFelt = false;
            Kills = Misses = 0;
            Speed = 0f;
        }

        // ------------------------------------------------------------------ helpers
        double Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216.0;
        }
        float RandRange(float a, float b) { return a + (float)Rand() * (b - a); }
        static double Dist(double ax, double ay, double bx, double by) { double dx = bx - ax, dy = by - ay; return Math.Sqrt(dx * dx + dy * dy); }
        public double DistanceTo(double x, double y) { return Dist(X, Y, x, y); }

        /// <summary>Where a dormant or fed heap settled (it roams within DormantLeash of this).</summary>
        public double HomeX { get; private set; }
        public double HomeY { get; private set; }

        void Enter(WormState s)
        {
            if (s == WormState.Dormant || s == WormState.Fed) { HomeX = X; HomeY = Y; }
            State = s;
            StateSeconds = 0f;
            SearchInsideSeconds = 0f;
            HasGoal = false;
            turnTimer = 0f;
            WantsAmbush = false;
            if (s != WormState.Attack) { Locked = false; LockBroken = false; Direct = false; AmbushRunning = false; }
            Body = (s == WormState.Alerted || s == WormState.Attack) ? WormBodyMode.Serpent : WormBodyMode.Heap;
        }

        void SetGoal(double x, double y) { GoalX = x; GoalY = y; HasGoal = true; }

        /// <summary>True while the swarm is up to something (not idle): the dread layer counts this as chasing.</summary>
        public bool Aware { get { return State == WormState.Alerted || State == WormState.Search || State == WormState.Attack || State == WormState.Scramble || State == WormState.Wander; } }

        // ------------------------------------------------------------------ senses

        /// <summary>A signal from (sx, sy). strength: touchdown speed in m/s (ignored otherwise).</summary>
        public WormFelt Feel(WormSignal kind, double sx, double sy, float strength)
        {
            double d = Dist(X, Y, sx, sy);
            double range = S.SpotRange(kind, strength);
            WormFelt felt;
            if (State == WormState.Attack) felt = WormFelt.Ignored;   // locked on: one thing at a time
            else if (d <= range) { Spot(sx, sy, d); felt = WormFelt.Spotted; }
            else if (kind == WormSignal.Touchdown) { Alert(sx, sy); felt = WormFelt.Alerted; }
            else felt = WormFelt.Nothing;
            if (kind != WormSignal.Presence || felt == WormFelt.Spotted)
            {
                HasFelt = true;
                LastSignal = kind; LastSignalDistance = d; LastSignalRange = range; LastFelt = felt; LastSignalAge = 0f;
            }
            return felt;
        }

        /// <summary>Test only: behave as if a touchdown at (sx, sy) were felt far away (alert, travel, search), whatever the range.</summary>
        public void ForceAlert(double sx, double sy)
        {
            Alert(sx, sy);
            HasFelt = true; LastSignal = WormSignal.Touchdown; LastSignalDistance = Dist(X, Y, sx, sy); LastSignalRange = 0.0; LastFelt = WormFelt.Alerted; LastSignalAge = 0f;
        }

        /// <summary>A landed craft (or a Kerbal on the ground) at (cx, cy): spotted at once within PresenceMetres.</summary>
        public WormFelt Presence(double cx, double cy) { return Feel(WormSignal.Presence, cx, cy, 0f); }

        void Alert(double sx, double sy)
        {
            // it surfaces at a random point 500-1500 m from the craft, then searches round it
            HasBelief = true;
            BeliefX = sx; BeliefY = sy; BeliefRadius = S.SearchCircleMetres * 0.5;
            double a = Rand() * 2.0 * Math.PI;
            double r = S.AlertMinMetres + Rand() * (S.AlertMaxMetres - S.AlertMinMetres);
            alertX = sx + Math.Cos(a) * r; alertY = sy + Math.Sin(a) * r;
            if (Dist(X, Y, sx, sy) <= r)
            {
                // already that close: no serpent run, it searches from where it is
                Enter(WormState.Search);
                return;
            }
            Enter(WormState.Alerted);
            SetGoal(alertX, alertY);
        }

        void Spot(double sx, double sy, double d)
        {
            Enter(WormState.Attack);
            Locked = true;
            LockBroken = false;
            LockX = TargetX = sx; LockY = TargetY = sy;
            HasBelief = true; BeliefX = sx; BeliefY = sy; BeliefRadius = 0.0;
            Direct = d <= S.PresenceMetres;
            Body = Direct ? WormBodyMode.Ambush : WormBodyMode.Serpent;
            WantsAmbush = Direct;
            SetGoal(sx, sy);
        }

        /// <summary>While locked on: the craft is at (cx, cy). The strike follows it unless it moved LockEscapeMetres sideways from
        /// where the lock began; then the aim stays where it last held.</summary>
        public void TrackCraft(double cx, double cy)
        {
            if (State != WormState.Attack || !Locked) return;
            if (!LockBroken && Dist(cx, cy, LockX, LockY) <= S.LockEscapeMetres) { TargetX = cx; TargetY = cy; }
            else LockBroken = true;
            SetGoal(TargetX, TargetY);
        }

        /// <summary>The caller started the ambush.</summary>
        public void AmbushStarted()
        {
            if (State != WormState.Attack) Enter(WormState.Attack);
            AmbushRunning = true;
            WantsAmbush = false;
            Body = WormBodyMode.Ambush;
        }

        /// <summary>The ambush is over: kill -> a fed, dormant heap; miss -> scramble.</summary>
        public void AmbushEnded(bool kill)
        {
            if (kill) { Kills++; Enter(WormState.Fed); HasBelief = false; }
            else { Misses++; Enter(WormState.Scramble); scrambleSerpent = false; scrambleSerpentLeft = S.ScrambleSerpentSeconds; HasBelief = true; BeliefX = X; BeliefY = Y; BeliefRadius = S.ScrambleRadius; }
        }

        // ------------------------------------------------------------------ per-frame update

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            if (dt > 0.25f) dt = 0.25f;
            StateSeconds += dt;
            LastSignalAge += dt;
            switch (State)
            {
                case WormState.Dormant:
                case WormState.Fed:
                    if (S.DormantLeash <= 0f) { Idle(dt, S.DormantSpeed, S.DormantTurnMin, S.DormantTurnMax); break; }
                    // roam from point to point inside the leash circle round its spot; it never wanders off
                    Body = WormBodyMode.Heap;
                    turnTimer -= dt;
                    if (!HasGoal || turnTimer <= 0f) { turnTimer = RandRange(S.WanderTurnMin, S.WanderTurnMax); SetCirclePoint(HomeX, HomeY, S.DormantLeash); }
                    if (MoveToGoal(dt, S.DormantSpeed)) HasGoal = false;
                    break;

                case WormState.Alerted:
                    Body = WormBodyMode.Serpent;
                    SetGoal(alertX, alertY);
                    if (MoveToGoal(dt, S.SerpentSpeed)) Enter(WormState.Search);   // arrived: it unburrows as a heap and searches
                    break;

                case WormState.Search:
                {
                    Body = WormBodyMode.Heap;
                    double R = BeliefRadius > 1.0 ? BeliefRadius : S.SearchCircleMetres * 0.5;
                    bool outside = Dist(X, Y, BeliefX, BeliefY) > R;
                    if (!outside) SearchInsideSeconds += dt;   // patience counts only inside the circle (closing in can take minutes)
                    if (S.SearchGiveUpSeconds > 0f && SearchInsideSeconds >= S.SearchGiveUpSeconds) { Enter(WormState.Wander); break; }
                    if (outside)
                    {
                        SetGoal(BeliefX, BeliefY);   // close in first, then wander inside the circle
                        turnTimer = 0f;
                    }
                    else if (!HasGoal || PickInside(dt)) SetCirclePoint(BeliefX, BeliefY, R);
                    if (MoveToGoal(dt, S.SearchSpeed)) HasGoal = false;
                    break;
                }

                case WormState.Attack:
                    if (AmbushRunning) { Speed = 0f; Body = WormBodyMode.Ambush; break; }
                    if (Direct) { Speed = 0f; WantsAmbush = true; Body = WormBodyMode.Ambush; break; }
                    Body = WormBodyMode.Serpent;
                    if (WantsAmbush) { Speed = 0f; break; }   // waiting for the caller to start the strike: no closer
                    SetGoal(TargetX, TargetY);
                    MoveToGoal(dt, S.SerpentSpeed);
                    // start early enough that the plunge's forward carry puts the burrow at AmbushMetres, not inside it
                    if (Dist(X, Y, TargetX, TargetY) <= S.AmbushMetres + Speed * S.PlungeLeadSeconds) WantsAmbush = true;
                    break;

                case WormState.Scramble:
                    if (!scrambleSerpent)
                    {
                        Body = WormBodyMode.Heap; Speed = 0f;   // the spire fell into a heap: it lies still
                        if (StateSeconds >= S.ScrambleHeapSeconds) { scrambleSerpent = true; HasGoal = false; }
                        break;
                    }
                    Body = WormBodyMode.Serpent;
                    scrambleSerpentLeft -= dt;
                    if (scrambleSerpentLeft <= 0f) { Enter(WormState.Wander); break; }
                    if (!HasGoal) SetCirclePoint(BeliefX, BeliefY, S.ScrambleRadius);
                    if (MoveToGoal(dt, S.ScrambleSerpentSpeed)) HasGoal = false;
                    break;

                case WormState.Wander:
                    if (StateSeconds >= S.WanderSeconds) { Enter(WormState.Dormant); break; }
                    Idle(dt, S.WanderSpeed, S.WanderTurnMin, S.WanderTurnMax);
                    break;
            }
        }

        bool PickInside(float dt) { turnTimer -= dt; if (turnTimer > 0f) return false; turnTimer = RandRange(S.WanderTurnMin, S.WanderTurnMax); return true; }

        void SetCirclePoint(double cx, double cy, double r)
        {
            double a = Rand() * 2.0 * Math.PI, q = r * Math.Sqrt(Rand());
            SetGoal(cx + Math.Cos(a) * q, cy + Math.Sin(a) * q);
        }

        /// <summary>Heap wandering at `speed`, a new random heading every so often (no goal).</summary>
        /// <summary>Set by the caller each frame: 1 = the heap keeps up; lower = its worms trail far behind, so a crawling heap
        /// slows down and waits for them (the serpent is not affected).</summary>
        public float HeapSpeedScale = 1f;
        float HeapScaled(float speed) { return Body == WormBodyMode.Heap ? speed * Math.Max(0.05f, Math.Min(1f, HeapSpeedScale)) : speed; }

        void Idle(float dt, float speed, float turnMin, float turnMax)
        {
            Body = WormBodyMode.Heap;
            turnTimer -= dt;
            if (turnTimer <= 0f)
            {
                turnTimer = RandRange(turnMin, turnMax);
                double a = Heading + (Rand() - 0.5) * 2.0 * Math.PI * 0.6;   // changes direction now and then, mostly not a full turn
                SetGoal(X + Math.Cos(a) * 1e4, Y + Math.Sin(a) * 1e4);
            }
            if (HasGoal) TurnTo(dt, Math.Atan2(GoalY - Y, GoalX - X), speed > 5f ? 0.6 : 0.25);
            Speed = HeapScaled(speed);
            Advance(dt);
        }

        /// <summary>Moves toward the goal; true when it is there.</summary>
        bool MoveToGoal(float dt, float speed)
        {
            double d = Dist(X, Y, GoalX, GoalY);
            if (d <= S.ArriveMetres) { Speed = 0f; return true; }
            double bearing = Math.Atan2(GoalY - Y, GoalX - X);
            TurnTo(dt, bearing, speed > 10f ? 1.2 : 0.5);
            // slow down while turning toward a goal off to the side, so it cannot orbit round it
            double align = Math.Cos(Wrap(bearing - Heading));
            Speed = (float)Math.Min(HeapScaled(speed) * Math.Max(0.15, align), d / Math.Max(dt, 1e-3));
            Advance(dt);
            return Dist(X, Y, GoalX, GoalY) <= S.ArriveMetres;
        }

        static double Wrap(double e)
        {
            while (e > Math.PI) e -= 2 * Math.PI;
            while (e < -Math.PI) e += 2 * Math.PI;
            return e;
        }

        void TurnTo(float dt, double bearing, double ratePerSecond)
        {
            double e = Wrap(bearing - Heading);
            double m = ratePerSecond * dt;
            Heading += e > m ? m : (e < -m ? -m : e);
        }

        void Advance(float dt)
        {
            double step = Speed * dt;
            X += Math.Cos(Heading) * step;
            Y += Math.Sin(Heading) * step;
        }
    }
}
