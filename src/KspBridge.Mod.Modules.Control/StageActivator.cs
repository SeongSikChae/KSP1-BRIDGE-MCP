using KSP.UI.Screens;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>What one try to activate the next stage did.</summary>
    public sealed class StageAttempt
    {
        /// <summary>The stage that was activated; null when nothing was activated.</summary>
        public int? Stage { get; internal set; }

        /// <summary>The game refused the staging (cool-down and the like); trying again later may work.</summary>
        public bool Refused { get; internal set; }

        /// <summary>Why nothing usable happened (no stage left, refused, no part activated); null on success.</summary>
        public string Failure { get; internal set; }
    }

    /// <summary>
    /// Activates get_flight's next stage by its number (control D015), so a different stage can never fire. Success is judged from
    /// what the game did: it raised onStageActivate and a part of that stage was activated (control D010). Leaves events to the
    /// caller.
    /// </summary>
    public static class StageActivator
    {
        /// <summary>The next stage (get_flight stage.next), or null when no stage is left.</summary>
        public static int? NextStage(Vessel vessel)
        {
            List<StagedPart> staged = new List<StagedPart>();
            foreach (Part part in vessel.parts)
            {
                if (IsStaged(part))
                    staged.Add(new StagedPart(part.inverseStage, IsActivated(part)));
            }
            return StageRef.Judge(vessel.currentStage, staged).Next;
        }

        public static StageAttempt ActivateNext(Vessel vessel)
        {
            int? next = NextStage(vessel);
            string before = StageOutcome.Before(next);
            if (before != null)
                return new StageAttempt { Failure = before };

            int stage = next.Value;
            List<Part> parts = vessel.parts.FindAll(p => IsStaged(p) && p.inverseStage == stage && !IsActivated(p));
            bool accepted = Activate(stage);
            // A part that staging destroyed or detached still counts: the list was taken before.
            int activated = parts.FindAll(p => p == null || IsActivated(p)).Count;
            string after = StageOutcome.After(stage, accepted, activated);
            if (after != null)
                return new StageAttempt { Failure = after, Refused = !accepted };

            // The staging key also triggers the Stage action group.
            vessel.ActionGroups.ToggleGroup(KSPActionGroup.Stage);
            return new StageAttempt { Stage = stage };
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

        private sealed class StageListener
        {
            public bool Raised { get; private set; }

            public void OnStageActivate(int stage) => Raised = true;
        }
    }
}
