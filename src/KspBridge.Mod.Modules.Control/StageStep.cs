using KSP.UI.Screens;
using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Step "stage": activates get_flight's next stage by its number (D015), so a different stage can never fire. Success is judged
    /// from what the game did: it raised onStageActivate and a part of that stage was activated (D010).
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
                Vessel vessel = ControlStep.RequireVessel(context);
                List<StagedPart> staged = new List<StagedPart>();
                foreach (Part part in vessel.parts)
                {
                    if (IsStaged(part))
                        staged.Add(new StagedPart(part.inverseStage, IsActivated(part)));
                }
                int? next = StageRef.Judge(vessel.currentStage, staged).Next;
                Require(StageOutcome.Before(next));

                int stage = next.Value;
                List<Part> parts = vessel.parts.FindAll(p => IsStaged(p) && p.inverseStage == stage && !IsActivated(p));
                bool accepted = Activate(stage);
                // A part that staging destroyed or detached still counts: the list was taken before.
                int activated = parts.FindAll(p => p == null || IsActivated(p)).Count;
                Require(StageOutcome.After(stage, accepted, activated));

                // The staging key also triggers the Stage action group.
                vessel.ActionGroups.ToggleGroup(KSPActionGroup.Stage);
                events.Add("stage_activated", data: new Dictionary<string, object> { ["stage"] = stage });
            }

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input.
            }

            public void End(string reason)
            {
            }

            private static void Require(string failure)
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
            }

            /// <summary>True when the game accepted the staging (it raised onStageActivate during the call).</summary>
            private static bool Activate(int stage)
            {
                StageListener listener = new StageListener();
                GameEvents.onStageActivate.Add(listener.OnStageActivate);
                try
                {
                    StageManager.ActivateStage(stage);
                }
                finally
                {
                    GameEvents.onStageActivate.Remove(listener.OnStageActivate);
                }
                return listener.Raised;
            }

            private static bool IsActivated(Part part) => part.State == PartStates.ACTIVE || part.State == PartStates.DEACTIVATED;

            /// <summary>
            /// Same rule as the flight module's FlightSnapshot.IsStaged (src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs), which is
            /// internal there: the part has a staging icon and staging is on, so a fired separator is not counted.
            /// </summary>
            private static bool IsStaged(Part part)
                => part != null && (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;
        }

        private sealed class StageListener
        {
            public bool Raised { get; private set; }

            public void OnStageActivate(int stage) => Raised = true;
        }
    }
}
