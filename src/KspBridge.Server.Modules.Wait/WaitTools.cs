using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("wait")]

namespace KspBridge.Server.Modules.Wait
{
    /// <summary>Tools of the wait feature (paired with the mod's wait module and its "wait" step type).</summary>
    [McpServerToolType]
    public sealed class WaitTools
    {
        private readonly IBridgeClient bridge;

        public WaitTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "submit_wait")]
        [Description("Submit a one-step plan that holds for the given game seconds (UT, time warp counts) without touching the controls. " +
                     "Same as submit_plan with {\"steps\":[{\"type\":\"wait\",\"params\":{\"seconds\":N}}]}. Needs the flight scene.")]
        public async Task<string> SubmitWait(
            [Description("Game seconds to wait (> 0)")] double seconds,
            [Description("End a running plan and start this one")] bool replace = false,
            CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("wait", "submit", new JsonObject { ["seconds"] = seconds, ["replace"] = replace }, cancellationToken)).ToJson();
    }
}
