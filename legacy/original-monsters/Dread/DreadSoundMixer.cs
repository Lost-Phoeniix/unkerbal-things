// Dread soundscape, audio-thread side: four looping beds and a pool of one-shots, added to the buffer the host hands over.
// Allocates nothing. The game thread only writes volatile fields and a single-producer request ring. Pure C# (no UnityEngine).
using System;

namespace KrakensKreations.Dread
{
    public sealed class DreadSoundMixer
    {
        public const int BedSlots = 4;
        public const int WaterSlot = 2;     // the slot the water low-pass acts on
        const int Voices = 16;
        const int RingSize = 32;

        struct Request
        {
            public DreadClip Clip;
            public float Gain, Pitch, Pan, CutoffHz;
        }

        sealed class Voice
        {
            public DreadClip Clip;
            public double Pos, Inc;
            public float GainL, GainR, Coef;
            public double L1, L2, R1, R2;
        }

        sealed class Bed
        {
            public DreadClip Clip;
            public double Pos;
            public float Now;
        }

        public volatile float MasterLevel;
        public volatile float Bed0, Bed1, Bed2, Bed3;
        public volatile float WaterCutoffHz = 20000f;
        volatile DreadClip clip0, clip1, clip2, clip3;

        /// <summary>Test hook: when set, the audio thread copies its stereo output here (interleaved).</summary>
        public volatile float[] Record;
        public volatile int RecordPosition;

        readonly int fs;
        readonly Voice[] voices = new Voice[Voices];
        readonly Bed[] beds = new Bed[BedSlots];
        readonly Request[] ring = new Request[RingSize];
        volatile int head;
        volatile int tail;
        readonly float gainCoef, cutCoef;
        float masterNow, cutNow = 20000f, waterA = 1f;
        double wl1, wl2, wr1, wr2;
        float peak;
        int played, dropped;
        uint rng = 2463534242u;

        public int Played { get { return played; } }
        public int Dropped { get { return dropped; } }

        public DreadSoundMixer(int sampleRate)
        {
            fs = sampleRate;
            for (int i = 0; i < Voices; i++) voices[i] = new Voice();
            for (int i = 0; i < BedSlots; i++) beds[i] = new Bed();
            gainCoef = 1f - (float)Math.Exp(-1.0 / (0.05 * fs));
            cutCoef = 1f - (float)Math.Exp(-1.0 / (0.3 * fs));
        }

        /// <summary>Largest output sample since the last call.</summary>
        public float TakePeak() { float p = peak; peak = 0f; return p; }

        /// <summary>Game thread: the loop in a slot. Null frees it; its gain has to be near zero by then.</summary>
        public void SetBed(int slot, DreadClip clip)
        {
            switch (slot)
            {
                case 0: clip0 = clip; break;
                case 1: clip1 = clip; break;
                case 2: clip2 = clip; break;
                case 3: clip3 = clip; break;
            }
        }

        public void SetBedGain(int slot, float gain)
        {
            switch (slot)
            {
                case 0: Bed0 = gain; break;
                case 1: Bed1 = gain; break;
                case 2: Bed2 = gain; break;
                case 3: Bed3 = gain; break;
            }
        }

        /// <summary>Game thread: starts a one-shot. pan -1..1, cutoffHz 0 = no filter. False when the request ring is full.</summary>
        public bool Play(DreadClip clip, float gain, float pitch, float pan, float cutoffHz)
        {
            if (clip == null) return false;
            int h = head, next = (h + 1) % RingSize;
            if (next == tail) { dropped++; return false; }
            ring[h].Clip = clip;
            ring[h].Gain = gain;
            ring[h].Pitch = pitch;
            ring[h].Pan = pan;
            ring[h].CutoffHz = cutoffHz;
            head = next;
            return true;
        }

        void Start(ref Request q)
        {
            Voice v = null;
            for (int i = 0; i < Voices; i++) if (voices[i].Clip == null) { v = voices[i]; break; }
            if (v == null) { dropped++; return; }
            double a = (Clamp(q.Pan, -1f, 1f) + 1.0) * Math.PI / 4.0;
            v.GainL = q.Gain * (float)(Math.Cos(a) * Math.Sqrt(2.0));
            v.GainR = q.Gain * (float)(Math.Sin(a) * Math.Sqrt(2.0));
            v.Pos = 0.0;
            v.Inc = q.Pitch * q.Clip.Rate / (double)fs;
            v.Coef = q.CutoffHz > 0f ? OnePole(q.CutoffHz) : 1f;
            v.L1 = v.L2 = v.R1 = v.R2 = 0.0;
            v.Clip = q.Clip;
            played++;
        }

        float OnePole(float hz) { return 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Min(hz, fs * 0.45) / fs); }

