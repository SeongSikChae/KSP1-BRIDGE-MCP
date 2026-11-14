using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class FlightTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_status", ReadOnly = true)]
        [Description("KspBridge mod status: current game scene, whether there is an active vessel, logic version (version), loader_version, load_count (logic loads since KSP start), logic_error when the logic failed to load, and editor ({facility, ready, craft, part_count} in the VAB/SPH, null elsewhere; poll until editor.ready after open_editor), flight ({active_vessel {id, name} or null, plan_state idle|running|completed|aborted, step (index, null without a plan), sas, rcs (on/off), translating} in the flight scene, null elsewhere; details in get_flight), and input_locks [{id, mask (hex), controls}]: the game's current input locks, " +
            "controls = blocked ControlTypes bits (e.g. CAMERACONTROLS for camera drag; aliases joined A|B, unnamed bits as hex), " +
            "and reset {scene_ready (false from a scene load request until that scene is ready; FLIGHT only after its initial save), " +
            "requested_scene, loading_seconds (while loading; if it keeps growing, reload_mod), can_recover, can_revert_launch, " +
            "can_revert_editor, revert_editor_facility, revert_unavailable [reasons], recovery (last flight-scene recover_vessel " +
            "{state pending|done|failed, vessel, id, crew, funds, science, reputation, error})}; the can_* flags are judged only in a " +
            "loaded flight scene and are false elsewhere; translate {active, vessel_id, ended} (last rcs_translate pulse in any scene, null before any); " +
            "debug (debug tool state, null unless GameData/KspBridge/debug.cfg enables the debug tools).")]
        public Task<string> GetStatus(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/status", null, ct);

        [McpServerTool(Name = "reload_mod", Destructive = true)]
        [Description("Reload the KspBridge logic DLL (GameData/KspBridge/PluginData/KspBridge.Logic.dll) without restarting KSP. The result lists the versions before/after. If the new DLL fails to load or start, the previous logic is kept.")]
        public Task<string> ReloadMod(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/reload", new { }, ct);

        [McpServerTool(Name = "get_flight", ReadOnly = true)]
        [Description("Active vessel flight state: vessel, flight, orbit, propulsion, plan ({state:idle} without a plan; plan.warp = warp in effect: rails rate or a warp-to) " +
            "and time_warp {mode rails|physics, rate_index, rate, auto_warp (a warp-to pending or running)}, " +
            "controls {sas {on, mode, speed_mode orbit|surface|target (navball speed display), available_modes}, rcs {on, parts, monopropellant {amount, max} or null}, translate (last rcs_translate pulse, see rcs_translate; null before any)} (null with controls_error if it could not be read), " +
            "maneuver_nodes [{index, ut, time_to, prograde, normal, radial (m/s), dv, burn_time (s across stages; null when no stage can fire), dv_short (the stages cannot supply dv), " +
            "orbit_after {apoapsis, periapsis, eccentricity, inclination, period, body} (KSP's prediction after the node; null until computed)}] (KSP maneuver nodes, earliest first), " +
            "target (null without one; else as set_target returns). " +
            "Field names match flight-plan condition variables; null means not known yet (e.g. stage_dv while the game recalculates).")]
        public Task<string> GetFlight(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/flight", null, ct);

        [McpServerTool(Name = "set_sas", Destructive = true)]
        [Description("Turn the stock SAS on or off on the active vessel and set its mode, like the player (not while a flight plan runs: plan_running). " +
            "mode: stability, prograde, retrograde, normal, antinormal, radial_in, radial_out, target, anti_target, maneuver (on: true only; " +
            "omitted: stability when turning on, unchanged when already on). reference: orbit, surface or target sets the navball speed display " +
            "first, which is the frame the mode follows (the game may switch it again by itself, e.g. by altitude or near a target; check speed_mode). " +
            "Availability is the game's own check: sas_mode_unavailable lists the modes available now (e.g. target without a target, maneuver " +
            "without a node), sas_unavailable when the vessel has no SAS. Any error leaves SAS and the speed display unchanged (if the SAS group cannot be put back, e.g. a cooldown, the message says \"SAS group left ...\"). Other errors: " +
            "group_locked (action group cooldown or override), eva_not_supported, not_controllable. Returns {on, mode, speed_mode, available_modes}.")]
        public Task<string> SetSas([Description("true = on, false = off")] bool on,
            [Description("SAS mode (on: true only)")] string? mode = null,
            [Description("orbit, surface or target: navball speed display to use (on: true only)")] string? reference = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/sas", new { on, mode, reference }, ct);

        [McpServerTool(Name = "set_rcs", Destructive = true)]
        [Description("Turn the RCS action group on or off on the active vessel, like the player's R key (not while a flight plan runs: plan_running; " +
            "a plan uses the step key rcs instead). Errors leave the group unchanged: group_locked (action group cooldown or override), " +
            "eva_not_supported, not_controllable. Returns {on, parts (parts with RCS thrusters), monopropellant {amount, max} or null}.")]
        public Task<string> SetRcs([Description("true = on, false = off")] bool on, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/rcs", new { on }, ct);

        [McpServerTool(Name = "rcs_translate", Destructive = true)]
        [Description("RCS translation pulse on the active vessel for seconds (0, 30] of game time, then zero. Axes in [-1, 1] relative to the control part: " +
            "x right, y top (dorsal), z nose; 0 or at least 0.05 in magnitude (RCS dead zone). Not while a flight plan runs (plan_running); a plan " +
            "submitted during a pulse stops it. rcs_off when the RCS group is off (set_rcs first), on_rails during rails warp, eva_not_supported, " +
            "not_controllable. A new pulse replaces the running one. get_flight.controls.translate shows {active, vessel_id, x, y, z, remaining_s, " +
            "dv_local {x, y, z} (non-gravitational velocity change in the same axes, m/s), dv_note (landed, atmosphere, frame_changed: not RCS alone), " +
            "ended (done, replaced, plan, scene, vessel_changed, vessel_lost, revert, leave, recover, on_rails, rcs_off, not_controllable, docked, " +
            "error, logic_stop)}; get_status.translate keeps {active, vessel_id, ended} in any scene.")]
        public Task<string> RcsTranslate([Description("Pulse length in seconds of game time, (0, 30]")] double seconds,
            [Description("Right (+) / left (-), [-1, 1]")] double x = 0,
            [Description("Top (+) / bottom (-), [-1, 1]")] double y = 0,
            [Description("Nose (+) / tail (-), [-1, 1]")] double z = 0,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/translate", new { seconds, x, y, z }, ct);

        [McpServerTool(Name = "list_vessels", ReadOnly = true)]
        [Description("Vessels in the flight scene, nearest to the active vessel first: id (persistentId, as get_flight / get_status), name, type, situation, body, " +
            "orbit {apoapsis, periapsis, eccentricity, inclination, period, body}, active, loaded (inside physics range), distance m, " +
            "docking_ports [{part_id, node_type (size, e.g. size1), state, free}] (from the save snapshot when unloaded). Debris, flags and space objects " +
            "are left out unless include_debris. Flight scene only (not_in_flight).")]
        public Task<string> ListVessels([Description("true = also debris, flags and space objects")] bool include_debris = false, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, "api/vessels?include_debris=" + (include_debris ? "true" : "false"), null, ct);

        [McpServerTool(Name = "set_target", Destructive = true)]
        [Description("Set the active vessel's target like the player: a body (body name, e.g. Mun), a vessel (vessel_id from list_vessels), a docking port " +
            "(vessel_id + port = part_id from list_vessels; the vessel must be loaded: target_unloaded otherwise, approach it with the vessel as target first; " +
            "port_occupied when already docked), or clear: true. Not while a flight plan runs (plan_running). bad_request for an unknown body/vessel/port or the " +
            "active vessel itself. Returns {target} as get_flight.target: {kind vessel|body|port, name, id, port_id, distance, relative_speed, " +
            "closest_approach {distance, ut, time_to, relative_speed} (vessel or port target orbiting the same body)}.")]
        public Task<string> SetTarget(
            [Description("Body name, e.g. Mun")] string? body = null,
            [Description("Vessel id (persistentId) from list_vessels")] double? vessel_id = null,
            [Description("Docking port part_id from list_vessels (with vessel_id)")] double? port = null,
            [Description("true = clear the target")] bool? clear = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/target", new { body, vessel_id, port, clear }, ct);

        [McpServerTool(Name = "add_maneuver_node", Destructive = true)]
        [Description("Add a KSP maneuver node to the active vessel at an orbit position (at: apoapsis, periapsis, ascending_node, descending_node; " +
            "nodes are relative to the target's orbit plane when the target orbits the same body, else the equator) or at ut (game time, not in the past); " +
            "give at or ut. Δv components prograde, normal, radial in m/s (default 0). Not while a flight plan runs (plan_running). " +
            "Errors: nodes_unavailable (no patched conics, e.g. tracking station level), no_solution (the position is undefined: nodes of a coplanar orbit, " +
            "apoapsis of a hyperbola, a passed periapsis), bad_request. Returns {maneuver_nodes} as in get_flight.")]
        public Task<string> AddManeuverNode(
            [Description("apoapsis, periapsis, ascending_node, descending_node or ut")] string? at = null,
            [Description("Node time (game UT seconds) when at is ut or omitted")] double? ut = null,
            [Description("Prograde Δv m/s")] double prograde = 0,
            [Description("Normal Δv m/s")] double normal = 0,
            [Description("Radial (out) Δv m/s")] double radial = 0,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/nodes/add", new { at, ut, prograde, normal, radial }, ct);

        [McpServerTool(Name = "plan_maneuver", Destructive = true)]
        [Description("Compute and create KSP maneuver nodes for a target orbit on the active vessel (execute them with the flight-plan mode execute_node, one node per step). " +
            "kind: change_apoapsis (altitude; at apoapsis|periapsis|ut, default periapsis: the node point becomes the periapsis), " +
            "change_periapsis (altitude; at default apoapsis), circularize (at default apoapsis), " +
            "change_inclination (inclination deg against the target's orbit plane when the target orbits the same body, else the equator; " +
            "at ascending_node|descending_node|auto, default auto = the node at the larger radius; any point when already coplanar), " +
            "hohmann (altitude of the target circular orbit; two nodes: transfer and arrival circularization; at apoapsis|periapsis|ut|auto, auto = now + 60 s), " +
            "transfer_body (needs a body target orbiting the same parent, e.g. set_target Mun, and a near-circular orbit; periapsis = wanted periapsis altitude inside the body's SOI; " +
            "one transfer node; returns computed_periapsis and predicted.encounter / encounter_periapsis from KSP; correct after the SOI change with change_periapsis at ut), " +
            "capture (at the periapsis of the current orbit, also a hyperbola after an SOI entry; altitude = wanted apoapsis, default the periapsis altitude = circular). " +
            "Orbit-position nodes closer than 30 s move one orbit later. " +
            "Altitudes are above sea level, above the atmosphere and inside the SOI. Existing nodes are replaced unless append: true (plans from the orbit after the last node). " +
            "Not while a flight plan runs (plan_running). Errors: bad_request (an apoapsis below / periapsis above the node point, altitude out of range, at not allowed), " +
            "no_solution (undefined time, a non-finite result, an eccentric start orbit for transfer_body, the search time budget), no_target (transfer_body without a body target of the same parent), " +
            "no_encounter (no SOI entry found), nodes_unavailable. Returns {maneuver_nodes, created, total_dv, burn_time, dv_short, predicted (KSP's orbit after the last node)}.")]
        public Task<string> PlanManeuver(
            [Description("change_apoapsis, change_periapsis, circularize, change_inclination, hohmann, transfer_body or capture")] string kind,
            [Description("Target altitude m above sea level (change_apoapsis, change_periapsis, hohmann; capture apoapsis)")] double? altitude = null,
            [Description("Target inclination deg (change_inclination)")] double? inclination = null,
            [Description("Node position: apoapsis, periapsis, ascending_node, descending_node, ut or auto (allowed values depend on kind)")] string? at = null,
            [Description("Node time (game UT seconds) with at ut")] double? ut = null,
            [Description("true = keep the existing nodes and plan after the last one")] bool? append = null,
            [Description("Wanted periapsis altitude m inside the target body's SOI (transfer_body)")] double? periapsis = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/plan_maneuver", new { kind, altitude, inclination, at, ut, append, periapsis }, ct);

        [McpServerTool(Name = "update_maneuver_node", Destructive = true)]
        [Description("Change a maneuver node of the active vessel: index from get_flight.maneuver_nodes (0 = earliest); only the given fields change " +
            "(ut not in the past; prograde, normal, radial m/s). Not while a flight plan runs (plan_running). Errors: nodes_unavailable, bad_request " +
            "(index out of range). Returns {maneuver_nodes}.")]
        public Task<string> UpdateManeuverNode(
            [Description("Node index from get_flight.maneuver_nodes")] int index,
            [Description("New node time (game UT seconds)")] double? ut = null,
            [Description("Prograde Δv m/s")] double? prograde = null,
            [Description("Normal Δv m/s")] double? normal = null,
            [Description("Radial (out) Δv m/s")] double? radial = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/nodes/update", new { index, ut, prograde, normal, radial }, ct);

        [McpServerTool(Name = "remove_maneuver_node", Destructive = true)]
        [Description("Remove a maneuver node of the active vessel by index, or all of them with all: true (give one of the two). " +
            "Not while a flight plan runs (plan_running). Returns {maneuver_nodes}.")]
        public Task<string> RemoveManeuverNode(
            [Description("Node index from get_flight.maneuver_nodes")] int? index = null,
            [Description("true = remove every node")] bool? all = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/nodes/remove", new { index, all }, ct);

        [McpServerTool(Name = "submit_flight_plan", Destructive = true)]
        [Description("Start a flight plan on the active vessel (replaces a running plan). plan = {auto_stage?, staging_options?, abort?, steps:[{name?, mode, ...mode args, on_start?, until?, abort?, timeout_s?, rcs?}]}. " +
            "rcs (any step): true/false sets the RCS group when the step starts (RCS adds its torque to attitude control; e.g. true for vacuum turns); " +
            "a step without rcs after the plan changed RCS puts it back to the state before the plan; the plan end restores it too. " +
            "Modes: attitude (pitch+heading [+roll] or reference prograde|retrograde|surface_prograde|normal|antinormal|radial_in|radial_out, throttle 0..1; until required); " +
            "gravity_turn (target_apoapsis m required and above the atmosphere, heading 90, turn_start_altitude 1000, turn_end_altitude 45000, final_pitch 0, shape 0.6, " +
            "max_throttle 1, max_aoa 5 deg, aoa_free_q 1 kPa; completes above the atmosphere once the apoapsis is reached at throttle 0); " +
            "coast (throttle 0, reference prograde by default or none for no attitude control, warp true; until required: with warp it time-warps on rails above the atmosphere " +
            "toward the time leaves of until (time_to_apoapsis|time_to_periapsis with < or <=, met|step_time with > or >=) and stops 15..10 s before; " +
            "a player cancel is respected for the rest of the step; no plan warp while the player's own warp is on); " +
            "circularize (tolerance 0.5 m/s; waits, with warp, until the burn start before apoapsis, or burns at once when that time or the apoapsis has passed; " +
            "burns along the Δv to the circular velocity at the current position, only while attitude_error <= 5 deg; completes by itself); " +
            "land (margin 0.8, final_altitude 20 m, final_speed 2 m/s, gear_altitude 500 m or false; from a descent: ground height is the radar altitude " +
            "minus the vessel bottom; starts the burn when the vertical stopping distance at margin × full thrust reaches the ground and the axis is within 15 deg " +
            "of the target, or regardless of attitude once there is no margin left or below final_altitude; then holds final_speed below final_altitude; " +
            "the attitude tilts from vertical toward surface retrograde by 5 deg per m/s of horizontal speed; turns on the Gear group at gear_altitude; " +
            "events landing_infeasible (accel judged before and during the burn, dv only during it; once each; a burning tick where it holds gets throttle 1 " +
            "unless the axis is still turning after a forced start or points below the horizon), touchdown, landed {tilt_deg, tipped}; throttle 0 after touchdown " +
            "and completes by itself once landed and still (speed < 0.5 m/s, angular speed < 0.05 rad/s) for 2 s, so add timeout_s if it may keep sliding; " +
            "submitted on an already landed vessel it reports touchdown and landed at once (refused with no_thrust if no engine is running; on the pad, PRELAUNCH is not landed, so the step does not end by itself); no warp: put a coast step before it). " +
            "Conditions: {var, op, value, for_s?} | {all:[...]} | {any:[...]} | [...] (all); var is a get_flight field name (flight, orbit, propulsion, step_time, met, attitude_error), " +
            "string vars (situation, staging) allow only == and !=; a var that is null (not known) is false. " +
            "on_start:[\"stage\"] activates the next stage when the step starts (needed for the first ignition on the pad). " +
            "auto_stage:true runs burnout → tailoff → separate → settle → ignite by itself (parallel boosters keep the core burning and are dropped only once " +
            "the dynamic pressure is at most max_separate_q, at most max_separate_q_wait_s after the wait began; serial stages cut the throttle, " +
            "separate and wait settle_distance before igniting); it never fires a stage when nothing with an engine is left (out_of_stages), and is refused " +
            "(invalid_plan control_part_jettisoned) when a later separation would drop the control part. A flamed-out engine with propellant left " +
            "(air-starved jets included) is a pause, not a burnout: with no working engine, staging \"paused\" keeps the throttle while KSP relights it, " +
            "and after max_flameout_pause_s of wanted thrust it ends with flameout_timeout, then no_thrust. staging_options: tailoff_ratio 0.05, max_tailoff_s 5, " +
            "settle_distance 2 m, max_settle_s 3, max_separate_q 25 kPa, max_separate_q_wait_s 30, max_flameout_pause_s 3. To restart after a no_thrust abort (out_of_stages, ignite_blocked, stage_failed), on_start:[\"stage\"] is required. " +
            "Invalid plans fail with invalid_plan \"<path>: <reason>\"; no engine and nothing staging fails with no_thrust. " +
            "Returns {plan_id, state, steps, event_seq, epoch}; follow progress with get_flight_events.")]
        public Task<string> SubmitFlightPlan([Description("Flight plan JSON object")] JsonElement plan, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/flight/plan", new { plan }, ct);

        [McpServerTool(Name = "replace_flight_steps", Destructive = true)]
        [Description("Replace the steps after the current one in the running flight plan (include_current: true replaces from the current step and starts it now). " +
            "Same step format as submit_flight_plan (step key rcs included: with include_current the new current step applies it at once); error paths index the new steps array (steps[i]...). Empty steps with include_current false drops the remaining steps. " +
            "A staging sequence in progress finishes. 409 no_plan without a running plan. Returns {plan_id, state, steps, event_seq, epoch}.")]
        public Task<string> ReplaceFlightSteps([Description("Steps array")] JsonElement steps,
            [Description("Replace from the current step and start it now (default false)")] bool include_current = false, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/steps", new { steps, include_current }, ct);

        [McpServerTool(Name = "abort_flight", Destructive = true)]
        [Description("Abort the running flight plan: throttle 0, SAS back to its state and mode before the plan, RCS back to its state before the plan if the plan changed it (step key rcs), control released (event plan_aborted reason user). no_plan when nothing runs.")]
        public Task<string> AbortFlight(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/flight/abort", new { }, ct);

        [McpServerTool(Name = "revert_flight", Destructive = true)]
        [Description("Revert the flight like the pause menu: target launch (back to the moment after launch) or editor (back to the VAB/SPH " +
            "the craft came from). A running flight plan is aborted first (plan_aborted reason revert). Returns {accepted, target, facility} " +
            "at once; poll get_status until reset.requested_scene is the target scene (FLIGHT or EDITOR) and reset.scene_ready (scene " +
            "already reads the new scene while loading; if reset.loading_seconds keeps growing, reload_mod). revert_unavailable with the " +
            "reasons (no_post_init_state, restart_disabled, no_prelaunch_state, leave_to_editor_disabled, unknown_facility) when the game " +
            "does not allow it. scene_loading while a scene loads; not_supported in mission games; recovery_pending while recover_vessel " +
            "is in progress.")]
        public Task<string> RevertFlight([Description("launch or editor")] string target, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/revert", new { target }, ct);

        [McpServerTool(Name = "leave_flight", Destructive = true)]
        [Description("Go to the space center like the flight scene's Space Center button: saves, the vessel stays where it is. A running " +
            "flight plan is aborted only when leaving is allowed (plan_aborted reason leave). Returns {accepted} at once; poll get_status until " +
            "reset.requested_scene is SPACECENTER and reset.scene_ready (scene already reads SPACECENTER while loading; if " +
            "reset.loading_seconds keeps growing, reload_mod). leave_unavailable when the game does not allow it now (under thrust, in the " +
            "air, moving over the surface, on a ladder, ...: the plan keeps running) or in this game; scene_loading; not_supported in " +
            "mission games; recovery_pending.")]
        public Task<string> LeaveFlight(CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/flight/leave", new { }, ct);

        [McpServerTool(Name = "recover_vessel", Destructive = true)]
        [Description("Recover vessels like the game, without the recovery result dialog. In flight: the active vessel (landed or splashed " +
            "on the home body; get_status reset.can_recover), via the space center like the game's recover button; a running plan is " +
            "aborted only when recovery is allowed (reason recover); returns {accepted, vessel} at once, the result shows in get_status " +
            "reset.recovery {state pending|done|failed, error, ...}: poll until state is not pending (reset.requested_scene SPACECENTER " +
            "and reset.scene_ready come first; if reset.loading_seconds keeps growing, reload_mod). In the editor or space center: every " +
            "vessel landed exactly at site; returns {recovered: [...]}. Each result {vessel, id (as get_flight), crew, funds, science, " +
            "reputation}: the changes over the recovery (contract rewards included), null when the game has no such currency. Errors: " +
            "not_recoverable, leave_unavailable, save_disabled, scene_loading, recovery_pending, no_game, not_supported (mission games, " +
            "or no space center loaded), scene_not_allowed (tracking station and other scenes), 400 for a missing or unknown site.")]
        public Task<string> RecoverVessel(
            [Description("Launch site, e.g. LaunchPad, Runway; required outside the flight scene, ignored in flight")] string? site = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/vessel/recover", new { site }, ct);

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
            "Event: {seq, ut, met, type, step, data}; types plan_started, plan_replaced, steps_replaced, step_started, step_completed, stage_burnout, staged {stage, reason, q_kpa (separate only)}, " +
            "staging_completed {stage, tailoff_s, settle_s, held_s (dynamic-pressure wait), ignited}, separation_held {stage, q_kpa}, " +
            "separation_forced {stage, q_kpa, reason timeout|serial (the core ran dry while waiting: separates as a serial stage)}, flameout_pause {stage}, flameout_resumed {stage, pause_s}, flameout_timeout {stage, pause_s}, " +
            "out_of_stages, ignite_blocked, stage_failed, warp_started, warp_stopped {rate, until_ut} (plan warps only), warp_unavailable {reason player_warp|not_supported|mode_switch_refused}, " +
            "landing_infeasible {reason accel|dv, a_req, a_max, a_max_basis best|actual, dv_need, dv_left, altitude} (land; once per step and reason; " +
            "altitude = ground height of the vessel bottom, not the sea-level altitude; dv_need/dv_left are null for accel, a_req is null below final_altitude), " +
            "touchdown {vertical_speed (up positive, negative when descending), horizontal_speed, situation} (land; first ground contact, or the first tick when submitted already landed), landed {tilt_deg, tipped (tilt > 45), time_since_touchdown_s} (land; on completion), " +
            "plan_completed, plan_aborted {reason, condition, message}; " +
            "part events on any loaded vessel with or without a plan (debris included, active = the active vessel; step is -1 unless a plan is running; kept apart from plan events, so a vessel breaking apart never pushes plan events out): " +
            "part_lost {part, part_id, vessel_id, active, vessel, q_kpa, g, temp, max_temp, skin_temp, skin_max_temp} (not for whole-vessel removal: recovery, debris cleanup, EVA boarding), " +
            "part_collision {part, part_id, vessel_id, active, kind part|crash|splashdown, other, speed m/s, crash_tolerance} (a collision that destroys the part comes right after its part_lost), " +
            "part_limit {part, part_id, vessel_id, active, kind overheat|pressure|g, msg \"current / limit\"}, part_joint_break {part, part_id, vessel_id, active, parent, parent_id, force} (physical breaks only, not decouplers).")]
        public Task<string> GetFlightEvents([Description("Return events after this seq (default 0)")] long since = 0,
            [Description("Maximum events (1..500, default 100)")] int limit = 100, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, $"api/flight/events?since={since}&limit={limit}", null, ct);
    }
}
