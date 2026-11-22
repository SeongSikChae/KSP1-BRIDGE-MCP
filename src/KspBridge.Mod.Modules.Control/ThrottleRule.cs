using KspBridge.Mod.Core;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>When a plan that set the throttle leaves it at 0 (D005, D006).</summary>
    internal static class ThrottleRule
    {
        /// <summary>
        /// The plan did not complete and its vessel is still there: abort, step failure, timeout or reload. A scene change, a new
        /// flight and a vessel change leave the throttle alone.
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
                    return true;
                default:
                    return false;
            }
        }
    }
}
