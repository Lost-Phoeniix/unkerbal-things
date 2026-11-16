// Dread: the hunt of a walking monster, as a belief about where the craft is. Pure C# (no UnityEngine); tested offline.
//
// The monster has no access to the craft. The only ways in are Hear(...) (a noise event: kind, loudness and the place it came from)
// and Sight(...) (line of sight reported by the caller, with the craft's place, honoured only inside the sight range). Each one
// updates a belief: a point and an error radius. The radius depends on distance and loudness when a noise is heard, and grows with
// time while nothing new arrives. Everything the monster does (walk to the believed spot, sweep the area around it, close in on
// a craft it can see) is driven by the belief and by its own position, never by the true position.
//
// Movement: the monster owns a heading and walks only along it. The heading turns toward the wanted bearing at a limited rate and
// the speed is gated by how well they agree, so it turns before it moves off and can neither side-step nor walk backwards. Speed
// changes at a limited acceleration and braking. Positions are metres on a flat plane (the caller maps the planet to it).
using System;
using System.Collections.Generic;

namespace UnkerbalThings.Dread
{
    public enum HuntState { Dormant, Investigate, Search, Hunt, Attack, GiveUp }

    public enum HuntNoise { Throttle, Rcs, Lights, Impact, Wheels, Decouple }

    /// <summary>What the monster knows of the ground. Walkable: false for sea, forbidden zones, off map. Height: metres.</summary>
    public interface IHuntTerrain
    {
        bool Walkable(double x, double y);
        double Height(double x, double y);
    }

    public sealed class HuntSettings
    {
        // Hearing range at loudness 1, metres (the range shrinks in proportion to the loudness).
        public float HearThrottle = 4000f;
        public float HearRcs = 1200f;
        public float HearLights = 1800f;     // seen rather than heard: the caller only reports it with line of sight
        public float HearImpact = 8000f;
        public float HearWheels = 1500f;
        public float HearDecouple = 5000f;
        public float HearMinMetres;          // every noise carries at least this far, however quiet (0 = off: range times loudness only)

        public float SightMetres = 1500f;    // keeps sight of a craft it already sees up to here
        public float NoticeMetres = 400f;    // first notices a craft (line of sight) only up to here
        public float AttackMetres = 60f;
        public float GiveUpSeconds = 120f;   // searching without any new information
        public float ErrorGrowth = 1.2f;     // metres per second added to the error radius while nothing new arrives
        public float MaxError = 500f;

        public float TurnDegPerSecond = 100f;
        // Motion follows the body (see HeadingMotion). NaturalTurn false restores turning on the spot with the speed gated by alignment.
        public bool NaturalTurn = true;
        public float TurnRadiusMetres = 12f;
        public float TurnRadiusPerSpeed = 2.0f;
        public float TurnYawAccelDegPerSecond2 = 30f;
        public float TurnCreepSpeed = 5f;
        public float TurnEaseFloor = 0.45f;
        public float Accel = 0.45f;          // of top speed, per second
        public float Brake = 0.9f;
        public float InvestigateFactor = 0.6f;
        public float SearchFactor = 0.2f;
        public float HuntFactor = 0.85f;
        public float AttackFactor = 1f;
        public float LingerMetres = 90f;     // while asked to hold back, it does not close in further than this

        public float SearchCell = 60f;
        public float SearchMinRadius = 90f;
        public float SearchMaxRadius = 600f;
        public float ListenMin = 2.5f;
        public float ListenMax = 5f;

        public float MaxGradient = 1.1f;     // rise over run of a step it will take (about 48 degrees)

        public void Clamp()
        {
            GiveUpSeconds = Math.Max(5f, Math.Min(GiveUpSeconds, 1800f));
            NoticeMetres = Math.Max(20f, Math.Min(NoticeMetres, 5000f));
            SightMetres = Math.Max(NoticeMetres, Math.Min(SightMetres, 20000f));
            TurnDegPerSecond = Math.Max(20f, Math.Min(TurnDegPerSecond, 300f));
            TurnRadiusMetres = Math.Max(0.5f, Math.Min(TurnRadiusMetres, 500f));
            TurnRadiusPerSpeed = Math.Max(0f, Math.Min(TurnRadiusPerSpeed, 20f));
            TurnYawAccelDegPerSecond2 = Math.Max(2f, Math.Min(TurnYawAccelDegPerSecond2, 720f));
            TurnCreepSpeed = Math.Max(0.5f, Math.Min(TurnCreepSpeed, 50f));
            TurnEaseFloor = Math.Max(0f, Math.Min(TurnEaseFloor, 1f));
            HearThrottle = Math.Max(0f, HearThrottle);
            HearRcs = Math.Max(0f, HearRcs);
            HearLights = Math.Max(0f, HearLights);
            HearImpact = Math.Max(0f, HearImpact);
            HearWheels = Math.Max(0f, HearWheels);
            HearDecouple = Math.Max(0f, HearDecouple);
        }
    }

    public sealed class DreadHunt
    {
        readonly HuntSettings S;
        readonly IHuntTerrain T;
        uint rng;

