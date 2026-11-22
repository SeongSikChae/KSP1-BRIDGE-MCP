using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Checked parameters of an attitude step.</summary>
    internal sealed class AttitudeArgs
    {
        public AttitudeTarget Target;
        public double ToleranceDeg;
        public double SettleRateDps;
    }

    internal enum GroupChange
    {
        On,
        Off,
        Toggle,
    }

    /// <summary>Checked parameters of an action group step.</summary>
    internal sealed class ActionGroupArgs
    {
        public string Name;
        public KSPActionGroup Group;
        public GroupChange Change;
    }

    /// <summary>Step parameter checks and the name tables. Bad input is bad_request, so it rejects the plan at submit time.</summary>
    internal static class ControlArgs
    {
        public const double DefaultToleranceDeg = 2;
        public const double DefaultSettleRateDps = 0.5;

        private static readonly Dictionary<string, AttitudeDirection> Directions =
            new Dictionary<string, AttitudeDirection>(StringComparer.Ordinal)
            {
                ["prograde"] = AttitudeDirection.Prograde,
                ["retrograde"] = AttitudeDirection.Retrograde,
                ["normal"] = AttitudeDirection.Normal,
                ["antinormal"] = AttitudeDirection.Antinormal,
                ["radial_out"] = AttitudeDirection.RadialOut,
                ["radial_in"] = AttitudeDirection.RadialIn,
                ["surface_prograde"] = AttitudeDirection.SurfacePrograde,
                ["surface_retrograde"] = AttitudeDirection.SurfaceRetrograde,
            };

        private static readonly Dictionary<string, KSPActionGroup> Groups =
            new Dictionary<string, KSPActionGroup>(StringComparer.Ordinal)
            {
                ["1"] = KSPActionGroup.Custom01,
                ["2"] = KSPActionGroup.Custom02,
                ["3"] = KSPActionGroup.Custom03,
                ["4"] = KSPActionGroup.Custom04,
                ["5"] = KSPActionGroup.Custom05,
                ["6"] = KSPActionGroup.Custom06,
                ["7"] = KSPActionGroup.Custom07,
                ["8"] = KSPActionGroup.Custom08,
                ["9"] = KSPActionGroup.Custom09,
                ["10"] = KSPActionGroup.Custom10,
                ["gear"] = KSPActionGroup.Gear,
                ["light"] = KSPActionGroup.Light,
                ["brakes"] = KSPActionGroup.Brakes,
                ["abort"] = KSPActionGroup.Abort,
                ["sas"] = KSPActionGroup.SAS,
                ["rcs"] = KSPActionGroup.RCS,
            };

        public static string DirectionName(AttitudeDirection direction)
        {
            foreach (KeyValuePair<string, AttitudeDirection> pair in Directions)
            {
                if (pair.Value == direction)
                    return pair.Key;
            }
            throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }

        public static double Throttle(Dictionary<string, object> args)
        {
            double value = args.Num("value", required: true).Value;
            Range("value", value, 0, 1);
            return value;
        }

        public static AttitudeArgs Attitude(Dictionary<string, object> args)
        {
            double? pitch = args.Num("pitch");
            double? heading = args.Num("heading");
            double? roll = args.Num("roll");
            string direction = args.Str("direction");

            AttitudeTarget target;
            if (direction != null)
            {
                if (pitch.HasValue || heading.HasValue)
                    throw BridgeException.BadRequest("give either 'direction' or 'pitch' and 'heading', not both");
                if (roll.HasValue)
                    throw BridgeException.BadRequest("'roll' needs 'pitch' and 'heading'; it cannot be given with 'direction'");
                if (!Directions.TryGetValue(direction, out AttitudeDirection known))
                    throw BridgeException.BadRequest("unknown 'direction' '" + direction + "' (one of " + string.Join(", ", Directions.Keys) + ")");
                target = AttitudeTarget.Along(known);
            }
            else
            {
                if (!pitch.HasValue || !heading.HasValue)
                    throw BridgeException.BadRequest("give 'pitch' and 'heading' together, or 'direction'");
                Range("pitch", pitch.Value, -90, 90);
                if (!(heading.Value >= 0 && heading.Value < 360))
                    throw BridgeException.BadRequest("'heading' must be from 0 to below 360 (got " + Text(heading.Value) + ")");
                if (roll.HasValue)
                    Range("roll", roll.Value, -180, 180);
                target = AttitudeTarget.PitchHeading(pitch.Value, heading.Value, roll);
            }

            return new AttitudeArgs
            {
                Target = target,
                ToleranceDeg = Positive(args, "tolerance_deg", DefaultToleranceDeg),
                SettleRateDps = Positive(args, "settle_rate_dps", DefaultSettleRateDps),
            };
        }

        public static ActionGroupArgs ActionGroup(Dictionary<string, object> args)
        {
            string name = GroupName(args);
            if (!Groups.TryGetValue(name, out KSPActionGroup group))
                throw BridgeException.BadRequest("unknown 'group' '" + name + "' (one of " + string.Join(", ", Groups.Keys) + ")");
            return new ActionGroupArgs { Name = name, Group = group, Change = Change(args.Str("state", required: true)) };
        }

        /// <summary>A custom group may be given as the string "1".."10" or as a number.</summary>
        private static string GroupName(Dictionary<string, object> args)
        {
            if (args != null && args.TryGetValue("group", out object raw) && raw is double number)
                return Text(number);
            return args.Str("group", required: true);
        }

        private static GroupChange Change(string state)
        {
            switch (state)
            {
                case "on":
                    return GroupChange.On;
                case "off":
                    return GroupChange.Off;
                case "toggle":
                    return GroupChange.Toggle;
                default:
                    throw BridgeException.BadRequest("unknown 'state' '" + state + "' (one of on, off, toggle)");
            }
        }

        private static double Positive(Dictionary<string, object> args, string key, double fallback)
        {
            double value = args.Num(key) ?? fallback;
            if (!(value > 0))
                throw BridgeException.BadRequest("'" + key + "' must be > 0 (got " + Text(value) + ")");
            return value;
        }

        private static void Range(string key, double value, double min, double max)
        {
            if (!(value >= min && value <= max))
                throw BridgeException.BadRequest("'" + key + "' must be from " + Text(min) + " to " + Text(max) + " (got " + Text(value) + ")");
        }

        private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
