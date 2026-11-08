using System;
using System.Collections.Generic;
using KSP.UI.Screens;

namespace KspBridge.Handlers
{
    public static class UiHandler
    {
        /// <summary>POST /api/ui/launch — maneuver mode, staging stack delta-v info, resources app.</summary>
        public static object SetupLaunch()
        {
            Guard.ActiveVessel();
            if (FlightUIModeController.Instance == null)
            {
                throw ApiException.SceneNotAllowed("launch UI setup (flight scene only)");
            }
            return new Dictionary<string, object>
            {
                ["maneuver_mode"] = Step(ManeuverMode),
                ["deltav_info"] = Step(DeltaVInfo),
                ["resources"] = Step(Resources),
            };
        }

        private static void ManeuverMode()
        {
            FlightUIModeController c = FlightUIModeController.Instance;
            c.SetMode(FlightUIMode.MANEUVER_INFO);
            if (c.Mode != FlightUIMode.MANEUVER_INFO)
            {
                throw new InvalidOperationException("the game refused maneuver mode (mode " + c.Mode + ")");
            }
        }

        /// <summary>Total, headings and per-stage info panels; the staging quadrant itself is left alone (expanding it covers the maneuver panel).</summary>
        private static void DeltaVInfo()
        {
            StageManager s = StageManager.Instance;
            s.EnableDeltaVTotal(true);
            s.EnableDeltaVHeadings(true);
            s.EnableInfoPanels(true);
            s.ToggleInfoPanels(true);
        }

        private static void Resources()
        {
            ResourceDisplay r = ResourceDisplay.Instance;
            if (r == null || r.appLauncherButton == null)
            {
                throw new InvalidOperationException("resource app is not ready");
            }
            if (!r.IsShowing)
            {
                r.appLauncherButton.SetTrue(true);
            }
        }

        private static Dictionary<string, object> Step(Action a)
        {
            try
            {
                a();
                return new Dictionary<string, object> { ["ok"] = true };
            }
            catch (Exception e)
            {
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = e.Message };
            }
        }
    }
}
