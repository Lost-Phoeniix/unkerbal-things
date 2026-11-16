// Dread: what the player may see near the Moho swarm. Pure C# (no UnityEngine); game side EntityTracker.ViewLock.cs.
//
// - Outside (the orbiting flight camera): when the swarm comes within PullMetres it slowly drowns in static, then goes black.
// - With a Swarm Imager on the craft the view then switches to the imager's camera feed (ModuleUTImagerFeed): a black-and-white tube
//   camera with blown-out highlights, on a slow pan/tilt head with hard stops. It can also be entered by hand when the swarm is away.
// - EVA: the kerbal's own eyes, first person, clear (kerbals' sight is not affected).
// - IVA: always clear (kerbals look out of the windows); the player can fly from IVA.
// - During an eruption the feed and the EVA view cut to black (hides the strike; the player does not know what happened).
// Part menus stay usable in the feed and in EVA (the camera mode stays Flight); action groups work everywhere.
using System;

namespace UnkerbalThings.Dread
{
    public enum ViewMode { Outside, Feed, EvaEye }

    public struct ViewLockInput
    {
        public float Dt;
        public double SwarmDistance;   // m from the active vessel; infinity: no swarm on this body
        public bool HasImager;          // the active vessel carries a working imager
        public bool Eva;                // the active vessel is a kerbal on EVA
        public bool WantFeed;           // the player asked for the feed (button / action group); ignored while locked
        public bool Attack;             // an eruption is under way (blackout)
    }

    public sealed class ViewLockSettings
    {
        public bool Enabled = true;
        public float PullMetres = 1000f;      // the outside view starts to go within this distance (most of the approach is flown by navball and feed)
        public float ReleaseMetres = 1300f;   // and comes back beyond this one
        public float FadeSeconds = 4f;        // outside picture -> static -> black
        public float RecoverSeconds = 2f;     // black -> picture, after release
        public bool EvaFirstPerson = true;    // false: an EVA kerbal near the swarm gets static/black like a craft without imager
        public bool IvaClear = true;          // false: the old Dread screen effects also in IVA
        public float BlackoutIn = 0.06f;      // s to cut to black when an eruption starts
        public float BlackoutOut = 1.2f;      // s to come back after it
    }

    public sealed class ViewLock
    {
        public readonly ViewLockSettings S;
        public ViewMode Mode { get; private set; }
        /// <summary>0..1: how far the outside view has drowned (0 clear, ~0.6 all static, 1 black with faint static).</summary>
        public float OutsideFade { get; private set; }
        /// <summary>The swarm is close: the outside view is not available.</summary>
        public bool Locked { get; private set; }
        /// <summary>0..1: the feed / EVA view is cut to black.</summary>
        public float Blackout { get; private set; }

        public ViewLock(ViewLockSettings s) { S = s ?? new ViewLockSettings(); }

        public void Reset() { Mode = ViewMode.Outside; OutsideFade = 0f; Locked = false; Blackout = 0f; }

        public void Step(in ViewLockInput i)
        {
            float dt = Math.Max(0f, i.Dt);
            if (!S.Enabled) { Mode = i.HasImager && i.WantFeed && !i.Eva ? ViewMode.Feed : ViewMode.Outside; Locked = false; OutsideFade = 0f; Blackout = 0f; return; }
            double d = i.SwarmDistance;
            bool known = !double.IsNaN(d) && !double.IsInfinity(d);
            if (!known) Locked = false;
            else if (!Locked && d <= S.PullMetres) Locked = true;
            else if (Locked && d > S.ReleaseMetres) Locked = false;

            if (Locked) OutsideFade = Math.Min(1f, OutsideFade + dt / Math.Max(0.05f, S.FadeSeconds));
            else OutsideFade = Math.Max(0f, OutsideFade - dt / Math.Max(0.05f, S.RecoverSeconds));

            bool drowned = OutsideFade >= 1f;
            ViewMode next;
            if (i.Eva)
                next = Locked && S.EvaFirstPerson && (drowned || Mode == ViewMode.EvaEye) ? ViewMode.EvaEye : ViewMode.Outside;
            else if (!i.HasImager)
                next = ViewMode.Outside;
            else if (Locked)
                next = drowned || Mode == ViewMode.Feed ? ViewMode.Feed : ViewMode.Outside;
            else
                next = i.WantFeed ? ViewMode.Feed : ViewMode.Outside;
            // leaving a locked view: the outside starts black and recovers (never a sudden clear picture)
            if (next == ViewMode.Outside && Mode != ViewMode.Outside && OutsideFade < 1f && Locked) OutsideFade = 1f;
            // leaving a blacked-out feed or helmet (craft destroyed, vessel switch): the outside comes back from the same darkness
            if (next == ViewMode.Outside && Mode != ViewMode.Outside && Blackout > OutsideFade) OutsideFade = Blackout;
            Mode = next;

            float target = i.Attack && Mode != ViewMode.Outside ? 1f : 0f;
            float rate = target > Blackout ? 1f / Math.Max(0.01f, S.BlackoutIn) : 1f / Math.Max(0.01f, S.BlackoutOut);
            Blackout = target > Blackout ? Math.Min(target, Blackout + dt * rate) : Math.Max(target, Blackout - dt * rate);
        }

