using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>When a plan that set the throttle leaves it at 0 (D005, D006).</summary>
    internal static class ThrottleRule
    {
        /// <summary>
        /// The plan did not complete and its vessel is still there: abort, step failure, timeout, reload, part loss or no thrust. A
        /// scene change, a new flight, a vessel change and a destroyed vessel leave the throttle alone.
        /// </summary>
        public static bool ShouldCut(string endReason, bool sameVessel)
        {
            if (!sameVessel)
                return false;
            switch (endReason)
            {
                case EndReasons.PlannerAbort:
                case EndReasons.StepFailed:
                case EndReasons.Timeout:
                case EndReasons.Reload:
                case EndReasons.PartLoss:
                case EndReasons.NoThrust:
                    return true;
                default:
                    return false;
            }
        }
    }
}
