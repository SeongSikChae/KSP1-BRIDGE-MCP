using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class FlightTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_status", ReadOnly = true)]
        [Description("KspBridge mod status: current game scene, whether there is an active vessel, logic version (version), loader_version, load_count (logic loads since KSP start) and logic_error when the logic failed to load.")]
        public Task<string> GetStatus(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/status", null, ct);

        [McpServerTool(Name = "reload_mod", Destructive = true)]
        [Description("Reload the KspBridge logic DLL (GameData/KspBridge/PluginData/KspBridge.Logic.dll) without restarting KSP. Triggers, throttle hold and attitude hold are released first; the result lists what was released and the versions before/after. If the new DLL fails to load or start, the previous logic is kept.")]
        public Task<string> ReloadMod(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/reload", new { }, ct);

        [McpServerTool(Name = "get_vessel_state", ReadOnly = true)]
        [Description("Active vessel flight state: altitude, speeds, orbit (Ap/Pe), attitude, controls, stage, resources. Units: m, m/s, s, deg, t. prediction: thrust_kn, max_accel, twr, drag_accel (measured), and while descending stop_time/stop_distance/stop_dv (full-thrust burn from now until vertical speed 0, drag included), time_to_impact, burn_margin (height above terrain minus stop_distance; ignite near 0+margin). Vertical 1-D estimates. warp: rate, index, mode (rails/physics), stop_ut of a running warp_to, max_rails_rate (stock limiter now) and rails_limit (reason when limited). g_force; crew: conscious, inactive_for, g_load (accumulated G, 1 = blackout); sas: on, requested (held mode), actual, steering (SAS really steering), available_modes, events (mode_lost/steering_lost with reason vessel_modified/crew_unconscious/mode_unavailable, reapplied).")]
        public Task<string> GetVesselState(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/vessel", null, ct);

        [McpServerTool(Name = "set_throttle")]
        [Description("Set the main throttle of the active vessel. hold=true (default) keeps re-applying this value every physics tick so keyboard throttle input cannot change it; hold=false sets it once and lets manual input change it.")]
        public Task<string> SetThrottle(
            [Description("Throttle 0.0 ~ 1.0")] double value,
            [Description("Keep the value locked until set_throttle is called again (default true)")] bool hold = true,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/throttle", new { value, hold }, ct);

        [McpServerTool(Name = "activate_next_stage", Destructive = true)]
        [Description("Activate the next stage (same as pressing space). Irreversible: may drop parts.")]
        public Task<string> ActivateNextStage(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/stage", new { }, ct);

        [McpServerTool(Name = "setup_launch_ui")]
        [Description("Set up the flight screen for launch prep: maneuver mode (Ap/Pe bottom-left), staging stack with total and per-stage delta-v/TWR/ISP/burn time info (left), and the resources app (top-right). Flight scene only. Reports ok/error per item.")]
        public Task<string> SetupLaunchUi(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/ui/launch", new { }, ct);

        [McpServerTool(Name = "set_sas")]
        [Description("Turn SAS on or off.")]
        public Task<string> SetSas(bool enabled, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/sas", new { enabled }, ct);

        [McpServerTool(Name = "set_sas_mode")]
        [Description("Set SAS mode (turns SAS on if off; releases an attitude hold). The game keeps holding this mode: if it is dropped (staging, crew blackout, mode unavailable) it is re-applied as soon as possible and logged in get_vessel_state sas.events. set_sas(false), another set_sas_mode or set_attitude releases it. Modes: stability_assist, prograde, retrograde, normal, antinormal, radial_in, radial_out, target, anti_target, maneuver.")]
        public Task<string> SetSasMode([Description("SAS mode name")] string mode, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/sas/mode", new { mode }, ct);

        [McpServerTool(Name = "set_rcs")]
        [Description("Turn RCS on or off.")]
        public Task<string> SetRcs(bool enabled, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/rcs", new { enabled }, ct);

        [McpServerTool(Name = "set_action_group")]
        [Description("Toggle or set an action group.")]
        public Task<string> SetActionGroup(
            [Description("custom01..custom10, gear, light, brakes, abort")] string group,
            [Description("toggle, on, off")] string action,
            CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/action-group", new { group, action }, ct);

        [McpServerTool(Name = "list_maneuver_nodes", ReadOnly = true)]
        [Description("List maneuver nodes of the active vessel with delta-v components, resulting orbit and patches (predicted path after the node: body, start_ut/end_ut, " +
            "end = encounter (enters a moon's SOI) / escape / maneuver / final, periapsis/apoapsis; in an encounter patch the periapsis is the closest approach). " +
            "transfers: each moon of the current body with phase (deg ahead of the vessel), hohmann_phase, time_to_window/window_ut (when the phase equals hohmann_phase), " +
            "hohmann_dv (prograde) and transfer_time; estimates for a near-circular orbit, then refine with the node patches.")]
        public Task<string> ListManeuverNodes(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/nodes", null, ct);

        [McpServerTool(Name = "add_maneuver_node")]
        [Description("Add a maneuver node. Give exactly one of ut (universal time, s) or inSeconds (seconds from now). Delta-v in m/s. " +
            "Returns the node with orbit_after, patches (encounters, closest approach) and transfers as in list_maneuver_nodes.")]
        public Task<string> AddManeuverNode(
            [Description("Universal time of the node (s)")] double? ut = null,
            [Description("Seconds from now")] double? inSeconds = null,
            [Description("Prograde delta-v (m/s)")] double prograde = 0,
            [Description("Normal delta-v (m/s)")] double normal = 0,
            [Description("Radial-out delta-v (m/s)")] double radial = 0,
            CancellationToken ct = default)
        {
            if (ut.HasValue == inSeconds.HasValue)
            {
                return Task.FromResult("error: bad_request: give exactly one of ut or inSeconds.");
            }

            object body = ut.HasValue
                ? new { ut = ut.Value, prograde, normal, radial }
                : new { in_seconds = inSeconds!.Value, prograde, normal, radial };
            return client.SendAsync(HttpMethod.Post, "api/nodes", body, ct);
        }

        [McpServerTool(Name = "execute_node", Destructive = true)]
        [Description("Execute the first maneuver node in-game: hold the node direction (own controller), wait until aligned (< 2 deg) and not rotating, " +
            "optionally time-warp to 30 s before ignition, ignite at node time minus the time to burn half the delta-v, throttle down near the end and cut when the remaining delta-v < tolerance. " +
            "Returns the plan at once (node_dv, burn_time, half_dv_time, ignite_ut, ignite_in, stage_dv, warnings); follow progress with wait_until field node_exec " +
            "(aligning, waiting, burning, done, aborted) and node_dv_remaining. No auto staging: a stage with less delta-v than the node only warns, and the run aborts on flameout " +
            "before any trigger can stage, so it cannot continue through staging; split such burns into nodes per stage. " +
            "Call it at 1x (not during time warp). The throttle is held at 0 from the start until ignition. It never burns late: not aligned in time or ignition missed by > 1 s → aborted " +
            "(not_aligned_in_time / missed_ignition). It also aborts when the node is removed or changed, the vessel changes, or another tool takes over the attitude or throttle (overridden).")]
        public Task<string> ExecuteNode(
            [Description("Cut-off remaining delta-v (m/s), default 0.1")] double tolerance = 0.1,
            [Description("Time-warp while waiting for ignition (default true)")] bool warp = true,
            [Description("Remove the node when done (default true)")] bool removeWhenDone = true,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/node/execute", new { tolerance, warp, remove_when_done = removeWhenDone }, ct);

        [McpServerTool(Name = "cancel_node")]
        [Description("Cancel a running execute_node: throttle to 0, release the node attitude hold. Returns the executor state.")]
        public Task<string> CancelNode(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/node/cancel", new { }, ct);

        [McpServerTool(Name = "warp_to")]
        [Description("Time-warp (stock auto-warp, rails) until `lead` seconds before a target, then return to 1x. target: ut (give ut), ap, pe, node (first maneuver node), soi (next SOI change). Returns at once with target_ut/stop_ut; check arrival with wait_until field ut >= stop_ut. When the throttle is above 0 or rails warp is not allowed yet (atmosphere), it is scheduled instead (scheduled=true, waiting_for): it starts by itself as soon as both are clear, with the target time recomputed then (e.g. ap during the ascent burn); get_vessel_state warp.scheduled shows it, stop_warp cancels it. Armed triggers stop the warp when their condition becomes true.")]
        public Task<string> WarpTo(
            [Description("ut, ap, pe, node or soi")] string target,
            [Description("Universal time (s), only for target=ut")] double? ut = null,
            [Description("Stop this many seconds before the target (default 30)")] double lead = 30,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/warp/to", new { target, ut, lead }, ct);

        [McpServerTool(Name = "set_warp")]
        [Description("Set the time-warp rate. Rails rates 1, 5, 10, 50, 100, 1000, 10000, 100000 (no thrust, altitude-limited); physics=true rates 1, 2, 3, 4 (works in atmosphere and while burning). The game may lower the rate (limited=true). rate 1 = normal speed. Armed triggers stop the warp when their condition becomes true.")]
        public Task<string> SetWarp(
            [Description("Warp rate from the list")] double rate,
            [Description("Physics warp instead of rails (default false)")] bool physics = false,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/warp", new { rate, physics }, ct);

        [McpServerTool(Name = "stop_warp")]
        [Description("Cancel any auto-warp and return to 1x immediately.")]
        public Task<string> StopWarp(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/warp/stop", new { }, ct);

        [McpServerTool(Name = "remove_maneuver_node", Destructive = true)]
        [Description("Remove a maneuver node by index (from list_maneuver_nodes).")]
        public Task<string> RemoveManeuverNode([Description("Node index")] int index, CancellationToken ct)
            => client.SendAsync(HttpMethod.Delete, $"api/nodes/{index}", null, ct);
    }
}
