using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Mod.Modules.Maneuver
{
    internal enum NodeTimeKind
    {
        Ut,
        InSeconds,
        Apoapsis,
        Periapsis,
    }

    internal enum NodeGoalKind
    {
        /// <summary>Delta-v given as components.</summary>
        Components,
        Apoapsis,
        Periapsis,
        Circular,
    }

    /// <summary>Checked parameters of create_node (spec C2). Bad input is bad_request.</summary>
    internal sealed class CreateNodeArgs
    {
        public NodeTimeKind Time;

        /// <summary>The UT (Ut) or the game seconds from now (InSeconds).</summary>
        public double TimeValue;

        public NodeGoalKind Goal;

        /// <summary>The target altitude (m) of an apoapsis or periapsis goal.</summary>
        public double GoalValue;

        public double ProgradeMps;
        public double NormalMps;
        public double RadialMps;
    }

    /// <summary>Checked parameters of delete_node (spec C4).</summary>
    internal sealed class DeleteNodeArgs
    {
        /// <summary>The node number; null with All.</summary>
        public int? Index;

        public bool All;
    }

    /// <summary>Checked parameters of execute_node (spec C5).</summary>
    internal sealed class ExecuteNodeArgs
    {
        public const double DefaultToleranceMps = 0.5;

        public double ToleranceMps = DefaultToleranceMps;
        public bool AutoStage = true;
    }

    /// <summary>The argument rules of the three tools and the step, without the game.</summary>
    internal static class NodeArgs
    {
        private const string Ut = "ut";
        private const string InS = "in_s";
        private const string At = "at";
        private const string Prograde = "prograde_mps";
        private const string Normal = "normal_mps";
        private const string Radial = "radial_mps";
        private const string GoalKey = "goal";
        private const string ApKey = "apoapsis_m";
        private const string PeKey = "periapsis_m";
        private const string CircularKey = "circular";

        private static readonly string[] CreateKeys = { Ut, InS, At, Prograde, Normal, Radial, GoalKey };
        private static readonly string[] GoalKeys = { ApKey, PeKey, CircularKey };
        private static readonly string[] DeleteKeys = { "index", "all" };
        private static readonly string[] ExecuteKeys = { "tolerance_mps", "auto_stage" };

        public static CreateNodeArgs Create(Dictionary<string, object> args)
        {
            args = args ?? new Dictionary<string, object>();
            Known(args, CreateKeys);
            CreateNodeArgs a = new CreateNodeArgs();

            double? ut = Number(args, Ut);
            double? inS = Number(args, InS);
            string at = args.Str(At);
            if ((ut.HasValue ? 1 : 0) + (inS.HasValue ? 1 : 0) + (at != null ? 1 : 0) != 1)
                throw BridgeException.BadRequest("give exactly one time: 'ut', 'in_s' or 'at'");
            if (inS.HasValue && !(inS.Value > 0))
                throw BridgeException.BadRequest("'in_s' must be > 0 (got " + Text(inS.Value) + ")");
            if (at != null && at != "apoapsis" && at != "periapsis")
                throw BridgeException.BadRequest("'at' must be apoapsis or periapsis (got '" + at + "')");
            a.Time = ut.HasValue ? NodeTimeKind.Ut : inS.HasValue ? NodeTimeKind.InSeconds
                : at == "apoapsis" ? NodeTimeKind.Apoapsis : NodeTimeKind.Periapsis;
            a.TimeValue = ut ?? inS ?? 0;

            double? prograde = Number(args, Prograde);
            double? normal = Number(args, Normal);
            double? radial = Number(args, Radial);
            bool components = prograde.HasValue || normal.HasValue || radial.HasValue;
            Dictionary<string, object> goal = args.Obj(GoalKey);
            if (components && goal != null)
                throw BridgeException.BadRequest("give either delta-v components or 'goal', not both");
            if (!components && goal == null)
                throw BridgeException.BadRequest("give delta-v components (prograde_mps, normal_mps, radial_mps) or 'goal'");
            if (components)
            {
                a.Goal = NodeGoalKind.Components;
                a.ProgradeMps = prograde ?? 0;
                a.NormalMps = normal ?? 0;
                a.RadialMps = radial ?? 0;
                if (a.ProgradeMps == 0 && a.NormalMps == 0 && a.RadialMps == 0)
                    throw BridgeException.BadRequest("the delta-v components are all 0");
                return a;
            }
            ParseGoal(goal, a);
            return a;
        }

        private static void ParseGoal(Dictionary<string, object> goal, CreateNodeArgs a)
        {
            Known(goal, GoalKeys, "goal.");
            double? ap = Number(goal, ApKey, "goal.");
            double? pe = Number(goal, PeKey, "goal.");
            bool circular = goal.TryGetValue(CircularKey, out object c) && c != null;
            if ((ap.HasValue ? 1 : 0) + (pe.HasValue ? 1 : 0) + (circular ? 1 : 0) != 1)
                throw BridgeException.BadRequest("give exactly one goal: 'apoapsis_m', 'periapsis_m' or 'circular'");
            if (circular)
            {
                if (!goal.Bool(CircularKey))
                    throw BridgeException.BadRequest("'goal.circular' takes only true");
                a.Goal = NodeGoalKind.Circular;
                return;
            }
            a.Goal = ap.HasValue ? NodeGoalKind.Apoapsis : NodeGoalKind.Periapsis;
            a.GoalValue = ap ?? pe.Value;
        }

        public static DeleteNodeArgs Delete(Dictionary<string, object> args)
        {
            args = args ?? new Dictionary<string, object>();
            Known(args, DeleteKeys);
            double? index = Number(args, "index");
            bool all = args.TryGetValue("all", out object v) && v != null;
            if ((index.HasValue ? 1 : 0) + (all ? 1 : 0) != 1)
                throw BridgeException.BadRequest("give exactly one of 'index' and 'all'");
            if (all)
            {
                if (!args.Bool("all"))
                    throw BridgeException.BadRequest("'all' takes only true");
                return new DeleteNodeArgs { All = true };
            }
            if (!(index.Value >= 0) || index.Value != Math.Floor(index.Value) || index.Value > int.MaxValue)
                throw BridgeException.BadRequest("'index' must be an integer >= 0 (got " + Text(index.Value) + ")");
            return new DeleteNodeArgs { Index = (int)index.Value };
        }

        public static ExecuteNodeArgs Execute(Dictionary<string, object> args)
        {
            args = args ?? new Dictionary<string, object>();
            Known(args, ExecuteKeys);
            ExecuteNodeArgs e = new ExecuteNodeArgs();
            double? tolerance = Number(args, "tolerance_mps");
            if (tolerance.HasValue && !(tolerance.Value > 0 && tolerance.Value <= 100))
                throw BridgeException.BadRequest("'tolerance_mps' must be > 0 and <= 100 (got " + Text(tolerance.Value) + ")");
            e.ToleranceMps = tolerance ?? ExecuteNodeArgs.DefaultToleranceMps;
            e.AutoStage = args.Bool("auto_stage", true);
            return e;
        }

        private static void Known(Dictionary<string, object> args, string[] keys, string prefix = "")
        {
            string unknown = args.Keys.FirstOrDefault(k => !keys.Contains(k));
            if (unknown != null)
                throw BridgeException.BadRequest("unknown parameter '" + prefix + unknown + "' (" + string.Join(", ", keys) + ")");
        }

        private static double? Number(Dictionary<string, object> args, string key, string prefix = "")
        {
            double? value;
            try
            {
                value = args.Num(key);
            }
            catch (BridgeException) when (prefix.Length > 0)
            {
                throw BridgeException.BadRequest("'" + prefix + key + "' must be a number");
            }
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                throw BridgeException.BadRequest("'" + prefix + key + "' must be a finite number");
            return value;
        }

        private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