        // ---- own body (the caller sets the position each frame; the heading and speed are the monster's own)
        public double X, Y;
        public double Heading;                 // radians, atan2(y, x)
        public float Speed;                    // m/s along the heading

        // ---- belief
        public HuntState State { get; private set; }
        public bool HasBelief { get; private set; }
        public double BeliefX { get; private set; }
        public double BeliefY { get; private set; }
        public double BeliefError { get; private set; }
        public float InfoAge { get; private set; }
        public bool HasSight { get; private set; }
        /// <summary>Stopped on purpose to listen (Search): the steps stop and it does not move.</summary>
        public bool Listening { get; private set; }
        public float SearchSeconds { get; private set; }
        public int Stalls { get; private set; }
        public int WaypointsDone { get; private set; }
        public double WaypointX { get; private set; }
        public double WaypointY { get; private set; }
        public bool HasWaypoint { get; private set; }
        public double Odometer { get; private set; }
        /// <summary>While true the monster holds at LingerMetres from what it sees instead of closing in (the director's release window).</summary>
        public bool Linger;
        /// <summary>Seconds spent turning on the spot while pinned against terrain it may not enter (should stay 0 in open ground).</summary>
        public float PivotSeconds;
        float pinnedTime;
        bool pivoting;

        /// <summary>The heading/speed/turn model. The caller's retreat code uses the same class.</summary>
        public readonly HeadingMotion Motion;
        readonly TurnSettings turnSettings = new TurnSettings();
        /// <summary>Body size factor for the turn radius (1 = the Moho creature).</summary>
        public float TurnScale { get { return turnSettings.SizeScale; } set { turnSettings.SizeScale = Math.Max(0.05f, value); } }
        /// <summary>Yaw rate of the last update, radians per second (positive = counter-clockwise on the plane).</summary>
        public double YawRate { get { return Motion.YawRate; } }

        // ---- sprint. SprintSpeed 0 = off. In Hunt/Attack with sight, or with a belief younger than SprintBeliefAge
        // that is nearer than SprintBeliefMetres, the speed eases up to SprintSpeed at SprintAccel (m/s2) instead of the factor of the top speed.
        public float SprintSpeed;
        public float RunFloor;               // when it moves (except while searching), never slower than this (0 = off); braking for a goal still applies
        public float SprintAccel = 20f;
        public float SprintBeliefAge = 5f;
        public float SprintBeliefMetres = 1500f;
        public bool Sprinting { get; private set; }
        /// <summary>True while the caller flies the monster on a leap: it keeps its heading and speed, nothing steers, brakes or checks the ground.</summary>
        public bool Airborne;

        double searchCx, searchCy, frontier;
        readonly HashSet<long> visited = new HashSet<long>();
        float listenTimer;
        bool listenPending;
        float sightLost;
        float giveUpHold;
        float investigateSeconds;
        float noticeCooldown;
        float walkTime;
        double progressRef = double.MaxValue;
        float progressTimer;
        double detourBearing;
        float detourTimer;
        double detourSide = 1.0;
        float probeTimer, headingProbeTimer;
        bool probeBlocked;
        float blockedTime;
        bool blockedNow;

        public DreadHunt(HuntSettings settings, IHuntTerrain terrain, int seed)
        {
            S = settings ?? new HuntSettings();
            turnSettings.Enabled = S.NaturalTurn;
            turnSettings.RadiusMin = S.TurnRadiusMetres;
            turnSettings.RadiusPerSpeed = S.TurnRadiusPerSpeed;
            turnSettings.MaxYawDegPerSecond = S.TurnDegPerSecond;
            turnSettings.YawAccelDegPerSecond2 = S.TurnYawAccelDegPerSecond2;
            turnSettings.CreepSpeed = S.TurnCreepSpeed;
            turnSettings.EaseFloor = S.TurnEaseFloor;
            turnSettings.Clamp();
            Motion = new HeadingMotion(turnSettings);
            T = terrain;
            rng = (uint)seed * 2654435761u + 362436069u;
            if (rng == 0) rng = 362436069u;
            detourSide = (Rand() < 0.5f) ? 1.0 : -1.0;
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFFu) / 16777216f;
        }

        float RandRange(float a, float b) { return a + (b - a) * Rand(); }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Aware { get { return State != HuntState.Dormant && State != HuntState.GiveUp; } }

        public void Place(double x, double y, double headingRadians)
        {
            X = x; Y = y; Heading = headingRadians;
        }

        public void Reset()
        {
            State = HuntState.Dormant;
            HasBelief = false;
            HasSight = false;
            Listening = false;
            HasWaypoint = false;
            SearchSeconds = 0f;
            visited.Clear();
            blockedTime = 0f;
            detourTimer = 0f;
            Stalls = 0;
            listenPending = false;
        }

        float RangeOf(HuntNoise kind)
        {
            switch (kind)
            {
                case HuntNoise.Throttle: return S.HearThrottle;
                case HuntNoise.Rcs: return S.HearRcs;
                case HuntNoise.Lights: return S.HearLights;
                case HuntNoise.Impact: return S.HearImpact;
                case HuntNoise.Wheels: return S.HearWheels;
                default: return S.HearDecouple;
            }
        }

