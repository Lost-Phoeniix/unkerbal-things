// DreadSynth: pure C# procedural horror-ambience synth. No UnityEngine, no System.Linq.
// Feed it from OnAudioFilterRead:   synth.Render(data, channels);   (overwrites the buffer)
//
// Threading: public level/parameter fields are volatile and may be written from the game thread at any
// time. Events are requested with the Trigger* methods (they only set volatile request fields) and are
// consumed by the audio thread at 32-sample control boundaries. Render() allocates nothing and is
// deterministic: same seed + same parameter/trigger history at the same control boundaries => same samples,
// independent of how the audio callback chops the stream (see tests).
//
// Layers: drone (v2: sparse tones, no noise bed, no periodic modulation), hull creaks/groans (v2: irregular
// stick-slip impulses into inharmonic plate-like modes, low-passed; CreakPing is kept only as an alias of
// stick-slip), thumps (v2: impact into inharmonic hull modes with a faint long ring), creature presence
// (default level 0), signal static, one riser per chase (default level 0), and a HullSilence event
// (short fades, everything cut, then fade back).
using System;
using System.Threading;

namespace KrakensKreations.Dread
{
    public sealed class DreadSynth
    {
        // ---- event kinds written to the event log ----
        public const int EvCreak = 1;
        public const int EvThump = 2;
        public const int EvCreature = 3;
        public const int EvRiser = 4;
        public const int EvSilence = 5;
        public const int EvThrustCreak = 6;

        // ---- creak kinds for TriggerCreak ----
        public const int CreakRandom = -1;
        public const int CreakStickSlip = 0;
        /// <summary>Kept for API compatibility. Tonal pings were removed; this now produces a stick-slip creak.</summary>
        public const int CreakPing = 1;
        public const int CreakGroan = 2;

        // ---- parameters (any thread) ----
        public volatile float MasterLevel = 1f;
        public volatile float DroneLevel = 1f;
        public volatile float CreakLevel = 1f;
        public volatile float ThumpLevel = 1f;
        /// <summary>Creature voice. Default 0 (ear test judged it bad; real recordings will likely replace it). Code kept.</summary>
        public volatile float CreatureLevel = 0f;
        /// <summary>Radio static layer. Drive this with the director's signalNoise (0 = silent).</summary>
        public volatile float StaticLevel = 0f;
        /// <summary>Riser. Default 0 (ear test: sounded like brushes and a fan). Code kept.</summary>
        public volatile float RiserLevel = 0f;
        /// <summary>0..1, shapes timbre/loudness of the drone and the cadence of automatic events.</summary>
        public volatile float Menace = 0f;
        /// <summary>0..1 throttle; with ThrustCorrelation a thrust rise sometimes provokes a creak.</summary>
        public volatile float Thrust = 0f;
        public volatile bool ThrustCorrelation = false;
        /// <summary>Automatic irregular scheduling of creaks (20-90 s), thumps and creatures.</summary>
        public volatile bool AutoEvents = true;
        /// <summary>Duration of the next riser in seconds (clamped 4..60).</summary>
        public volatile float RiserSeconds = 16f;
        /// <summary>Test hook: scales the creature breath component (1 = normal, 0 = pulses only).</summary>
        public volatile float CreatureBreathScale = 1f;
        /// <summary>Debug read-out: current period-doubling depth of the creature voice (0 when not phonating).</summary>
        public float CreatureDoubling { get { return crDbgPsd; } }
        float crDbgPsd, crDbgPitch;
        /// <summary>Debug read-out: current effective pitch (Hz) of the creature voice, 0 when not phonating.</summary>
        public float CreaturePitch { get { return crDbgPitch; } }

        // ---- event requests (set by game thread, consumed by audio thread) ----
        volatile int reqCreak;     // 0 none, else kind+2
        volatile int reqThump;
        volatile int reqCreature;
        volatile int reqRiser;
        volatile int reqChase;
        volatile int reqSilence;
        volatile float reqSilenceHold = 2.5f;

        public void TriggerCreak(int kind) { reqCreak = (kind < 0 || kind > 2) ? 1 : kind + 2; }
        public void TriggerCreak() { reqCreak = 1; }
        public void TriggerThump() { reqThump = 1; }
        public void TriggerCreature() { reqCreature = 1; }
        /// <summary>Starts the single riser of this chase (ignored if one already ran since NewChase()).</summary>
        public void TriggerRiser() { reqRiser = 1; }
        /// <summary>Re-arms the riser (call when a new chase begins).</summary>
        public void NewChase() { reqChase = 1; }
        /// <summary>Everything fades out (~120 ms), stays silent for holdSeconds, fades back in (~350 ms).</summary>
        public void TriggerHullSilence(float holdSeconds)
        {
            if (!(holdSeconds >= 0.3f)) holdSeconds = 0.3f;
            if (holdSeconds > 10f) holdSeconds = 10f;
            reqSilenceHold = holdSeconds;
            reqSilence = 1;
        }

        // ---- event log (audio thread writes; read from tests / debug) ----
        const int EvCap = 512;
        readonly double[] evT = new double[EvCap];
        readonly int[] evK = new int[EvCap];
        volatile int evCount;
        public int EventCount { get { return evCount; } }
        public double GetEventTime(int i) { return evT[i]; }
        public int GetEventKind(int i) { return evK[i]; }

        // ---- stick-slip log (test / debug read-out): every friction slip of every creak, in order ----
        const int SlipCap = 16384;
        readonly double[] slT = new double[SlipCap];
        readonly int[] slE = new int[SlipCap];
        volatile int slCount; int creakSerial;
        /// <summary>Number of logged friction slips (capped), their absolute times in seconds, and the creak event index each belongs to.</summary>
        public int SlipCount { get { return slCount; } }
        public double GetSlipTime(int i) { return slT[i]; }
        public int GetSlipEvent(int i) { return slE[i]; }
        /// <summary>True while the hull silence event has the output faded out or fading.</summary>
        public bool InSilence { get { return silState != 0; } }
        public int SampleRate { get { return fs; } }

