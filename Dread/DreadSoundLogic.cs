// Dread soundscape, game-thread side: from where the craft is and what the director and the hunt report, it decides which bed is up
// and which clip plays when. Nothing is on a clock: every gap is drawn at random, clumps included, no two gaps alike, no clip twice running.
// Vacuum: only what touches the craft (hull, ground). Below the surface: water, hull under pressure, whales.
// A monster that stops to listen silences the place. Pure C# (no UnityEngine): the offline tests render through this same code.
using System;

namespace UnkerbalThings.Dread
{
    public enum DreadSoundEnv { None, VacuumLanded, VacuumFlying, Underwater, Surface }

    public enum DreadSoundKind { Groan, Hit, Crunch, ThumpFar, ThumpMid, ThumpNear, Land, Creak, Boom, Cage, Whale, Erupt, Seize, Kill, Count }

    public struct DreadSoundCue
    {
        public DreadSoundKind Kind;
        public float Gain, Pitch, Pan, CutoffHz;
    }

    public struct DreadSoundInput
    {
        public float Dt;
        public DreadSoundEnv Env;
        public float DepthMetres;
        public float Menace;
        /// <summary>Metres to the nearest monster, below 0 when there is none.</summary>
        public float Distance;
        /// <summary>-1 left of the listener .. 1 right of it, where the nearest monster is.</summary>
        public float Bearing;
        public bool Chasing;
        public bool Searching;
        public bool Listening;
        /// <summary>The body itself has a monster living on it, so its bed runs even when none is active.</summary>
        public bool Haunted;
        public float VesselTons;
        public bool HullSilence;
        public bool Attack;
        /// <summary>High time warp and the like: everything fades and nothing new starts.</summary>
        public bool Suspended;
        /// <summary>The Moho worm travelling under the ground: metres from the craft to it; 0 or below (the default) when it is not travelling.</summary>
        public float WormRumbleMetres;
        /// <summary>0..1, its speed against its sprint.</summary>
        public float WormRumbleSpeed;
        /// <summary>The ambush dive and the silence before the eruption: everything of ours goes quiet, fast.</summary>
        public bool WormSilence;
        /// <summary>The swarm holds the craft.</summary>
        public bool WormGrip;
    }

    public sealed class DreadSoundLogic
    {
        public const int MaxCues = 8;
        public static readonly string[] KindNames = { "groan", "hit", "crunch", "thump-far", "thump-mid", "thump-near", "land", "creak", "boom", "cage", "whale", "erupt", "seize", "kill" };

        // Clips are normalised in the mix tool (loudest 400 ms at -18 dBFS, beds at -30 dBFS RMS), so these are real levels.
        const float BedFileDb = -30f;
        public float BedTrimDb, EventTrimDb;
        public bool Whales = true;
        /// <summary>Seconds the game's music takes to fade out once something is out there (and the soundscape to fade in); 0 leaves the music alone.</summary>
        public float MusicFadeSeconds = 25f;
        /// <summary>Below this depth the water scene starts (it stops again 20 m above it). A monster or menace starts it at any depth.</summary>
        public float WaterSceneDepth = 100f;
        /// <summary>The body's own idle bed (Moho with no monster about). Off: silence and the game's music until something spawns.</summary>
        public bool HauntedBed;

        public readonly DreadSoundCue[] Cues = new DreadSoundCue[MaxCues];
        public int CueCount { get; private set; }
        /// <summary>Which clip set is needed: 0 none, 1 vacuum, 2 water.</summary>
        public int WantedSet { get; private set; }
        public float VacBedA { get; private set; }
        public float VacBedB { get; private set; }
        public float WaterBed { get; private set; }
        public float PressureBed { get; private set; }
        public float WaterCutoffHz { get; private set; }
        /// <summary>The worm's rumble through the ground (vacuum set, bed slot 2) and its low-pass: closer is louder and brighter.</summary>
        public float RumbleBed { get; private set; }
        public float RumbleCutoffHz { get; private set; }
        /// <summary>Level of all one-shots (mixer VoiceLevel): 1, pulled to 0 in the worm's silence.</summary>
        public float EventGain { get; private set; } = 1f;
        public float Tension { get; private set; }
        /// <summary>True in the places the soundscape replaces the synth's hull, drone and voice.</summary>
        public bool OwnsSound { get; private set; }
        /// <summary>Factor for the game's music, 1 = untouched. Falls slowly to 0 while the soundscape runs, comes back slowly after.</summary>
        public float MusicGain { get; private set; }