        // ------------------------------------------------------------------ senses

        /// <summary>A noise came from (sx, sy). Returns true when the monster heard it. The error radius of what it learned depends on
        /// the distance and the loudness: near and loud is precise, far and quiet is vague.</summary>
        public bool Hear(HuntNoise kind, float loudness, double sx, double sy)
        {
            if (loudness > 1f) loudness = 1f;
            if (loudness <= 0.02f) return false;
            double d = Dist(X, Y, sx, sy);
            double range = Math.Max(RangeOf(kind) * loudness, S.HearMinMetres);
            if (d > range) return false;
            double e = 12.0 + 0.10 * d * (1.7 - loudness);
            double ang = Rand() * 2 * Math.PI;
            double r = e * Math.Sqrt(Rand());
            Fuse(sx + Math.Cos(ang) * r, sy + Math.Sin(ang) * r, e);
            if (State == HuntState.Hunt || State == HuntState.Attack) return true;
            double before = (State == HuntState.Search) ? Dist(BeliefX, BeliefY, searchCx, searchCy) : 0.0;
            if (State == HuntState.Search && before < 0.5 * Math.Max(BeliefError, 30.0))
            {
                SearchSeconds = 0f;                  // the sound was where it is already looking: it keeps looking, with fresh patience
            }
            else
            {
                if (State == HuntState.Investigate) investigateSeconds = 0f; else BeginInvestigate();
            }
            return true;
        }

        /// <summary>A rough rumour of where something might be, (x, y) give or take error metres: a monster that knows nothing yet walks
        /// there and searches. It learns nothing more from it; only a real noise or sight makes it hunt. Ignored once it is aware.</summary>
        public void Rumor(double x, double y, double error)
        {
            if (State != HuntState.Dormant) return;
            Fuse(x, y, error);
            BeginInvestigate();
            // the usual 400 s of investigating cover about 10 km; a rumour can be farther (spawns reach 18 km), so it gets the time to walk
            // there even at 15 m/s
            investigateSeconds = -(float)Math.Max(0.0, Dist(X, Y, BeliefX, BeliefY) / 15.0 - 400.0);
        }

        /// <summary>Line of sight to the craft, reported by the caller each frame. Only a craft within the notice range (sight range once
        /// it is being hunted) is seen at all; (sx, sy) is not used otherwise.</summary>
        public void Sight(bool lineOfSight, double sx, double sy)
        {
            bool hunting = State == HuntState.Hunt || State == HuntState.Attack;
            double limit = hunting ? S.SightMetres : S.NoticeMetres;
            bool seen = lineOfSight && (hunting || noticeCooldown <= 0f) && Dist(X, Y, sx, sy) <= limit;
            HasSight = seen;
            if (!seen) return;
            HasBelief = true;
            BeliefX = sx; BeliefY = sy; BeliefError = 2.0;
            InfoAge = 0f;
            sightLost = 0f;
            if (!hunting)
            {
                State = HuntState.Hunt;
                HasWaypoint = false;
                Listening = false;
                listenPending = false;
                ResetProgress();
            }
        }

        void Fuse(double mx, double my, double e)
        {
            double eb = BeliefError;
            if (!HasBelief || Dist(mx, my, BeliefX, BeliefY) > 2.0 * (eb + e))
            {
                HasBelief = true;
                BeliefX = mx; BeliefY = my; BeliefError = e;
            }
            else
            {
                double k = (eb * eb) / (eb * eb + e * e);
                BeliefX += k * (mx - BeliefX);
                BeliefY += k * (my - BeliefY);
                BeliefError = Math.Sqrt(eb * eb * e * e / (eb * eb + e * e));
            }
            InfoAge = 0f;
        }

        // ------------------------------------------------------------------ state changes

        void BeginInvestigate()
        {
            State = HuntState.Investigate;
            investigateSeconds = 0f;
            HasWaypoint = false;
            Listening = false;
            listenPending = false;
            Stalls = 0;
            ResetProgress();
        }

        void BeginSearch(double cx, double cy)
        {
            State = HuntState.Search;
            searchCx = cx; searchCy = cy;
            frontier = S.SearchCell;
            visited.Clear();
            SearchSeconds = 0f;
            HasWaypoint = false;
            listenTimer = RandRange(S.ListenMin, S.ListenMax);   // stops and listens first
            listenPending = true;
            Stalls = 0;
            ResetProgress();
        }

        void ResetProgress()
        {
            progressRef = double.MaxValue;
            progressTimer = 0f;
            walkTime = 0f;
            blockedTime = 0f;
            detourTimer = 0f;
            ClearPath();
        }

        long Key(int ix, int iy) { return ((long)ix << 32) ^ (uint)iy; }

        void CellOf(double x, double y, out int ix, out int iy)
        {
            ix = (int)Math.Round((x - searchCx) / S.SearchCell);
            iy = (int)Math.Round((y - searchCy) / S.SearchCell);
        }