        // ================= internals =================
        const int Ctl = 32;               // control block (samples)
        const float CreakGain = 0.14f;
        const float ThumpGain = 0.50f;
        const float CreatureGain = 0.30f;
        const float RiserGain = 0.085f;
        const float DroneGain = 0.034f;
        const float StaticGain = 0.35f;
        const float TwoPi = 6.2831853071795864769f;
        /// <summary>Overall output scale: leaves headroom on top of the game's engines and music; the limiter is only a safety net.</summary>
        const float OutGain = 0.50f;
        long limCount;
        /// <summary>Samples (per channel) where the safety limiter was reducing gain, and total samples rendered (per channel).</summary>
        public long LimiterActiveSamples { get { return limCount; } }
        public long TotalSamples { get { return tSamples; } }

        readonly int fs;
        readonly float fsf;
        readonly double phaseK;           // Hz -> uint phase increment
        uint rng;
        long tSamples;
        int ctr;

        // sine table
        static readonly float[] Tab = BuildTab();
        static float[] BuildTab()
        {
            float[] t = new float[4097];
            for (int i = 0; i < 4097; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / 4096.0);
            return t;
        }
        static float Sin(uint ph)
        {
            int i = (int)(ph >> 20);
            float fr = (ph & 0xFFFFFu) * (1f / 1048576f);
            float a = Tab[i];
            return a + (Tab[i + 1] - a) * fr;
        }

        // rng
        float Rnd()
        {
            uint x = rng; x ^= x << 13; x ^= x >> 17; x ^= x << 5; rng = x;
            return (x >> 8) * (1f / 16777216f);
        }
        float Noise() { return Rnd() * 2f - 1f; }
        float R(float a, float b) { return a + (b - a) * Rnd(); }

        static float Smooth(float u) { if (u <= 0f) return 0f; if (u >= 1f) return 1f; return u * u * (3f - 2f * u); }

        // ---- filters ----
        struct Svf
        {
            public float ic1, ic2, g, k, a1, a2, a3;
            public void Set(float f, float q, float fsr)
            {
                float lim = fsr * 0.45f; if (f > lim) f = lim; if (f < 5f) f = 5f;
                g = (float)Math.Tan(Math.PI * f / fsr); k = 1f / q;
                a1 = 1f / (1f + g * (g + k)); a2 = g * a1; a3 = g * a2;
            }
            // unity-peak bandpass
            public float Bp(float v)
            {
                float v3 = v - ic2;
                float v1 = a1 * ic1 + a2 * v3;
                float v2 = ic2 + a2 * ic1 + a3 * v3;
                ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
                return k * v1;
            }
            public void Clear() { ic1 = 0f; ic2 = 0f; }
        }
        struct Bq
        {
            public float b0, b1, b2, a1, a2, z1, z2;
            public void HighPass(float f, float q, float fsr)
            {
                double w = 2.0 * Math.PI * f / fsr, c = Math.Cos(w), al = Math.Sin(w) / (2.0 * q), a0 = 1.0 + al;
                b0 = (float)((1.0 + c) / 2.0 / a0); b1 = (float)(-(1.0 + c) / a0); b2 = b0;
                a1 = (float)(-2.0 * c / a0); a2 = (float)((1.0 - al) / a0);
            }
            public float Proc(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
            public void Flush()
            {
                if (z1 < 1e-15f && z1 > -1e-15f) z1 = 0f;
                if (z2 < 1e-15f && z2 > -1e-15f) z2 = 0f;
            }
        }

        // ---- level smoothing ----
        const int NL = 7; // 0 drone 1 creak 2 thump 3 creature 4 static 5 riser 6 master
        readonly float[] lvSm = new float[NL];
        readonly float[] lvCur = new float[NL];
        readonly float[] lvNxt = new float[NL];
        readonly float[] lvInc = new float[NL];
        float lvCoef;
        float menSm;
        float menCoef;

        static float Clamp01(float v) { if (!(v > 0f)) return 0f; if (v > 1f) return 1f; return v; }

        // ---- output stage ----
        Bq hpL1, hpL2, hpR1, hpR2;
        // silence event
        int silState; // 0 idle 1 out 2 hold 3 in
        int silN, silOutN, silHoldN, silInN;
        float silGain = 1f;

        // ---- drone ----
        // v2: no noise bed and no periodic modulation. A steady pressure-like low tone (55 Hz plus its locked second
        // harmonic so small speakers carry it) and six faint upper tones at mutually dissonant, non-beating
        // spacings that mostly sit at zero and swell in and out over tens of seconds.
        const int NP = 8;
        static readonly double[] DF = { 55.0, 110.0, 148.0, 233.1, 293.7, 358.6, 471.9, 587.0 };
        static readonly float[] DA = { 1f, 0.40f, 0.30f, 0.32f, 0.24f, 0.22f, 0.18f, 0.14f };
        readonly uint[] dPh = new uint[NP];
        readonly uint[] dInc = new uint[NP];
        readonly float[] dG = new float[NP];
        readonly float[] dGi = new float[NP];
        readonly float[] dPl = new float[NP];
        readonly float[] dPr = new float[NP];
        readonly float[] dAFrom = new float[NP], dATo = new float[NP], dAPos = new float[NP], dADur = new float[NP], dFFrom = new float[NP], dFTo = new float[NP], dFPos = new float[NP], dFDur = new float[NP];
        float DrawDroneAmp(int p) { return p < 2 ? R(0.75f, 1.15f) : (Rnd() < 0.28f ? R(0.35f, 1.0f) : 0f); }

        // ---- hull voices (creaks / groans) ----
        // Physical stick-slip: irregular slip impulses (clustered, heavy-tailed gaps) excite NM inharmonic
        // plate-like modal resonators; the whole is low-passed as if heard through a hull.
        const int NM = 7;
        static readonly float[] PlateRatio = { 1.00f, 1.27f, 1.59f, 1.88f, 2.30f, 2.80f, 3.40f };
        sealed class HullVoice
        {
            public bool active; public int kind, n, len, att, rel;
            public Svf[] s = new Svf[NM]; public float[] f0 = new float[NM], fb = new float[NM], g = new float[NM], q = new float[NM];
            public int nf; public float amp, sag;
            public int nextSlip, slipPos, clusterLeft, clMean, serial; public float slipAmp, gapMax;
            public float lpA, lp1, lp2, lp3, hiss;
            public float panL, panR; public int delay; public bool delayLeft; public float[] dl = new float[64]; public int dlPos;
        }
        const int NHull = 3;
        readonly HullVoice[] hull = new HullVoice[NHull];
        int slipLen; float hissDecay;

