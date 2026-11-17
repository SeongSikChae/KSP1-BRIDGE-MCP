using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("debug")]

namespace KspBridge.Server.Modules.Debug
{
    /// <summary>
    /// Verification helpers (paired with the mod's debug module). They change the scene: check get_status first,
    /// someone else's flight may be running. A running plan ends with end_reason scene_change or new_flight.
    /// </summary>
    [McpServerToolType]
    public sealed class DebugTools
    {
        private readonly IBridgeClient bridge;

        public DebugTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "debug_launch", Destructive = true)]
        [Description("Launch a saved or stock craft (Ships/VAB or Ships/SPH) to the launch site with default crew. Loads the flight scene; " +
                     "poll get_status until scene=FLIGHT and ready=true. Check get_status first: this replaces the current flight.")]
        public async Task<string> DebugLaunch(
            [Description("Craft name without .craft (stock example: Kerbal X)")] string craft = "Kerbal X",
            [Description("VAB or SPH")] string facility = "VAB",
            [Description("Launch site; default LaunchPad (VAB) or Runway (SPH)")] string? site = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["craft"] = craft, ["facility"] = facility };
            if (site is not null)
                args["site"] = site;
            return (await bridge.CallAsync("debug", "launch", args, cancellationToken)).ToJson();
        }

        [McpServerTool(Name = "debug_revert", Destructive = true)]
        [Description("Revert the current flight to launch. Reloads the flight scene; poll get_status until ready=true.")]
        public async Task<string> DebugRevert(CancellationToken cancellationToken)
            => (await bridge.CallAsync("debug", "revert", null, cancellationToken)).ToJson();

        [McpServerTool(Name = "debug_space_center", Destructive = true)]
        [Description("Save and leave the flight for the space center (like the Space Center button).")]
        public async Task<string> DebugSpaceCenter(CancellationToken cancellationToken)
            => (await bridge.CallAsync("debug", "space_center", null, cancellationToken)).ToJson();
    }
}
