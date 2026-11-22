using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>
    /// Ends the plan with no_thrust when the plan asked for it: off by default, because a stage that burns out is normal in a plan
    /// that stages afterwards (D002).
    /// </summary>
    internal sealed class NoThrustGuard : IPlanGuard
    {
        private static readonly GuardOption[] Keys = { new GuardOption(EndReasons.NoThrust, false) };

        public IReadOnlyList<GuardOption> Options => Keys;

        public int Priority => 1;

        public void Start(int planId)
        {
        }

        public GuardTrip Check(Func<string, bool> enabled)
        {
            Vessel vessel = enabled(EndReasons.NoThrust) ? FlightGlobals.ActiveVessel : null;
            if (vessel == null)
                return null;
            EngineState state = EngineReader.Read(vessel);
            return NoThrustRule.Judge(state)
                ? new GuardTrip(EndReasons.NoThrust, "throttle is up but every engine is out (" + state.FlamedOut + " flamed out, none running)")
                : null;
        }

        public void Stop()
        {
        }
    }
}
