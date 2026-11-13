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
    }
}