        // ---- thump voices: impact pulse into inharmonic hull modes + faint long ring ----
        const int NT = 7;
        sealed class ThumpVoice
        {
            public bool active; public int n, len, fadeN, pl, p2, pl2; public int[] endN = new int[NT];
            public float amp, a2, panL, panR;
            public Svf[] s = new Svf[NT]; public float[] g = new float[NT];
        }
        readonly float[] thFr = new float[NT];
        const int NThump = 3;
        readonly ThumpVoice[] thump = new ThumpVoice[NThump];

        // ---- creature ----
        bool crActive, crFlag; int crPhase, crPhrasesLeft, crSeg, crSegLen, crN, crAtt, crRel, crHold;
        float crAmp, crPan, crBase, crPf0, crF, crTarget, crJn, crAn, crPsd, crRoughRate, crRoughDepth, crBreath, crPulseG, crLpA, crLp1, crLp2, crLp3, crSlew, crJa, crAa, crPph, crRateMul;
        uint crPsdU, crPsdInc, crRph;
        readonly float[] crFa = new float[3], crFb = new float[3], crQ = new float[3], crG = new float[3], crPj = new float[3], crNj = new float[3];
        readonly Svf[] crS = new Svf[3];
        const float CrScale = 0.70f;
        static readonly float[] CrRatio = { 0.75f, 0.84f, 0.94f, 1f, 1.1f, 1.25f, 1.5f, 1.8f };

        // ---- riser ----
        bool riserActive, riserUsed; int riN, riLen; float riF0, riF1, riAmp; uint riPh1, riPh2, riInc1, riInc2; float riEnv, riEnvInc; int blk;

        // ---- static ----
        float hl1, hl2, hr1, hr2, aHi, aLo, gate, gateTgt, gateCoef; int gateLeft;
        Svf crkL, crkR;

        // ---- scheduler ----
        double creakNext, thumpNext, creatureNext, lastBig = -100, lastCreakGap, lastThumpGap, lastCreatureGap;
        float thEma, thEmaCoef; double lastThrustTrig = -100, thrustPending = -1; int thrustCount, thrustBreakAt;

        public DreadSynth(int sampleRate, int seed)
        {
            if (sampleRate < 8000) sampleRate = 8000;
            fs = sampleRate; fsf = sampleRate;
            phaseK = 4294967296.0 / sampleRate;
            rng = (uint)seed * 2654435761u + 0x9E3779B9u; if (rng == 0) rng = 1u;
            for (int i = 0; i < 8; i++) Rnd();

            lvCoef = 1f - (float)Math.Exp(-Ctl / (fsf * 0.05));
            menCoef = 1f - (float)Math.Exp(-Ctl / (fsf * 0.4));
            thEmaCoef = 1f - (float)Math.Exp(-Ctl / (fsf * 1.5));

            hpL1.HighPass(32f, 0.5412f, fsf); hpL2.HighPass(32f, 1.3066f, fsf);
            hpR1 = hpL1; hpR2 = hpL2;

            for (int p = 0; p < NP; p++)
            {
                dPh[p] = (uint)(Rnd() * 4294967295.0);
                double pan = (Rnd() * 2.0 - 1.0) * 0.7 * (p < 2 ? 0.0 : 1.0); // the low tone sits in the centre
                dPl[p] = (float)Math.Cos((pan + 1.0) * Math.PI / 4.0);
                dPr[p] = (float)Math.Sin((pan + 1.0) * Math.PI / 4.0);
                dADur[p] = R(10f, 40f); dAFrom[p] = DrawDroneAmp(p); dATo[p] = DrawDroneAmp(p); dAPos[p] = Rnd() * dADur[p];
                dFDur[p] = R(6f, 20f); dFFrom[p] = R(-0.004f, 0.004f); dFTo[p] = R(-0.004f, 0.004f); dFPos[p] = Rnd() * dFDur[p];
                dG[p] = 0f;
            }
            slipLen = (int)(0.0012 * fs); if (slipLen < 8) slipLen = 8; hissDecay = (float)Math.Exp(-1.0 / (0.008 * fs));

            for (int i = 0; i < NHull; i++) hull[i] = new HullVoice();
            for (int i = 0; i < NThump; i++) thump[i] = new ThumpVoice();
            crSlew = 1f - (float)Math.Exp(-1.0 / (0.003 * fs)); crJa = 1f - (float)Math.Exp(-TwoPi * 70.0 / fs); crAa = 1f - (float)Math.Exp(-TwoPi * 100.0 / fs);
            aHi = 1f - (float)Math.Exp(-TwoPi * 4500.0 / fs);
            aLo = 1f - (float)Math.Exp(-TwoPi * 1200.0 / fs);
            gateCoef = 1f - (float)Math.Exp(-1.0 / (0.006 * fs));
            crkL.Set(3000f, 2.5f, fsf); crkR.Set(3600f, 2.5f, fsf);

            creakNext = 6.0 + 25.0 * Rnd();
            thumpNext = 15.0 + 40.0 * Rnd();
            creatureNext = 40.0 + 60.0 * Rnd();
            thrustBreakAt = 3 + (int)(Rnd() * 3f);
            ctr = 0;
        }

        // ================= Render =================
        public void Render(float[] buffer, int channels)
        {
            if (buffer == null || channels < 1) return;
            int frames = buffer.Length / channels;
            int idx = 0;
            for (int i = 0; i < frames; i++)
            {
                if (ctr == 0) Control();
                ctr = (ctr + 1) & (Ctl - 1);
                float l, r;
                Tick(out l, out r);
                if (channels == 1) buffer[idx] = (l + r) * 0.5f;
                else
                {
                    buffer[idx] = l; buffer[idx + 1] = r;
                    for (int c = 2; c < channels; c++) buffer[idx + c] = 0f;
                }
                idx += channels;
            }
            // any trailing partial frame
            for (; idx < buffer.Length; idx++) buffer[idx] = 0f;
        }

        // ================= control rate =================
        void Log(int kind)
        {
            int c = evCount;
            if (c < EvCap) { evT[c] = tSamples / (double)fs; evK[c] = kind; evCount = c + 1; }
        }

