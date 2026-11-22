using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>Time warp feature: the step type warp.</summary>
    public sealed class WarpModule : IBridgeModule
    {
        public string Name => "warp";

        public void Register(ModuleContext context) => context.AddStepType(new WarpStepType(context.Events));
    }
}
