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
        public StagingOptions StagingOptions = StagingOptions.Default;
        public Condition Abort;
        public List<Step> Steps;
        public uint VesselId;
        public uint ControlPartId;

        /// <summary>The plan JSON (reload handover); rewritten when steps are replaced.</summary>
        public string Raw;

        /// <summary>The parsed plan object behind Raw; its "steps" list is kept in step with Steps.</summary>
        private Dictionary<string, object> _source;

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
            raw.TryGetValue("staging_options", out object so);
            plan.StagingOptions = StagingOptions.Parse(so, "staging_options");
            List<object> steps = raw.TryGetValue("steps", out object s) ? s as List<object> : null;
            if (steps == null || steps.Count == 0)
            {
                throw Invalid("steps", "must be a non-empty array");
            }
            plan.Steps = ParseSteps(steps, "steps", v.mainBody);
            plan._source = raw;
            if (plan.AutoStage)
            {
                CheckControlPart(v, plan.ControlPartId);
            }
            return plan;
        }

        /// <summary>reload: parses and validates the handed-over plan JSON again with this logic's rules, keeping its id.
        /// Throws (400 invalid_plan or any parse error) when it no longer passes.</summary>
        public static Plan Restore(string rawJson, string id, Vessel v)
        {
            Dictionary<string, object> raw = Json.Json.Parse(rawJson) as Dictionary<string, object>
                ?? throw new InvalidOperationException("handed-over plan is not an object");
            Plan plan = Parse(raw, rawJson, v);
            plan.Id = id;
            return plan;
        }

        /// <summary>Every condition leaf of the plan in a fixed order: plan abort, then per step until and abort (reload timers).</summary>
        public List<Leaf> Leaves()
        {
            List<Leaf> l = new List<Leaf>();
            Abort?.CollectLeaves(l);
            foreach (Step s in Steps)
            {
                s.Until?.CollectLeaves(l);
                s.Abort?.CollectLeaves(l);
            }
            return l;
        }

        /// <summary>auto_stage: the control part must stay on the root vessel through every later stage; otherwise KSP would
        /// switch the active vessel to the dropped side after a separation (ModuleDecouple.WaitAndSwitchFocus).</summary>
        private static void CheckControlPart(Vessel v, uint controlPartId)
        {
            for (int n = v.currentStage - 1; n >= 0; n--)
            {
                foreach (Part p in AutoStager.Cut(v, n))
                {
                    if (p.persistentId == controlPartId)
                    {
                        throw Invalid("auto_stage", "control part leaves the root vessel at stage " + n + " (control_part_jettisoned)");
                    }
                }
            }
        }

        /// <summary>Parses a steps array; error paths index this array ("steps[i]..."). body: the vessel's body at submit time.</summary>
        public static List<Step> ParseSteps(List<object> raw, string path, CelestialBody body)
        {
            List<Step> steps = new List<Step>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                steps.Add(Step.Parse(raw[i], path + "[" + i + "]", body));
            }
            return steps;
        }

        /// <summary>A copy whose steps are this plan's first keep steps followed by newSteps (Submit-style trial before replacing).</summary>
        public Plan WithSteps(int keep, List<Step> newSteps)
        {
            Plan copy = (Plan)MemberwiseClone();
            copy.Steps = Combine(Steps, keep, newSteps);
            return copy;
        }

        /// <summary>Replaces the steps from index keep with newSteps and rewrites Raw (reload handover).</summary>
        public void ReplaceSteps(int keep, List<Step> newSteps)
        {
            // Everything is computed first and assigned last, so a failure leaves Steps, _source and Raw as they were.
            List<Step> steps = Combine(Steps, keep, newSteps);
            List<object> rawSteps = new List<object>(steps.Count);
            foreach (Step s in steps)
            {
                rawSteps.Add(s.Source);
            }
            Dictionary<string, object> source = new Dictionary<string, object>(_source) { ["steps"] = rawSteps };
            string raw = Json.Json.Serialize(source);
            Steps = steps;
            _source = source;
            Raw = raw;
        }

        private static List<Step> Combine(List<Step> steps, int keep, List<Step> newSteps)
        {
            List<Step> l = new List<Step>(keep + newSteps.Count);
            l.AddRange(steps.GetRange(0, keep));
            l.AddRange(newSteps);
            return l;
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

            /// <summary>The step object as given (reload handover).</summary>
            public Dictionary<string, object> Source;

            public static Step Parse(object raw, string path, CelestialBody body)
            {
                Dictionary<string, object> o = raw as Dictionary<string, object> ?? throw Invalid(path, "step must be an object");
                Step step = new Step { Source = o };
                step.Mode = o.TryGetValue("mode", out object m) ? m as string : null;
                string[] modeArgs;
                switch (step.Mode)
                {
                    case "attitude":
                        modeArgs = AttitudeMode.Args;
                        step.CreateMode = AttitudeMode.Parse(o, path);
                        break;
                    case "gravity_turn":
                        modeArgs = GravityTurnMode.Args;
                        step.CreateMode = GravityTurnMode.Parse(o, path, body);
                        break;
                    case "coast":
                        modeArgs = CoastMode.Args;
                        step.CreateMode = CoastMode.Parse(o, path, step);
                        break;
                    case "circularize":
                        modeArgs = CircularizeMode.Args;
                        step.CreateMode = CircularizeMode.Parse(o, path);
                        break;
                    case null:
                        throw Invalid(path + ".mode", "required");
                    default:
                        throw Invalid(path + ".mode", "unknown mode " + step.Mode + " (attitude, gravity_turn, coast, circularize)");
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
