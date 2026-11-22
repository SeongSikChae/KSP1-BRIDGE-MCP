using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Step "action_group": sets or toggles a group with the game's own functions, which also run the part actions bound to it
    /// (D013). Turning SAS on is judged before the change (D009) and first releases a held attitude (D004).
    /// </summary>
    internal sealed class ActionGroupStepType : IStepType
    {
        public const string TypeName = "action_group";

        private readonly EventLog events;

        public ActionGroupStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Set or toggle an action group. params.group (required): \"1\"..\"10\" (or a number), gear, light, brakes, abort, sas, rcs. " +
               "params.state (required): on, off, toggle. Completes on the same frame (event action_group_changed with the state after " +
               "the change). Turning sas on releases a held attitude first. Fails the plan (step_failed) when sas is to be turned on but " +
               "the vessel has no usable SAS, or when there is no active vessel.";

        public IStepRun Create(Dictionary<string, object> parameters) => new GroupRun(events, ControlArgs.ActionGroup(parameters));

        private sealed class GroupRun : IStepRun
        {
            private readonly EventLog events;
            private readonly ActionGroupArgs args;

            public GroupRun(EventLog events, ActionGroupArgs args)
            {
                this.events = events;
                this.args = args;
            }

            public void Begin(StepContext context)
            {
                Vessel vessel = ControlStep.RequireVessel(context);
                ActionGroupList groups = vessel.ActionGroups;
                bool turnsOn = args.Change == GroupChange.On || (args.Change == GroupChange.Toggle && !groups[args.Group]);
                if (args.Group == KSPActionGroup.SAS && turnsOn)
                {
                    // The game would switch the group and turn it off again later, on a rendered frame, so it is asked beforehand.
                    if (!vessel.Autopilot.SAS.CanEngageSAS())
                        throw new InvalidOperationException("the vessel has no usable SAS");
                    context.Plan.ReleaseHold("sas");
                }

                if (args.Change == GroupChange.Toggle)
                    groups.ToggleGroup(args.Group);
                else
                    groups.SetGroup(args.Group, args.Change == GroupChange.On);

                events.Add("action_group_changed", data: new Dictionary<string, object>
                {
                    ["group"] = args.Name,
                    ["state"] = groups[args.Group],
                });
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
