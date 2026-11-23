using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Ascent to orbit: the step types ascend and circularize with automatic staging. No requests of its own.</summary>
    public sealed class AscentModule : IBridgeModule
    {
        public string Name => "ascent";

        public void Register(ModuleContext context)
        {
            context.AddStepType(new AscendStepType(context.Events));
            context.AddStepType(new CircularizeStepType(context.Events));
        }
    }
}
