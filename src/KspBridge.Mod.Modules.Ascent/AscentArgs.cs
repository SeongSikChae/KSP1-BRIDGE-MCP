using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Checked parameters of an ascend step (spec C2). Bad input is bad_request, so it rejects the plan at submit time.</summary>
    internal sealed class AscendArgs
    {
        public const double DefaultMaxQKpa = 20;
        public const double DefaultMaxAoaDeg = 5;
        public const double DefaultHeading = 90;

        public double ApoapsisM;

        /// <summary>Given heading, or null when an inclination is given.</summary>
        public double? Heading;

        public double? InclinationDeg;

        /// <summary>North (true) or south launch for an inclination.</summary>
        public bool North = true;

        public double MaxQKpa = DefaultMaxQKpa;
        public double MaxAoaDeg = DefaultMaxAoaDeg;

        /// <summary>Null: the default for the body (TurnProfile).</summary>
        public double? TurnStartM;

        public double? TurnEndM;

        public bool AutoStage = true;
    }

    /// <summary>Checked parameters of a circularize step (spec C2).</summary>
    internal sealed class CircularizeArgs
    {
        public const double DefaultToleranceMps = 0.5;

        public double ToleranceMps = DefaultToleranceMps;

        /// <summary>Target inclination the burn also turns the orbit plane to (D016); null keeps the current plane.</summary>
        public double? InclinationDeg;

        public bool AutoStage = true;
    }

    internal static class AscentArgs
    {
        private static readonly string[] AscendKeys =
            { "apoapsis_m", "heading", "inclination_deg", "launch", "max_q_kpa", "max_aoa_deg", "turn_start_m", "turn_end_m", "auto_stage" };

        private static readonly string[] CircularizeKeys = { "tolerance_mps", "inclination_deg", "auto_stage" };

        public static AscendArgs Ascend(Dictionary<string, object> args)
        {
            Known(args, AscendKeys);
            AscendArgs a = new AscendArgs();

            double? ap = Number(args, "apoapsis_m");
            if (!ap.HasValue)
                throw BridgeException.BadRequest("'apoapsis_m' is required");
            if (!(ap.Value > 0))
                throw BridgeException.BadRequest("'apoapsis_m' must be > 0 (got " + Text(ap.Value) + ")");
            a.ApoapsisM = ap.Value;

            double? heading = Number(args, "heading");
            double? inclination = Number(args, "inclination_deg");
            string launch = args.Str("launch");
            if (heading.HasValue && inclination.HasValue)
                throw BridgeException.BadRequest("give either 'heading' or 'inclination_deg', not both");
            if (launch != null && !inclination.HasValue)
                throw BridgeException.BadRequest("'launch' needs 'inclination_deg'");
            if (heading.HasValue && !(heading.Value >= 0 && heading.Value < 360))
                throw BridgeException.BadRequest("'heading' must be from 0 to below 360 (got " + Text(heading.Value) + ")");
            if (inclination.HasValue && !(inclination.Value >= 0 && inclination.Value <= 180))
                throw BridgeException.BadRequest("'inclination_deg' must be from 0 to 180 (got " + Text(inclination.Value) + ")");
            if (launch != null && launch != "north" && launch != "south")
                throw BridgeException.BadRequest("'launch' must be north or south (got '" + launch + "')");
            a.InclinationDeg = inclination;
            a.Heading = inclination.HasValue ? (double?)null : heading ?? AscendArgs.DefaultHeading;
            a.North = launch != "south";

            double? maxQ = Number(args, "max_q_kpa");
            if (maxQ.HasValue && !(maxQ.Value > 0))
                throw BridgeException.BadRequest("'max_q_kpa' must be > 0 (got " + Text(maxQ.Value) + ")");
            a.MaxQKpa = maxQ ?? AscendArgs.DefaultMaxQKpa;

            double? maxAoa = Number(args, "max_aoa_deg");
            if (maxAoa.HasValue && !(maxAoa.Value > 0 && maxAoa.Value <= 90))
                throw BridgeException.BadRequest("'max_aoa_deg' must be > 0 and <= 90 (got " + Text(maxAoa.Value) + ")");
            a.MaxAoaDeg = maxAoa ?? AscendArgs.DefaultMaxAoaDeg;

            double? start = Number(args, "turn_start_m");
            if (start.HasValue && !(start.Value >= 0))
                throw BridgeException.BadRequest("'turn_start_m' must be >= 0 (got " + Text(start.Value) + ")");
            a.TurnStartM = start;

            double? end = Number(args, "turn_end_m");
            if (end.HasValue && !(end.Value > 0))
                throw BridgeException.BadRequest("'turn_end_m' must be > 0 (got " + Text(end.Value) + ")");
            a.TurnEndM = end;

            a.AutoStage = args.Bool("auto_stage", true);
            return a;
        }

        public static CircularizeArgs Circularize(Dictionary<string, object> args)
        {
            Known(args, CircularizeKeys);
            CircularizeArgs c = new CircularizeArgs();
            double? tolerance = Number(args, "tolerance_mps");
            if (tolerance.HasValue && !(tolerance.Value > 0 && tolerance.Value <= 100))
                throw BridgeException.BadRequest("'tolerance_mps' must be > 0 and <= 100 (got " + Text(tolerance.Value) + ")");
            c.ToleranceMps = tolerance ?? CircularizeArgs.DefaultToleranceMps;
            double? inclination = Number(args, "inclination_deg");
            if (inclination.HasValue && !(inclination.Value >= 0 && inclination.Value <= 180))
                throw BridgeException.BadRequest("'inclination_deg' must be from 0 to 180 (got " + Text(inclination.Value) + ")");
            c.InclinationDeg = inclination;
            c.AutoStage = args.Bool("auto_stage", true);
            return c;
        }

        private static void Known(Dictionary<string, object> args, string[] keys)
        {
            string unknown = args.Keys.FirstOrDefault(k => !keys.Contains(k));
            if (unknown != null)
                throw BridgeException.BadRequest("unknown parameter '" + unknown + "' (" + string.Join(", ", keys) + ")");
        }

        private static double? Number(Dictionary<string, object> args, string key)
        {
            double? value = args.Num(key);
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                throw BridgeException.BadRequest("'" + key + "' must be a finite number");
            return value;
        }

        private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
