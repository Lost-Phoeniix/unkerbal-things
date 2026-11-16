// Dread: footsteps of a walking monster, heard only while landed on an airless body (the ground carries them, nothing else can).
// Far away only the bass of each step gets through a low-pass; closer the filter opens and the pitch lifts, so distance can be
// heard. Faster movement means shorter gaps. Every change is a smooth fade. Pure C# (no UnityEngine): the offline mix tool renders
// its preview from this same code.
using System;
using System.IO;
using System.Threading;

namespace UnkerbalThings.Dread
{
    public struct DreadStepInput
    {
        public float Dt;
        /// <summary>Metres to the nearest walking monster, below 0 when there is none.</summary>
        public float Distance;
        /// <summary>Monster speed over the ground, m/s.</summary>
        public float Speed;
        /// <summary>False while it hovers or leaps: no feet on the ground.</summary>
        public bool Walking;
        /// <summary>Stopped on purpose to listen (the hunt's Search): no step, and the last one dies away quickly.</summary>
        public bool Listening;
        public bool Chasing;
        public bool Landed;
        public bool Vacuum;
        /// <summary>True when the monster reports its real footfalls (Footfalls) and the timer must not invent any. Silence still means no footfalls.</summary>
        public bool UseFootfalls;
        /// <summary>Footfalls since the last update (only read when UseFootfalls).</summary>
        public int Footfalls;
    }

    /// <summary>Game-thread side: turns distance, speed and vessel state into level, filter, pitch and step timing.</summary>
    public sealed class DreadStepLogic
    {
        public const float FarGainDb = -30f;
        public const float FarCutoffHz = 90f;
        public const float NearCutoffHz = 3200f;
        public const float FarPitch = 0.8f;
        public const float NearPitch = 1.15f;
        public const float FadeInSeconds = 2.5f;
        public const float FadeOutSeconds = 1.5f;
        public const float MinWalkSpeed = 0.5f;
        /// <summary>Shortest timed gap. Was 0.5 s; lowered so that a timer-driven monster at sprint speed still speeds up (monsters with feet use the real footfalls).</summary>
        public const float MinInterval = 0.35f;

        public float FarMetres = 2500f;
        public float NearMetres = 30f;

        public float Gain { get; private set; }
        public float CutoffHz { get; private set; }
        public float Pitch { get; private set; }
        /// <summary>True for the one update in which a step lands.</summary>
        public bool StepFired { get; private set; }
        /// <summary>Steps to play in this update: 1 for a timed step, the number of reported footfalls (at most 4) in footfall mode.</summary>
        public int StepCount { get; private set; }

        float gate, logDist = -1f, speed, timer = 0.5f, lastGap;
        uint rng;

        public DreadStepLogic(int seed)
        {
            rng = (uint)seed * 2654435761u + 88172645u;
            if (rng == 0) rng = 88172645u;
            CutoffHz = FarCutoffHz;
            Pitch = FarPitch;
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFFu) / 16777216f;
        }

        static float C01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        /// <summary>0 at the far limit, 1 at the near limit; even per doubling of distance.</summary>
        public static float Closeness(float metres, float near, float far)
        {
            float d = Math.Max(metres, 1f);
            return 1f - C01((float)((Math.Log(d) - Math.Log(near)) / (Math.Log(far) - Math.Log(near))));
        }

        /// <summary>Base gap between steps: about 3.5 s at a slow walk, 0.7 s at a sprint; a chase is a further 20% quicker.</summary>
        public static float StepInterval(float metresPerSecond, bool chasing)
        {
            float g = 3.5f * (float)Math.Pow(Math.Max(metresPerSecond, MinWalkSpeed) / 2f, -0.55);
            if (chasing) g *= 0.8f;
            return g < MinInterval ? MinInterval : (g > 6f ? 6f : g);
        }

