using KspBridge.Mod.Core;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Step "throttle": sets the game's throttle as if the player had, so it stays in the later steps and after the plan completes
    /// (D001). When the plan ends otherwise, the throttle goes to 0 (ThrottleRule).
    /// </summary>
    internal sealed class ThrottleStepType : IStepType
    {
        public const string TypeName = "throttle";

        private const string EndKey = "throttle_cut";

        private readonly EventLog events;

        public ThrottleStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Set the throttle to params.value (0..1, required) as if the player had. Completes on the same frame. The value stays in " +
               "the later steps and after the plan completes; when the plan ends by planner_abort, step_failed, timeout or reload the " +
               "throttle is set to 0 (event throttle_cut). Fails the plan (step_failed) when there is no active vessel or the vessel is " +
               "not controllable (no working control source: no crew, power or signal); the failing step changes nothing.";

        public IStepRun Create(Dictionary<string, object> parameters) => new ThrottleRun(events, ControlArgs.Throttle(parameters));

        /// <summary>
        /// FlightInputHandler.state is what the game carries from frame to frame (the full/cut throttle keys write it too);
        /// ctrlState is what the vessel uses until the game copies the state over.
        /// </summary>
        private static void Write(Vessel vessel, double value)
        {
            FlightInputHandler.state.mainThrottle = (float)value;
            vessel.ctrlState.mainThrottle = (float)value;
        }

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
                context.Plan.AtEnd(EndKey, reason => Cut(vessel, reason));
                Write(vessel, value);
            }

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input: the throttle is the game's own value.
            }

            public void End(string reason)
            {
            }

            private void Cut(Vessel vessel, string reason)
            {
                Vessel active = FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
                if (!ThrottleRule.ShouldCut(reason, active != null && active == vessel))
                    return;
                double from = vessel.ctrlState.mainThrottle;
                Write(vessel, 0);
                events.Add(EndKey, data: new Dictionary<string, object> { ["reason"] = reason, ["from"] = from });
            }
        }
    }
}
