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

        /// <summary>The running plan without progress values (get_status reads it; it never calls a step's progress report).</summary>
        public Dictionary<string, object> CurrentSummary => current?.ToDict();

        /// <summary>The running plan with progress values at the current UT (get_plan current).</summary>
        public Dictionary<string, object> CurrentSummaryWithProgress() => current?.ToDict(SafeClock());

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

            PlanScope scope = new PlanScope(this, plan);
            foreach (PlanStep step in plan.Steps)
                step.Context.Plan = scope;

            plan.Id = NextPlanId++;
            current = plan;
            controlFailure = null;
            events.Add("plan_submitted", plan.Id, new Dictionary<string, object> { ["steps"] = plan.Steps.Count });
            PlanStarted?.Invoke();
            return plan.ToDict(SafeClock());
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
                if (controlFailure != null)
                {
                    // Also before Begin: a hold can fail between steps, and the next step must not act on the vessel then.
                    FailStep(step, "control error: " + controlFailure);
                    return;
                }

                if (!step.Begun)
                {
                    step.Begun = true;
                    step.State = "running";
                    step.StartUT = ut;
                    step.Context.StartUT = ut;
                    plan.State = "running";
                    events.Add("step_started", plan.Id, new Dictionary<string, object> { ["index"] = plan.StepIndex, ["type"] = step.Type });
                    step.Run.Begin(step.Context);
                    if (current != plan)
                        return;   // Begin raised a game event that ended the plan (e.g. staging changed the active vessel)
                }

                if (step.TimeoutS.HasValue && step.Context.Elapsed > step.TimeoutS.Value)
                {
                    step.Message = "timed out after " + step.TimeoutS.Value.ToString(CultureInfo.InvariantCulture) + " s";
                    Finish(EndReasons.Timeout, "step " + plan.StepIndex + " (" + step.Type + ") " + step.Message);
                    return;
                }

                StepResult result = step.Run.Judge(step.Context);
                if (current != plan)
                    return;
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

        /// <summary>
        /// Called from the vessel's OnFlyByWire: the plan's hold writes first (also between steps), then the running step, so a
        /// step that writes an axis itself wins.
        /// </summary>
        public void Control(FlightCtrlState state)
        {
            Plan plan = current;
            if (plan == null || controlFailure != null)
                return;
            try
            {
                plan.Hold?.Control(state, vessel());
                PlanStep step = plan.CurrentStep;
                if (step != null && step.Begun)
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

            ReleaseHold(plan, reason);
            RunEndActions(plan, reason);

            Dictionary<string, object> summary = plan.ToDict();
            LastSummary = summary;
            LastSession = plan.SessionId;
            events.Add(reason == EndReasons.Completed ? "plan_completed" : "plan_aborted", plan.Id,
                new Dictionary<string, object> { ["end_reason"] = reason, ["detail"] = detail });
            PlanEnded?.Invoke(reason);
            return summary;
        }

        private bool ReleaseHold(Plan plan, string reason)
        {
            IPlanHold hold = plan.Hold;
            if (hold == null)
                return false;
            plan.Hold = null;
            events.Add("hold_released", plan.Id, new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["hold"] = PlanProgress.DescribeHold(hold, out _),
            });
            return true;
        }

        private void RunEndActions(Plan plan, string reason)
        {
            foreach (PlanEndAction end in plan.EndActions)
            {
                try
                {
                    end.Action(reason);
                }
                catch (Exception ex)
                {
                    events.Add("plan_end_error", plan.Id, new Dictionary<string, object>
                    {
                        ["key"] = end.Key,
                        ["message"] = ex.GetType().Name + ": " + ex.Message,
                    });
                }
            }
        }

        /// <summary>The hold and end actions of one plan; unusable once the plan has ended.</summary>
        private sealed class PlanScope : IPlanScope
        {
            private readonly Coordinator coordinator;
            private readonly Plan plan;

            public PlanScope(Coordinator coordinator, Plan plan)
            {
                this.coordinator = coordinator;
                this.plan = plan;
            }

            public IPlanHold Hold => plan.Hold;

            public void SetHold(IPlanHold hold, string reason)
            {
                if (hold == null)
                    throw new ArgumentNullException(nameof(hold));
                RequireRunning();
                coordinator.ReleaseHold(plan, reason);
                plan.Hold = hold;
            }

            public bool ReleaseHold(string reason)
            {
                RequireRunning();
                return coordinator.ReleaseHold(plan, reason);
            }

            public void AtEnd(string key, Action<string> action)
            {
                RequireRunning();
                if (!plan.EndActions.Exists(a => a.Key == key))
                    plan.EndActions.Add(new PlanEndAction { Key = key, Action = action });
            }

            private void RequireRunning()
            {
                if (coordinator.current != plan)
                    throw new InvalidOperationException("the plan has ended");
            }
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
