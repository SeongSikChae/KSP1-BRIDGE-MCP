using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class FlightTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_status", ReadOnly = true)]
        [Description("KspBridge mod status: current game scene, whether there is an active vessel, logic version (version), loader_version, load_count (logic loads since KSP start), logic_error when the logic failed to load, and editor ({facility, ready, craft, part_count} in the VAB/SPH, null elsewhere; poll until editor.ready after open_editor), and input_locks [{id, mask (hex), controls}]: the game's current input locks, " +
            "controls = blocked ControlTypes bits (e.g. CAMERACONTROLS for camera drag; aliases joined A|B, unnamed bits as hex).")]
        public Task<string> GetStatus(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/status", null, ct);

        [McpServerTool(Name = "reload_mod", Destructive = true)]
        [Description("Reload the KspBridge logic DLL (GameData/KspBridge/PluginData/KspBridge.Logic.dll) without restarting KSP. The result lists the versions before/after. If the new DLL fails to load or start, the previous logic is kept.")]
        public Task<string> ReloadMod(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/reload", new { }, ct);

        [McpServerTool(Name = "get_flight", ReadOnly = true)]
        [Description("Active vessel flight state: vessel, flight, orbit, propulsion, plan ({state:idle} without a plan; plan.warp = warp in effect: rails rate or a warp-to) " +
            "and time_warp {mode rails|physics, rate_index, rate, auto_warp (a warp-to pending or running)}. " +
            "Field names match flight-plan condition variables; null means not known yet (e.g. stage_dv while the game recalculates).")]
        public Task<string> GetFlight(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/flight", null, ct);

        [McpServerTool(Name = "submit_flight_plan", Destructive = true)]
        [Description("Start a flight plan on the active vessel (replaces a running plan). plan = {auto_stage?, staging_options?, abort?, steps:[{name?, mode, ...mode args, on_start?, until?, abort?, timeout_s?}]}. " +
            "Modes: attitude (pitch+heading [+roll] or reference prograde|retrograde|surface_prograde|normal|antinormal|radial_in|radial_out, throttle 0..1; until required); " +
            "gravity_turn (target_apoapsis m required and above the atmosphere, heading 90, turn_start_altitude 1000, turn_end_altitude 45000, final_pitch 0, shape 0.6, " +
            "max_throttle 1, max_aoa 5 deg, aoa_free_q 1 kPa; completes above the atmosphere once the apoapsis is reached at throttle 0); " +
            "coast (throttle 0, reference prograde by default or none for no attitude control, warp true; until required: with warp it time-warps on rails above the atmosphere " +
            "toward the time leaves of until (time_to_apoapsis|time_to_periapsis with < or <=, met|step_time with > or >=) and stops 15..10 s before; " +
            "a player cancel is respected for the rest of the step; no plan warp while the player's own warp is on). " +
            "Conditions: {var, op, value, for_s?} | {all:[...]} | {any:[...]} | [...] (all); var is a get_flight field name (flight, orbit, propulsion, step_time, met, attitude_error), " +
            "string vars (situation, staging) allow only == and !=; a var that is null (not known) is false. " +
            "on_start:[\"stage\"] activates the next stage when the step starts (needed for the first ignition on the pad). " +
            "auto_stage:true runs burnout → tailoff → separate → settle → ignite by itself (parallel boosters keep the core burning; serial stages cut the throttle, " +
            "separate and wait settle_distance before igniting); it never fires a stage when nothing with an engine is left (out_of_stages), and is refused " +
            "(invalid_plan control_part_jettisoned) when a later separation would drop the control part. staging_options: tailoff_ratio 0.05, max_tailoff_s 5, " +
            "settle_distance 2 m, max_settle_s 3. To restart after a no_thrust abort (out_of_stages, ignite_blocked, stage_failed), on_start:[\"stage\"] is required. " +
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

        [McpServerTool(Name = "set_time_warp", Destructive = true)]
        [Description("Set time warp like the player's keys (not part of a flight plan): rate_index 0 stops warp and cancels a warp-to; " +
            "1.. sets the rails rate (or the physics rate with physics: true; switching mode passes through rate 0). The game may cap the rate " +
            "(altitude, acceleration): returns the actual {mode, rate_index, rate, auto_warp}; re-read get_flight.time_warp to confirm. " +
            "warp_to_running while a warp-to runs (stop it first), mode_switch_refused when the game refuses the rails/physics switch. " +
            "A flight plan treats this like the player: stopping a coast warp latches it for the rest of the step; a step change or abort stops rails warp; a coast step does not take over a warp set here (warp_unavailable player_warp).")]
        public Task<string> SetTimeWarp([Description("Rate index; 0 = stop")] int rate_index,
            [Description("Physics warp instead of rails (default false)")] bool physics = false, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/time_warp", new { rate_index, physics }, ct);

        [McpServerTool(Name = "get_flight_events", ReadOnly = true)]
        [Description("Flight events with seq > since (oldest first). Returns {events, next, truncated, epoch}: pass next as the next since. " +
            "truncated: events were dropped or the cursor is ahead of this session (next = 0, read again from 0). " +
            "epoch identifies the event session (kept across reload_mod); if it changes, reset since to 0. " +
            "Event: {seq, ut, met, type, step, data}; types plan_started, plan_replaced, steps_replaced, step_started, step_completed, stage_burnout, staged {stage, reason}, " +
            "staging_completed {stage, tailoff_s, settle_s, ignited}, out_of_stages, ignite_blocked, stage_failed, warp_started, warp_stopped {rate, until_ut} (plan warps only), warp_unavailable {reason player_warp|not_supported|mode_switch_refused}, plan_completed, plan_aborted {reason, condition, message}.")]
        public Task<string> GetFlightEvents([Description("Return events after this seq (default 0)")] long since = 0,
            [Description("Maximum events (1..500, default 100)")] int limit = 100, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, $"api/flight/events?since={since}&limit={limit}", null, ct);
    }
}
