using KspBridge.Server.Contracts;
using KspBridge.Shared;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KspBridge.Server.Modules
{
    /// <summary>모듈 세대와 무관하게 항상 있는 호스트 도구.</summary>
    public static class HostTools
    {
        [McpServerTool(Name = "reload", Destructive = false, Idempotent = false)]
        [Description("Reload logic without restarting KSP or the MCP client. target=server reloads MCP tool modules from the server modules folder; " +
                     "target=mod reloads the KSP mod logic generation (a running plan ends with end_reason=reload); target=all does both. " +
                     "Deploys reload automatically; use this to force a reload.")]
        public static async Task<string> Reload(ModuleHost host, IBridgeClient bridge,
            [Description("server | mod | all")] string target = "all", CancellationToken cancellationToken = default)
        {
            JsonObject result = new();
            if (target is "server" or "all")
            {
                bool ok = host.Reload();
                result["server"] = new JsonObject { ["ok"] = ok, ["generation"] = host.Generation, ["error"] = host.LastError };
            }
            if (target is "mod" or "all")
                result["mod"] = (await bridge.ReloadModAsync(cancellationToken)).ToNode();
            if (result.Count == 0)
                return BridgeResult.Failure(ErrorCodes.BadRequest, $"Unknown target '{target}'. Use server, mod or all.").ToJson();
            return result.ToJsonString(BridgeResult.OutputOptions);
        }
    }
}