        /// <summary>The player may leave the feed by hand only while the swarm is not close.</summary>
        public bool CanLeaveFeed { get { return !Locked; } }

        /// <summary>Outside fade -> screen amounts: picture loss first, then static, then black with faint static.</summary>
        public static void OutsideScreen(float fade, out float loss, out float snow, out float dark)
        {
            float f = Clamp01(fade);
            loss = Smooth(0f, 0.6f, f);
            snow = Smooth(0.25f, 0.7f, f);
            dark = Smooth(0.65f, 1f, f) * 0.9f;
        }

        /// <summary>Feed degradation 0..1 from the swarm distance: clean at the pull radius and beyond, worn out at `nearMetres`;
        /// slow at first, faster near the end (most of a 1 km approach stays watchable).</summary>
        public static float FeedWear(double distance, float pullMetres, float nearMetres)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || pullMetres <= nearMetres) return 0f;
            float t = Clamp01((float)((pullMetres - distance) / (pullMetres - nearMetres)));
            return (float)Math.Pow(t, 1.4);
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        static float Smooth(float a, float b, float x)
        {
            float t = Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>Ground proximity warning (the radar part, GroundRadarPart.cs): flying blind near the swarm needs it.
    /// Level 0 quiet, 1 terrain (low and sinking), 2 sink rate, 3 pull up (impact within a few seconds).</summary>
    public static class ProximityWarn
    {
        /// <summary>Seconds to impact along the path: the smaller of straight down (height over sink rate) and ahead (range along
        /// the velocity over speed, NaN or infinity when nothing is ahead). Infinity when climbing and nothing is ahead.</summary>
        public static double TimeToImpact(double height, double verticalSpeed, double rangeAhead, double speed)
        {
            double down = verticalSpeed < -0.1 ? Math.Max(0.0, height) / -verticalSpeed : double.PositiveInfinity;
            double ahead = speed > 0.5 && !double.IsNaN(rangeAhead) && !double.IsInfinity(rangeAhead) ? Math.Max(0.0, rangeAhead) / speed : double.PositiveInfinity;
            return Math.Min(down, ahead);
        }

        public static int Level(double height, double verticalSpeed, double tti, double armHeight)
        {
            if (height > armHeight && (double.IsInfinity(tti) || tti > 20)) return 0;
            if (tti <= 4) return 3;
            if (tti <= 10 && verticalSpeed < -3) return 2;
            if (height <= armHeight * 0.5 && verticalSpeed < -0.5) return 1;
            return 0;
        }

        /// <summary>Seconds between beeps for a level (0: none; 3: continuous tone, returned as 0.12 s pulses).</summary>
        public static float BeepInterval(int level, double tti)
        {
            switch (level)
            {
            case 3: return 0.12f;
            case 2: return (float)Math.Max(0.2, Math.Min(0.8, tti / 12.0));
            case 1: return 1.2f;
            default: return 0f;
            }
        }

        public static string Callout(int level)
        {
            switch (level)
            {
            case 3: return "PULL UP";
            case 2: return "SINK RATE";
            case 1: return "TERRAIN";
            default: return "";
            }
        }
    }

    /// <summary>One axis of the imager's camera head: follows its target at a fixed rate between hard stops.</summary>
    public sealed class GimbalAxis
    {
        public float Min, Max, Rate;
        /// <summary>No stops (the pan turns all the way round). The target then leads the head by at most
        /// MaxLead degrees, so a long mouse drag cannot wind up turns the head has to unwind afterwards.</summary>
        public readonly bool Unlimited;
        public float MaxLead = 90f;
        public float Value { get; private set; }
        public float Target { get; private set; }
        /// <summary>The head is pressed against a stop (target clamped and reached): for the stop shudder and sound.</summary>
        public bool AtStop { get; private set; }
        public bool Moving { get; private set; }

        /// <summary>min >= max: no stops (Unlimited).</summary>
        public GimbalAxis(float min, float max, float rate, float start = 0f)
        {
            Unlimited = !(max > min);
            Min = Math.Min(min, max); Max = Math.Max(min, max); Rate = Math.Max(0.1f, rate);
            Value = Target = Unlimited ? start : Clamp(start);
        }

        float Clamp(float v) { return v < Min ? Min : v > Max ? Max : v; }

        /// <summary>Push the target by `delta` degrees; the target never passes a stop.</summary>
        public void Push(float delta)
        {
            if (Unlimited)
            {
                float lead = Target + delta - Value;
                Target = Value + (lead > MaxLead ? MaxLead : lead < -MaxLead ? -MaxLead : lead);
                return;
            }
            float want = Target + delta;
            Target = Clamp(want);
            if (want != Target && Math.Abs(Value - Target) < 0.01f) AtStop = true;
        }

        public void Step(float dt)
        {
            AtStop = AtStop && Math.Abs(Value - Target) < 0.01f;
            float diff = Target - Value;
            float step = Rate * Math.Max(0f, dt);
            Moving = Math.Abs(diff) > 0.01f;
            Value = Math.Abs(diff) <= step ? Target : Value + Math.Sign(diff) * step;
            if (Unlimited && Math.Abs(Value) > 360f)
            {
                // keep the numbers small; the angle is the same
                float wrap = Value > 0f ? -360f : 360f;
                Value += wrap; Target += wrap;
            }
        }

        /// <summary>The angle in -180..180 (for the overlay).</summary>
        public float Wrapped { get { float a = Value % 360f; if (a > 180f) a -= 360f; else if (a < -180f) a += 360f; return a; } }

        /// <summary>The target follows the head where it is (after a stop or on entering).</summary>
        public void Hold() { Target = Value; }
    }

    /// <summary>The imager's zoom: a few fixed fields of view; each change takes time and the picture hunts for focus meanwhile.</summary>
    public sealed class ZoomSteps
    {
        public readonly float[] Fovs;
        public readonly float SecondsPerStep;
        public int Index { get; private set; }
        public float Fov { get; private set; }
        /// <summary>0..1: the picture is out of focus while the zoom moves and for a moment after.</summary>
        public float Blur { get; private set; }
        float from, t = 1f;

        public ZoomSteps(float[] fovs, float secondsPerStep)
        {
            Fovs = fovs != null && fovs.Length > 0 ? fovs : new[] { 50f };
            SecondsPerStep = Math.Max(0.05f, secondsPerStep);
            Fov = from = Fovs[0];
        }

        public bool In() { return Go(Index + 1); }
        public bool Out() { return Go(Index - 1); }

        bool Go(int i)
        {
            if (i < 0 || i >= Fovs.Length || i == Index || t < 1f) return false;
            from = Fov; Index = i; t = 0f;
            return true;
        }

        public void Step(float dt)
        {
            if (t < 1f)
            {
                t = Math.Min(1f, t + Math.Max(0f, dt) / SecondsPerStep);
                float e = t * t * (3f - 2f * t);
                Fov = from + (Fovs[Index] - from) * e;
                Blur = 1f;
            }
            else
            {
                Fov = Fovs[Index];
                Blur = Math.Max(0f, Blur - Math.Max(0f, dt) / 0.6f);   // focus settles a little after the zoom stops
            }
        }
    }
}
