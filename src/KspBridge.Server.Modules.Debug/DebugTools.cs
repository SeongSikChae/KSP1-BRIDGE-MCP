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

        private static readonly TimeSpan OrbitPoll = TimeSpan.FromMilliseconds(50);

        /// <summary>Above the mod's own 10 s time-out, so the mod's answer normally comes first.</summary>
        private static readonly TimeSpan OrbitTimeout = TimeSpan.FromSeconds(15);

        [McpServerTool(Name = "debug_set_orbit", Destructive = true)]
        [Description("Put the active vessel on an orbit (the game's set-orbit cheat) for testing: periapsis now, given apoapsis and " +
                     "periapsis altitudes above sea level and inclination. Answers when the game shows the orbit (situation ORBITING, " +
                     "Ap/Pe within 1 km); not_ready if that does not happen within 10 s or outside a ready flight scene. bad_request " +
                     "for an unknown body, apoapsis below periapsis, periapsis inside the atmosphere or not above the highest terrain " +
                     "(or that height unknown), apoapsis outside the sphere of influence, or while a plan is running (abort_plan first; " +
                     "nothing is changed).")]
        public async Task<string> DebugSetOrbit(
            [Description("Apoapsis altitude above sea level (m)")] double apoapsis_m,
            [Description("Periapsis altitude above sea level (m)")] double periapsis_m,
            [Description("Inclination (degrees)")] double inclination_deg = 0,
            [Description("Celestial body internal name, case-insensitive; default is the vessel's current body")] string? body = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["apoapsis_m"] = apoapsis_m, ["periapsis_m"] = periapsis_m, ["inclination_deg"] = inclination_deg };
            if (body is not null)
                args["body"] = body;
            BridgeResult result = await bridge.CallAsync("debug", "set_orbit.start", args, cancellationToken);
            if (!IsPending(result))
                return result.ToJson();
            JsonNode request = result.Data!["request"]!.DeepClone();
            DateTime deadline = DateTime.UtcNow + OrbitTimeout;
            while (true)
            {
                await Task.Delay(OrbitPoll, cancellationToken);
                result = await bridge.CallAsync("debug", "set_orbit.result", new JsonObject { ["request"] = request.DeepClone() }, cancellationToken);
                if (!IsPending(result))
                    return result.ToJson();
                if (DateTime.UtcNow > deadline)
                    return BridgeResult.Failure("not_ready", "set_orbit request " + request.ToJsonString() + " has not finished within " +
                        OrbitTimeout.TotalSeconds + " s").ToJson();
            }
        }

        private static bool IsPending(BridgeResult result)
            => result.Ok && result.Data is JsonObject data && data["pending"] is JsonValue pending && pending.TryGetValue(out bool p) && p;

        [McpServerTool(Name = "debug_space_center", Destructive = true)]
        [Description("Save and leave the flight for the space center (like the Space Center button).")]
        public async Task<string> DebugSpaceCenter(CancellationToken cancellationToken)
            => (await bridge.CallAsync("debug", "space_center", null, cancellationToken)).ToJson();
    }
}