        double Gap(double lo, double hi, double pw, double scale, double prev)
        {
            double g = 0;
            for (int t = 0; t < 3; t++)
            {
                double u = Rnd();
                g = (lo + (hi - lo) * Math.Pow(u, pw)) * scale;
                if (prev <= 0 || Math.Abs(g - prev) > 0.15 * prev) break; // repeat penalty: no near-identical gaps
            }
            return g;
        }

        void Control()
        {
            double sec = tSamples / (double)fs;
            // levels
            float t0 = Clamp01(DroneLevel), t1 = Clamp01(CreakLevel), t2 = Clamp01(ThumpLevel), t3 = Clamp01(CreatureLevel),
                  t4 = Clamp01(StaticLevel), t5 = Clamp01(RiserLevel), t6 = Clamp01(MasterLevel);
            lvSm[0] += (t0 - lvSm[0]) * lvCoef; lvSm[1] += (t1 - lvSm[1]) * lvCoef; lvSm[2] += (t2 - lvSm[2]) * lvCoef;
            lvSm[3] += (t3 - lvSm[3]) * lvCoef; lvSm[4] += (t4 - lvSm[4]) * lvCoef; lvSm[5] += (t5 - lvSm[5]) * lvCoef;
            lvSm[6] += (t6 - lvSm[6]) * lvCoef;
            const float inv = 1f / Ctl;
            for (int k = 0; k < NL; k++)
            {
                float v = lvSm[k]; if (v < 1e-7f) { v = 0f; lvSm[k] = 0f; }
                lvCur[k] = lvNxt[k]; lvNxt[k] = v; lvInc[k] = (v - lvCur[k]) * inv;
            }
            menSm += (Clamp01(Menace) - menSm) * menCoef;
            float men = menSm;

            hpL1.Flush(); hpL2.Flush(); hpR1.Flush(); hpR2.Flush();

            // ---- requests ----
            int rq;
            rq = Interlocked.Exchange(ref reqChase, 0); if (rq != 0) riserUsed = false;
            rq = Interlocked.Exchange(ref reqSilence, 0);
            if (rq != 0 && silState == 0)
            {
                silOutN = (int)(0.12 * fs); silInN = (int)(0.35 * fs); silHoldN = (int)(reqSilenceHold * fs);
                silState = 1; silN = 0; Log(EvSilence);
            }
            rq = Interlocked.Exchange(ref reqCreak, 0); if (rq != 0 && silState == 0) { StartCreak(rq - 2); Log(EvCreak); }
            rq = Interlocked.Exchange(ref reqThump, 0); if (rq != 0 && silState == 0) { StartThump(); Log(EvThump); lastBig = sec; }
            rq = Interlocked.Exchange(ref reqCreature, 0); if (rq != 0 && silState == 0) { StartCreature(); Log(EvCreature); lastBig = sec; }
            rq = Interlocked.Exchange(ref reqRiser, 0);
            if (rq != 0 && silState == 0 && !riserUsed && !riserActive) { StartRiser(); Log(EvRiser); lastBig = sec; }

            // ---- thrust-correlated creak ----
            float th = Clamp01(Thrust);
            if (ThrustCorrelation && silState == 0)
            {
                if (thrustPending < 0 && th - thEma > 0.3f && sec - lastThrustTrig > 10.0)
                {
                    lastThrustTrig = sec; thrustCount++;
                    if (thrustCount == thrustBreakAt) thrustPending = sec + 7.0 + 7.0 * Rnd();   // the one deliberate break
                    else if (thrustCount > thrustBreakAt && Rnd() > 0.5f) { }                      // correlation now unreliable
                    else thrustPending = sec + 0.25 + 0.85 * Rnd();
                }
                if (thrustPending >= 0 && sec >= thrustPending)
                {
                    thrustPending = -1; StartCreak(-1); Log(EvThrustCreak);
                }
            }
            thEma += (th - thEma) * thEmaCoef;

            // ---- automatic irregular events ----
            if (AutoEvents && silState == 0)
            {
                bool calm = sec - lastBig > 5.0;
                if (sec >= creakNext)
                {
                    StartCreak(-1); Log(EvCreak);
                    double g = Gap(20.0, 90.0, 1.4, 1.0 - 0.35 * men, lastCreakGap); lastCreakGap = g; creakNext = sec + g;
                }
                if (sec >= thumpNext)
                {
                    if (calm)
                    {
                        StartThump(); Log(EvThump); lastBig = sec;
                        double g = Gap(35.0, 140.0, 1.2, 1.0 - 0.3 * men, lastThumpGap); lastThumpGap = g; thumpNext = sec + g;
                    }
                    else thumpNext = sec + 6.0;
                }
                if (sec >= creatureNext)
                {
                    if (calm && men > 0.35f)
                    {
                        StartCreature(); Log(EvCreature); lastBig = sec;
                        double g = Gap(70.0, 200.0, 1.1, 1.0 - 0.5 * men, lastCreatureGap); lastCreatureGap = g; creatureNext = sec + g;
                    }
                    else creatureNext = sec + 10.0;
                }
            }

            // ---- drone partial targets ----
            blk++;
            if ((blk & 3) == 0)   // drone partial targets every 128 samples (slow LFOs), linear ramp in between
            {
                float up = 0.8f + 0.8f * men;
                double fLow = DF[0];
                for (int p = 0; p < NP; p++)
                {
                    // each partial wanders on its own: amplitude between random targets (10-40 s segments, upper
                    // tones mostly at zero), small pitch drift between random targets (6-20 s segments); never repeating
                    const float step = 4f * Ctl;
                    dAPos[p] += step / fsf; if (dAPos[p] >= dADur[p]) { dAFrom[p] = dATo[p]; dATo[p] = DrawDroneAmp(p); dAPos[p] = 0f; dADur[p] = R(10f, 40f); }
                    dFPos[p] += step / fsf; if (dFPos[p] >= dFDur[p]) { dFFrom[p] = dFTo[p]; dFTo[p] = R(-0.004f, 0.004f); dFPos[p] = 0f; dFDur[p] = R(6f, 20f); }
                    double amp = DA[p] * (dAFrom[p] + (dATo[p] - dAFrom[p]) * Smooth(dAPos[p] / dADur[p]));
                    if (p >= 2) amp *= up;
                    double f = DF[p] * (1.0 + dFFrom[p] + (dFTo[p] - dFFrom[p]) * Smooth(dFPos[p] / dFDur[p]));
                    if (p == 0) fLow = f; else if (p == 1) f = 2.0 * fLow;   // second harmonic stays locked to the low tone
                    dInc[p] = (uint)(f * phaseK);
                    float gt = (float)amp * DroneGain;
                    dGi[p] = (gt - dG[p]) * (inv * 0.25f);
                }
            }

            // ---- hull voice filter updates (every 64 samples) ----
            if ((blk & 3) == 0)   // every 128 samples: the sag is slow
            {
                for (int i = 0; i < NHull; i++)
                {
                    HullVoice v = hull[i];
                    if (!v.active) continue;
                    float t = v.n < 0 ? 0f : (float)v.n / v.len;
                    float sagMul = 1f - v.sag * Smooth(t);     // slow sagging pitch over the event
                    for (int j = 0; j < v.nf; j++)
                        v.s[j].Set(v.f0[j] * sagMul, v.q[j], fsf);
                }
            }

            // ---- riser pitch and envelope (linear ramp across the block) ----
            if (riserActive)
            {
                float u = (float)riN / riLen; if (u > 1f) u = 1f;
                float f = riF0 * (float)Math.Pow(riF1 / riF0, Math.Pow(u, 1.3));
                riInc1 = (uint)(f * phaseK); riInc2 = (uint)(f * 1.007 * phaseK);
                int nxt = riN + Ctl; float un = (float)nxt / riLen; if (un > 1f) un = 1f;
                float e = (float)Math.Pow(un, 1.7);
                int left = riLen - nxt; int fadeN = (int)(0.6 * fs);
                if (left < fadeN) e *= Smooth((float)left / fadeN);
                riEnvInc = (e - riEnv) * inv;
            }

            // ---- creature formants ----
            if (crActive && crPhase == 1 && (blk & 1) == 0) CreatureFilters(Smooth((float)crN / crSegLen));

            // ---- static gate ----
            if (lvNxt[4] <= 0f && lvCur[4] <= 0f) { hl1 = hl2 = hr1 = hr2 = 0f; crkL.Clear(); crkR.Clear(); }
        }

