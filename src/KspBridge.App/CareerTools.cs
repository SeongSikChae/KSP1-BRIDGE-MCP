using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class CareerTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_career", ReadOnly = true)]
        [Description("Career status: funds, science, reputation, UT, and facility levels (level/max_level, 0-based). Career mode only.")]
        public Task<string> GetCareer(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/career", null, ct);

        [McpServerTool(Name = "list_contracts", ReadOnly = true)]
        [Description("Contracts with id, title, state, rewards and objective parameters (nested). Default: offered + active. " +
            "waypoints (when the contract has survey/visit spots): name, body, latitude, longitude, altitude, on_surface, parameter (objective title); " +
            "in flight on the same body also distance (m, great circle) and bearing (deg from the active vessel).")]
        public Task<string> ListContracts([Description("offered, active or completed (optional)")] string? state = null, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, string.IsNullOrWhiteSpace(state) ? "api/contracts" : "api/contracts?state=" + Uri.EscapeDataString(state), null, ct);

        [McpServerTool(Name = "list_facilities", ReadOnly = true)]
        [Description("KSC facilities: id, level/max_level, upgrade_cost and can_afford (KSC scene only, else null), and effects of the current and next level (pad/runway max mass and size, VAB/SPH max parts and action groups, R&D science cost limit, Mission Control active contracts and flight_planning = maneuver nodes, Tracking Station patches ahead, Astronaut Complex crew and EVA, Administration strategies; null = unlimited). Career mode only.")]
        public Task<string> ListFacilities(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/facilities", null, ct);

        [McpServerTool(Name = "upgrade_facility", Destructive = true)]
        [Description("Upgrade a KSC facility by one level (id from list_facilities). KSC scene only; spends funds; cannot be undone. Refused when at max level, when a vessel sits on the pad/runway being upgraded, or when funds are short.")]
        public Task<string> UpgradeFacility(string id, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, $"api/facilities/{Uri.EscapeDataString(id)}/upgrade", new { }, ct);

        [McpServerTool(Name = "accept_contract")]
        [Description("Accept an offered contract by id (from list_contracts).")]
        public Task<string> AcceptContract([Description(ContractIdArg)] string id, CancellationToken ct)
            => ContractAction(id, "accept", ct);

        [McpServerTool(Name = "decline_contract", Destructive = true)]
        [Description("Decline an offered contract by id. It disappears from the offers.")]
        public Task<string> DeclineContract([Description(ContractIdArg)] string id, CancellationToken ct)
            => ContractAction(id, "decline", ct);

        /// <summary>
        /// Contract ids can exceed 2^53 (e.g. 192925036314244835); a JSON number argument loses digits in clients that
        /// parse numbers as doubles, so the id is taken as a string and passed through unchanged.
        /// </summary>
        private const string ContractIdArg = "Contract id exactly as list_contracts shows it, as a string (ids can have 19 digits)";

        private Task<string> ContractAction(string id, string action, CancellationToken ct)
        {
            string text = id?.Trim() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^-?[0-9]{1,19}$"))
            {
                return Task.FromResult("error: bad_request: contract id must be an integer (as a string).");
            }
            return client.SendAsync(HttpMethod.Post, $"api/contracts/{text}/{action}", new { }, ct);
        }
    }

    [McpServerToolType]
    public sealed class ScienceTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_science", ReadOnly = true)]
        [Description("Experiments on the active vessel: id, title, can_run_now, expected_science for the current situation/biome, stored data and its value.")]
        public Task<string> GetScience(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/science", null, ct);

        [McpServerTool(Name = "run_experiment")]
        [Description("Run an experiment by id (from get_science). Data stays in the part until recovered or transmitted.")]
        public Task<string> RunExperiment(string id, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/science/run", new { id }, ct);

        [McpServerTool(Name = "discard_science", Destructive = true)]
        [Description("Discard stored data of an experiment by id and reset it if resettable. Cannot be undone.")]
        public Task<string> DiscardScience(string id, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/science/discard", new { id }, ct);
    }

    [McpServerToolType]
    public sealed class LaunchTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "list_craft", ReadOnly = true)]
        [Description("Saved craft (this save's Ships and stock Ships): name, facility, stock flag.")]
        public Task<string> ListCraft([Description("VAB or SPH (optional)")] string? facility = null, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, string.IsNullOrWhiteSpace(facility) ? "api/craft" : "api/craft?facility=" + Uri.EscapeDataString(facility), null, ct);

        [McpServerTool(Name = "list_crew", ReadOnly = true)]
        [Description("Kerbals: name, trait (Pilot/Engineer/Scientist), level, status, available.")]
        public Task<string> ListCrew(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/crew", null, ct);

        [McpServerTool(Name = "launch_craft", Destructive = true)]
        [Description("Put a saved craft on the launch pad (VAB) or runway (SPH) and start the flight. Spends funds in career. Returns accepted immediately; poll get_status until scene is FLIGHT.")]
        public Task<string> LaunchCraft(
            [Description("Craft name (from list_craft)")] string craft,
            [Description("VAB or SPH")] string facility = "VAB",
            [Description("LaunchPad or Runway (default by facility)")] string? site = null,
            [Description("Kerbal names to seat in order; empty = game default crew")] string[]? crew = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/launch", new { craft, facility, site, crew = crew ?? [] }, ct);

        [McpServerTool(Name = "recover_vessel", Destructive = true)]
        [Description("Recover the landed or splashed-down active vessel (refunds parts, banks science, ends the flight).")]
        public Task<string> RecoverVessel(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/recover", new { }, ct);

        [McpServerTool(Name = "revert_to_launch", Destructive = true)]
        [Description("Revert the current flight to just before launch (the flight's progress is lost).")]
        public Task<string> RevertToLaunch(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/revert", new { }, ct);
    }

    [McpServerToolType]
    public sealed class AscentTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "get_stages", ReadOnly = true)]
        [Description("Game-computed per-stage delta-v (vac/ASL/actual), TWR, burn time, masses and the parts each stage activates. staging lists every stage still to fire (decouple-only stages included) with drops (parts that leave the vessel) and control (gimbal, reaction wheel torque, control surfaces, RCS, steerable). analysis gives com/col (root frame, +Y = craft up), stability and the lowest point; warnings flag unsteerable stages, protected parts dropped, CoL ahead of CoM and parts below the engines.")]
        public Task<string> GetStages(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/stages", null, ct);

        [McpServerTool(Name = "set_attitude")]
        [Description("Hold an attitude until released. frame 'surface' (default): pitch = degrees above the horizon (90 = straight up), heading = compass degrees (90 = east), roll optional (omit = free). " +
            "Other frames follow a moving reference instead (no pitch/heading/roll): prograde, retrograde, normal, antinormal, radial_in, radial_out (orbital velocity), " +
            "surface_prograde, surface_retrograde (surface velocity; e.g. re-entry), node (first maneuver node burn direction); pitchOffset raises the nose from the reference toward local up, yawOffset turns it clockwise seen from above. " +
            "maxAoaDeg keeps the nose within that angle of the surface velocity while dynamic pressure is >= 1 kPa (any frame). " +
            "mode 'own' = KspBridge controller, 'sas' = stock SAS steering to the same target (needs SAS, works at any pilot level).")]
        public Task<string> SetAttitude(
            double? pitch = null,
            double? heading = null,
            [Description("own or sas")] string mode = "own",
            double? roll = null,
            [Description("surface (default), prograde, retrograde, normal, antinormal, radial_in, radial_out, surface_prograde, surface_retrograde, node")] string? frame = null,
            [Description("Degrees, frames other than surface")] double? pitchOffset = null,
            [Description("Degrees, frames other than surface")] double? yawOffset = null,
            [Description("Max angle of attack (deg) while dynamic pressure >= 1 kPa")] double? maxAoaDeg = null,
            CancellationToken ct = default)
        {
            Dictionary<string, object?> body = new() { ["mode"] = mode };
            if (frame is null or "surface")
            {
                body["pitch"] = pitch;
                body["heading"] = heading;
                body["roll"] = roll;
            }
            else if (pitch.HasValue || heading.HasValue || roll.HasValue)
            {
                // Pass them on so the mod rejects them instead of silently ignoring a requested roll.
                body["pitch"] = pitch;
                body["heading"] = heading;
                body["roll"] = roll;
            }
            if (frame != null)
            {
                body["frame"] = frame;
            }
            if (pitchOffset.HasValue)
            {
                body["pitch_offset"] = pitchOffset.Value;
            }
            if (yawOffset.HasValue)
            {
                body["yaw_offset"] = yawOffset.Value;
            }
            if (maxAoaDeg.HasValue)
            {
                body["max_aoa_deg"] = maxAoaDeg.Value;
            }
            return client.SendAsync(HttpMethod.Post, "api/attitude", body, ct);
        }

        [McpServerTool(Name = "release_attitude")]
        [Description("Stop holding the attitude target.")]
        public Task<string> ReleaseAttitude(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/attitude/off", new { }, ct);

        [McpServerTool(Name = "wait_until")]
        [Description("Wait until a condition holds (or timeout, max 120 s). Returns met, elapsed, value and a vessel snapshot; returns early if the vessel changes or the scene changes. Situation values e.g. LANDED, SPLASHED, FLYING, SUB_ORBITAL, ORBITING. " + TriggerTools.Fields + " " +
            "Also returns early (met=false, reason='anomaly', anomaly={type, detail}) on abortOn anomalies: part_lost (a part of this vessel destroyed), " +
            "control_lost (attitude hold or SAS on, pitch/yaw input at its limit for 2.5 s but the vessel does not turn that way: no steering, no power, or aero overpowering), " +
            "flameout (an engine flamed out). Default abortOn = [part_lost, control_lost]; [] turns it off. For actions that must happen exactly when a condition is met, use add_trigger instead.")]
        public Task<string> WaitUntil(
            string? field = null,
            [Description(">=, <= or ==")] string? op = null,
            [Description("Number, string (situation, node_exec, ascent_state) or boolean (stage_flameout, any_flameout)")] System.Text.Json.JsonElement? value = null,
            [Description("Seconds, 1..120")] double timeout = 60,
            [Description(TriggerTools.ConditionHelp)] System.Text.Json.JsonElement? condition = null,
            [Description("Anomalies that end the wait early: part_lost, control_lost, flameout. Omit = [part_lost, control_lost]; [] = none.")] string[]? abortOn = null,
            CancellationToken ct = default)
        {
            if (condition.HasValue == (field != null))
            {
                return Task.FromResult("error: bad_request: give either condition or field/op/value.");
            }
            Dictionary<string, object?> body = condition.HasValue
                ? new() { ["condition"] = TriggerTools.NormalizeCondition(condition.Value) }
                : new() { ["field"] = field, ["op"] = op, ["value"] = value.HasValue ? TriggerTools.NormalizeValue(field!, value.Value) : null };
            body["timeout"] = timeout;
            if (abortOn != null)
            {
                body["abort_on"] = abortOn;
            }
            return client.SendAsync(HttpMethod.Post, "api/wait", body, ct, TimeSpan.FromSeconds(Math.Clamp(timeout, 1, 120) + 15));
        }
    }
}
