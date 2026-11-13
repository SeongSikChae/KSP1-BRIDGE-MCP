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

        [McpServerTool(Name = "debug_input_locks_error", Destructive = true)]
        [Description(Flag + "make listing the input locks fail, so get_status returns input_locks null with input_locks_error " +
            "(other fields unchanged). Returns get_status.debug.")]
        public Task<string> InputLocksError([Description("true to inject the failure, false to stop")] bool on, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/input_locks_error", new { on }, ct);

        [McpServerTool(Name = "debug_add_part_timeout", Destructive = true)]
        [Description(Flag + "make add_part onto a parent fail with error timeout, as when the main thread does not answer " +
            "(the spawned parts are discarded later, KSP.log 'discarded N loose part(s) after a timeout'). Root placement is not " +
            "affected. Returns get_status.debug.")]
        public Task<string> AddPartTimeout([Description("true to inject the timeout, false to stop")] bool on, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/debug/add_part_timeout", new { on }, ct);
    }
}
