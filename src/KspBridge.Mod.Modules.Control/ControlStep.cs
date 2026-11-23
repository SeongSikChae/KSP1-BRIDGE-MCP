using KspBridge.Mod.Core;
using System;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>The checks a control step makes before it changes anything; other modules' steps use the same ones.</summary>
    public static class ControlStep
    {
        /// <summary>The active vessel; an exception (the step fails) when there is none.</summary>
        public static Vessel RequireVessel(StepContext context)
        {
            if (context.Vessel == null)
                throw new InvalidOperationException("there is no active vessel");
            return context.Vessel;
        }

        /// <summary>
        /// The active vessel when the game lets it be controlled (D022, D023); an exception (the step fails) otherwise. Partial
        /// control counts as controllable. Checked before a step changes anything.
        /// </summary>
        public static Vessel RequireControllable(StepContext context)
        {
            Vessel vessel = RequireVessel(context);
            if (!vessel.IsControllable)
                throw new InvalidOperationException("the vessel is not controllable");
            return vessel;
        }
    }
}
