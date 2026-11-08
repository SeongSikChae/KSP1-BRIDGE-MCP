using System.Collections.Generic;
using KSP.UI.Screens;

namespace KspBridge.Handlers
{
    public static class ControlHandler
    {
        private static readonly Dictionary<string, KSPActionGroup> Groups = new Dictionary<string, KSPActionGroup>
        {
            ["custom01"] = KSPActionGroup.Custom01,
            ["custom02"] = KSPActionGroup.Custom02,
            ["custom03"] = KSPActionGroup.Custom03,
            ["custom04"] = KSPActionGroup.Custom04,
            ["custom05"] = KSPActionGroup.Custom05,
            ["custom06"] = KSPActionGroup.Custom06,
            ["custom07"] = KSPActionGroup.Custom07,
            ["custom08"] = KSPActionGroup.Custom08,
            ["custom09"] = KSPActionGroup.Custom09,
            ["custom10"] = KSPActionGroup.Custom10,
            ["gear"] = KSPActionGroup.Gear,
            ["light"] = KSPActionGroup.Light,
            ["brakes"] = KSPActionGroup.Brakes,
            ["abort"] = KSPActionGroup.Abort,
        };

        public static object SetThrottle(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            double value = body.GetDouble("value");
            if (value < 0 || value > 1)
            {
                throw ApiException.BadRequest("value must be between 0 and 1");
            }
            bool hold = !body.Has("hold") || body.GetBool("hold");
            FlightInputHandler.state.mainThrottle = (float)value;
            if (hold)
            {
                Flight.ThrottleLock.Set(v, (float)value);
            }
            else
            {
                Flight.ThrottleLock.Clear();
            }
            return new Dictionary<string, object>
            {
                ["throttle"] = (double)FlightInputHandler.state.mainThrottle,
                ["hold"] = Flight.ThrottleLock.Active(v),
            };
        }

        public static object ActivateNextStage()
        {
            Guard.ActiveVessel();
            StageManager.ActivateNextStage();
            return new Dictionary<string, object> { ["current_stage"] = StageManager.CurrentStage };
        }

        public static object SetSas(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            bool enabled = body.GetBool("enabled");
            if (!enabled)
            {
                Flight.SasMonitor.Clear(); // SAS off releases the held mode
            }
            v.ActionGroups.SetGroup(KSPActionGroup.SAS, enabled);
            return new Dictionary<string, object> { ["sas"] = v.ActionGroups[KSPActionGroup.SAS] };
        }

        public static object SetSasMode(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            VesselAutopilot.AutopilotMode mode = SasModes.Parse(body.GetString("mode"));
            bool wasOn = v.ActionGroups[KSPActionGroup.SAS];
            if (!wasOn)
            {
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }
            if (!v.Autopilot.CanSetMode(mode))
            {
                if (!wasOn)
                {
                    v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                }
                throw ApiException.BadRequest("SAS mode '" + SasModes.ToName(mode) + "' is not available now (pilot level, target or maneuver node missing)");
            }
            Attitude.AttitudeController.Release(); // the SAS mode replaces an attitude hold
            Flight.SasMonitor.Apply(v, mode);
            Flight.SasMonitor.Request(v, mode);
            return new Dictionary<string, object>
            {
                ["sas"] = v.ActionGroups[KSPActionGroup.SAS],
                ["sas_mode"] = SasModes.ToName(v.Autopilot.Mode),
                ["hold"] = true,
            };
        }

        public static object SetRcs(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            v.ActionGroups.SetGroup(KSPActionGroup.RCS, body.GetBool("enabled"));
            return new Dictionary<string, object> { ["rcs"] = v.ActionGroups[KSPActionGroup.RCS] };
        }

        public static object SetActionGroup(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            string name = body.GetString("group").ToLowerInvariant();
            if (!Groups.TryGetValue(name, out KSPActionGroup group))
            {
                throw ApiException.BadRequest("unknown group '" + name + "'. Valid: " + string.Join(", ", Groups.Keys));
            }

            string action = body.GetString("action").ToLowerInvariant();
            switch (action)
            {
                case "toggle":
                    v.ActionGroups.ToggleGroup(group);
                    break;
                case "on":
                    v.ActionGroups.SetGroup(group, true);
                    break;
                case "off":
                    v.ActionGroups.SetGroup(group, false);
                    break;
                default:
                    throw ApiException.BadRequest("action must be toggle, on or off");
            }
            return new Dictionary<string, object> { ["group"] = name, ["state"] = v.ActionGroups[group] };
        }
    }
}
