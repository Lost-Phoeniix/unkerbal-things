// Dread: staged warnings of a monster that is coming. Pure C# (no UnityEngine); tested offline.
//
// Three stages by distance (default 2.5 km, 1.5 km, 500 m: the radius and 0.6 and 0.2 of it), each once per chase. A chase starts when
// the monster becomes aware of the craft (searching or hunting) and ends when it gives up, goes away or leaves the radius by 15%.
// A craft on the ground feels it through the ground (tremor, camera shake, instruments); a craft in flight only gets instrument and
// signal effects, because nothing carries the vibration to it. The text is one line of instrument output, never advice.
using System;

namespace UnkerbalThings.Dread
{
    public struct DreadWarning
    {
        public int Stage;            // 1, 2, 3
        public float Distance;       // metres at the moment it fired
        public bool Ground;          // true: the vibration reaches the craft through the ground
        public float Strength;       // 0..1, grows with the stage
        public string Text;
    }

    public sealed class DreadWarnings
    {
        public float RadiusMetres = 2500f;
        public float Stage2Fraction = 0.6f;
        public float Stage3Fraction = 0.2f;
        public float ResetFraction = 1.15f;

        readonly bool[] fired = new bool[3];
        bool chase;

        public bool InChase { get { return chase; } }
        public bool Fired(int stage) { return stage >= 1 && stage <= 3 && fired[stage - 1]; }

        public float StageMetres(int stage)
        {
            return stage == 1 ? RadiusMetres : (stage == 2 ? RadiusMetres * Stage2Fraction : RadiusMetres * Stage3Fraction);
        }

        public void Reset()
        {
            for (int i = 0; i < 3; i++) fired[i] = false;
            chase = false;
        }

        /// <summary>distance: metres to the monster (negative = none). aware: it is searching or hunting. landed: the craft rests on the ground.
        /// Returns true when a warning fires in this update (at most one).</summary>
        public bool Update(float distance, bool aware, bool landed, out DreadWarning w)
        {
            w = new DreadWarning();
            if (distance < 0f || !aware || distance > RadiusMetres * ResetFraction)
            {
                Reset();
                return false;
            }
            chase = true;
            int stage = 0;
            for (int s = 3; s >= 1; s--)
            {
                if (distance <= StageMetres(s)) { stage = s; break; }
            }
            if (stage == 0 || fired[stage - 1]) return false;
            // a stage that was jumped over counts as given: the distance only gets one cue per stage
            for (int s = 1; s <= stage; s++) fired[s - 1] = true;
            w.Stage = stage;
            w.Distance = distance;
            w.Ground = landed;
            w.Strength = stage == 1 ? 0.35f : (stage == 2 ? 0.65f : 1f);
            w.Text = Describe(stage, distance, landed);
            return true;
        }

        public static string Describe(int stage, float distance, bool ground)
        {
            string range = distance >= 950f ? (distance / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM" : (Math.Round(distance / 50f) * 50f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " M";
            if (ground)
            {
                switch (stage)
                {
                    case 1: return "SEIS CH1  0.8 HZ EVENT  RNG ~" + range;
                    case 2: return "SEIS CH1  PERIODIC 0.6 HZ  RNG ~" + range;
                    default: return "SEIS CH1  SATURATED  RNG ~" + range;
                }
            }
            switch (stage)
            {
                case 1: return "COMM  NOISE FLOOR +6 DB  RNG ~" + range;
                case 2: return "COMM  NOISE FLOOR +14 DB  RNG ~" + range;
                default: return "COMM  CARRIER LOST  RNG ~" + range;
            }
        }
    }
}
