using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class FlightTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_status", ReadOnly = true)]
        [Description("KspBridge mod status: current game scene, whether there is an active vessel, logic version (version), loader_version, load_count (logic loads since KSP start), logic_error when the logic failed to load, and editor ({facility, ready, craft, part_count} in the VAB/SPH, null elsewhere; poll until editor.ready after open_editor).")]
        public Task<string> GetStatus(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/status", null, ct);

        [McpServerTool(Name = "reload_mod", Destructive = true)]
        [Description("Reload the KspBridge logic DLL (GameData/KspBridge/PluginData/KspBridge.Logic.dll) without restarting KSP. The result lists the versions before/after. If the new DLL fails to load or start, the previous logic is kept.")]
        public Task<string> ReloadMod(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/reload", new { }, ct);

        [McpServerTool(Name = "get_flight", ReadOnly = true)]
        [Description("Active vessel flight state: vessel, flight, orbit, propulsion and plan ({state:idle} without a plan). " +
            "Field names match flight-plan condition variables; null means not known yet (e.g. stage_dv while the game recalculates).")]
        public Task<string> GetFlight(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/flight", null, ct);

        [McpServerTool(Name = "submit_flight_plan", Destructive = true)]
        [Description("Start a flight plan on the active vessel (replaces a running plan). plan = {auto_stage?, staging_options?, abort?, steps:[{name?, mode, ...mode args, on_start?, until?, abort?, timeout_s?}]}. " +
            "Modes: attitude (pitch+heading [+roll] or reference prograde|retrograde|surface_prograde|normal|antinormal|radial_in|radial_out, throttle 0..1; until required). " +
            "Conditions: {var, op, value, for_s?} | {all:[...]} | {any:[...]} | [...] (all); var is a get_flight field name (flight, orbit, propulsion, step_time, met, attitude_error), " +
            "string vars (situation, staging) allow only == and !=; a var that is null (not known) is false. " +
            "on_start:[\"stage\"] activates the next stage when the step starts (needed for the first ignition on the pad). " +
            "Invalid plans fail with invalid_plan \"<path>: <reason>\"; no engine and nothing staging fails with no_thrust. " +
            "Returns {plan_id, state, steps, event_seq, epoch}; follow progress with get_flight_events.")]
        public Task<string> SubmitFlightPlan([Description("Flight plan JSON object")] JsonElement plan, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/flight/plan", new { plan }, ct);

        [McpServerTool(Name = "replace_flight_steps", Destructive = true)]
        [Description("Replace the steps after the current one in the running flight plan (include_current: true replaces from the current step and starts it now). " +
            "Same step format as submit_flight_plan; error paths index the new steps array (steps[i]...). Empty steps with include_current false drops the remaining steps. " +
            "A staging sequence in progress finishes. 409 no_plan without a running plan. Returns {plan_id, state, steps, event_seq, epoch}.")]
        public Task<string> ReplaceFlightSteps([Description("Steps array")] JsonElement steps,
            [Description("Replace from the current step and start it now (default false)")] bool include_current = false, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/steps", new { steps, include_current }, ct);

        [McpServerTool(Name = "abort_flight", Destructive = true)]
        [Description("Abort the running flight plan: throttle 0, SAS back to its state before the plan, control released (event plan_aborted reason user). no_plan when nothing runs.")]
        public Task<string> AbortFlight(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/flight/abort", new { }, ct);

        [McpServerTool(Name = "get_flight_events", ReadOnly = true)]
        [Description("Flight events with seq > since (oldest first). Returns {events, next, truncated, epoch}: pass next as the next since. " +
            "truncated: events were dropped or the cursor is ahead of this session (next = 0, read again from 0). " +
            "epoch identifies the event session (kept across reload_mod); if it changes, reset since to 0. " +
            "Event: {seq, ut, met, type, step, data}; types plan_started, plan_replaced, step_started, step_completed, staged, stage_failed, plan_completed, plan_aborted {reason, condition, message}.")]
        public Task<string> GetFlightEvents([Description("Return events after this seq (default 0)")] long since = 0,
            [Description("Maximum events (1..500, default 100)")] int limit = 100, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, $"api/flight/events?since={since}&limit={limit}", null, ct);
    }
}