        public void Update(in DreadStepInput i)
        {
            StepFired = false;
            StepCount = 0;
            float dt = i.Dt > 0f ? (i.Dt < 1f ? i.Dt : 1f) : 0f;
            bool present = i.Distance >= 0f && i.Distance < FarMetres;
            bool audible = present && i.Landed && i.Vacuum;

            gate += audible ? dt / FadeInSeconds : -dt / FadeOutSeconds;
            gate = C01(gate);

            if (i.Distance >= 0f)
            {
                float ld = (float)Math.Log(Math.Max(i.Distance, 1f));
                // smoothed, so that a different monster becoming the nearest one is a glide rather than a jump
                logDist = logDist < 0f ? ld : logDist + (ld - logDist) * (1f - (float)Math.Exp(-dt / 0.5f));
            }
            float metres = logDist < 0f ? FarMetres : (float)Math.Exp(logDist);
            float c = Closeness(metres, NearMetres, FarMetres);
            // the outer 30% of the range fades in as well: it never starts at a hard distance
            float e = C01((FarMetres - metres) / (0.3f * FarMetres));
            float edge = e * e * (3f - 2f * e);
            float smoothGate = gate * gate * (3f - 2f * gate);

            Gain = smoothGate * edge * (float)Math.Pow(10.0, FarGainDb * (1f - c) / 20.0);
            CutoffHz = FarCutoffHz * (float)Math.Pow(NearCutoffHz / FarCutoffHz, c);
            Pitch = FarPitch + (NearPitch - FarPitch) * c;

            float s = (i.Walking && !i.Listening) ? Math.Max(i.Speed, 0f) : 0f;
            // it speeds up gradually but stops quickly: sudden silence is the scary part (listening stops at once)
            speed += (s - speed) * (1f - (float)Math.Exp(-dt / (i.Listening ? 0.05f : (s < speed ? 0.35f : 1f))));
            if (speed < MinWalkSpeed || !i.Walking || i.Listening || gate <= 0f) return;   // standing still is silence: the timer waits too
            if (i.UseFootfalls)
            {
                // the feet decide: one step per footfall, nothing in between
                if (i.Footfalls > 0)
                {
                    StepFired = true;
                    StepCount = i.Footfalls > 4 ? 4 : i.Footfalls;
                }
                return;
            }
            timer -= dt;
            if (timer > 0f) return;
            StepFired = true;
            StepCount = 1;
            float gap = StepInterval(speed, i.Chasing);
            float next = gap;
            for (int t = 0; t < 3; t++)
            {
                next = gap * (0.85f + 0.3f * Rand());
                if (lastGap <= 0f || Math.Abs(next - lastGap) > 0.03f * next) break;   // never two identical gaps
            }
            lastGap = next;
            timer = next;
        }
    }

    /// <summary>Audio-thread side: plays the step clip through a moving low-pass. Mixes into the buffer; allocates nothing.</summary>
    public sealed class DreadStepVoice
    {
        const int Voices = 4;
        const int Block = 64;

        public volatile float MasterLevel = 1f;
        public volatile float Gain;
        public volatile float CutoffHz = 90f;
        public volatile float Pitch = 1f;
        int reqStep;

        /// <summary>Test hook: when set, the audio thread copies its output here. Used only by the in-game test.</summary>
        public volatile float[] Record;
        public volatile int RecordPosition;
        float peak;

        /// <summary>Largest output sample since the last call.</summary>
        public float TakePeak() { float p = peak; peak = 0f; return p; }

        readonly float[][] clips;
        readonly double rateRatio;
        readonly float[][] playing = new float[Voices][];
        int lastClip = -1;
        uint rng = 88172645u;
        readonly int fs;
        readonly double[] pos = new double[Voices];
        readonly double[] inc = new double[Voices];
        readonly bool[] on = new bool[Voices];
        int nextVoice, ctr;
        float gainNow, cutNow = 90f;
        readonly float gainCoef, gainFallCoef, cutCoef;
        double b0, b1, b2, a1, a2;
        double x11, x12, y11, y12, x21, x22, y21, y22;

        public DreadStepVoice(int sampleRate, float[] monoClip, int clipRate) : this(sampleRate, monoClip != null && monoClip.Length > 16 ? new[] { monoClip } : null, clipRate) { }

        /// <summary>Several recordings of a step: never the same one twice running, and each a little off in pitch.</summary>
        public DreadStepVoice(int sampleRate, float[][] monoClips, int clipRate)
        {
            fs = sampleRate;
            clips = monoClips != null && monoClips.Length > 0 ? monoClips : new[] { BuildFallback(sampleRate) };
            rateRatio = (monoClips != null && monoClips.Length > 0 ? clipRate : sampleRate) / (double)sampleRate;
            gainCoef = 1f - (float)Math.Exp(-1.0 / (0.02 * fs));
            gainFallCoef = 1f - (float)Math.Exp(-1.0 / (0.12 * fs));   // a footfall that is switched off rings out briefly
            cutCoef = 1f - (float)Math.Exp(-Block / (0.15 * fs));
            SetCoefs(cutNow);
        }

        public void TriggerStep() { Interlocked.Increment(ref reqStep); }

        void SetCoefs(double fc)
        {
            double f = Math.Min(Math.Max(fc, 20.0), fs * 0.45);
            double w = 2 * Math.PI * f / fs, al = Math.Sin(w) / (2 * 0.7071), c = Math.Cos(w), a0 = 1 + al;
            b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - al) / a0;
        }

        /// <summary>Adds the steps to an interleaved buffer.</summary>
        public void MixInto(float[] buffer, int channels)
        {
            if (buffer == null || channels < 1) return;
            int req = Interlocked.Exchange(ref reqStep, 0);
            for (; req > 0; req--)
            {
                int v = nextVoice; nextVoice = (nextVoice + 1) % Voices;
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                int pick = (int)(rng % (uint)clips.Length);
                if (clips.Length > 1 && pick == lastClip) pick = (pick + 1) % clips.Length;
                lastClip = pick;
                double wobble = clips.Length > 1 ? 0.97 + 0.06 * ((rng >> 8) & 0xFFFF) / 65536.0 : 1.0;
                playing[v] = clips[pick];
                on[v] = true; pos[v] = 0.0; inc[v] = rateRatio * Pitch * wobble;
            }
            int frames = buffer.Length / channels;
            float targetGain = Gain * Clamp01(MasterLevel);
            float[] rec = Record;
            int recPos = RecordPosition;
            for (int i = 0; i < frames; i++)
            {
                if (ctr == 0)
                {
                    float c = CutoffHz;
                    cutNow = (float)Math.Exp(Math.Log(cutNow) + (Math.Log(Math.Max(c, 20f)) - Math.Log(cutNow)) * cutCoef);
                    SetCoefs(cutNow);
                }
                ctr = (ctr + 1) % Block;
                double x = 0.0;
                for (int v = 0; v < Voices; v++)
                {
                    if (!on[v]) continue;
                    float[] clip = playing[v];
                    int p = (int)pos[v];
                    if (p + 1 >= clip.Length) { on[v] = false; continue; }
                    double f = pos[v] - p;
                    x += clip[p] + (clip[p + 1] - clip[p]) * f;
                    pos[v] += inc[v];
                }
                double y1 = b0 * x + b1 * x11 + b2 * x12 - a1 * y11 - a2 * y12;
                x12 = x11; x11 = x; y12 = y11; y11 = y1;
                double y2 = b0 * y1 + b1 * x21 + b2 * x22 - a1 * y21 - a2 * y22;
                x22 = x21; x21 = y1; y22 = y21; y21 = y2;
                if (Math.Abs(y21) < 1e-20) y21 = 0.0;
                if (Math.Abs(y11) < 1e-20) y11 = 0.0;
                gainNow += (targetGain - gainNow) * (targetGain < gainNow ? gainFallCoef : gainCoef);
                float o = (float)(y2 * gainNow);
                int idx = i * channels;
                buffer[idx] += o;
                if (channels > 1) buffer[idx + 1] += o;
                float ao = o < 0f ? -o : o;
                if (ao > peak) peak = ao;
                if (rec != null && recPos < rec.Length) rec[recPos++] = o;
            }
            if (rec != null) RecordPosition = recPos;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        // Only used when the clip file is missing: a plain low body, so the distance cue still works.
        static float[] BuildFallback(int rate)
        {
            float[] x = new float[(int)(1.6 * rate)];
            double ph = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)rate, f = 40.0 + 30.0 * Math.Exp(-t / 0.09);
                ph += 2 * Math.PI * f / rate;
                x[i] = (float)(0.7 * Math.Tanh(2.2 * Math.Sin(ph)) * Math.Exp(-t / 0.35) * Math.Min(1.0, t / 0.004));
            }
            return x;
        }

        /// <summary>Reads a 16-bit PCM WAV to mono. Returns null when the file is missing or not in that format.</summary>
        public static float[] LoadWav(string path, out int rate)
        {
            rate = 48000;
            try
            {
                if (!File.Exists(path)) return null;
                byte[] b = File.ReadAllBytes(path);
                int p = 12, ch = 0, bits = 0, off = -1, len = 0;
                while (p + 8 <= b.Length)
                {
                    int n = BitConverter.ToInt32(b, p + 4);
                    string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                    if (id == "fmt ") { ch = BitConverter.ToInt16(b, p + 10); rate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToInt16(b, p + 22); }
                    else if (id == "data") { off = p + 8; len = Math.Min(n, b.Length - off); break; }
                    p += 8 + n + (n & 1);
                }
                if (off < 0 || bits != 16 || ch < 1 || rate < 8000) return null;
                int frames = len / (2 * ch);
                float[] o = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    float a = 0f;
                    for (int k = 0; k < ch; k++) a += BitConverter.ToInt16(b, off + (i * ch + k) * 2) / 32768f;
                    o[i] = a / ch;
                }
                return o;
            }
            catch (Exception) { return null; }
        }
    }
}
