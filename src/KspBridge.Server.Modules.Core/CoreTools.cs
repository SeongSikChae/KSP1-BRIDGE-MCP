using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("core")]

namespace KspBridge.Server.Modules.Core
{
    /// <summary>
    /// Planner tools of the core: status, plan submit/get/abort, flight events, step types.
    /// The plan runs inside KSP (Coordinator); these tools only hand it over and read its state.
    /// </summary>
    [McpServerToolType]
    public sealed class CoreTools
    {
        private readonly IBridgeClient bridge;
        private readonly IServerHostInfo host;

        public CoreTools(IBridgeClient bridge, IServerHostInfo host)
        {
            this.bridge = bridge;
            this.host = host;
        }

        [McpServerTool(Name = "get_status", ReadOnly = true, Idempotent = true)]
        [Description("Bridge and game status: scene, ready (requests may touch the game), versions of server host/modules, mod loader/logic " +
                     "and whether their protocol matches, reload generations, active vessel (name, situation, altitude), the current plan " +
                     "(null when no plan belongs to this flight), and the controller (attached, bridge_flybywire_handlers). Call it first and " +
                     "before any scene change; if mod.ok is false the error says why (KSP not running, not loaded, busy).")]
        public async Task<string> GetStatus(CancellationToken cancellationToken)
        {
            BridgeResult mod = await bridge.CallAsync("core", "status", null, cancellationToken);
            // Loader 상태는 Mod가 응답했지만 로직이 없을 때만 묻는다. 연결 자체가 안 되면 다시 물어도 같은 시간만 더 든다.
            BridgeResult loader = mod.Ok || mod.ErrorCode == "mod_unreachable"
                ? BridgeResult.Success(null)
                : await bridge.GetLoaderStatusAsync(cancellationToken);

            JsonObject server = new()
            {
                ["host_version"] = host.HostVersion,
                ["protocol"] = host.ProtocolVersion,
                ["generation"] = host.Generation,
                ["loaded_at"] = host.LoadedAt?.ToString("o"),
                ["modules"] = new JsonArray(host.Modules.Select(m => (JsonNode)JsonValue.Create($"{m.Name} {m.Version}")!).ToArray()),
                ["last_error"] = host.LastError,
            };

            JsonObject result = new() { ["server"] = server };
            if (mod.Ok)
            {
                JsonNode? modProtocol = mod.Data?["versions"]?["protocol"];
                server["protocol_match"] = modProtocol is not null && modProtocol.GetValue<double>() == host.ProtocolVersion;
                result["mod"] = mod.ToNode();
            }
            else
            {
                // 로직이 응답하지 않아도 Loader 상태(모든 규약 버전에서 응답)로 Mod 쪽 버전과 규약 일치 여부를 알린다.
                JsonObject modNode = mod.ToNode();
                JsonNode? loaderProtocol = loader.Ok ? loader.Data?["protocol"] : null;
                server["protocol_match"] = loaderProtocol is null ? null : loaderProtocol.GetValue<double>() == host.ProtocolVersion;
                if (loader.Ok && loader.Data is not null)
                    modNode["loader"] = loader.Data.DeepClone();
                result["mod"] = modNode;
            }
            return result.ToJsonString(BridgeResult.OutputOptions);
        }

        [McpServerTool(Name = "submit_plan", Destructive = false)]
        [Description("Submit a flight plan to the in-game Coordinator. It runs step by step on physics frames even if you are slow or " +
                     "disconnected. plan = {\"steps\":[{\"type\":\"<step type>\",\"params\":{...},\"timeout_s\":<optional>}]}. " +
                     "Step types and their params come from list_step_types. A plan needs the flight scene with ready=true. " +
                     "If a plan is running, set replace=true to end it (end_reason=planner_abort) and start this one. " +
                     "The answer is the plan summary with the same progress values as get_plan's current. " +
                     "Follow progress with get_plan / get_flight_events.")]
        public async Task<string> SubmitPlan(
            [Description("Plan object: {\"steps\":[{\"type\":\"wait\",\"params\":{\"seconds\":5}}]}")] JsonElement plan,
            [Description("End a running plan and start this one")] bool replace = false,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["plan"] = JsonNode.Parse(plan.GetRawText()), ["replace"] = replace };
            return (await bridge.CallAsync("core", "plan.submit", args, cancellationToken)).ToJson();
        }

        [McpServerTool(Name = "get_plan", ReadOnly = true, Idempotent = true)]
        [Description("The running plan (current) and the last ended plan (last) with every step's state, end_reason " +
                     "(completed, planner_abort, step_failed, timeout, reload, vessel_change, scene_change, new_flight) and end_detail. " +
                     "current also has progress values in game seconds: elapsed_s (since submit), step_elapsed_s, " +
                     "step_timeout_remaining_s (null without timeout_s), step_progress (what the step reports, e.g. wait: target_s, " +
                     "remaining_s; null when the step reports nothing) and step_progress_error. Step values are null until the current " +
                     "step begins (right after submit and between steps). current.hold is what the plan keeps holding across steps " +
                     "(e.g. {type: attitude, target}; null when nothing is held) and hold_error why it could not be read. " +
                     "Works in any scene and never fails.")]
        public async Task<string> GetPlan(CancellationToken cancellationToken)
            => (await bridge.CallAsync("core", "plan.get", null, cancellationToken)).ToJson();

        [McpServerTool(Name = "abort_plan", Destructive = true)]
        [Description("End the running plan now (end_reason=planner_abort). The controller releases the vessel.")]
        public async Task<string> AbortPlan(
            [Description("Why, recorded as end_detail")] string reason = "aborted by planner",
            CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("core", "plan.abort", new JsonObject { ["reason"] = reason }, cancellationToken)).ToJson();

        [McpServerTool(Name = "get_flight_events", ReadOnly = true, Idempotent = true)]
        [Description("Flight events after seq 'since' (plan_submitted, step_started, step_completed, plan_completed, plan_aborted, " +
                     "hold_released, plan_end_error, logic_started, and from control steps throttle_cut, stage_activated, " +
                     "action_group_changed), oldest first. Pass the returned last_seq as 'since' next time to read only new events.")]
        public async Task<string> GetFlightEvents(
            [Description("Return events with seq greater than this")] long since = 0,
            [Description("Maximum number of events")] int limit = 100,
            CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("core", "events", new JsonObject { ["since"] = since, ["limit"] = limit }, cancellationToken)).ToJson();

        [McpServerTool(Name = "list_step_types", ReadOnly = true, Idempotent = true)]
        [Description("Step types the loaded mod modules provide, with their parameters, for submit_plan.")]
        public async Task<string> ListStepTypes(CancellationToken cancellationToken)
            => (await bridge.CallAsync("core", "step_types", null, cancellationToken)).ToJson();
    }
}