        bool deep;
        float music = 1f;
        float tension, duck = 1f, envA, envW, envS, waterLevelDb = -44f;
        float hullTimer, whaleTimer, attackTimer, gripTimer, lastGap, lastWhaleGap, rumble, walkR, eventGain = 1f;
        float walkA, walkB, walkW, walkP;
        DreadSoundKind lastKind = DreadSoundKind.Count;
        int run;
        bool wasChasing;
        DreadSoundEnv env;
        float depth, tons = 10f, bearing, distance = -1f;
        readonly int[][] history = new int[(int)DreadSoundKind.Count][];
        uint rng;

        public DreadSoundLogic(int seed)
        {
            rng = (uint)seed * 2654435761u + 362436069u;
            if (rng == 0) rng = 362436069u;
            for (int i = 0; i < history.Length; i++) history[i] = new[] { -1, -1, -1 };
            hullTimer = U(6f, 18f);
            whaleTimer = U(8f, 25f);
            WaterCutoffHz = 2400f;
            MusicGain = 1f;
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFFu) / 16777216f;
        }
        float U(float a, float b) { return a + (b - a) * Rand(); }
        float LogU(float a, float b) { return a * (float)Math.Pow(b / a, Rand()); }
        float Gauss() { return (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - Rand())) * Math.Cos(2.0 * Math.PI * Rand())); }
        static float C01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Db(float db) { return (float)Math.Pow(10.0, db / 20.0); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        static float Follow(float now, float target, float dt, float tau) { return now + (target - now) * (1f - (float)Math.Exp(-dt / tau)); }

        // slow random level, so that a loop's own shape cannot be learned
        float Walk(float x, float dt, float tau, float sd)
        {
            float a = (float)Math.Exp(-dt / tau);
            return a * x + sd * (float)Math.Sqrt(1f - a * a) * Gauss();
        }

        /// <summary>Picks a clip of a kind that was not among the last ones played.</summary>
        public int Pick(DreadSoundKind kind, int count)
        {
            if (count <= 0) return -1;
            int[] h = history[(int)kind];
            int keep = Math.Min(h.Length, count - 1), pick = 0;
            for (int t = 0; t < 40; t++)
            {
                pick = (int)(Rand() * count);
                if (pick >= count) pick = count - 1;
                bool seen = false;
                for (int k = 0; k < keep; k++) if (h[k] == pick) seen = true;
                if (!seen) break;
            }
            for (int k = h.Length - 1; k > 0; k--) h[k] = h[k - 1];
            h[0] = pick;
            return pick;
        }

        public void Update(in DreadSoundInput i)
        {
            CueCount = 0;
            float dt = i.Dt > 0f ? (i.Dt < 1f ? i.Dt : 1f) : 0f;
            env = i.Env;
            depth = Math.Max(i.DepthMetres, 0f);
            tons = i.VesselTons > 0f ? i.VesselTons : tons;
            bearing = i.Bearing;
            distance = i.Distance;
            bool vac = env == DreadSoundEnv.VacuumLanded || env == DreadSoundEnv.VacuumFlying;
            bool under = env == DreadSoundEnv.Underwater;
            bool monster = i.Distance >= 0f;

            // tension: the director's menace, or how close it is if the monster knows about the craft
            float c = monster ? DreadStepLogic.Closeness(i.Distance, 30f, 3000f) : 0f;
            float aware = i.Chasing ? 1f : (i.Searching ? 0.75f : 0.4f);
            float want = Math.Max(C01(i.Menace), c * aware);
            if (under) want = Math.Max(want, 0.25f * C01(depth / 500f));
            tension = Follow(tension, want, dt, want > tension ? 3f : 12f);
            Tension = tension;

            // the beds run only where something is out there (and deep under water): a monster spawning starts them, slowly, while the music goes
            bool threat = i.Menace > 0.02f || monster;
            deep = under && depth > (deep ? WaterSceneDepth - 20f : WaterSceneDepth);
            bool vacOn = vac && (threat || (i.Haunted && HauntedBed)) && !i.Suspended;
            bool waterOn = ((under && (deep || threat)) || (env == DreadSoundEnv.Surface && threat)) && !i.Suspended;
            float rise = Math.Max(MusicFadeSeconds, 3f) / 3f;
            envA = Follow(envA, vacOn ? 1f : 0f, dt, vacOn ? rise : 2f);
            envW = Follow(envW, waterOn ? 1f : 0f, dt, waterOn ? rise : 3f);
            envS = Follow(envS, under && waterOn ? 1f : 0f, dt, rise);
            bool scene = (vacOn && threat) || waterOn;
            float musicTo = scene && MusicFadeSeconds > 0f ? 0f : 1f;
            // a straight line down over MusicFadeSeconds (squared, so it sounds even), and half as fast back once it is over
            music += (musicTo - music) > 0f ? Math.Min(musicTo - music, dt / Math.Max(MusicFadeSeconds * 2f, 1f)) : Math.Max(musicTo - music, -dt / Math.Max(MusicFadeSeconds, 1f));
            MusicGain = music * music;
            OwnsSound = vac || under || env == DreadSoundEnv.Surface;
            WantedSet = vac ? 1 : (under || env == DreadSoundEnv.Surface ? 2 : 0);

            // a listening monster and the director's hull silence pull the bed down in a fade, not a cut
            // far away a search stops every few seconds, and that must not become a pumping bed: it only counts once the monster is near (full from about 700 m, nothing beyond 1800 m)
            float listen = i.Listening && monster ? C01(1.6f * DreadStepLogic.Closeness(i.Distance, 150f, 1800f)) : 0f;
            float duckTo = i.WormSilence ? 0f : (i.HullSilence ? 0.06f : 1f - 0.75f * listen);
            duck = Follow(duck, duckTo, dt, duckTo < duck ? (i.WormSilence ? 0.25f : 0.8f) : 3f);

            // the worm under the ground reaches a landed craft only through the ground: louder and less dull the closer it is,
            // a little louder when it sprints. Nothing while flying, nothing in the silence.
            walkR = Walk(walkR, dt, 9f, 2f);
            bool rumbleOn = env == DreadSoundEnv.VacuumLanded && i.WormRumbleMetres > 0f && !i.WormSilence && !i.Suspended;
            float rc = rumbleOn ? DreadStepLogic.Closeness(i.WormRumbleMetres, 15f, 2500f) : 0f;
            float rumbleTo = rumbleOn ? Db(Lerp(-42f, -6f, rc) + Lerp(-6f, 0f, C01(i.WormRumbleSpeed)) + walkR + BedTrimDb - BedFileDb) : 0f;
            rumble = Follow(rumble, rumbleTo, dt, rumbleTo > rumble ? 0.6f : (i.WormSilence ? 0.2f : 1.2f));
            RumbleBed = rumble;
            RumbleCutoffHz = Lerp(250f, 2000f, rc * rc);
            // the silence also takes the one-shots already sounding (a groan's tail must not run into it); back at once after it
            eventGain = Follow(eventGain, i.WormSilence ? 0f : 1f, dt, i.WormSilence ? 0.15f : 0.02f);
            EventGain = eventGain;

            walkA = Walk(walkA, dt, 22f, 2.5f);
            walkB = Walk(walkB, dt, 31f, 2.5f);
            walkW = Walk(walkW, dt, 26f, 1.5f);
            walkP = Walk(walkP, dt, 19f, 2.5f);
            float vacDb = Lerp(-54f, -38f, tension) + BedTrimDb - BedFileDb;
            VacBedA = envA * duck * Db(vacDb + walkA);
            VacBedB = envA * duck * Db(vacDb - 3f + walkB);
            waterLevelDb = Follow(waterLevelDb, under ? Lerp(-37f, -33f, tension) : -44f, dt, 2f);
            WaterBed = envW * duck * Db(waterLevelDb + BedTrimDb - BedFileDb + walkW);
            PressureBed = envW * envS * duck * Db(Lerp(-58f, -40f, tension) + BedTrimDb - BedFileDb + walkP);
            WaterCutoffHz = under ? DepthCutoff(depth) : 3000f;

            bool quiet = listen > 0.2f || i.HullSilence || i.WormSilence || i.Suspended || env == DreadSoundEnv.None;
            if (i.Chasing && !wasChasing && !quiet) hullTimer = Math.Min(hullTimer, U(0.8f, 3f));
            wasChasing = i.Chasing;

            if (i.Attack && !i.Suspended && (vac || under))
            {
                attackTimer -= dt;
                if (attackTimer <= 0f)
                {
                    attackTimer = LogU(0.35f, 1.4f);
                    if (vac) Cue(DreadSoundKind.Crunch, -3f + U(-2f, 2f), 1f, 0f, 0f);
                    else if (Rand() < 0.5f) Cue(DreadSoundKind.Boom, -3f, 1f, 0f, EventCutoff());
                    else Cue(DreadSoundKind.Cage, -3f, 1f, 0f, EventCutoff());
                }
            }

            // the swarm on the craft: bodies pressing and the structure giving, again and again, overlapping
            if (i.WormGrip && vac && !i.Suspended)
            {
                gripTimer -= dt;
                if (gripTimer <= 0f)
                {
                    gripTimer = U(1.1f, 2.8f);
                    Cue(DreadSoundKind.Seize, U(-5f, -1f), MassPitch() * U(0.95f, 1.05f), U(-0.35f, 0.35f), 0f);
                }
            }
            else gripTimer = 0f;

            if (quiet || !(vacOn || (waterOn && under)) || (vac ? envA : envW) < 0.3f) return;
            hullTimer -= dt;
            if (hullTimer <= 0f)
            {
                PlayHull();
                hullTimer = NextHullGap(i.Haunted && !monster && i.Menace <= 0.02f);
            }
            if (under && Whales)
            {
                whaleTimer -= dt;
                if (whaleTimer <= 0f)
                {
                    PlayWhale();
                    whaleTimer = NextWhaleGap();
                }
            }
        }

        // shallow water is open, deep water only lets the low end through (1 m: 2400 Hz, 300 m and below: 320 Hz)
        public static float DepthCutoff(float metres)
        {
            float u = C01((float)(Math.Log(Math.Max(metres, 1f)) / Math.Log(300.0)));
            return 2400f * (float)Math.Pow(320f / 2400f, u);
        }

        float EventCutoff()
        {
            if (env != DreadSoundEnv.Underwater) return 0f;
            float u = C01((float)(Math.Log(Math.Max(depth, 1f)) / Math.Log(300.0)));
            return 6000f * (float)Math.Pow(1800f / 6000f, u);
        }

        // bigger craft: lower and a little louder (10 t: as recorded, 1000 t: 20% lower)
        float MassPitch() { return 1.04f - 0.24f * C01((float)(Math.Log10(Math.Max(tons, 1f)) / 3.0)); }
        float MassGainDb() { return 2.5f * C01((float)(Math.Log10(Math.Max(tons, 1f)) / 3.0)); }

        float NextHullGap(bool idle)
        {
            float gap = 0f;
            for (int t = 0; t < 4; t++)
            {
                // about one in five comes right after the last, which is what stops it sounding like a schedule
                gap = Rand() < 0.2f ? U(0.4f, 2.5f) : LogU(Lerp(25f, 4f, tension), Lerp(90f, 18f, tension));
                if (idle) gap *= 1.6f;
                if (lastGap <= 0f || Math.Abs(gap - lastGap) > 0.03f * gap) break;
            }
            lastGap = gap;
            return gap;
        }

        float NextWhaleGap()
        {
            float gap = 0f;
            for (int t = 0; t < 4; t++)
            {
                gap = LogU(Lerp(30f, 14f, tension), Lerp(100f, 45f, tension));
                if (lastWhaleGap <= 0f || Math.Abs(gap - lastWhaleGap) > 0.03f * gap) break;
            }
            lastWhaleGap = gap;
            return gap;
        }

        void PlayHull()
        {
            DreadSoundKind k;
            for (int t = 0; ; t++)
            {
                float u = Rand();
                if (env == DreadSoundEnv.Underwater) k = u < 0.68f ? DreadSoundKind.Creak : (u < 0.88f ? DreadSoundKind.Cage : DreadSoundKind.Boom);
                else if (env == DreadSoundEnv.VacuumLanded) k = u < 0.62f ? DreadSoundKind.Groan : (u < 0.82f || tension < 0.3f ? DreadSoundKind.Hit : DreadSoundKind.Crunch);
                else k = u < 0.8f || tension < 0.6f ? (u < 0.8f ? DreadSoundKind.Groan : DreadSoundKind.Hit) : DreadSoundKind.Crunch;
                if (k != lastKind || run < 2 || t > 8) break;
            }
            float db;
            switch (k)
            {
                case DreadSoundKind.Crunch: db = Lerp(-10f, -2f, tension); break;
                case DreadSoundKind.Hit:
                case DreadSoundKind.Cage: db = Lerp(-16f, -7f, tension); break;
                case DreadSoundKind.Boom: db = Lerp(-14f, -5f, tension); break;
                default: db = Lerp(-14f, -5f, tension); break;
            }
            Cue(k, db + MassGainDb() + U(-1.5f, 1.5f), MassPitch() * U(0.97f, 1.03f), U(-0.25f, 0.25f), EventCutoff());
        }

        void PlayWhale()
        {
            // far: a distant, dull call; near and hunting: louder, more open, lower
            float c = distance >= 0f ? DreadStepLogic.Closeness(distance, 60f, 4000f) : 0f;
            float depthOpen = EventCutoff();
            float cut = Math.Min(depthOpen, 900f * (float)Math.Pow(4000f / 900f, c));
            Cue(DreadSoundKind.Whale, Lerp(-13f, -2f, Math.Max(c, 0.5f * tension)), U(0.97f, 1.03f) * Lerp(1f, 0.9f, c * tension), distance >= 0f ? 0.7f * bearing : U(-0.5f, 0.5f), cut);
        }

        void Cue(DreadSoundKind kind, float db, float pitch, float pan, float cutoff)
        {
            if (CueCount >= MaxCues) return;
            if (kind == lastKind) run++; else { lastKind = kind; run = 1; }
            Cues[CueCount++] = new DreadSoundCue { Kind = kind, Gain = Db(db + EventTrimDb), Pitch = pitch, Pan = pan, CutoffHz = cutoff };
        }

        // ---- things that happened: called on the game thread between updates, played by the next Update's caller through Drain ----

        readonly DreadSoundCue[] pending = new DreadSoundCue[MaxCues];
        int pendingCount;

        void Push(DreadSoundKind kind, float db, float pitch, float pan, float cutoff)
        {
            if (pendingCount >= MaxCues) return;
            pending[pendingCount++] = new DreadSoundCue { Kind = kind, Gain = Db(db + EventTrimDb), Pitch = pitch, Pan = pan, CutoffHz = cutoff };
        }

        /// <summary>Moves what happened since the last update into Cues. Call after Update.</summary>
        public void Drain()
        {
            for (int k = 0; k < pendingCount && CueCount < MaxCues; k++) Cues[CueCount++] = pending[k];
            pendingCount = 0;
        }

        /// <summary>A step of the staged warning. Through the ground only: a craft in flight gets none.</summary>
        public void Warning(int stage, float strength)
        {
            if (env != DreadSoundEnv.VacuumLanded) return;
            DreadSoundKind k = stage <= 1 ? DreadSoundKind.ThumpFar : (stage == 2 ? DreadSoundKind.ThumpMid : DreadSoundKind.ThumpNear);
            Push(k, Lerp(-12f, -1f, C01(strength)), U(0.97f, 1.03f), 0f, 0f);
            // the craft answers the shock a moment later, only as a loose rattle of its own structure
            if (stage >= 2) hullTimer = Math.Min(hullTimer, U(0.6f, 2f));
        }

        /// <summary>The monster coming down from a leap, scale 0..1 by distance and force. Through the ground only.</summary>
        public void Landing(float scale)
        {
            if (env != DreadSoundEnv.VacuumLanded || scale < 0.02f) return;
            float s = C01(scale);
            if (s > 0.35f) Push(DreadSoundKind.Land, Lerp(-10f, 0f, s), U(0.97f, 1.03f), 0f, 0f);
            else Push(s > 0.12f ? DreadSoundKind.ThumpMid : DreadSoundKind.ThumpFar, Lerp(-14f, -5f, s / 0.35f), U(0.97f, 1.03f), 0f, 0f);
        }

        /// <summary>The Moho worm erupts under the craft. Landed: the ground bursts under it. Flying: only debris reaches the hull.</summary>
        public void WormErupt(float strength)
        {
            float s = C01(strength);
            if (env == DreadSoundEnv.VacuumLanded) Push(DreadSoundKind.Erupt, Lerp(-6f, 0f, s), U(0.97f, 1.03f), U(-0.15f, 0.15f), 0f);
            else if (env == DreadSoundEnv.VacuumFlying) Push(DreadSoundKind.Hit, Lerp(-16f, -8f, s), MassPitch() * U(0.97f, 1.03f), U(-0.3f, 0.3f), 0f);
        }

        /// <summary>The swarm destroys the craft: the structure fails, tanks vent.</summary>
        public void WormKill()
        {
            if (env == DreadSoundEnv.VacuumLanded || env == DreadSoundEnv.VacuumFlying) Push(DreadSoundKind.Kill, 0f, MassPitch() * U(0.97f, 1.03f), 0f, 0f);
        }

        /// <summary>The director asks for a creak.</summary>
        public void DirectorCreak()
        {
            if (env == DreadSoundEnv.Underwater || env == DreadSoundEnv.Surface) Push(DreadSoundKind.Creak, Lerp(-13f, -5f, tension), MassPitch() * U(0.97f, 1.03f), U(-0.25f, 0.25f), EventCutoff());
            else if (env != DreadSoundEnv.None) Push(DreadSoundKind.Groan, Lerp(-13f, -5f, tension), MassPitch() * U(0.97f, 1.03f), U(-0.25f, 0.25f), 0f);
        }

        /// <summary>The director asks for a thump: through the ground when landed, a knock on the hull otherwise.</summary>
        public void DirectorThump()
        {
            if (env == DreadSoundEnv.VacuumLanded) Push(tension > 0.6f ? DreadSoundKind.ThumpMid : DreadSoundKind.ThumpFar, Lerp(-12f, -4f, tension), U(0.97f, 1.03f), 0f, 0f);
            else if (env == DreadSoundEnv.VacuumFlying) Push(DreadSoundKind.Hit, Lerp(-14f, -6f, tension), MassPitch() * U(0.97f, 1.03f), U(-0.25f, 0.25f), 0f);
            else if (env != DreadSoundEnv.None) Push(DreadSoundKind.Boom, Lerp(-12f, -4f, tension), U(0.97f, 1.03f), U(-0.25f, 0.25f), EventCutoff());
        }

        /// <summary>A scare elsewhere in the mod wants a sound of this kind right now, if the place allows it. strength 0..1.</summary>
        public void Scare(DreadSoundKind kind, float strength)
        {
            bool vac = env == DreadSoundEnv.VacuumLanded || env == DreadSoundEnv.VacuumFlying;
            bool water = env == DreadSoundEnv.Underwater || env == DreadSoundEnv.Surface;
            bool fromVac = kind <= DreadSoundKind.Land || kind >= DreadSoundKind.Erupt;
            if (!(vac && fromVac) && !(water && !fromVac)) return;
            if (kind == DreadSoundKind.Whale && !Whales) return;
            Push(kind, Lerp(-14f, 0f, C01(strength)), U(0.97f, 1.03f), kind == DreadSoundKind.Whale ? 0.7f * bearing : 0f, water ? EventCutoff() : 0f);
        }
    }
}
