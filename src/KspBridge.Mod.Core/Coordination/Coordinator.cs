using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Core
{
    /// <summary>Plan operations modules may use.</summary>
    public interface ICoordinator
    {
        bool Running { get; }

        /// <summary>Validates and starts a plan ({"steps":[{"type","params","timeout_s"}]}). Returns the plan summary.</summary>
        Dictionary<string, object> Submit(Dictionary<string, object> plan, bool replace);

        /// <summary>Ends the running plan with <paramref name="reason"/>. Returns its summary, or null when no plan runs.</summary>
        Dictionary<string, object> Abort(string reason, string detail);
    }

    /// <summary>
    /// Runs a submitted plan step by step on physics frames: begins the step, applies its timeout, asks it to judge
    /// itself, and moves on or ends the plan. Keeps running regardless of the Planner (LLM) being slow or gone.
    /// KSP-independent except through the injected clock/vessel/readiness, so the state machine is unit-tested.
    /// </summary>
    public sealed class Coordinator : ICoordinator
    {
        private readonly StepRegistry registry;
        private readonly EventLog events;
        private readonly Func<double> clock;
        private readonly Func<Vessel> vessel;
        private readonly Func<string> blockReason;
        private readonly Func<int> sessionId;
        private Plan current;
        private string controlFailure;

        public Coordinator(StepRegistry registry, EventLog events, Func<double> clock, Func<Vessel> vessel,
            Func<string> blockReason, Func<int> sessionId)
        {
            this.registry = registry;
            this.events = events;
            this.clock = clock;
            this.vessel = vessel;
            this.blockReason = blockReason;
            this.sessionId = sessionId;
        }

        /// <summary>Raised when a plan is accepted (the Controller attaches).</summary>
        public event Action PlanStarted;

        /// <summary>Raised when a plan ends, with the end reason (the Controller detaches).</summary>
        public event Action<string> PlanEnded;

        public int NextPlanId { get; set; } = 1;

        public bool Running => current != null;

        /// <summary>Summary of the last ended plan and the flight session it ran in.</summary>
        public Dictionary<string, object> LastSummary { get; set; }
        public int LastSession { get; set; } = -1;

        public Dictionary<string, object> CurrentSummary => current?.ToDict();

        public Dictionary<string, object> Submit(Dictionary<string, object> body, bool replace)
        {
            string blocked = blockReason();
            if (blocked != null)
                throw BridgeException.NotReady(blocked);

            List<object> steps = body.Arr("steps", required: true);
            if (steps.Count == 0)
                throw BridgeException.BadRequest("'steps' must not be empty");

            Plan plan = new Plan { SessionId = sessionId(), SubmittedUT = clock() };
            for (int i = 0; i < steps.Count; i++)
            {
                if (!(steps[i] is Dictionary<string, object> step))
                    throw BridgeException.BadRequest("steps[" + i + "] must be an object");
                string type = step.Str("type", required: true);
                IStepType stepType = registry.Find(type)
                    ?? throw BridgeException.BadRequest("steps[" + i + "]: unknown step type '" + type + "' (see list_step_types)");
                Dictionary<string, object> parameters = step.Obj("params") ?? new Dictionary<string, object>();
                double? timeout = step.Num("timeout_s");
                if (timeout.HasValue && !(timeout.Value > 0))
                    throw BridgeException.BadRequest("steps[" + i + "].timeout_s must be > 0");
                IStepRun run;
                try
                {
                    run = stepType.Create(parameters);
                }
                catch (BridgeException ex)
                {
                    throw new BridgeException(ex.Code, "steps[" + i + "] (" + type + "): " + ex.Message);
                }
                plan.Steps.Add(new PlanStep
                {
                    Type = type,
                    Parameters = parameters,
                    TimeoutS = timeout,
                    Run = run,
                    Context = new StepContext(parameters),
                });
            }

            if (current != null)
            {
                if (!replace)
                    throw BridgeException.BadRequest("plan " + current.Id + " is running; abort it or submit with replace=true");
                Finish(EndReasons.PlannerAbort, "replaced by a new plan");
            }

            plan.Id = NextPlanId++;
            current = plan;
            controlFailure = null;
            events.Add("plan_submitted", plan.Id, new Dictionary<string, object> { ["steps"] = plan.Steps.Count });
            PlanStarted?.Invoke();
            return plan.ToDict();
        }

        public Dictionary<string, object> Abort(string reason, string detail)
        {
            if (current == null)
                return null;
            Dictionary<string, object> summary = Finish(reason, detail);
            return summary;
        }

        /// <summary>One physics frame.</summary>
        public void FixedUpdate()
        {
            if (current == null)
                return;
            Plan plan = current;
            PlanStep step = plan.CurrentStep;
            double ut = clock();
            step.Context.UT = ut;
            step.Context.Vessel = vessel();

            try
            {
                if (!step.Begun)
                {
                    step.Begun = true;
                    step.State = "running";
                    step.StartUT = ut;
                    step.Context.StartUT = ut;
                    plan.State = "running";
                    events.Add("step_started", plan.Id, new Dictionary<string, object> { ["index"] = plan.StepIndex, ["type"] = step.Type });
                    step.Run.Begin(step.Context);
                }

                if (controlFailure != null)
                {
                    FailStep(step, "control error: " + controlFailure);
                    return;
                }

                if (step.TimeoutS.HasValue && step.Context.Elapsed > step.TimeoutS.Value)
                {
                    step.Message = "timed out after " + step.TimeoutS.Value.ToString(CultureInfo.InvariantCulture) + " s";
                    Finish(EndReasons.Timeout, "step " + plan.StepIndex + " (" + step.Type + ") " + step.Message);
                    return;
                }

                StepResult result = step.Run.Judge(step.Context);
                if (result.Status == StepStatus.Done)
                    CompleteStep(plan, step, ut);
                else if (result.Status == StepStatus.Failed)
                    FailStep(step, result.Message);
            }
            catch (Exception ex)
            {
                if (current == plan)
                    FailStep(step, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Called from the vessel's OnFlyByWire: the running step controls the vessel.</summary>
        public void Control(FlightCtrlState state)
        {
            PlanStep step = current?.CurrentStep;
            if (step == null || !step.Begun || controlFailure != null)
                return;
            try
            {
                step.Run.Control(state, step.Context);
            }
            catch (Exception ex)
            {
                // Judged on the next physics frame; never throw into KSP's input pipeline.
                controlFailure = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void CompleteStep(Plan plan, PlanStep step, double ut)
        {
            step.State = "done";
            step.EndUT = ut;
            SafeEnd(step, EndReasons.Completed);
            events.Add("step_completed", plan.Id, new Dictionary<string, object> { ["index"] = plan.StepIndex, ["type"] = step.Type });
            plan.StepIndex++;
            if (plan.StepIndex >= plan.Steps.Count)
                Finish(EndReasons.Completed, null);
        }

        private void FailStep(PlanStep step, string message)
        {
            step.Message = message;
            Finish(EndReasons.StepFailed, "step " + current.StepIndex + " (" + step.Type + "): " + message);
        }

        private Dictionary<string, object> Finish(string reason, string detail)
        {
            Plan plan = current;
            double ut = SafeClock();
            PlanStep step = plan.CurrentStep;
            if (step != null && step.Begun && step.State == "running")
            {
                step.State = reason == EndReasons.StepFailed ? "failed" : "aborted";
                step.EndUT = ut;
                SafeEnd(step, reason);
            }
            plan.State = reason == EndReasons.Completed ? "completed" : "aborted";
            plan.EndReason = reason;
            plan.EndDetail = detail;
            plan.EndedUT = ut;
            current = null;
            controlFailure = null;

            Dictionary<string, object> summary = plan.ToDict();
            LastSummary = summary;
            LastSession = plan.SessionId;
            events.Add(reason == EndReasons.Completed ? "plan_completed" : "plan_aborted", plan.Id,
                new Dictionary<string, object> { ["end_reason"] = reason, ["detail"] = detail });
            PlanEnded?.Invoke(reason);
            return summary;
        }

        private static void SafeEnd(PlanStep step, string reason)
        {
            try
            {
                step.Run.End(reason);
            }
            catch (Exception ex)
            {
                step.Message = (step.Message != null ? step.Message + "; " : "") + "end error: " + ex.Message;
            }
        }

        private double SafeClock()
        {
            try
            {
                return clock();
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
