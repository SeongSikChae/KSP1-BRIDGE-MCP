using KspBridge.Mod.Core;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Step "throttle": sets the game's throttle as if the player had, so it stays in the later steps and after the plan completes
    /// (D001). When the plan ends otherwise, the throttle goes to 0 (ThrottleControl, ThrottleRule).
    /// </summary>
    internal sealed class ThrottleStepType : IStepType
    {
        public const string TypeName = "throttle";

        private readonly EventLog events;

        public ThrottleStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Set the throttle to params.value (0..1, required) as if the player had. Completes on the same frame. The value stays in " +
               "the later steps and after the plan completes; when the plan ends by planner_abort, step_failed, timeout, reload, " +
               "part_loss or no_thrust the throttle is set to 0 (event throttle_cut). On rails in a warp step it reads 0 and warp puts it " +
               "back when it completes; if the plan ends before that it may stay 0. An ascend or circularize step sets the throttle " +
               "itself while it runs and leaves it at 0 when it completes, so a value set before it does not carry over. Fails the plan " +
               "(step_failed) when there is no active vessel or the vessel is not controllable (no working control source: no crew, " +
               "power or signal); the failing step changes nothing.";

        public IStepRun Create(Dictionary<string, object> parameters) => new ThrottleRun(events, ControlArgs.Throttle(parameters));

        private sealed class ThrottleRun : IStepRun
        {
            private readonly EventLog events;
            private readonly double value;

            public ThrottleRun(EventLog events, double value)
            {
                this.events = events;
                this.value = value;
            }

            public void Begin(StepContext context)
            {
                Vessel vessel = ControlStep.RequireControllable(context);
                ThrottleControl.CutAtEnd(context.Plan, vessel, events);
                ThrottleControl.Write(vessel, value);
            }

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input: the throttle is the game's own value.
            }

            public void End(string reason)
            {
            }
        }
    }
}
