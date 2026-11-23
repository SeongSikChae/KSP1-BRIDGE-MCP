using KspBridge.Server.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("maneuver")]

namespace KspBridge.Server.Modules.Maneuver
{
    /// <summary>
    /// Maneuver nodes (paired with the mod's maneuver module and its execute_node step type). The tools pass the call's arguments
    /// on unchanged, so the mod judges unknown parameters and wrong kinds as bad_request (plan decision); the parameters are
    /// declared only to describe them.
    /// </summary>
    [McpServerToolType]
    public sealed class ManeuverTools
    {
        private const string Item =
            "A node item: index (UT order, 0 = first), ut, in_s (game seconds to it, negative when past), prograde_mps, normal_mps " +
            "(orbit normal, position × velocity), radial_mps (in the orbit plane, away from the body) — the game's components at the " +
            "node — dv_mps, burn_time_s and burn_start_ut (ut − burn_time_s/2; null when the burn time cannot be estimated or there " +
            "is no orbit after the node), after (the game's orbit after the node: body, apoapsis_m (null when open), periapsis_m, " +
            "eccentricity, inclination_deg, next_apoapsis_ut and next_periapsis_ut: the first after the node UT on that conic, null " +
            "when open / passed) or null with after_missing (the reason). The first node also has remaining_dv_mps (delta-v still to " +
            "burn; null without an orbit after it). The burn time is for the delta-v left on the first node, the whole delta-v on the " +
            "others.";

        private const string Readiness =
            "not_ready outside a ready flight scene, without an active vessel, or when maneuver nodes are not usable (the tracking " +
            "station must give orbit prediction and the mission control unlock flight planning; the reason names the facility).";

        private readonly IBridgeClient bridge;

        public ManeuverTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "create_node")]
        [Description("Add a maneuver node to the active vessel after the last node and return it as a node item. Time: exactly one of " +
                     "ut (UT, s), in_s (game seconds from now, > 0) or at (apoapsis / periapsis: the first one after now and after the " +
                     "last node on the reference orbit; one period later when the last node is on it). The time must be after now and " +
                     "after the last node, and before the reference orbit (the orbit after the last node, or the current orbit) leaves " +
                     "the sphere of influence or enters another body's. Delta-v: components prograde_mps, normal_mps, radial_mps (at " +
                     "least one, not all 0, missing = 0), or goal with exactly one of apoapsis_m (sea level, m: prograde only; not below " +
                     "the node altitude, inside the sphere of influence), periapsis_m (prograde only; 0 up to the node altitude) or " +
                     "circular true (circular orbit at the node position, prograde and radial). A goal is met on the game's orbit after " +
                     "the node within 1 m. Hohmann transfer: create_node at periapsis with goal.apoapsis_m, then create_node at " +
                     "apoapsis with goal.circular true; run them with a plan warp (to node, lead_s 60) → execute_node → warp (to node, " +
                     "lead_s 60) → execute_node. bad_request (no node is added) while a plan runs, when landed, splashed or on the pad, " +
                     "for an apoapsis / periapsis the reference orbit does not have, a goal it cannot reach, when the game computes no " +
                     "orbit after the node, or for arguments outside these rules or unknown ones. " + Readiness + " " + Item)]
        public async Task<string> CreateNode(
            RequestContext<CallToolRequestParams> context,
            [Description("Node time, UT (s)")] JsonElement? ut = null,
            [Description("Node time, game seconds from now (number > 0)")] JsonElement? in_s = null,
            [Description("Node time: \"apoapsis\" or \"periapsis\"")] JsonElement? at = null,
            [Description("Delta-v along the velocity (m/s, number)")] JsonElement? prograde_mps = null,
            [Description("Delta-v along the orbit normal, position × velocity (m/s, number)")] JsonElement? normal_mps = null,
            [Description("Delta-v in the orbit plane away from the body (m/s, number)")] JsonElement? radial_mps = null,
            [Description("Goal object with exactly one of apoapsis_m (number), periapsis_m (number) or circular (true)")] JsonElement? goal = null,
            CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("maneuver", "create", Raw(context), cancellationToken)).ToJson();

        [McpServerTool(Name = "get_nodes", ReadOnly = true, Idempotent = true)]
        [Description("The active vessel's maneuver nodes in UT order: {ut, nodes: [node item]}; an empty list without nodes. Does not " +
                     "change anything; works while a plan runs. " + Readiness + " " + Item)]
        public async Task<string> GetNodes(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("maneuver", "get", Raw(context), cancellationToken)).ToJson();

        [McpServerTool(Name = "delete_node")]
        [Description("Remove a maneuver node by index (the nodes after it move up one number) or every node with all true, and return " +
                     "the list as get_nodes does. all with no node is not an error. bad_request (nothing removed) for a number that is " +
                     "not there, both or neither of index and all, all not true, unknown arguments, or while a plan runs. " + Readiness)]
        public async Task<string> DeleteNode(
            RequestContext<CallToolRequestParams> context,
            [Description("Node number (integer >= 0, UT order)")] JsonElement? index = null,
            [Description("true to remove every node")] JsonElement? all = null,
            CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("maneuver", "delete", Raw(context), cancellationToken)).ToJson();

        /// <summary>The call's arguments as they came.</summary>
        private static JsonObject Raw(RequestContext<CallToolRequestParams> context)
        {
            JsonObject args = new();
            if (context.Params?.Arguments is { } arguments)
            {
                foreach (KeyValuePair<string, JsonElement> pair in arguments)
                    args[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
            }
            return args;
        }
    }
}
