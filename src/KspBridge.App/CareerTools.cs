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
        [Description("Contracts with id, title, state, rewards and objective parameters (nested). Default: offered + active.")]
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
        [Description("Game-computed per-stage delta-v (vac/ASL/actual), TWR, burn time, masses and the parts each stage activates.")]
        public Task<string> GetStages(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/stages", null, ct);

        [McpServerTool(Name = "set_attitude")]
        [Description("Hold a surface-relative attitude until released. pitch: degrees above the horizon (90 = straight up), heading: compass degrees (90 = east), roll optional (omit = free). mode 'own' = KspBridge controller, 'sas' = stock SAS lock (needs SAS).")]
        public Task<string> SetAttitude(
            double pitch,
            double heading,
            [Description("own or sas")] string mode = "own",
            double? roll = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/attitude", new { mode, pitch, heading, roll }, ct);

        [McpServerTool(Name = "release_attitude")]
        [Description("Stop holding the attitude target.")]
        public Task<string> ReleaseAttitude(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/attitude/off", new { }, ct);

        [McpServerTool(Name = "wait_until")]
        [Description("Wait until a condition holds (or timeout, max 120 s). Returns met, elapsed, value and a vessel snapshot; returns early if the vessel changes or the scene changes. Situation values e.g. LANDED, SPLASHED, FLYING, SUB_ORBITAL, ORBITING. " + TriggerTools.Fields + " For actions that must happen exactly when a condition is met, use add_trigger instead.")]
        public Task<string> WaitUntil(
            string field,
            [Description(">=, <= or ==")] string op,
            [Description("Number, string (situation) or boolean (stage_flameout, any_flameout)")] System.Text.Json.JsonElement value,
            [Description("Seconds, 1..120")] double timeout = 60,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/wait", new { field, op, value = TriggerTools.NormalizeValue(field, value), timeout }, ct,
                TimeSpan.FromSeconds(Math.Clamp(timeout, 1, 120) + 15));
    }
}
