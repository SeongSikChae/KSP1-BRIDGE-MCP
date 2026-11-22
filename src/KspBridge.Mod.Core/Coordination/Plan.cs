using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Core
{
    /// <summary>Plan end reasons (plan.end_reason).</summary>
    public static class EndReasons
    {
        public const string Completed = "completed";
        public const string PlannerAbort = "planner_abort";
        public const string StepFailed = "step_failed";
        public const string Timeout = "timeout";
        public const string Reload = "reload";
        public const string VesselChange = "vessel_change";
        public const string SceneChange = "scene_change";
        public const string NewFlight = "new_flight";
    }

    internal sealed class PlanStep
    {
        public string Type;
        public Dictionary<string, object> Parameters;
        public double? TimeoutS;
        public IStepRun Run;
        public StepContext Context;
        public string State = "pending";   // pending, running, done, failed, aborted
        public bool Begun;
        public double? StartUT;
        public double? EndUT;
        public string Message;

        public Dictionary<string, object> ToDict(int index) => new Dictionary<string, object>
        {
            ["index"] = index,
            ["type"] = Type,
            ["params"] = Parameters,
            ["timeout_s"] = TimeoutS,
            ["state"] = State,
            ["start_ut"] = StartUT,
            ["end_ut"] = EndUT,
            ["message"] = Message,
        };
    }

    /// <summary>An action a step registered for the end of the plan (IPlanScope.AtEnd).</summary>
    internal sealed class PlanEndAction
    {
        public string Key;
        public Action<string> Action;
    }

    internal sealed class Plan
    {
        public int Id;
        public int SessionId;
        public List<PlanStep> Steps = new List<PlanStep>();
        public IPlanHold Hold;
        public List<PlanEndAction> EndActions = new List<PlanEndAction>();
        public string State = "pending";   // pending, running, completed, aborted
        public int StepIndex;
        public string EndReason;
        public string EndDetail;
        public double SubmittedUT;
        public double? EndedUT;

        public PlanStep CurrentStep => StepIndex < Steps.Count ? Steps[StepIndex] : null;

        /// <summary>Summary without progress values (ended plans, get_status).</summary>
        public Dictionary<string, object> ToDict() => ToDict(null);

        /// <summary>Summary; with <paramref name="now"/> (UT) the running plan's progress values are added.</summary>
        public Dictionary<string, object> ToDict(double? now)
        {
            Dictionary<string, object> summary = new Dictionary<string, object>
            {
                ["id"] = Id,
                ["state"] = State,
                ["step_index"] = StepIndex,
                ["step_type"] = CurrentStep?.Type,
                ["step_count"] = Steps.Count,
                ["end_reason"] = EndReason,
                ["end_detail"] = EndDetail,
                ["submitted_ut"] = SubmittedUT,
                ["ended_ut"] = EndedUT,
                ["steps"] = Steps.Select((s, i) => (object)s.ToDict(i)).ToList(),
            };
            if (now.HasValue && EndReason == null)
                PlanProgress.Add(this, summary, now.Value);
            return summary;
        }
    }

    /// <summary>
    /// Progress values of a running plan (get_plan current, submit responses). Step values are null while the current step has
    /// not begun (right after submit, and between steps until the next physics frame). A step's progress report never affects
    /// the plan: an exception is shown as step_progress_error (hold_error for the hold).
    /// </summary>
    internal static class PlanProgress
    {
        public static void Add(Plan plan, Dictionary<string, object> summary, double now)
        {
            summary["elapsed_s"] = now - plan.SubmittedUT;
            PlanStep step = plan.CurrentStep;
            bool begun = step != null && step.Begun && step.State == "running" && step.StartUT.HasValue;
            double? stepElapsed = begun ? now - step.StartUT.Value : (double?)null;
            summary["step_elapsed_s"] = stepElapsed;
            summary["step_timeout_remaining_s"] = begun && step.TimeoutS.HasValue
                ? Math.Max(0, step.TimeoutS.Value - stepElapsed.Value)
                : (double?)null;

            object progress = null;
            string error = null;
            if (begun && step.Run is IStepProgress reporter)
            {
                try
                {
                    step.Context.UT = now;
                    progress = reporter.Progress(step.Context);
                }
                catch (Exception ex)
                {
                    progress = null;
                    error = ex.GetType().Name + ": " + ex.Message;
                }
            }
            summary["step_progress"] = progress;
            summary["step_progress_error"] = error;

            summary["hold"] = DescribeHold(plan.Hold, out string holdError);
            summary["hold_error"] = holdError;
        }

        /// <summary>What <paramref name="hold"/> reports; null with the error when it throws.</summary>
        public static Dictionary<string, object> DescribeHold(IPlanHold hold, out string error)
        {
            error = null;
            try
            {
                return hold?.Describe();
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