        // ================= event starters (audio thread) =================
        HullVoice FreeHull()
        {
            for (int i = 0; i < NHull; i++) if (!hull[i].active) return hull[i];
            return null;
        }

        float LogUni(float a, float b) { return a * (float)Math.Exp(Math.Log(b / a) * Rnd()); }

        // Creak = physical stick-slip. Slips come at irregular, heavy-tailed, clustered intervals (no steady rate);
        // each slip is a ~1 ms impulse into NM inharmonic plate-like modes (about 80-900 Hz) whose pitch sags slowly;
        // the sum is then low-passed three times, as if heard through a hull. Kinds: stick-slip (1-3.5 s) and
        // groan (3-6 s, lower, more sag). CreakPing is an alias of stick-slip: no tonal pings exist any more.
        void StartCreak(int kind)
        {
            HullVoice v = FreeHull(); if (v == null) return;
            if (kind < 0) kind = Rnd() < 0.6f ? CreakStickSlip : CreakGroan;
            else if (kind == CreakPing) kind = CreakStickSlip;
            v.kind = kind; v.n = 0; v.amp = R(0.5f, 1f); v.serial = creakSerial++;
            float pan = R(-0.8f, 0.8f);
            v.panL = (float)Math.Cos((pan + 1f) * Math.PI / 4.0); v.panR = (float)Math.Sin((pan + 1f) * Math.PI / 4.0);
            v.delay = (int)(Math.Abs(pan) * 0.0007 * fs); if (v.delay > 60) v.delay = 60;
            v.delayLeft = pan > 0f;
            for (int i = 0; i < 64; i++) v.dl[i] = 0f;
            v.dlPos = 0;
            float lenS, baseF, tauLo, tauHi, lpHz;
            if (kind == CreakGroan)
            {
                lenS = R(3f, 6f); baseF = R(75f, 170f); tauLo = 0.10f; tauHi = 0.35f; lpHz = R(450f, 650f);
                v.sag = R(0.10f, 0.28f); v.clMean = 4 + (int)(Rnd() * 6f); v.gapMax = R(0.9f, 1.6f);
                v.att = (int)(Math.Min(R(0.8f, 1.8f), lenS * 0.35f) * fs); v.rel = (int)(Math.Min(R(1.0f, 2.0f), lenS * 0.4f) * fs);
            }
            else
            {
                lenS = R(1f, 3.5f); baseF = R(130f, 260f); tauLo = 0.04f; tauHi = 0.15f; lpHz = R(550f, 800f);
                v.sag = R(0f, 0.07f); v.clMean = 3 + (int)(Rnd() * 6f); v.gapMax = R(0.35f, 0.8f);
                v.att = (int)(Math.Min(R(0.25f, 0.8f), lenS * 0.35f) * fs); v.rel = (int)(Math.Min(R(0.4f, 1.1f), lenS * 0.45f) * fs);
            }
            v.len = (int)(lenS * fs);
            int nf = 0;
            for (int j = 0; j < NM; j++)
            {
                float f = baseF * PlateRatio[j] * R(0.97f, 1.03f);
                if (f > 900f) break;
                float tau = R(tauLo, tauHi) * (1f - 0.06f * j);
                float q = (float)Math.PI * f * tau; if (q < 6f) q = 6f; if (q > 1500f) q = 1500f;
                float gg = R(0.5f, 1f);
                v.f0[j] = f; v.fb[j] = f; v.q[j] = q; v.g[j] = gg * fsf * tau * 0.5f;
                v.s[j].Clear(); v.s[j].Set(f, q, fsf);
                nf++;
            }
            v.nf = nf;
            v.lpA = 1f - (float)Math.Exp(-TwoPi * lpHz / fs); v.lp1 = 0f; v.lp2 = 0f; v.lp3 = 0f;
            v.nextSlip = (int)(R(0.02f, 0.2f) * fs); v.slipPos = slipLen; v.clusterLeft = 2 + v.clMean / 2; v.slipAmp = 0f; v.hiss = 0f;
            v.active = true;
        }