        /// <summary>Adds the soundscape to an interleaved buffer.</summary>
        public void MixInto(float[] buffer, int channels)
        {
            if (buffer == null || channels < 1) return;
            while (tail != head)
            {
                int t = tail;
                Start(ref ring[t]);
                ring[t].Clip = null;
                tail = (t + 1) % RingSize;
            }
            beds[0].Clip = Swap(beds[0], clip0);
            beds[1].Clip = Swap(beds[1], clip1);
            beds[2].Clip = Swap(beds[2], clip2);
            beds[3].Clip = Swap(beds[3], clip3);
            float t0 = Bed0, t1 = Bed1, t2 = Bed2, t3 = Bed3, masterT = MasterLevel, cutT = WaterCutoffHz;
            int frames = buffer.Length / channels;
            float[] rec = Record;
            int recPos = RecordPosition;
            for (int i = 0; i < frames; i++)
            {
                masterNow += (masterT - masterNow) * gainCoef;
                double l = 0.0, r = 0.0;
                // the water bed, through the depth filter
                Bed b = beds[WaterSlot];
                b.Now += (t2 - b.Now) * gainCoef;
                if ((i & 63) == 0)
                {
                    cutNow += (cutT - cutNow) * Math.Min(1f, cutCoef * 64f);
                    waterA = OnePole(cutNow);
                }
                if (b.Clip != null)
                {
                    double xl, xr;
                    ReadLoop(b, out xl, out xr);
                    wl1 += (xl - wl1) * waterA; wl2 += (wl1 - wl2) * waterA;
                    wr1 += (xr - wr1) * waterA; wr2 += (wr1 - wr2) * waterA;
                    l += wl2 * b.Now; r += wr2 * b.Now;
                }
                AddBed(beds[0], t0, ref l, ref r);
                AddBed(beds[1], t1, ref l, ref r);
                AddBed(beds[3], t3, ref l, ref r);
                for (int k = 0; k < Voices; k++)
                {
                    Voice v = voices[k];
                    DreadClip c = v.Clip;
                    if (c == null) continue;
                    int p = (int)v.Pos;
                    if (p + 1 >= c.L.Length) { v.Clip = null; continue; }
                    double f = v.Pos - p;
                    double xl = (c.L[p] + (c.L[p + 1] - c.L[p]) * f) / 32768.0;
                    double xr = (c.R[p] + (c.R[p + 1] - c.R[p]) * f) / 32768.0;
                    v.Pos += v.Inc;
                    if (v.Coef < 1f)
                    {
                        v.L1 += (xl - v.L1) * v.Coef; v.L2 += (v.L1 - v.L2) * v.Coef;
                        v.R1 += (xr - v.R1) * v.Coef; v.R2 += (v.R1 - v.R2) * v.Coef;
                        xl = v.L2; xr = v.R2;
                    }
                    l += xl * v.GainL; r += xr * v.GainR;
                }
                float ol = Soft((float)(l * masterNow)), or = Soft((float)(r * masterNow));
                int idx = i * channels;
                buffer[idx] += ol;
                buffer[channels > 1 ? idx + 1 : idx] += channels > 1 ? or : 0f;
                float al = ol < 0f ? -ol : ol, ar = or < 0f ? -or : or;
                if (al > peak) peak = al;
                if (ar > peak) peak = ar;
                if (rec != null && recPos + 1 < rec.Length) { rec[recPos++] = ol; rec[recPos++] = or; }
            }
            if (rec != null) RecordPosition = recPos;
            if (Math.Abs(wl1) < 1e-20) wl1 = 0.0;
            if (Math.Abs(wl2) < 1e-20) wl2 = 0.0;
            if (Math.Abs(wr1) < 1e-20) wr1 = 0.0;
            if (Math.Abs(wr2) < 1e-20) wr2 = 0.0;
        }

        // a new loop starts at a random point, so a bed never begins with the same second twice
        DreadClip Swap(Bed b, DreadClip wanted)
        {
            if (wanted != b.Clip && wanted != null)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                b.Pos = (rng & 0xFFFFFFu) / 16777216.0 * wanted.Frames;
            }
            return wanted;
        }

        void AddBed(Bed b, float target, ref double l, ref double r)
        {
            b.Now += (target - b.Now) * gainCoef;
            if (b.Clip == null) return;
            double xl, xr;
            ReadLoop(b, out xl, out xr);
            l += xl * b.Now; r += xr * b.Now;
        }

        void ReadLoop(Bed b, out double xl, out double xr)
        {
            DreadClip c = b.Clip;
            int n = c.L.Length, p = (int)b.Pos, q = p + 1 >= n ? 0 : p + 1;
            double f = b.Pos - p;
            xl = (c.L[p] + (c.L[q] - c.L[p]) * f) / 32768.0;
            xr = (c.R[p] + (c.R[q] - c.R[p]) * f) / 32768.0;
            b.Pos += c.Rate / (double)fs;
            if (b.Pos >= n) b.Pos -= n;
        }

        static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }

        // nothing below 0.7 is touched; above, a soft shoulder instead of clipping when several impacts meet
        static float Soft(float x)
        {
            float a = x < 0f ? -x : x;
            if (a <= 0.7f) return x;
            float y = 0.7f + 0.3f * (float)Math.Tanh((a - 0.7f) / 0.3f);
            return x < 0f ? -y : y;
        }
    }
}
