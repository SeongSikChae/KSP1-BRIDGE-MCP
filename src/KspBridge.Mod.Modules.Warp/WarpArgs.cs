using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Warp
{
    internal enum WarpGoal
    {
        Seconds,
        Ut,
        Apoapsis,
        Periapsis,

        /// <summary>The first maneuver node's burn start (docs/specs/기동-노드 D005).</summary>
        Node,
    }

    /// <summary>Checked parameters of a warp step. Bad input is bad_request, so it rejects the plan at submit time.</summary>
    internal sealed class WarpArgs
    {
        private const string SecondsKey = "seconds";
        private const string UtKey = "ut";
        private const string ToKey = "to";
        private const string LeadKey = "lead_s";
        private const string MaxRateKey = "max_rate";
        private const string PhysicsRateKey = "physics_rate";

        private static readonly string[] Keys = { SecondsKey, UtKey, ToKey, LeadKey, MaxRateKey, PhysicsRateKey };

        public WarpGoal Goal;

        /// <summary>Seconds from the start (Seconds) or the UT (Ut).</summary>
        public double Value;

        /// <summary>Seconds before the apoapsis, the periapsis or the node's burn start.</summary>
        public double Lead;

        /// <summary>Highest rails rate; null when the game's highest is fine.</summary>
        public double? MaxRate;

        /// <summary>Highest physics rate where rails warp is not possible; null for none.</summary>
        public int? PhysicsRate;

        public static WarpArgs Parse(Dictionary<string, object> args)
        {
            string unknown = args.Keys.FirstOrDefault(k => !Keys.Contains(k));
            if (unknown != null)
                throw BridgeException.BadRequest("unknown parameter '" + unknown + "' (" + string.Join(", ", Keys) + ")");

            double? seconds = Number(args, SecondsKey);
            double? ut = Number(args, UtKey);
            string to = args.Str(ToKey);
            if ((seconds.HasValue ? 1 : 0) + (ut.HasValue ? 1 : 0) + (to != null ? 1 : 0) != 1)
                throw BridgeException.BadRequest("give exactly one of 'seconds', 'ut' and 'to'");

            double? lead = Number(args, LeadKey);
            if (lead.HasValue && to == null)
                throw BridgeException.BadRequest("'lead_s' needs 'to'");
            if (lead.HasValue && lead.Value < 0)
                throw BridgeException.BadRequest("'lead_s' must be >= 0");
            if (seconds.HasValue && !(seconds.Value > 0))
                throw BridgeException.BadRequest("'seconds' must be > 0");

            double? maxRate = Number(args, MaxRateKey);
            if (maxRate.HasValue && maxRate.Value < 1)
                throw BridgeException.BadRequest("'max_rate' must be >= 1");
            double? physicsRate = Number(args, PhysicsRateKey);
            if (physicsRate.HasValue && (physicsRate.Value < 2 || physicsRate.Value > 4 || physicsRate.Value != Math.Floor(physicsRate.Value)))
                throw BridgeException.BadRequest("'physics_rate' must be 2, 3 or 4");

            return new WarpArgs
            {
                Goal = seconds.HasValue ? WarpGoal.Seconds : ut.HasValue ? WarpGoal.Ut : ParseTo(to),
                Value = seconds ?? ut ?? 0,
                Lead = lead ?? 0,
                MaxRate = maxRate,
                PhysicsRate = (int?)physicsRate,
            };
        }

        private static WarpGoal ParseTo(string to)
        {
            switch (to)
            {
                case "apoapsis":
                    return WarpGoal.Apoapsis;
                case "periapsis":
                    return WarpGoal.Periapsis;
                case "node":
                    return WarpGoal.Node;
                default:
                    throw BridgeException.BadRequest("'to' must be apoapsis, periapsis or node (got '" + to + "')");
            }
        }

        private static double? Number(Dictionary<string, object> args, string key)
        {
            double? value = args.Num(key);
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                throw BridgeException.BadRequest("'" + key + "' must be a finite number");
            return value;
        }
    }
}
