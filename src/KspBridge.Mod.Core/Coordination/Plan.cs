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

    internal sealed class Plan
    {
        public int Id;
        public int SessionId;
        public List<PlanStep> Steps = new List<PlanStep>();
        public string State = "pending";   // pending, running, completed, aborted
        public int StepIndex;
        public string EndReason;
        public string EndDetail;
        public double SubmittedUT;
        public double? EndedUT;

        public PlanStep CurrentStep => StepIndex < Steps.Count ? Steps[StepIndex] : null;

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
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
    }
}