        // one friction slip: log it and schedule the next one (log-uniform gaps; short inside a cluster, long between)
        void Slip(HullVoice v, float env)
        {
            float a = 0.12f + 0.88f * Rnd() * Rnd();
            v.slipAmp = (Rnd() < 0.5f ? -a : a) * env; v.slipPos = 0; v.hiss = a * env; for (int j = 0; j < v.nf; j++) v.f0[j] = v.fb[j] * R(0.985f, 1.015f);   // contact stiffness changes with every slip: modes wander a little
            int c = slCount; if (c < SlipCap) { slT[c] = tSamples / (double)fs; slE[c] = v.serial; slCount = c + 1; }
            float gap;
            if (v.clusterLeft > 0) { v.clusterLeft--; gap = LogUni(0.005f, 0.06f); }
            else { gap = LogUni(0.12f, v.gapMax); v.clusterLeft = 1 + (int)(Rnd() * Rnd() * 2f * v.clMean); }
            int gs = (int)(gap * fs); if (gs < slipLen + 2) gs = slipLen + 2;
            v.nextSlip = v.n + gs;
        }

        void StartThump()
        {
            float amp = R(0.6f, 1f);
            ThumpVoice a = null;
            for (int i = 0; i < NThump; i++) if (!thump[i].active) { a = thump[i]; break; }
            if (a == null) return;
            SetupThump(a, amp, 0, 1f);
            if (Rnd() < 0.55f)
            {
                ThumpVoice b = null;
                for (int i = 0; i < NThump; i++) if (!thump[i].active) { b = thump[i]; break; }
                if (b != null) SetupThump(b, amp * R(0.3f, 0.55f), (int)(R(0.25f, 0.7f) * fs), R(0.9f, 1.1f));
            }
        }

        // Heavy object on a metal hull: a short low impact pulse (3.5-9 ms, sometimes with a small rebound) into four
        // inharmonic body modes (about 50-250 Hz, decays 0.1-0.4 s) and three faint ring modes (150-900 Hz, 1.2-2.4 s).
        // Every hit draws new frequencies, decays, gains and pulse length.
        void SetupThump(ThumpVoice v, float amp, int delaySamples, float fMul)
        {
            v.amp = amp;
            float f0 = R(52f, 85f) * fMul;
            float[] fr = thFr;   // preallocated scratch: Render must not allocate
            fr[0] = f0; fr[1] = f0 * R(1.5f, 1.7f); fr[2] = f0 * R(2.05f, 2.25f); fr[3] = f0 * R(2.8f, 3.1f);
            fr[4] = R(150f, 420f); fr[5] = R(300f, 650f); fr[6] = R(450f, 900f);
            int maxLen = 0;
            for (int j = 0; j < NT; j++)
            {
                bool ring = j >= 4;
                float tau = ring ? R(1.2f, 2.4f) : R(0.12f, 0.4f) * (1f - 0.15f * j);
                float gg = ring ? R(0.08f, 0.20f) : (j == 0 ? R(0.6f, 0.9f) : R(0.6f, 1.2f));
                float q = (float)Math.PI * fr[j] * tau; if (q < 6f) q = 6f; if (q > 2500f) q = 2500f;
                v.s[j].Clear(); v.s[j].Set(fr[j], q, fsf); v.g[j] = gg * fsf * tau * 0.5f;
                v.endN[j] = (int)((ring ? 6f : 7f) * tau * fs); if (v.endN[j] > 14 * fs) v.endN[j] = 14 * fs; if (v.endN[j] > maxLen) maxLen = v.endN[j];
            }
            v.pl = (int)(R(0.0035f, 0.009f) * fs); if (v.pl < 8) v.pl = 8;
            v.pl2 = v.pl; v.p2 = (int)(R(0.03f, 0.09f) * fs); v.a2 = Rnd() < 0.6f ? R(0.2f, 0.5f) : 0f;
            v.fadeN = (int)(0.03 * fs);
            v.len = maxLen;
            v.n = -delaySamples;
            float p = R(-0.25f, 0.25f);
            v.panL = (float)Math.Cos((p + 1f) * Math.PI / 4.0); v.panR = (float)Math.Sin((p + 1f) * Math.PI / 4.0);
            v.active = true;
        }

        void StartCreature()
        {
            if (crActive) return;
            crPhrasesLeft = 2 + (int)(Rnd() * 4f);
            crAmp = R(0.6f, 1f); crPan = R(-0.35f, 0.35f); crBase = R(60f, 130f);
            crLpA = 1f - (float)Math.Exp(-TwoPi * R(650f, 1000f) / fs);
            crLp1 = 0f; crLp2 = 0f; crLp3 = 0f; crJn = 0f; crAn = 0f;
            for (int j = 0; j < 3; j++) crS[j].Clear();
            crPhase = 0; crSeg = (int)(R(0.05f, 0.3f) * fs);
            crActive = true;
        }

        void StartRiser()
        {
            float secs = RiserSeconds; if (!(secs >= 4f)) secs = 4f; if (secs > 60f) secs = 60f;
            riLen = (int)(secs * fs); riN = 0; riEnv = 0f; riEnvInc = 0f; riF0 = R(170f, 230f); riF1 = riF0 * R(3.2f, 4.2f); riAmp = R(0.8f, 1f);
            riPh1 = 0u; riPh2 = (uint)(Rnd() * 4294967295.0);            riInc1 = (uint)(riF0 * phaseK); riInc2 = (uint)(riF0 * 1.007 * phaseK);
            riserActive = true; riserUsed = true;
        }

