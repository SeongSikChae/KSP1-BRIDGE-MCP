using KspBridge.Mod.Modules.Flight;
using System;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>What a burn toward a delta-v does on one frame.</summary>
    public sealed class BurnCommand
    {
        public double PitchDeg;
        public double HeadingDeg;
        public double Throttle;
        public bool WantThrust;
        public StageReason Stage;
        public bool Done;
        public string Failure;
    }

    /// <summary>
    /// A burn along a delta-v vector without the game, shared by circularize and execute_node so both judge alike
    /// (docs/features/flight-ascent.md R8·R9): the direction follows the delta-v and is fixed once below 10 m/s, no thrust while the
    /// nose is more than 5° off, the throttle fades over the last 2 s of delta-v (at least 0.05), done at the tolerance or when below
    /// 10 m/s the delta-v grows again while the engines thrust, and automatic staging and engines exhausted while thrust is wanted.
    /// </summary>
    public sealed class BurnControl
    {
        public const double LockMps = 10;
        public const double GateDeg = 5;
        public const double FadeSeconds = 2;
        public const double MinThrottle = 0.05;
        public const double GrowMps = 0.05;
        public const double NoThrustKn = 0.01;

        private readonly double toleranceMps;
        private readonly bool autoStage;
        private double? lockedPitch;
        private double lockedHeading;
        private double? minimum;

        public BurnControl(double toleranceMps, bool autoStage)
        {
            this.toleranceMps = toleranceMps;
            this.autoStage = autoStage;
        }

        /// <summary>Angle (°) between the nose and the burn direction on the last frame.</summary>
        public double NoseError { get; private set; }

        /// <summary>The last frame's command; null before the first.</summary>
        public BurnCommand Last { get; private set; }

        /// <param name="dv">Delta-v left (world vector, m/s).</param>
        /// <param name="burning">The burn phase: before it the nose turns but nothing locks, completes or thrusts.</param>
        public BurnCommand Tick(Vec dv, bool burning, AscentReading r)
        {
            double remaining = dv.Length;
            double accel = r.Mass > 0 ? r.MaxThrustVac / r.Mass : 0;

            BurnCommand c = new BurnCommand();
            if (lockedPitch.HasValue)
            {
                c.PitchDeg = lockedPitch.Value;
                c.HeadingDeg = lockedHeading;
            }
            else
            {
                AoaLimit.PitchHeading(dv, r.Up, r.North, r.East, 0, out c.PitchDeg, out c.HeadingDeg);
                if (burning && remaining < LockMps)
                {
                    lockedPitch = c.PitchDeg;
                    lockedHeading = c.HeadingDeg;
                }
            }
            NoseError = AoaLimit.AngleDeg(r.Nose, AoaLimit.Direction(c.PitchDeg, c.HeadingDeg, r.Up, r.North, r.East));

            if (burning && (remaining <= toleranceMps || Grew(remaining, r)))
            {
                c.Done = true;
                Last = c;
                return c;
            }

            c.WantThrust = burning;
            if (burning && NoseError <= GateDeg)
                c.Throttle = accel > 0 ? Clamp(remaining / (accel * FadeSeconds), MinThrottle, 1) : 1;
            if (StageRule.Exhausted(c.WantThrust, autoStage, r.NextStage, r.Engines))
                c.Failure = "engines exhausted: thrust is wanted but no engine runs and staging cannot light one";
            c.Stage = StageRule.Judge(c.WantThrust, autoStage, r.NextStage, r.Engines);
            Last = c;
            return c;
        }

        /// <summary>
        /// Grew again: the minimum is kept only on thrusting frames (engine thrust ≥ 0.01 kN) below 10 m/s and dropped on any other
        /// frame; done when a thrusting frame below 10 m/s is more than 0.05 m/s above it.
        /// </summary>
        private bool Grew(double remaining, AscentReading r)
        {
            bool thrusting = r.Thrust >= NoThrustKn;
            if (!thrusting || remaining >= LockMps)
            {
                minimum = null;
                return false;
            }
            if (minimum.HasValue && remaining > minimum.Value + GrowMps)
                return true;
            minimum = minimum.HasValue ? Math.Min(minimum.Value, remaining) : remaining;
            return false;
        }

        private static double Clamp(double x, double min, double max) => double.IsNaN(x) ? min : x < min ? min : x > max ? max : x;
    }
}
