using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Basic control: the plan steps throttle, attitude, attitude_off, stage and action_group. No requests of its own.</summary>
    public sealed class ControlModule : IBridgeModule
    {
        public string Name => "control";

        public void Register(ModuleContext context)
        {
            context.AddStepType(new ThrottleStepType(context.Events));
            context.AddStepType(new AttitudeStepType());
            context.AddStepType(new AttitudeOffStepType());
            context.AddStepType(new StageStepType(context.Events));
            context.AddStepType(new ActionGroupStepType(context.Events));
        }
    }
}