        public double SearchRadius
        {
            get { return Math.Min(S.SearchMaxRadius, Math.Max(S.SearchMinRadius, BeliefError + 40.0)); }
        }

        public int VisitedCells { get { return visited.Count; } }

        bool ChooseWaypoint()
        {
            double cell = S.SearchCell;
            double R = SearchRadius;
            while (true)
            {
                int n = (int)Math.Ceiling(frontier / cell);
                bool found = false;
                double bestCost = double.MaxValue, bx = 0, by = 0;
                for (int ix = -n; ix <= n; ix++)
                {
                    for (int iy = -n; iy <= n; iy++)
                    {
                        double rc = Math.Sqrt((double)ix * ix + (double)iy * iy) * cell;
                        if (rc > frontier + 1e-6) continue;
                        long key = Key(ix, iy);
                        if (visited.Contains(key)) continue;
                        double cx = searchCx + ix * cell, cy = searchCy + iy * cell;
                        if (T != null && !T.Walkable(cx, cy)) { visited.Add(key); continue; }
                        double dx = cx - X, dy = cy - Y;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < cell * 0.3) { visited.Add(key); continue; }
                        double ang = Math.Abs(Wrap(Math.Atan2(dy, dx) - Heading));
                        double cost = d + 0.8 * rc + 40.0 * ang;
                        if (cost < bestCost) { bestCost = cost; bx = cx; by = cy; found = true; }
                    }
                }
                if (found)
                {
                    WaypointX = bx; WaypointY = by; HasWaypoint = true;
                    return true;
                }
                if (frontier >= R) return false;
                frontier = Math.Min(R, frontier + cell);
            }
        }

        void MarkSwept()
        {
            double cell = S.SearchCell;
            int ix, iy;
            CellOf(X, Y, out ix, out iy);
            for (int a = -1; a <= 1; a++)
            {
                for (int b = -1; b <= 1; b++)
                {
                    double cx = searchCx + (ix + a) * cell, cy = searchCy + (iy + b) * cell;
                    if (Dist(X, Y, cx, cy) <= cell * 0.55) visited.Add(Key(ix + a, iy + b));
                }
            }
        }

        // ------------------------------------------------------------------ per-frame update

        /// <summary>Advances the monster by dt seconds. topSpeed is its fastest speed in m/s.</summary>
        public void Update(float dt, float topSpeed)
        {
            if (dt <= 0f) return;
            if (dt > 0.25f) dt = 0.25f;
            if (topSpeed < 0.1f) topSpeed = 0.1f;
            InfoAge += dt;
            if (noticeCooldown > 0f) noticeCooldown -= dt;
            if (HasBelief && State != HuntState.Hunt && State != HuntState.Attack)
            {
                BeliefError = Math.Min(S.MaxError, BeliefError + S.ErrorGrowth * dt);
            }
            blockedNow = false;
            Listening = false;
            Sprinting = false;
            if (Airborne)
            {
                Advance(dt);
                if (HasBelief && State != HuntState.Hunt && State != HuntState.Attack) BeliefError = Math.Min(S.MaxError, BeliefError + S.ErrorGrowth * dt);
                return;
            }
            double goalX = X, goalY = Y;
            float factor = 0f;
            double arrive = 3.0;
            bool stopAtGoal = true;
            bool move = false;

            switch (State)
            {
                case HuntState.Dormant:
                    break;

                case HuntState.GiveUp:
                    giveUpHold += dt;
                    if (giveUpHold > 2.5f) Reset();
                    break;

                case HuntState.Investigate:
                    investigateSeconds += dt;
                    if (!HasBelief || investigateSeconds > 400f)
                    {
                        BeginSearch(X, Y);
                        break;
                    }
                    goalX = BeliefX; goalY = BeliefY;
                    factor = S.InvestigateFactor;
                    arrive = Math.Max(10.0, Math.Min(25.0, BeliefError * 0.2));
                    move = true;
                    if (Dist(X, Y, goalX, goalY) <= arrive)
                    {
                        BeginSearch(BeliefX, BeliefY);
                        move = false;
                    }
                    break;

                case HuntState.Search:
                    SearchSeconds += dt;
                    if (SearchSeconds >= S.GiveUpSeconds) { EnterGiveUp(); break; }
                    if (listenPending)
                    {
                        Listening = true;
                        if (Speed < 0.25f) listenTimer -= dt;
                        if (listenTimer <= 0f) { listenPending = false; Listening = false; }
                        break;
                    }
                    if (!HasWaypoint && !ChooseWaypoint()) { EnterGiveUp(); break; }
                    goalX = WaypointX; goalY = WaypointY;
                    factor = S.SearchFactor;
                    arrive = 6.0;
                    move = true;
                    MarkSwept();
                    if (Dist(X, Y, goalX, goalY) <= arrive)
                    {
                        HasWaypoint = false;
                        WaypointsDone++;
                        listenTimer = RandRange(S.ListenMin, S.ListenMax);
                        listenPending = true;
                        move = false;
                        ResetProgress();
                    }
                    break;

                case HuntState.Hunt:
                case HuntState.Attack:
                    if (!HasSight)
                    {
                        sightLost += dt;
                        if (sightLost > 0.6f)
                        {
                            BeliefError = Math.Max(BeliefError, 20.0);
                            BeginInvestigate();
                            break;
                        }
                    }
                    goalX = BeliefX; goalY = BeliefY;
                    double d = Dist(X, Y, goalX, goalY);
                    if (HasSight && d <= S.AttackMetres) State = HuntState.Attack;
                    else if (State == HuntState.Attack && (d > S.AttackMetres + 20.0 || !HasSight)) State = HuntState.Hunt;
                    factor = State == HuntState.Attack ? S.AttackFactor : S.HuntFactor;
                    Sprinting = SprintSpeed > 0f && !Linger && (HasSight || (HasBelief && InfoAge < SprintBeliefAge && d < SprintBeliefMetres));
                    arrive = 1.5;
                    stopAtGoal = false;
                    move = true;
                    if (Linger)
                    {
                        if (State == HuntState.Attack) State = HuntState.Hunt;
                        factor = S.HuntFactor * 0.6f;
                        arrive = S.LingerMetres;
                        stopAtGoal = true;
                    }
                    break;
            }

            if (move) Walk(dt, topSpeed, goalX, goalY, factor, arrive, stopAtGoal);
            else Brake(dt, topSpeed);
        }

        void EnterGiveUp()
        {
            State = HuntState.GiveUp;
            giveUpHold = 0f;
            HasWaypoint = false;
            listenPending = false;
            Listening = false;
        }

        void Brake(float dt, float topSpeed)
        {
            if (S.NaturalTurn)
            {
                Motion.Sync(Heading, Speed);
                Motion.Coast(dt, S.Brake * topSpeed);
                ApplyMotion();
                return;
            }
            float dec = S.Brake * topSpeed * dt;
            Speed = Math.Max(0f, Speed - dec);
            Advance(dt);
        }

        void Advance(float dt)
        {
            if (Speed <= 0f) return;
            double step = Speed * dt;
            X += Math.Cos(Heading) * step;
            Y += Math.Sin(Heading) * step;
            Odometer += step;
        }

        /// <summary>The caller moves the monster itself (it runs away from the craft): the same heading, speed and turn rules, with the goal as a bearing.
        /// move false: it only brakes. Updates Heading, Speed, X and Y.</summary>
        public void RetreatStep(float dt, double goalBearing, float wantedSpeed, float accel, float brake, bool move)
        {
            if (dt <= 0f) return;
            if (dt > 0.25f) dt = 0.25f;
            Motion.Sync(Heading, Speed);
            if (move) Motion.Step(dt, goalBearing, wantedSpeed, -1f, float.MaxValue, accel, brake, false);
            else Motion.Coast(dt, brake);
            ApplyMotion();
        }

        void ApplyMotion()
        {
            Heading = Motion.Heading;
            Speed = Motion.Speed;
            X += Motion.LastDX;
            Y += Motion.LastDY;
            Odometer += Math.Sqrt(Motion.LastDX * Motion.LastDX + Motion.LastDY * Motion.LastDY);
        }

        void Walk(float dt, float topSpeed, double gx, double gy, float factor, double arrive, bool stopAtGoal)
        {
            double dist = Dist(X, Y, gx, gy);
            double desired = (dist > 0.01) ? Math.Atan2(gy - Y, gx - X) : Heading;
            double bearing = Steer(desired, dt, gx, gy);
            if (S.NaturalTurn)
            {
                // motion follows the body: the goal only steers the heading, the speed follows how well they agree (HeadingMotion)
                float want = topSpeed * factor;
                if (Sprinting && SprintSpeed > want) want = SprintSpeed;
                if (RunFloor > want && State != HuntState.Search) want = RunFloor;   // it runs everywhere except while searching an area
                float cap = float.MaxValue;
                if (stopAtGoal)
                {
                    double brk = S.Brake * topSpeed;
                    cap = (float)(Math.Sqrt(2.0 * brk * Math.Max(0.0, dist - arrive * 0.5)) + 0.3);
                    if (dist <= arrive && Linger) cap = 0f;
                }
                bool pinned = headingBlocked && T != null && headingFree < 4.0;
                if (pivoting && !headingBlocked) pivoting = false;
                if (pinned && Speed < 0.3f)
                {
                    // standing at the edge of what it may walk on (a cliff, the sea) with the way ahead closed: the one case where it turns without moving,
                    // and only after it has stood there for a moment; it keeps turning until the way ahead is open. Counted in PivotSeconds.
                    pinnedTime += dt;
                    if (pinnedTime > 0.25f) pivoting = true;
                }
                else if (!pinned) pinnedTime = 0f;
                if (pivoting)
                {
                    Motion.Sync(Heading, 0f);
                    Motion.Pivot(dt, bearing, 0.6f * S.TurnDegPerSecond);
                    Heading = Motion.Heading;
                    PivotSeconds += dt;
                    WatchProgress(dt, gx, gy, arrive);
                    return;
                }
                if (pinned) cap = 0f;
                else if (headingCrowded && T != null)
                {
                    // the way ahead is closed: slow to the speed whose turn radius fits in the room left (never below the creep speed: it must turn by moving)
                    double room = headingFree - 8.0;
                    double vFit = ((room / turnSettings.SizeScale) - turnSettings.RadiusMin) / Math.Max(0.01f, turnSettings.RadiusPerSpeed);
                    double floor = turnSettings.CreepSpeed * turnSettings.SizeScale;
                    if (vFit < cap && !(Linger && dist <= arrive)) cap = (float)Math.Max(floor, vFit);
                }
                Motion.Sync(Heading, Speed);
                Motion.Step(dt, bearing, want, (float)dist, cap, Sprinting ? SprintAccel : S.Accel * topSpeed, S.Brake * topSpeed, blockedNow || headingBlocked);
                ApplyMotion();
                WatchProgress(dt, gx, gy, arrive);
                return;
            }

            // turn first: the heading follows the wanted bearing at a limited rate, a little slower when fast
            double rate = S.TurnDegPerSecond * Math.PI / 180.0 * (1.0 - 0.4 * Math.Min(1.0, Speed / topSpeed)) * (Sprinting ? 0.6 : 1.0);
            double err = Wrap(bearing - Heading);
            double turn = Math.Max(-rate * dt, Math.Min(rate * dt, err));
            Heading = Wrap(Heading + turn);
            double align = Math.Abs(Wrap(bearing - Heading));

            // speed: gated by alignment, capped so that it can brake in time, eased by acceleration and braking
            double a1 = 20.0 * Math.PI / 180.0, a2 = 75.0 * Math.PI / 180.0;
            double g = align <= a1 ? 1.0 : (align >= a2 ? 0.0 : 1.0 - (align - a1) / (a2 - a1));
            g = g * g * (3 - 2 * g);
            double target = topSpeed * factor * g;
            if (Sprinting && SprintSpeed > target) target = SprintSpeed * g;
            if (RunFloor > target && g > 0 && State != HuntState.Search) target = RunFloor * g;
            if (stopAtGoal)
            {
                double brake = S.Brake * topSpeed;
                double cap = Math.Sqrt(2.0 * brake * Math.Max(0.0, dist - arrive * 0.5)) + 0.3;
                if (target > cap) target = cap;
                if (dist <= arrive && Linger) target = 0.0;
            }
            if (blockedNow || headingBlocked) target = 0.0;
            if (target > Speed) Speed = (float)Math.Min(target, Speed + (Sprinting ? SprintAccel : S.Accel * topSpeed) * dt);
            else Speed = (float)Math.Max(target, Speed - S.Brake * topSpeed * dt);
            Advance(dt);
            WatchProgress(dt, gx, gy, arrive);
        }

        void WatchProgress(float dt, double gx, double gy, double arrive)
        {
            // progress watchdog: walking without getting nearer is a stall
            if (Speed > 0.3f)
            {
                walkTime += dt;
                progressTimer += dt;
                if (progressTimer >= 6f)
                {
                    double nowDist = Dist(X, Y, gx, gy);
                    if (nowDist > progressRef - 4.0 && nowDist > arrive) OnStall();
                    progressRef = nowDist;
                    progressTimer = 0f;
                }
            }
            if (blockedTime > 2f) { blockedTime = 0f; OnStall(); }
        }

        // The way ahead is checked against the terrain. A blocked way is first planned round on a coarse grid (so that it goes the
        // short way round a wall rather than along it); if no plan exists the monster picks the nearest clear heading and keeps it for a
        // moment so that it does not dither. The heading itself is checked as well: it may still be turning toward the chosen bearing.
        double Steer(double desired, float dt, double gx, double gy)
        {
            headingBlocked = false;
            double goalBearing = desired;
            if (T == null) return desired;
            replanCool -= dt;
            if (pathCount > 0)
            {
                pathAge += dt;
                if (Dist(gx, gy, planGoalX, planGoalY) > 200.0 || pathAge > 25f) ClearPath();
                else if (pathAge > 1.5f && Clear(goalBearing, 100.0)) ClearPath();
                else
                {
                    while (pathIdx < pathCount && Dist(X, Y, pathX[pathIdx], pathY[pathIdx]) < 25.0) pathIdx++;
                    if (pathIdx >= pathCount)
                    {
                        ClearPath();
                        replanCool = 4f;
                        blockedNow = true;
                        blockedTime += dt;
                        return desired;
                    }
                    desired = Math.Atan2(pathY[pathIdx] - Y, pathX[pathIdx] - X);
                }
            }
            probeTimer -= dt;
            headingProbeTimer -= dt;
            double look = Math.Max(10.0, Speed * 1.2 + 8.0);
            // an arc needs room: a quarter turn takes one radius. Only the speed is fitted to that room; what counts as blocked is as before.
            double lookArc = S.NaturalTurn ? Math.Max(look, Motion.Radius(Speed) + 12.0) : look;
            if (headingProbeTimer <= 0f)
            {
                headingProbeTimer = 0.08f;
                lastHeadingFree = FreeDistance(Heading, lookArc);
                lastHeadingBlocked = lastHeadingFree < look - 1e-6;
                lastHeadingCrowded = lastHeadingFree < lookArc - 1e-6;
            }
            headingFree = lastHeadingFree;
            headingBlocked = lastHeadingBlocked;
            headingCrowded = lastHeadingCrowded;
            if (detourTimer > 0f)
            {
                detourTimer -= dt;
                if (detourTimer > 0f) return detourBearing;
            }
            if (probeTimer > 0f && !probeBlocked)
            {
                headingBlocked = lastHeadingBlocked;
                return desired;
            }
            probeTimer = 0.12f;
            if (Clear(desired, look))
            {
                probeBlocked = false;
                blockedTime = 0f;
                return desired;
            }
            probeBlocked = true;
            if (pathCount == 0 && replanCool <= 0f)
            {
                replanCool = 4f;
                if (Plan(gx, gy))
                {
                    probeBlocked = false;
                    blockedTime = 0f;
                    return Math.Atan2(pathY[pathIdx] - Y, pathX[pathIdx] - X);
                }
            }
            double[] offsets = { 25, 50, 75, 100, 135 };
            for (int i = 0; i < offsets.Length; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    double side = (s == 0) ? detourSide : -detourSide;
                    double b = Wrap(desired + side * offsets[i] * Math.PI / 180.0);
                    if (Clear(b, look))
                    {
                        detourBearing = b;
                        detourTimer = 1.2f;
                        detourSide = side;
                        probeBlocked = false;
                        blockedTime = 0f;
                        return b;
                    }
                }
            }
            blockedNow = true;
            blockedTime += dt;
            return desired;
        }

        // Steps of at most about 10 m along the line: every one must be walkable and not too steep.
        bool Clear(double bearing, double look)
        {
            return FreeDistance(bearing, look) >= look - 1e-6;
        }

        // Distance along the line to the last clear step before the first blocked one (look when none is blocked).
        double FreeDistance(double bearing, double look)
        {
            double cx = Math.Cos(bearing), cy = Math.Sin(bearing);
            double prevH = T.Height(X, Y);
            int n = Math.Max(3, (int)Math.Ceiling(look / 10.0));
            double seg = look / n;
            for (int k = 1; k <= n; k++)
            {
                double px = X + cx * seg * k, py = Y + cy * seg * k;
                if (!T.Walkable(px, py)) return seg * (k - 1);
                double h = T.Height(px, py);
                if (Math.Abs(h - prevH) / seg > S.MaxGradient) return seg * (k - 1);
                prevH = h;
            }
            return look;
        }

        // ---- path round obstacles: A* on a grid of PathCell around the monster. Terrain is sampled on a lattice of PathCell/4 (each
        // lattice point once per plan, at most MaxSamples), and every edge is checked at lattice steps, so a wall between two nodes is seen.
        const int PN = 16;
        const int PW = 2 * PN + 1;
        const double PathCell = 100.0;
        const int Sub = 4;
        const int MaxSamples = 9000;
        readonly double[] pathX = new double[PW * PW], pathY = new double[PW * PW];
        int pathCount, pathIdx;
        float pathAge, replanCool;
        double planGoalX, planGoalY;
        bool headingBlocked, lastHeadingBlocked, headingCrowded, lastHeadingCrowded;
        double headingFree = double.MaxValue, lastHeadingFree = double.MaxValue;
        float[] pg; int[] pparent; bool[] pclosed; bool[] pseen;
        readonly int[] heapNode = new int[PW * PW * 8 + 16];
        readonly float[] heapKey = new float[PW * PW * 8 + 16];
        int heapN;
        readonly Dictionary<long, float> sampleCache = new Dictionary<long, float>();
        double planOx, planOy;

        public int PathPoints { get { return pathCount; } }
        public int PathIndex { get { return pathIdx; } }
        public double PathNextX { get { return pathCount > 0 && pathIdx < pathCount ? pathX[pathIdx] : double.NaN; } }
        public double PathNextY { get { return pathCount > 0 && pathIdx < pathCount ? pathY[pathIdx] : double.NaN; } }
        void ClearPath() { pathCount = 0; pathIdx = 0; pathAge = 0f; }

        // height at a lattice point, NaN when it cannot be walked on
        float Sample(int lx, int ly)
        {
            long key = ((long)lx << 32) ^ (uint)ly;
            float h;
            if (sampleCache.TryGetValue(key, out h)) return h;
            double step = PathCell / Sub;
            double x = planOx + lx * step, y = planOy + ly * step;
            h = T.Walkable(x, y) ? (float)T.Height(x, y) : float.NaN;
            sampleCache[key] = h;
            return h;
        }

        // an edge between nodes (ax, ay) and (bx, by), in node units, is passable if every lattice step on it is walkable and not too steep
        bool EdgeOk(int ax, int ay, int bx, int by)
        {
            int dx = bx - ax, dy = by - ay;
            double seg = PathCell / Sub * ((dx != 0 && dy != 0) ? 1.4142 : 1.0);
            float prev = Sample(ax * Sub, ay * Sub);
            if (float.IsNaN(prev)) return false;
            for (int k = 1; k <= Sub; k++)
            {
                float h = Sample(ax * Sub + dx * k, ay * Sub + dy * k);
                if (float.IsNaN(h) || Math.Abs(h - prev) / seg > S.MaxGradient) return false;
                prev = h;
            }
            return true;
        }

        void HeapPush(int node, float key)
        {
            int i = heapN++;
            if (i >= heapNode.Length) { heapN--; return; }
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heapKey[p] <= key) break;
                heapNode[i] = heapNode[p]; heapKey[i] = heapKey[p]; i = p;
            }
            heapNode[i] = node; heapKey[i] = key;
        }

        int HeapPop()
        {
            int top = heapNode[0];
            heapN--;
            if (heapN > 0)
            {
                int node = heapNode[heapN]; float key = heapKey[heapN];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= heapN) break;
                    if (c + 1 < heapN && heapKey[c + 1] < heapKey[c]) c++;
                    if (heapKey[c] >= key) break;
                    heapNode[i] = heapNode[c]; heapKey[i] = heapKey[c]; i = c;
                }
                heapNode[i] = node; heapKey[i] = key;
            }
            return top;
        }

        bool Plan(double gx, double gy)
        {
            if (pg == null) { pg = new float[PW * PW]; pparent = new int[PW * PW]; pclosed = new bool[PW * PW]; pseen = new bool[PW * PW]; }
            Array.Clear(pclosed, 0, pclosed.Length);
            Array.Clear(pseen, 0, pseen.Length);
            sampleCache.Clear();
            heapN = 0;
            double ox = X, oy = Y;
            planOx = ox; planOy = oy;
            int gix = (int)Math.Round((gx - ox) / PathCell), giy = (int)Math.Round((gy - oy) / PathCell);
            gix = Math.Max(-PN, Math.Min(PN, gix)); giy = Math.Max(-PN, Math.Min(PN, giy));
            int start = PN * PW + PN, goal = (gix + PN) * PW + (giy + PN);
            pg[start] = 0f; pparent[start] = -1; pseen[start] = true;
            HeapPush(start, 0f);
            int best = start;
            double bestD = Dist(ox, oy, gx, gy);
            bool reached = false;
            while (heapN > 0 && sampleCache.Count < MaxSamples)
            {
                int cur = HeapPop();
                if (pclosed[cur]) continue;
                pclosed[cur] = true;
                int cx = cur / PW - PN, cy = cur % PW - PN;
                double wx = ox + cx * PathCell, wy = oy + cy * PathCell;
                double dgoal = Dist(wx, wy, gx, gy);
                if (dgoal < bestD) { bestD = dgoal; best = cur; }
                if (cur == goal) { reached = true; break; }
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < -PN || nx > PN || ny < -PN || ny > PN) continue;
                        int nn = (nx + PN) * PW + (ny + PN);
                        if (pclosed[nn]) continue;
                        if (!EdgeOk(cx, cy, nx, ny)) continue;
                        float ng = pg[cur] + (float)(dx != 0 && dy != 0 ? 1.4142 : 1.0);
                        if (!pseen[nn] || ng < pg[nn])
                        {
                            pseen[nn] = true; pg[nn] = ng; pparent[nn] = cur;
                            double ddx = Math.Abs(gix - nx), ddy = Math.Abs(giy - ny);
                            float h = (float)(Math.Max(ddx, ddy) + 0.4142 * Math.Min(ddx, ddy));
                            HeapPush(nn, ng + h);
                        }
                    }
                }
            }
            if (best == start) return false;
            int count = 0;
            for (int n = best; n != start && n >= 0; n = pparent[n]) count++;
            if (count == 0) return false;
            pathCount = count;
            int idx = count - 1;
            for (int n = best; n != start && n >= 0; n = pparent[n])
            {
                pathX[idx] = ox + (n / PW - PN) * PathCell;
                pathY[idx] = oy + (n % PW - PN) * PathCell;
                idx--;
            }
            if (reached) { pathX[pathCount - 1] = gx; pathY[pathCount - 1] = gy; }
            pathIdx = 0; pathAge = 0f;
            planGoalX = gx; planGoalY = gy;
            return true;
        }

        void OnStall()
        {
            Stalls++;
            progressRef = double.MaxValue;
            progressTimer = 0f;
            detourSide = -detourSide;
            switch (State)
            {
                case HuntState.Search:
                    if (HasWaypoint)
                    {
                        int ix, iy;
                        CellOf(WaypointX, WaypointY, out ix, out iy);
                        visited.Add(Key(ix, iy));
                    }
                    HasWaypoint = false;
                    break;
                case HuntState.Investigate:
                    if (Stalls >= 2) BeginSearch(X, Y);
                    break;
                case HuntState.Hunt:
                case HuntState.Attack:
                    if (Stalls >= 3)
                    {
                        noticeCooldown = 10f;
                        HasSight = false;
                        BeginSearch(X, Y);
                    }
                    else
                    {
                        detourBearing = Wrap(Heading + detourSide * Math.PI / 2);
                        detourTimer = 3f;
                    }
                    break;
            }
        }
    }
}
