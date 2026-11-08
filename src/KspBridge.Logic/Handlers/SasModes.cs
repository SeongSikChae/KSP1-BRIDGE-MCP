using System.Collections.Generic;

namespace KspBridge.Handlers
{
    public static class SasModes
    {
        private static readonly Dictionary<string, VesselAutopilot.AutopilotMode> ByName =
            new Dictionary<string, VesselAutopilot.AutopilotMode>
            {
                ["stability_assist"] = VesselAutopilot.AutopilotMode.StabilityAssist,
                ["prograde"] = VesselAutopilot.AutopilotMode.Prograde,
                ["retrograde"] = VesselAutopilot.AutopilotMode.Retrograde,
                ["normal"] = VesselAutopilot.AutopilotMode.Normal,
                ["antinormal"] = VesselAutopilot.AutopilotMode.Antinormal,
                ["radial_in"] = VesselAutopilot.AutopilotMode.RadialIn,
                ["radial_out"] = VesselAutopilot.AutopilotMode.RadialOut,
                ["target"] = VesselAutopilot.AutopilotMode.Target,
                ["anti_target"] = VesselAutopilot.AutopilotMode.AntiTarget,
                ["maneuver"] = VesselAutopilot.AutopilotMode.Maneuver,
            };

        public static VesselAutopilot.AutopilotMode Parse(string name)
        {
            if (ByName.TryGetValue(name.ToLowerInvariant(), out VesselAutopilot.AutopilotMode mode))
            {
                return mode;
            }
            throw ApiException.BadRequest("unknown SAS mode '" + name + "'. Valid: " + string.Join(", ", ByName.Keys));
        }

        public static string ToName(VesselAutopilot.AutopilotMode mode)
        {
            foreach (KeyValuePair<string, VesselAutopilot.AutopilotMode> pair in ByName)
            {
                if (pair.Value == mode)
                {
                    return pair.Key;
                }
            }
            return mode.ToString();
        }
    }
}
