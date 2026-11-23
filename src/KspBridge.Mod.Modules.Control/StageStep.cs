using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Step "stage": activates get_flight's next stage by its number (D015) through <see cref="StageActivator"/>, which judges
    /// success from what the game did (D010).
    /// </summary>
    internal sealed class StageStepType : IStepType
    {
        public const string TypeName = "stage";

        private readonly EventLog events;

        public StageStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Activate the next stage (get_flight stage.next). No params. Completes on the same frame (event stage_activated). Fails " +
               "the plan (step_failed) when no stage is left, when the game refuses staging (it cools down for about 0.6 s after each " +
               "stage, so put a wait between two stage steps), when no part of the stage was activated, or when there is no active vessel.";

        public IStepRun Create(Dictionary<string, object> parameters) => new StageRun(events);

        private sealed class StageRun : IStepRun
        {
            private readonly EventLog events;

            public StageRun(EventLog events) => this.events = events;

            public void Begin(StepContext context)
            {
                StageAttempt attempt = StageActivator.ActivateNext(ControlStep.RequireVessel(context));
                if (attempt.Failure != null)
                    throw new InvalidOperationException(attempt.Failure);
                events.Add("stage_activated", data: new Dictionary<string, object> { ["stage"] = attempt.Stage.Value });
            }

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input.
            }

            public void End(string reason)
            {
            }
        }
    }
}