        // ================= audio-rate =================
        void Tick(out float outL, out float outR)
        {
            tSamples++;
            for (int k = 0; k < NL; k++) lvCur[k] += lvInc[k];

            float l = 0f, r = 0f;

            // ---- drone ----
            float cd = lvCur[0];
            if (cd > 0f || lvNxt[0] > 0f)
            {
                float dl = 0f, dr = 0f;
                for (int p = 0; p < NP; p++)
                {
                    dPh[p] += dInc[p];
                    float g = dG[p]; dG[p] = g + dGi[p];
                    float s = Sin(dPh[p]) * g;
                    dl += s * dPl[p]; dr += s * dPr[p];
                }
                l += dl * cd; r += dr * cd;     // v2: tones only, no noise bed
            }
            else
            {
                for (int p = 0; p < NP; p++) { dPh[p] += dInc[p]; dG[p] += dGi[p]; }
            }

            // ---- hull voices ----
            float hl = 0f, hr = 0f;
            for (int i = 0; i < NHull; i++)
            {
                HullVoice v = hull[i];
                if (v.active) HullSample(v, ref hl, ref hr);
            }
            float cc = lvCur[1] * CreakGain; l += hl * cc; r += hr * cc;

            // ---- thumps ----
            float tl = 0f, tr = 0f;
            for (int i = 0; i < NThump; i++)
            {
                ThumpVoice v = thump[i];
                if (v.active) ThumpSample(v, ref tl, ref tr);
            }
            float ct = lvCur[2] * ThumpGain; l += tl * ct; r += tr * ct;

            // ---- creature ----
            if (crActive)
            {
                float y = CreatureSample();
                float cg = lvCur[3] * CreatureGain * y;
                float pl = (float)(1.0 - crPan) * 0.7071f, pr = (float)(1.0 + crPan) * 0.7071f;
                l += cg * pl; r += cg * pr;
            }

            // ---- static ----
            float cs = lvCur[4];
            if (cs > 0f || lvNxt[4] > 0f)
            {
                if (--gateLeft <= 0) { gateLeft = (int)(R(0.03f, 0.3f) * fs); gateTgt = R(0.1f, 1f); }
                gate += (gateTgt - gate) * gateCoef;
                float nl = Noise(), nr = Noise();
                hl1 += aHi * (nl - hl1); hl2 += aLo * (hl1 - hl2);
                hr1 += aHi * (nr - hr1); hr2 += aLo * (hr1 - hr2);
                float sl = (hl1 - hl2) * 0.9f * gate, sr = (hr1 - hr2) * 0.9f * gate;
                float pc = (4f + 900f * cs * cs) / fsf;
                float il = 0f, ir = 0f;
                if (Rnd() < pc)
                {
                    float a = Rnd(); a = a * a * (Rnd() < 0.5f ? -1f : 1f);
                    float w = Rnd();
                    if (w < 0.4f) il = a; else if (w < 0.8f) ir = a; else { il = a; ir = a * 0.7f; }
                }
                sl += crkL.Bp(il) * 1.2f; sr += crkR.Bp(ir) * 1.2f;
                l += sl * cs * StaticGain; r += sr * cs * StaticGain;
            }

            // ---- riser ----
            if (riserActive)
            {
                riEnv += riEnvInc; float env = riEnv;
                riPh1 += riInc1; riPh2 += riInc2;
                float a = Sin(riPh1), b = Sin(riPh2), h = Sin(riPh1 * 2u);
                float y = (a + 0.25f * h) * 0.5f;
                float yl = (y + 0.4f * b * 0.5f) * env * riAmp * lvCur[5] * RiserGain * 2f;
                float yr = (0.4f * y + b * 0.5f) * env * riAmp * lvCur[5] * RiserGain * 2f;
                l += yl; r += yr;
                if (++riN >= riLen) riserActive = false;
            }

            // ---- silence event ----
            if (silState != 0)
            {
                switch (silState)
                {
                    case 1:
                        silGain = Smooth(1f - (float)silN / silOutN);
                        if (++silN >= silOutN)
                        {
                            silState = 2; silN = 0; silGain = 0f;
                            for (int i = 0; i < NHull; i++) hull[i].active = false;
                            for (int i = 0; i < NThump; i++) thump[i].active = false;
                            crActive = false; riserActive = false;
                        }
                        break;
                    case 2:
                        silGain = 0f;
                        if (++silN >= silHoldN) { silState = 3; silN = 0; }
                        break;
                    default:
                        silGain = Smooth((float)silN / silInN);
                        if (++silN >= silInN) { silState = 0; silGain = 1f; }
                        break;
                }
            }

            // ---- master, DC/infrasound high-pass, silence, limiter ----
            float m = lvCur[6] * OutGain;
            l = hpL2.Proc(hpL1.Proc(l * m)) * silGain;
            r = hpR2.Proc(hpR1.Proc(r * m)) * silGain;
            if (l > 0.6f || l < -0.6f) limCount++;
            if (r > 0.6f || r < -0.6f) limCount++;
            outL = Lim(l); outR = Lim(r);
        }

        static float Lim(float x)
        {
            if (float.IsNaN(x)) return 0f;
            float a = x < 0f ? -x : x;
            if (a <= 0.6f) return x;
            float o = 0.6f + 0.38f * (float)Math.Tanh((a - 0.6f) / 0.38f);
            return x < 0f ? -o : o;
        }

        void HullSample(HullVoice v, ref float outL, ref float outR)
        {
            int n = v.n;
            float env;
            if (v.att > 0 && n < v.att) env = Smooth((float)n / v.att); else env = 1f;
            int left = v.len - n;
            if (left < v.rel) env *= Smooth((float)left / v.rel);

            if (n >= v.nextSlip) Slip(v, env);
            float exc = 0f;
            if (v.slipPos < slipLen)
            {
                exc = (0.5f - 0.5f * (float)Math.Cos(TwoPi * (v.slipPos + 0.5f) / slipLen)) * (2f / slipLen) * v.slipAmp;
                v.slipPos++;
            }
            float x = 0f;
            for (int j = 0; j < v.nf; j++) x += v.g[j] * v.s[j].Bp(exc);
            if (v.hiss > 1e-4f) { x += Noise() * v.hiss * 0.8f; v.hiss *= hissDecay; }   // short scrape of friction noise; the low-pass keeps only its low part
            // heavy low-pass (three one-poles): the hull swallows everything above a few hundred Hz
            v.lp1 += v.lpA * (x - v.lp1); v.lp2 += v.lpA * (v.lp1 - v.lp2); v.lp3 += v.lpA * (v.lp2 - v.lp3);
            x = v.lp3 * v.amp;

            // placement: pan + inter-aural delay
            v.dl[v.dlPos & 63] = x;
            float xd = v.dl[(v.dlPos - v.delay) & 63];
            v.dlPos++;
            if (v.delayLeft) { outL += xd * v.panL; outR += x * v.panR; }
            else { outL += x * v.panL; outR += xd * v.panR; }

            v.n = n + 1;
            if (v.n >= v.len) v.active = false;
        }

