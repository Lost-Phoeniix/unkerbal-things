// Dread: rules of the worm swarm science loop. Pure C# (no UnityEngine); tested
// offline. The game side is WormSciencePart.cs (part modules) and EntityTracker.WormHunt.cs.
//
// Every worm experiment takes time (the idea of timed experiments follows the Time For Science mod, MIT, by
// Rjoande; no code from it): a run starts only while its condition holds, needs that condition for its whole duration, and a
// lapse aborts it (it must be started again). When the time is up the stock experiment completes (data, dialog, transmit).
// The heavy proximity scanner: within ScanRange (50 m straight-line) of the heap for 20 s after starting. Straight above the heap
// that leaves a band to hover in: from 15 m (lower, the engines give you away) to 50 m.
using System;

namespace UnkerbalThings.Dread
{
    /// <summary>A timed experiment run: needs its condition for `Seconds` without a break.</summary>
    public sealed class WormRun
    {
        public readonly float Seconds;
        public bool Running { get; private set; }
        public bool Done { get; private set; }
        public bool Aborted { get; private set; }
        public float Elapsed { get; private set; }
        public float Progress { get { return Seconds > 0f ? Math.Min(1f, Elapsed / Seconds) : 1f; } }
        public float Remaining { get { return Math.Max(0f, Seconds - Elapsed); } }

        public WormRun(float seconds) { Seconds = Math.Max(0f, seconds); }

        /// <summary>Start while `ok` (the condition) holds. False: nothing starts.</summary>
        public bool Start(bool ok)
        {
            Aborted = false;
            Done = false;
            Elapsed = 0f;
            if (!ok) { Running = false; return false; }
            Running = Seconds > 0f;
            Done = Seconds <= 0f;
            return true;
        }

        /// <summary>One step while running; `ok`: the condition still holds. A lapse aborts the run.</summary>
        public void Step(float dt, bool ok)
        {
            if (!Running || dt <= 0f) return;
            if (!ok) { Running = false; Aborted = true; Elapsed = 0f; return; }
            Elapsed += dt;
            if (Elapsed >= Seconds) { Elapsed = Seconds; Running = false; Done = true; }
        }

        public void Cancel() { Running = false; Elapsed = 0f; }

        /// <summary>The finished result was handed to the experiment: ready for another run.</summary>
        public void Consume() { Done = false; Aborted = false; Elapsed = 0f; }
    }

    /// <summary>The proximity scan: a timed run whose condition is "within Range of the heap".</summary>
    public sealed class WormScan
    {
        public readonly float Range;
        readonly WormRun run;
        public float Seconds { get { return run.Seconds; } }
        public bool Running { get { return run.Running; } }
        public bool Done { get { return run.Done; } }
        public bool Aborted { get { return run.Aborted; } }
        public float Elapsed { get { return run.Elapsed; } }
        public float Progress { get { return run.Progress; } }

        public WormScan(float range, float seconds)
        {
            Range = Math.Max(1f, range);
            run = new WormRun(seconds);
        }

        /// <summary>Start a scan at `distance` m from the heap (NaN or infinity: no heap). False when out of range.</summary>
        public bool Start(float distance) { return run.Start(distance <= Range); }
        public void Step(float dt, float distance) { run.Step(dt, distance <= Range); }
        public void Cancel() { run.Cancel(); }
        public void Consume() { run.Consume(); }
    }

    public static class WormScience
    {
        /// <summary>True when a point `height` m above the heap's ground and `horizontal` m to the side is within `range` m.</summary>
        public static bool InScanBand(float height, float horizontal, float range)
        {
            return Math.Sqrt((double)height * height + (double)horizontal * horizontal) <= range;
        }

        /// <summary>Bearing in degrees (0 = north, clockwise) from east/north offsets.</summary>
        public static double Bearing(double east, double north)
        {
            double b = Math.Atan2(east, north) * 180.0 / Math.PI;
            return b < 0 ? b + 360.0 : b;
        }

        static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        public static string Compass(double bearingDeg)
        {
            int i = (int)Math.Floor(((bearingDeg % 360.0) + 360.0 + 22.5) / 45.0) % 8;
            return Points[i];
        }

        /// <summary>4-point compass for a poor reading.</summary>
        public static string Compass4(double bearingDeg)
        {
            int i = (int)Math.Floor(((bearingDeg % 360.0) + 360.0 + 45.0) / 90.0) % 4;
            return Points[i * 2];
        }

        /// <summary>"1:05" / "42 s" for a countdown.</summary>
        public static string Clock(float seconds)
        {
            int s = (int)Math.Ceiling(Math.Max(0f, seconds));
            return s >= 60 ? (s / 60) + ":" + (s % 60).ToString("00") : s + " s";
        }
    }

