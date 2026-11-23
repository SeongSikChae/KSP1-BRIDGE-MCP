using KspBridge.Mod.Core;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Writes the throttle as the player would and sets it to 0 when a plan ends badly (D001, D005, D006).</summary>
    public static class ThrottleControl
    {
        /// <summary>Key of the plan end action; one per plan, whichever step registers it first.</summary>
        public const string EndKey = "throttle_cut";

        /// <summary>
        /// FlightInputHandler.state is what the game carries from frame to frame (the full/cut throttle keys write it too);
        /// ctrlState is what the vessel uses until the game copies the state over.
        /// </summary>
        public static void Write(Vessel vessel, double value)
        {
            FlightInputHandler.state.mainThrottle = (float)value;
            vessel.ctrlState.mainThrottle = (float)value;
        }

        /// <summary>
        /// Registers the end action of <paramref name="plan"/>: when it ends by a reason ThrottleRule cuts for and the active vessel is
        /// still <paramref name="vessel"/>, the throttle goes to 0 (event throttle_cut with the reason and the value before).
        /// </summary>
        public static void CutAtEnd(IPlanScope plan, Vessel vessel, EventLog events)
            => plan.AtEnd(EndKey, reason => Cut(vessel, reason, events));

        private static void Cut(Vessel vessel, string reason, EventLog events)
        {
            Vessel active = FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            if (!ThrottleRule.ShouldCut(reason, active != null && active == vessel))
                return;
            double from = vessel.ctrlState.mainThrottle;
            Write(vessel, 0);
            events.Add(EndKey, data: new Dictionary<string, object> { ["reason"] = reason, ["from"] = from });
        }
    }
}
