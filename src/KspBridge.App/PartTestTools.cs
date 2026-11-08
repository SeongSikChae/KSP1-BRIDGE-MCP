using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class PartTestTools(KspBridgeClient client)
    {
        private const string PartArg = "Part name (e.g. parachuteSingle), part title, or part_id number from list_part_tests";

        [McpServerTool(Name = "list_part_tests", ReadOnly = true)]
        [Description("Part test contracts on the active vessel: one row per test-subject part with part_id, method (event = stock 'Run Test' button, staging = activating the part, haul = carry it through the conditions), contract, condition states, ready (all conditions met now), armed. The contract must be accepted before launch.")]
        public Task<string> ListPartTests(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/part-tests", null, ct);

        [McpServerTool(Name = "run_part_test", Destructive = true)]
        [Description("Run a part test now (stock 'Run Test', or activate a staging-tested part such as a parachute or engine). Refused when the contract conditions are not all met (not_ready lists the unmet ones) unless force=true; a test outside the conditions does not complete the contract and may use up the part.")]
        public Task<string> RunPartTest(
            [Description(PartArg)] JsonElement part,
            [Description("Run even when the conditions are not met (default false)")] bool force = false,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/part-tests/run", new { part = NormalizePart(part), force }, ct);

        [McpServerTool(Name = "arm_part_test")]
        [Description("Arm a part test: the game runs it on the first physics tick when all contract conditions are met (for short windows such as reentry). Registered as a one-shot trigger (list_triggers / remove_trigger); stops time warp like other triggers.")]
        public Task<string> ArmPartTest([Description(PartArg)] JsonElement part, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/part-tests/arm", new { part = NormalizePart(part) }, ct);

        /// <summary>A part_id may arrive as a numeric string; send it as a number.</summary>
        internal static object NormalizePart(JsonElement part)
        {
            if (part.ValueKind == JsonValueKind.String && ulong.TryParse(part.GetString(), out ulong id))
            {
                return id;
            }
            return part;
        }
    }
}
