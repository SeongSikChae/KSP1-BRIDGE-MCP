using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>What the ascend step does on one frame.</summary>
    internal sealed class AscendCommand
    {
        public double PitchDeg;
        public double HeadingDeg;
        public double Throttle;

        /// <summary>Thrust wanted before the dynamic pressure limit (spec terms).</summary>
        public bool WantThrust;

        public StageReason Stage;
        public bool Done;

        /// <summary>Why the step fails now; null when it does not.</summary>
        public string Failure;
    }

    /// <summary>
    /// The ascend step without the game (spec C3-C5, C7, C10): reached and refill state, phase, throttle from the apoapsis and the
    /// dynamic pressure, the target direction, completion, failures and what it records.
    /// </summary>
    internal sealed class AscendLogic
    {
        public const double KeepShare = 0.99;
        public const double FadeShare = 0.05;
        public const double MinThrottle = 0.05;
        public const double QFadeShare = 0.8;
        public const double NoThrustKn = 0.01;

        private readonly AscendArgs args;
        private readonly ControlLossRule loss = new ControlLossRule();
        private bool reached;
        private bool refilling;
        private AscendCommand last;
        private double apoapsis;
        private double noseError;
        private double q;
        private double? aoa;

        public AscendLogic(AscendArgs args, AscentReading start)
        {
            this.args = args;
            Profile = new TurnProfile(start.Altitude, args.ApoapsisM, start.AtmosphereTop, args.TurnStartM, args.TurnEndM);
        }

        public TurnProfile Profile { get; }

        public double MaxQKpa { get; private set; }

        /// <summary>Largest angle of attack at 1 kPa or more; null when there was none.</summary>
        public double? MaxAoaDeg { get; private set; }

        public string Phase => last == null || last.WantThrust ? "climb" : "coast";

        /// <summary>Why the step cannot start (spec C7); null when it can. Controllability is checked by the step.</summary>
        public string StartFailure(AscentReading r)
        {
            string failure = Profile.StartFailure(r.BodyRadius, r.SoiRadius);
            if (failure == null && args.InclinationDeg.HasValue)
                failure = LaunchAzimuth.StartFailure(args.InclinationDeg.Value, r.Latitude);
            return failure;
        }

        public AscendCommand Tick(AscentReading r)
        {
            double target = args.ApoapsisM;
            apoapsis = r.Eccentricity >= 1 ? double.PositiveInfinity : r.ApA;
            q = r.DynamicPressureKpa;

            if (apoapsis >= target)
            {
                reached = true;
                refilling = false;
            }
            else if (reached && apoapsis < KeepShare * target)
            {
                refilling = true;
            }

            AscendCommand c = new AscendCommand { WantThrust = !reached || refilling };
            double apThrottle = c.WantThrust ? Clamp((target - apoapsis) / (FadeShare * target), MinThrottle, 1) : 0;
            double qThrottle = r.AtmosphereTop.HasValue ? Clamp((args.MaxQKpa - q) / ((1 - QFadeShare) * args.MaxQKpa), 0, 1) : 1;
            c.Throttle = Math.Min(apThrottle, qThrottle);

            Vec direction;
            double heading = Heading(r);
            if (!c.WantThrust && r.InAtmosphere && r.SurfaceVelocity.Length >= AoaLimit.MinSpeed)
            {
                direction = r.SurfaceVelocity.Normalized();
            }
            else
            {
                double pitch = Math.Min(90, Profile.Pitch(r.Altitude, apoapsis) + Profile.Sag(reached, apoapsis, r.TimeToAp, r.VerticalSpeed));
                direction = AoaLimit.Limit(AoaLimit.Direction(pitch, heading, r.Up, r.North, r.East), r.SurfaceVelocity, q, args.MaxAoaDeg);
            }
            AoaLimit.PitchHeading(direction, r.Up, r.North, r.East, heading, out c.PitchDeg, out c.HeadingDeg);

            noseError = AoaLimit.AngleDeg(r.Nose, direction);
            aoa = r.SurfaceVelocity.Length >= AoaLimit.MinSpeed ? AoaLimit.AngleDeg(r.Nose, r.SurfaceVelocity) : (double?)null;
            MaxQKpa = Math.Max(MaxQKpa, q);
            if (aoa.HasValue && q >= ControlLossRule.MinQKpa)
                MaxAoaDeg = Math.Max(MaxAoaDeg ?? 0, aoa.Value);

            c.Failure = loss.Update(r.UT, r.InAtmosphere, q, r.Thrust, noseError);
            if (c.Failure == null && StageRule.Exhausted(c.WantThrust, args.AutoStage, r.NextStage, r.Engines))
                c.Failure = "engines exhausted: thrust is wanted but no engine runs and staging cannot light one";
            c.Stage = StageRule.Judge(c.WantThrust, args.AutoStage, r.NextStage, r.Engines);

            c.Done = c.Failure == null && reached && apoapsis >= KeepShare * target
                     && (!r.AtmosphereTop.HasValue || r.Altitude > r.AtmosphereTop.Value) && r.Thrust < NoThrustKn;
            if (c.Done)
            {
                c.Throttle = 0;
                c.Stage = StageReason.None;
            }
            last = c;
            return c;
        }

        /// <summary>step_progress (spec C10); <paramref name="stages"/> is the number of stages activated by the step.</summary>
        public Dictionary<string, object> Progress(int stages) => new Dictionary<string, object>
        {
            ["phase"] = Phase,
            ["target_apoapsis_m"] = args.ApoapsisM,
            ["apoapsis_m"] = double.IsInfinity(apoapsis) ? (double?)null : apoapsis,
            ["pitch_deg"] = last?.PitchDeg,
            ["heading_deg"] = last?.HeadingDeg,
            ["nose_error_deg"] = last == null ? (double?)null : noseError,
            ["throttle"] = last?.Throttle,
            ["q_kpa"] = q,
            ["aoa_deg"] = aoa,
            ["stages"] = stages,
        };

        private double Heading(AscentReading r)
        {
            if (!args.InclinationDeg.HasValue)
                return args.Heading.Value;
            Vec horizontal = r.OrbitVelocity - r.Up * Vec.Dot(r.OrbitVelocity, r.Up);
            double targetSpeed = Math.Sqrt(r.Gm / (r.BodyRadius + args.ApoapsisM));
            return LaunchAzimuth.Heading(args.InclinationDeg.Value, r.Latitude, args.North, Vec.Dot(horizontal, r.North),
                Vec.Dot(horizontal, r.East), targetSpeed);
        }

        private static double Clamp(double x, double min, double max) => double.IsNaN(x) ? min : x < min ? min : x > max ? max : x;
    }
}
