using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    public enum PlanState
    {
        Running,
        Completed,
        Aborted,
    }

    /// <summary>A validated flight plan for one vessel. Parse rejects anything invalid with 400 invalid_plan "path: reason".</summary>
    public sealed class Plan
    {
        private static readonly string[] PlanKeys = { "auto_stage", "staging_options", "abort", "steps" };
        private static readonly string[] StepKeys = { "name", "mode", "on_start", "until", "abort", "timeout_s" };

        public string Id;
        public PlanState State;
        public bool AutoStage;
        public Dictionary<string, object> StagingOptions;   // validated in unit 8
        public Condition Abort;
        public List<Step> Steps;
        public uint VesselId;
        public uint ControlPartId;

        /// <summary>The plan JSON as submitted (reload handover).</summary>
        public string Raw;

        /// <summary>400 invalid_plan whose message starts with the error path, e.g. "steps[2].until: unknown variable foo".</summary>
        public static ApiException Invalid(string path, string reason) => new ApiException(400, "invalid_plan", path + ": " + reason);

        public static Plan Parse(Dictionary<string, object> raw, string rawJson, Vessel v)
        {
            UnknownKeys(raw, PlanKeys, "plan");
            Plan plan = new Plan
            {
                Id = Guid.NewGuid().ToString("N"),
                Raw = rawJson,
                VesselId = v.persistentId,
                ControlPartId = v.GetReferenceTransformPart()?.persistentId ?? 0,
                AutoStage = OptionalBool(raw, "auto_stage", "plan", false),
                Abort = raw.TryGetValue("abort", out object abort) ? Condition.Parse(abort, "abort") : null,
            };
            if (raw.TryGetValue("staging_options", out object so))
            {
                plan.StagingOptions = so as Dictionary<string, object> ?? throw Invalid("staging_options", "must be an object");
            }
            List<object> steps = raw.TryGetValue("steps", out object s) ? s as List<object> : null;
            if (steps == null || steps.Count == 0)
            {
                throw Invalid("steps", "must be a non-empty array");
            }
            plan.Steps = new List<Step>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                plan.Steps.Add(Step.Parse(steps[i], "steps[" + i + "]"));
            }
            return plan;
        }

        internal static void UnknownKeys(Dictionary<string, object> o, string[] known, string path)
        {
            foreach (string key in o.Keys)
            {
                if (Array.IndexOf(known, key) < 0)
                {
                    throw Invalid(path, "unknown field " + key);
                }
            }
        }

        internal static bool OptionalBool(Dictionary<string, object> o, string name, string path, bool defaultValue)
        {
            if (!o.TryGetValue(name, out object v) || v == null)
            {
                return defaultValue;
            }
            return v is bool b ? b : throw Invalid(path + "." + name, "must be true or false");
        }

        /// <summary>A finite number within [min, max], or defaultValue when absent.</summary>
        internal static double OptionalNumber(Dictionary<string, object> o, string name, string path, double min, double max, double defaultValue)
        {
            if (!o.TryGetValue(name, out object v) || v == null)
            {
                return defaultValue;
            }
            if (!(v is double d) || double.IsNaN(d) || d < min || d > max)
            {
                throw Invalid(path + "." + name, "must be a number in [" + min + ", " + max + "]");
            }
            return d;
        }

        public sealed class Step
        {
            public string Name;
            public string Mode;
            public bool StageOnStart;
            public Condition Until;
            public Condition Abort;
            public double? TimeoutS;
            public Func<IFlightMode> CreateMode;

            public static Step Parse(object raw, string path)
            {
                Dictionary<string, object> o = raw as Dictionary<string, object> ?? throw Invalid(path, "step must be an object");
                Step step = new Step();
                step.Mode = o.TryGetValue("mode", out object m) ? m as string : null;
                string[] modeArgs;
                switch (step.Mode)
                {
                    case "attitude":
                        modeArgs = AttitudeMode.Args;
                        step.CreateMode = AttitudeMode.Parse(o, path);
                        break;
                    case null:
                        throw Invalid(path + ".mode", "required");
                    default:
                        throw Invalid(path + ".mode", "unknown mode " + step.Mode + " (attitude)");
                }
                foreach (string key in o.Keys)
                {
                    if (Array.IndexOf(StepKeys, key) < 0 && Array.IndexOf(modeArgs, key) < 0)
                    {
                        throw Invalid(path, "unknown field " + key + " for mode " + step.Mode);
                    }
                }
                if (o.TryGetValue("name", out object n) && n != null)
                {
                    step.Name = n as string ?? throw Invalid(path + ".name", "must be a string");
                }
                if (o.TryGetValue("on_start", out object os) && os != null)
                {
                    List<object> actions = os as List<object> ?? throw Invalid(path + ".on_start", "must be an array");
                    for (int i = 0; i < actions.Count; i++)
                    {
                        if (!(actions[i] is string a) || a != "stage")
                        {
                            throw Invalid(path + ".on_start[" + i + "]", "unknown action (stage)");
                        }
                        step.StageOnStart = true;
                    }
                }
                if (o.TryGetValue("until", out object until))
                {
                    step.Until = Condition.Parse(until, path + ".until");
                }
                if (o.TryGetValue("abort", out object abort))
                {
                    step.Abort = Condition.Parse(abort, path + ".abort");
                }
                if (o.ContainsKey("timeout_s"))
                {
                    double t = OptionalNumber(o, "timeout_s", path, 0, double.MaxValue, 0);
                    if (t <= 0)
                    {
                        throw Invalid(path + ".timeout_s", "must be greater than 0");
                    }
                    step.TimeoutS = t;
                }
                return step;
            }
        }
    }
}
