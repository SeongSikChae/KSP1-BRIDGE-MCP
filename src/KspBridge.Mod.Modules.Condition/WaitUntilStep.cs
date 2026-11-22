using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>Step "wait_until": completes on the physics frame its conditions hold and gives no control input (D001).</summary>
    internal sealed class WaitUntilStepType : IStepType
    {
        public const string TypeName = "wait_until";

        private readonly EventLog events;

        public WaitUntilStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Wait until conditions on the active vessel hold. Give params.all or params.any (exactly one): a non-empty list of " +
               "{\"value\", \"op\", \"to\"}; all = every condition, any = at least one, judged together every physics frame. " +
               "Values compared with >= or <= to a number (same values as get_flight): " +
               string.Join(", ", ConditionValues.All.Where(v => v.Kind == ConditionKind.Number)
                   .Select(v => v.Name + (v.Unit == null ? "" : " (" + v.Unit + ")"))) +
               ". Values compared with ==: situation (" + string.Join(", ", ConditionValues.Situations) + "), no_thrust (true/false: the " +
               "throttle is above 0, an engine has flamed out and none is running; false before ignition, after the spent stage is " +
               "dropped and at zero throttle). A value that does not exist (apoapsis of an open orbit) never holds. Completes on the " +
               "frame the conditions hold, also when they hold already (event condition_met with every value). Gives no control input: " +
               "the throttle and a held attitude stay. For 'condition or N seconds' put elapsed_s into any; timeout_s instead ends the " +
               "plan with timeout, and wins when both happen on the same frame. Fails the plan (step_failed) when there is no active vessel.";

        public IStepRun Create(Dictionary<string, object> parameters) => new WaitUntilRun(events, ConditionArgs.Parse(parameters));

        private sealed class WaitUntilRun : IStepRun, IStepProgress
        {
            private readonly EventLog events;
            private readonly ConditionSet conditions;

            public WaitUntilRun(EventLog events, ConditionSet conditions)
            {
                this.events = events;
                this.conditions = conditions;
            }

            public void Begin(StepContext context)
            {
            }

            public StepResult Judge(StepContext context)
            {
                if (context.Vessel == null)
                    return StepResult.Failed("there is no active vessel");
                ConditionOutcome outcome = conditions.Judge(Read(context));
                if (!outcome.Met)
                    return StepResult.Running;
                outcome.Summary["index"] = context.StepIndex;
                events.Add("condition_met", context.PlanId, outcome.Summary);
                return StepResult.Done;
            }

            /// <summary>The conditions with their values now.</summary>
            public Dictionary<string, object> Progress(StepContext context)
                => context.Vessel == null ? null : conditions.Judge(Read(context)).Summary;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input: the throttle is the game's own value and a held attitude is the plan's hold.
            }

            public void End(string reason)
            {
            }

            private ConditionReading Read(StepContext context) => new ConditionReading
            {
                Flight = conditions.UsesFlight
                    ? FlightReport.Build(FlightSnapshot.TakeBasic(context.Vessel), FlightInclude.None)
                    : null,
                ElapsedS = context.Elapsed,
                NoThrust = conditions.UsesNoThrust && NoThrustRule.Judge(EngineReader.Read(context.Vessel)),
            };
        }
    }
}
