using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>Condition feature: the step type wait_until and the plan guards that end a plan on losses or without thrust.</summary>
    public sealed class ConditionModule : IBridgeModule
    {
        public string Name => "condition";

        public void Register(ModuleContext context)
        {
            context.AddStepType(new WaitUntilStepType(context.Events));
            context.AddGuard(new LossGuard(context.Subscriptions, context.Events));
            context.AddGuard(new NoThrustGuard());
        }
    }
}