        void ThumpSample(ThumpVoice v, ref float outL, ref float outR)
        {
            if (v.n < 0) { v.n++; return; }
            int n = v.n;
            float exc = 0f;
            if (n < v.pl) exc = (0.5f - 0.5f * (float)Math.Cos(TwoPi * (n + 0.5f) / v.pl)) * (2f / v.pl);
            if (v.a2 > 0f) { int d = n - v.p2; if (d >= 0 && d < v.pl2) exc += v.a2 * (0.5f - 0.5f * (float)Math.Cos(TwoPi * (d + 0.5f) / v.pl2)) * (2f / v.pl2); }
            float x = 0f;
            for (int j = 0; j < NT; j++) if (n < v.endN[j]) x += v.g[j] * v.s[j].Bp(exc);   // a mode stops once it has decayed ~-60 dB
            int left = v.len - n;
            float aa = left < v.fadeN ? Smooth((float)left / v.fadeN) : 1f;
            float y = x * aa * v.amp;
            outL += y * v.panL; outR += y * v.panR;
            v.n = n + 1;
            if (v.n >= v.len) v.active = false;
        }

        // Creature: nonlinear animal-like voice. Short phrases of varied length; per phrase an unstable pitch with
        // abrupt jumps and noise FM, period doubling (subharmonics) that comes and goes, noise-modulated roughness
        // (30-70 Hz), a breath component, three moving resonant formants, then a hull-like low-pass.
        void StartPhrase()
        {
            float u = Rnd(); float len = 0.35f + 1.9f * u * u;
            crSegLen = (int)(len * fs); crSeg = crSegLen; crN = 0;
            crAtt = (int)(Math.Min(R(0.04f, 0.22f), len * 0.4f) * fs); crRel = (int)(Math.Min(R(0.10f, 0.45f), len * 0.5f) * fs);
            crPf0 = crBase * R(0.75f, 1.35f); crF = crPf0; crTarget = crPf0; crHold = 0;
            crPsd = Rnd() < 0.4f ? 0f : R(0.3f, 0.8f); crPsdInc = (uint)(R(0.6f, 2.2f) * phaseK); crPsdU = (uint)(Rnd() * 4294967295.0);
            crRoughRate = R(30f, 70f); crRoughDepth = R(0.45f, 0.8f);
            crBreath = R(0.5f, 1.6f); crPulseG = R(0.5f, 1f); crFlag = false; crRateMul = 1f; crPph = 0f;
            crFa[0] = R(180f, 480f); crFb[0] = R(180f, 520f); crQ[0] = R(4f, 9f); crG[0] = 1f;
            crFa[1] = R(380f, 1000f); crFb[1] = R(380f, 1000f); crQ[1] = R(4f, 9f); crG[1] = R(0.5f, 0.9f);
            crFa[2] = R(800f, 1500f); crFb[2] = R(800f, 1500f); crQ[2] = R(4f, 9f); crG[2] = R(0.2f, 0.5f);
            CreatureFilters(0f);
            crPhase = 1;
        }

        void CreatureFilters(float u)
        {
            for (int j = 0; j < 3; j++)
            {
                float f = crFa[j] + (crFb[j] - crFa[j]) * u;
                crS[j].Set(f, crQ[j], fsf);
                crPj[j] = crQ[j] * fsf / (TwoPi * f);
                crNj[j] = (float)Math.Sqrt(crQ[j] * fsf / (Math.PI * f)) * 1.73f;
            }
        }

        float CreatureSample()
        {
            float drive = 0f, ex = 0f, nse = 0f;
            if (crPhase == 1)
            {
                if (--crHold <= 0)
                {
                    crHold = (int)(R(0.06f, 0.4f) * fs);
                    crTarget = crPf0 * CrRatio[(int)(Rnd() * 8f) & 7];
                }
                crF += (crTarget - crF) * crSlew;
                crJn += crJa * (Noise() - crJn); crAn += crAa * (Noise() - crAn);
                float fe = crF * (1f + 1.1f * crJn); if (fe < 25f) fe = 25f; crDbgPitch = fe;
                crPsdU += crPsdInc; float b = 0.5f + 0.5f * Sin(crPsdU); float psd = crPsd * b * b; crDbgPsd = psd;
                crPph += fe * crRateMul / fsf;
                if (crPph >= 1f)
                {
                    crPph -= 1f; crFlag = !crFlag;
                    float a = 1f + 0.3f * Noise(); if (!crFlag) a *= 1f - psd;
                    crRateMul = crFlag ? 1f : 1f / (1f + 0.25f * psd);
                    ex = a;
                }
                crRph += (uint)(crRoughRate * (1f + 6f * crAn) * phaseK);
                float am = (1f - crRoughDepth * (0.5f + 0.5f * Sin(crRph))) * (1f + 3f * crAn); if (am < 0f) am = 0f;
                float env = crN < crAtt ? Smooth((float)crN / crAtt) : 1f;
                int left = crSegLen - crN; if (left < crRel) env *= Smooth((float)left / crRel);
                drive = env * am; nse = Noise();
                crN++;
                if (--crSeg <= 0)
                {
                    if (--crPhrasesLeft <= 0) { crPhase = 2; crSeg = (int)(0.3 * fs); }
                    else { crPhase = 0; crSeg = (int)(R(0.15f, 0.9f) * fs); }
                }
            }
            else
            {
                crDbgPsd = 0f; crDbgPitch = 0f;
                if (--crSeg <= 0) { if (crPhase == 2) { crActive = false; return 0f; } StartPhrase(); }
            }
            float y = 0f;
            for (int j = 0; j < 3; j++) y += crG[j] * crS[j].Bp(drive * (ex * crPulseG * crPj[j] + nse * crBreath * CreatureBreathScale * crNj[j] * 0.5f));
            crLp1 += crLpA * (y - crLp1); crLp2 += crLpA * (crLp1 - crLp2); crLp3 += crLpA * (crLp2 - crLp3);
            return crLp3 * crAmp * CrScale;
        }
    }
}