    /// <summary>The swarm messes with electronics: the closer it is, the worse every readout gets. The science
    /// parts are big because most of their bulk is shielding; a part's `shielding` (0..1) keeps its readout usable for longer.
    /// Readings are approximate (compass sector, rounded range), drift, drop letters and scroll past as a ticker.</summary>
    public static class WormReadout
    {
        /// <summary>Interference 0..1 from the swarm's distance: 1 at or inside `near`, 0 at or beyond `far`, smooth between.</summary>
        public static float Interference(double distance, float near, float far)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance)) return 0f;
            if (far <= near) return distance <= near ? 1f : 0f;
            double t = (far - distance) / (far - near);
            if (t <= 0) return 0f;
            if (t >= 1) return 1f;
            return (float)(t * t * (3 - 2 * t));
        }

        /// <summary>How corrupt a part's readout is: interference less what its shielding holds back (never below 0).</summary>
        public static float Corruption(float interference, float shielding)
        {
            float s = Math.Max(0f, Math.Min(1f, shielding));
            return Math.Max(0f, Math.Min(1f, interference * (1f - 0.8f * s)));
        }

        /// <summary>Spread (degrees, one side) of the heading error at this corruption.</summary>
        public static double HeadingSpread(float corruption) { return 12.0 + 70.0 * corruption; }

        /// <summary>Heading word: 8 points when clean, 4 when poor, "?" when lost.</summary>
        public static string Heading(double bearingDeg, float corruption)
        {
            if (corruption >= 0.75f) return "?";
            return corruption >= 0.4f ? WormScience.Compass4(bearingDeg) : WormScience.Compass(bearingDeg);
        }

        /// <summary>Rough range: never the exact figure. `noise` (-1..1) shifts it by up to a quarter; poor readings get coarser.</summary>
        public static string Range(double metres, float corruption, double noise)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "--";
            double m = Math.Max(0.0, metres * (1.0 + 0.25 * Math.Max(-1.0, Math.Min(1.0, noise)) * (0.3 + corruption)));
            if (corruption >= 0.75f) return m < 300 ? "CLOSE" : "--";
            if (m >= 5000) return "far";
            if (m >= 1000) return "~" + (Math.Round(m / 500.0) * 0.5).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km";
            if (m >= 300) return "~" + (Math.Round(m / 100.0) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
            return corruption >= 0.4f ? "near" : "<300 m";
        }

        /// <summary>Rounded short range for the scanner: to 5 m when clean, 10 or 20 m when poor; `noise` (-1..1) up to 15% off.</summary>
        public static string ShortRange(double metres, float corruption, double noise)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "--";
            double m = Math.Max(0.0, metres * (1.0 + 0.15 * Math.Max(-1.0, Math.Min(1.0, noise)) * (0.3 + corruption)));
            double step = corruption >= 0.6f ? 20 : corruption >= 0.3f ? 10 : 5;
            return "~" + (Math.Round(m / step) * step).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
        }

        const string Junk = "#%&*/\\|=+?<>~";

        /// <summary>Swaps letters for junk with probability `amount` (spaces stay, so words keep their shape).</summary>
        public static string Garble(string s, float amount, Random rng)
        {
            if (string.IsNullOrEmpty(s) || amount <= 0f || rng == null) return s;
            char[] c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
                if (c[i] != ' ' && rng.NextDouble() < amount) c[i] = Junk[rng.Next(Junk.Length)];
            return new string(c);
        }

        /// <summary>A `width`-character window over `message` (looping, a gap of spaces between loops) at scroll position `step`.</summary>
        public static string Ticker(string message, int width, int step)
        {
            if (string.IsNullOrEmpty(message) || width <= 0) return "";
            string loop = message + "   ";
            if (loop.Length <= width) loop = loop.PadRight(width + 1);
            int n = loop.Length;
            int start = ((step % n) + n) % n;
            char[] w = new char[width];
            for (int i = 0; i < width; i++) w[i] = loop[(start + i) % n];
            return new string(w);
        }

        /// <summary>A heading error that wanders instead of jumping: eases toward a new random target every so often.</summary>
        public sealed class Drift
        {
            readonly Random rng;
            double value, target, untilNext;
            public Drift(int seed) { rng = new Random(seed); }
            public double Value { get { return value; } }
            /// <summary>Advance by `dt` s with the given spread (the error stays within ±spread). Returns the error.</summary>
            public double Step(double dt, double spread)
            {
                untilNext -= dt;
                if (untilNext <= 0) { target = (rng.NextDouble() * 2 - 1) * spread; untilNext = 1.5 + rng.NextDouble() * 3; }
                double k = Math.Min(1.0, dt * 0.8);
                value += (target - value) * k;
                if (value > spread) value = spread; else if (value < -spread) value = -spread;
                return value;
            }
        }
    }
}
