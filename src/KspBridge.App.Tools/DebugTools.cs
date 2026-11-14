using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    /// <summary>Verification-only fault injection (#7). Every tool is 409 debug_disabled unless debug.cfg enables it.</summary>
    [McpServerToolType]
    public sealed class DebugTools(KspBridgeClient client)
    {
        private const string Flag = "Debug tool (needs GameData/KspBridge/debug.cfg with KSPBRIDGE_DEBUG { enabled = true } and reload_mod, else 409 debug_disabled): ";

        [McpServerTool(Name = "debug_clear", Destructive = true)]
        [Description(Flag + "release every debug state (a scene change and reload_mod also do). Returns get_status.debug.")]
        public Task<string> Clear(CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/clear", new { }, ct);

        [McpServerTool(Name = "debug_input_lock", Destructive = true)]
        [Description(Flag + "set the game control lock 'kspbridge_debug' to a ControlTypes mask (shown in get_status.input_locks), " +
            "or remove it with mask null. Replaces the previous mask. Returns get_status.debug.")]
        public Task<string> InputLock(
            [Description("ControlTypes mask as a hex string like \"0x8\" (0x0 allowed: a lock with no controls); null removes the lock")] string? mask = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/input_lock", new { mask }, ct);

        [McpServerTool(Name = "debug_dock_reject", Destructive = true)]
        [Description(Flag + "make the next count dock attempts of the flight-plan mode dock fail on purpose: the vessel stops 2 m before " +
            "contact (outside the ports' magnetic capture), and after 5 s at rest the attempt counts as failed (dock_attempt {forced: true}), " +
            "then it backs off and retries; more failures than retries end in plan_aborted dock_failed. 0 releases (debug_clear too). Returns get_status.debug.")]
        public Task<string> DockReject([Description("Attempts to fail, 0..10; 0 releases")] int count, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/dock_reject", new { count }, ct);

        [McpServerTool(Name = "debug_input_locks_error", Destructive = true)]
        [Description(Flag + "make listing the input locks fail, so get_status returns input_locks null with input_locks_error " +
            "(other fields unchanged). Returns get_status.debug.")]
        public Task<string> InputLocksError([Description("true to inject the failure, false to stop")] bool on, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/input_locks_error", new { on }, ct);

        [McpServerTool(Name = "debug_stage_delay", Destructive = true)]
        [Description(Flag + "hold the flight plan's stage confirmation (staged event) until N seconds of game time after its first " +
            "stage request, without re-requesting a stage that already fired; the failure deadline grows by N too. Not handed over: " +
            "after reload_mod the new logic confirms at once. Returns get_status.debug.")]
        public Task<string> StageDelay([Description("Seconds of game time, 0..60; 0 releases")] double seconds, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/stage_delay", new { seconds }, ct);

        [McpServerTool(Name = "debug_engine_stop", Destructive = true)]
        [Description(Flag + "stop an engine of the active vessel at once: shutdown (like the part's shutdown button: not ignited) or " +
            "flameout (stays ignited, flamed out). Multi-mode engines: first mode only. Returns {part_id, ignited, flameout} after " +
            "the call; not undone by debug_clear.")]
        public Task<string> EngineStop(
            [Description("Engine part id (persistentId, as get_craft / get_flight)")] long part_id,
            [Description("shutdown or flameout")] string mode,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/engine_stop", new { part_id, mode }, ct);

        [McpServerTool(Name = "debug_throttle_hold", Destructive = true)]
        [Description(Flag + "write a fixed value to one engine's own throttle (currentThrottle) every physics tick, before the flight " +
            "plan reads it, so it ignores the plan's throttle; needs a running flight plan (else 409 no_plan); throttle null releases. " +
            "Released when the plan ends (completed or aborted), when the part leaves the active " +
            "vessel, by debug_clear, a scene change and reload_mod. Returns get_status.debug.")]
        public Task<string> ThrottleHold(
            [Description("Engine part id (persistentId); ignored when throttle is null")] long? part_id = null,
            [Description("0..1 (clamped); null releases")] double? throttle = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/throttle_hold", new { part_id, throttle }, ct);

        [McpServerTool(Name = "debug_reload_fault", Destructive = true)]
        [Description(Flag + "make the next reload_mod fail: start_failures 1 = the new logic fails to start (the loader restarts " +
            "the previous one), 2 = the previous logic fails to restart too (double failure: the loader drops the handover, " +
            "get_status.logic_error); handover_failures 1..3 = the handover serialization fails up to that stage (1: plan aborted, " +
            "events kept; 2: only the event cursor kept; 3: nothing handed over, new epoch). Spent by that reload; start_failures " +
            ">= 1 with handover_failures 3 is refused. Both 0 releases. Returns get_status.debug.")]
        public Task<string> ReloadFault(
            [Description("0..2")] int start_failures = 0,
            [Description("0..3")] int handover_failures = 0,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/reload_fault", new { start_failures, handover_failures }, ct);

        [McpServerTool(Name = "debug_add_part_timeout", Destructive = true)]
        [Description(Flag + "make add_part onto a parent fail with error timeout, as when the main thread does not answer " +
            "(the spawned parts are discarded later, KSP.log 'discarded N loose part(s) after a timeout'). Root placement is not " +
            "affected. Returns get_status.debug.")]
        public Task<string> AddPartTimeout([Description("true to inject the timeout, false to stop")] bool on, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/add_part_timeout", new { on }, ct);
    }
}
