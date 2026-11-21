using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("check")]

namespace KspBridge.Server.Modules.Check
{
    /// <summary>Assembly check of the ship being built in the editor (paired with the mod's check module).</summary>
    [McpServerToolType]
    public sealed class CheckTools
    {
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

        /// <summary>Above the mod's own 10 s time-out, so the mod's answer normally comes first.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly IBridgeClient bridge;

        public CheckTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "check_ship", ReadOnly = true, Idempotent = true)]
        [Description("Check the ship being built in the VAB/SPH before launch. Per stage (first to fire first): start/end/dry/fuel mass (t), " +
                     "delta-v and TWR at sea level and in vacuum of the given body (from the game's delta-v calculator; TWR at the stage's " +
                     "start mass), burn time, and for engine stages the centre-of-mass offset from the thrust line (m, degrees); totals; " +
                     "and warnings {code, stage, parts (ids), message, values}: no_separation (engine not dropped before the next engine " +
                     "stage), fuel_blocked (engine cannot draw fuel on board), no_engine_fuel (tank feeds no engine), com_offset (thrust " +
                     "line misses the CoM more than the gimbals can cancel), no_control, low_launch_twr (VAB, first stage below 1), " +
                     "leftover_fuel (fuel left after the last stage), no_parachute. Read-only: the ship is not changed; the game's " +
                     "delta-v window shows the given body for a moment while the check runs.")]
        public async Task<string> CheckShip(
            [Description("Celestial body internal name, case-insensitive (e.g. Kerbin, Mun, Duna); default is the home body")] string? body = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new();
            if (body is not null)
                args["body"] = body;
            BridgeResult result = await bridge.CallAsync("check", "start", args, cancellationToken);
            if (!IsPending(result))
                return result.ToJson();
            JsonNode check = result.Data!["check"]!.DeepClone();
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                await Task.Delay(Poll, cancellationToken);
                result = await bridge.CallAsync("check", "result", new JsonObject { ["check"] = check.DeepClone() }, cancellationToken);
                if (!IsPending(result))
                    return result.ToJson();
                if (DateTime.UtcNow > deadline)
                    return BridgeResult.Failure("not_ready", "check " + check.ToJsonString() + " has not finished within " + Timeout.TotalSeconds +
                        " s; call check_ship again").ToJson();
            }
        }

        private static bool IsPending(BridgeResult result)
            => result.Ok && result.Data is JsonObject data && data["pending"] is JsonValue pending && pending.TryGetValue(out bool p) && p;
    }
}
