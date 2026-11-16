// Dread: how big the Moho kill blast is and how hard it throws the worms.
// Pure C# (no UnityEngine); tested offline.
//
// The sizing is ported from Volumetric Explosion FX (VEFX) 1.1.1, Source/Core/ResourceClassifier.cs, VisualEnergy.cs,
// VolumeEvolution.cs (FireballRadius, DomeRadius) and Ballistics.cs (LaunchSpeed), reduced to the vacuum case (atmosphere 0,
// no impact speed, no warheads):
//
//   MIT License
//   Copyright (c) 2026 Warnix
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
//   files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
//   modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice
//   shall be included in all copies or substantial portions of the Software.
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//   WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
//   COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
//   ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Our own addition: FlingSpeed maps VEFX's wreckage launch speed (4-90 m/s) onto the worms' fling, capped by the setting
// mohoKillFlingMax (Moho: 2.7 m/s^2, so 15 m/s throws a worm up to ~80 m).
using System;
using System.Collections.Generic;

namespace UnkerbalThings.Dread
{
    public sealed class KillBlastSize
    {
        public double BurnableKg, Score, Fire, Radius, FireballRadius, DomeRadius, LaunchSpeed;
        public override string ToString()
        {
            return "burnable " + BurnableKg.ToString("F0") + " kg, score " + Score.ToString("F1") + ", fireball " + FireballRadius.ToString("F1") +
                " m, dome " + DomeRadius.ToString("F1") + " m, wreckage " + LaunchSpeed.ToString("F0") + " m/s";
        }
    }

    public static class KillBlast
    {
        const int Fuel = 1, Oxidizer = 2, Solid = 4, Hypergolic = 8;

        static int Kind(string name)
        {
            switch (name)
            {
                case "LiquidFuel": case "Kerosene": case "RP-1": case "LqdHydrogen": case "LH2": case "LqdMethane": case "Methane": return Fuel;
                case "MMH": case "UDMH": return Fuel | Hypergolic;
                case "Oxidizer": case "LqdOxygen": case "LOX": return Oxidizer;
                case "NTO": return Oxidizer | Hypergolic;
                case "SolidFuel": return Solid;
                case "MonoPropellant": return Hypergolic;
                default: return 0;
            }
        }

        static double Clamp(double x, double a, double b) { return x < a ? a : (x > b ? b : x); }

        /// <summary>
        /// The blast of a whole craft going up at once (VEFX merges deaths within 0.08 s and 12 m into one fireball, its fuel
        /// summed). resources: (resource name, kg) of every part; massKg: the craft's mass; sizeMetres: its largest extent.
        /// </summary>
        public static KillBlastSize Size(IEnumerable<KeyValuePair<string, double>> resources, double massKg, double sizeMetres)
        {
            double fuel = 0, ox = 0, self = 0;
            if (resources != null)
                foreach (KeyValuePair<string, double> r in resources)
                {
                    int k = Kind(r.Key);
                    double kg = Math.Max(0, r.Value);
                    if ((k & Fuel) != 0) fuel += kg;
                    if ((k & Oxidizer) != 0) ox += kg;
                    if ((k & Solid) != 0 || ((k & Hypergolic) != 0 && (k & (Fuel | Oxidizer)) == 0)) self += kg;
                }
            var s = new KillBlastSize();
            s.BurnableKg = Math.Min(self + Math.Min(fuel, ox), 1e7);   // vacuum: no fuel burns in air
            double mass = Math.Min(Math.Max(massKg, 0), 1e7);
            s.Score = Clamp(Math.Log10(1 + s.BurnableKg * 40 + mass * 0.05), 0, 9);
            double extent = Clamp(sizeMetres * 0.25, 0.15, 12);
            s.Radius = Clamp(extent + Math.Pow(1 + s.BurnableKg, 1.0 / 3) * 0.5, 0.35, 30);
            s.Fire = s.BurnableKg / (s.BurnableKg + 10);
            double m = 0.3 * s.BurnableKg;
            s.FireballRadius = m < 1 ? 0 : Clamp(2.9 * Math.Pow(m, 1.0 / 3), 1, 60);
            s.DomeRadius = Clamp(Math.Max(s.Radius * 2.4, s.FireballRadius * 1.2), 1.2, 110);
            s.LaunchSpeed = Clamp(4 + s.Fire * (10 + 8 * s.Score), 4, 90);
            return s;
        }

        /// <summary>How fast the blast throws the worms nearest to it (m/s): an empty craft still a third of the cap.</summary>
        public static float FlingSpeed(KillBlastSize s, float max)
        {
            if (s == null || max <= 0f) return 0f;
            return (float)(max * Clamp((s.LaunchSpeed - 4) / 50, 0.3, 1));
        }

        /// <summary>How far out (m) the worms feel the blast: the dome, at least the mound's size, at most 30 m.</summary>
        public static float FlingReach(KillBlastSize s, float moundRadius)
        {
            double d = s != null ? s.DomeRadius : 0;
            return (float)Clamp(Math.Max(d, moundRadius * 1.6), 6, 30);
        }
    }
}
