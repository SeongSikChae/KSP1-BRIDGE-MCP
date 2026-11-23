using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>What the circularize step does on one frame.</summary>
    internal sealed class CircularizeCommand
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
    /// The circularize step without the game (spec C8-C10): the burn start centered on the apoapsis and the delta-v to the circular
    /// velocity; the burn itself (direction fixed below 10 m/s, the 5° gate, the throttle and completion) is <see cref="BurnControl"/>.
    /// </summary>
    internal sealed class CircularizeLogic
    {
        public const double LockMps = BurnControl.LockMps;
        public const double GateDeg = BurnControl.GateDeg;
        public const double FadeSeconds = BurnControl.FadeSeconds;
        public const double MinThrottle = BurnControl.MinThrottle;
        public const double GrowMps = BurnControl.GrowMps;
        public const double NoThrustKn = BurnControl.NoThrustKn;

        private readonly CircularizeArgs args;
        private readonly BurnControl burn;
        private bool burning;

        public CircularizeLogic(CircularizeArgs args)
        {
            this.args = args;
            burn = new BurnControl(args.ToleranceMps, args.AutoStage);
        }

        public double StartUt { get; private set; }
        public double? BurnSeconds { get; private set; }
        public double RemainingDv { get; private set; }

        /// <summary>UT of the first burning frame; null before it.</summary>
        public double? BurnStartUt { get; private set; }

        public string Phase => burning ? "burn" : "wait";

        /// <summary>Why the step cannot start (spec C7, C8); null when it can. Controllability is checked by the step.</summary>
        public string StartFailure(AscentReading r)
        {
            string failure = OrbitFailure(r);
            if (failure == null && args.InclinationDeg.HasValue)
                failure = LaunchAzimuth.StartFailure(args.InclinationDeg.Value, r.Latitude);
            return failure;
        }

        private static string OrbitFailure(AscentReading r)
        {
            if (r.OnSurface)
                return "the vessel is landed, splashed or on the launch pad";
            if (r.Eccentricity >= 1)
                return "the orbit is open (eccentricity " + r.Eccentricity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
            if (r.BodyRadius + r.ApA >= r.SoiRadius)
                return "the apoapsis is outside the sphere of influence";
            if (r.AtmosphereTop.HasValue && r.ApA <= r.AtmosphereTop.Value)
                return "the apoapsis is not above the atmosphere";
            return null;
        }

        public CircularizeCommand Tick(AscentReading r)
        {
            Vec dv = Circular(r) - r.OrbitVelocity;
            RemainingDv = dv.Length;
            double accel = r.Mass > 0 ? r.MaxThrustVac / r.Mass : 0;

            if (!burning)
            {
                BurnSeconds = BurnEstimate.Seconds(ApoapsisDv(r), r.Stages, accel);
                StartUt = r.UT + r.TimeToAp - (BurnSeconds ?? 0) / 2;
                if (r.UT >= StartUt || r.VerticalSpeed < 0)
                {
                    burning = true;
                    BurnStartUt = r.UT;
                }
            }

            CircularizeCommand c = From(burn.Tick(dv, burning, r));
            return c;
        }

        private static CircularizeCommand From(BurnCommand b) => new CircularizeCommand
        {
            PitchDeg = b.PitchDeg,
            HeadingDeg = b.HeadingDeg,
            Throttle = b.Throttle,
            WantThrust = b.WantThrust,
            Stage = b.Stage,
            Done = b.Done,
            Failure = b.Failure,
        };

        /// <summary>step_progress (spec C10).</summary>
        public Dictionary<string, object> Progress(int stages) => new Dictionary<string, object>
        {
            ["phase"] = Phase,
            ["start_ut"] = StartUt,
            ["burn_time_s"] = BurnSeconds,
            ["remaining_dv_mps"] = RemainingDv,
            ["nose_error_deg"] = burn.Last == null ? (double?)null : burn.NoseError,
            ["throttle"] = burn.Last?.Throttle,
            ["stages"] = stages,
        };

        /// <summary>
        /// The circular orbital velocity here (spec C8): horizontal with the circular speed, along the current horizontal velocity
        /// without an inclination, otherwise along the β of the inclination at this latitude that is closer to it (D016, D018).
        /// </summary>
        private Vec Circular(AscentReading r)
        {
            double speed = Math.Sqrt(r.Gm / (r.BodyRadius + r.Altitude));
            Vec horizontal = r.OrbitVelocity - r.Up * Vec.Dot(r.OrbitVelocity, r.Up);
            if (args.InclinationDeg.HasValue)
            {
                double beta = LaunchAzimuth.Nearest(args.InclinationDeg.Value, r.Latitude, Vec.Dot(horizontal, r.North),
                    Vec.Dot(horizontal, r.East)) * Math.PI / 180;
                return (r.North * Math.Cos(beta) + r.East * Math.Sin(beta)) * speed;
            }
            return horizontal.Length > 1e-6 ? horizontal.Normalized() * speed : new Vec(0, 0, 0);
        }

        /// <summary>Δv from the orbit's speed at the apoapsis to the circular speed there (vis-viva).</summary>
        private static double ApoapsisDv(AscentReading r)
        {
            double rAp = r.BodyRadius + r.ApA;
            double vAp = Math.Sqrt(Math.Max(0, r.Gm * (2 / rAp - 1 / r.SemiMajorAxis)));
            return Math.Max(0, Math.Sqrt(r.Gm / rAp) - vAp);
        }

    }
}
