using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>Reads <see cref="EngineState"/> from a vessel (KSP side). A multi-mode engine has one module per mode; both count.</summary>
    internal static class EngineReader
    {
        public static EngineState Read(Vessel vessel)
        {
            EngineState state = new EngineState { Throttle = vessel.ctrlState != null ? vessel.ctrlState.mainThrottle : 0 };
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null)
                    continue;
                PartModuleList modules = part.Modules;
                for (int j = 0; j < modules.Count; j++)
                {
                    if (!(modules[j] is ModuleEngines engine))
                        continue;
                    if (engine.flameout)
                        state.FlamedOut++;
                    if (engine.isOperational)
                        state.Operational++;
                }
            }
            return state;
        }
    }
}
