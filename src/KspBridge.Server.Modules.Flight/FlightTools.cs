using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("flight")]

namespace KspBridge.Server.Modules.Flight
{
    /// <summary>Flight observation of the active vessel (paired with the mod's flight module).</summary>
    [McpServerToolType]
    public sealed class FlightTools
    {
        private readonly IBridgeClient bridge;

        public FlightTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "get_flight", ReadOnly = true, Idempotent = true)]
        [Description("Flight state of the active vessel (flight scene only; read-only). Default answer: vessel, situation (PRELAUNCH, " +
                     "LANDED, FLYING, SUB_ORBITAL, ORBITING, ESCAPING, ...), body, ut and met_s (s); position (latitude/longitude deg, " +
                     "altitude_m above sea level, terrain_altitude_m above ground or water); speed (surface, orbital, vertical (up +), " +
                     "horizontal surface, m/s); orbit (apoapsis/periapsis m above sea level, inclination deg, eccentricity, semi-major " +
                     "axis m, period s, time to apoapsis/periapsis s; apoapsis, period and time to apoapsis are null on open orbits); " +
                     "attitude (navball pitch/heading/roll deg, right wing down is + roll); control (throttle 0-1, sas, rcs); mass_t, " +
                     "thrust_kn (current), twr (current thrust / weight here); stage: last_activated (null before staging or when its " +
                     "parts are gone), next (fires on the next staging), reference (last_activated, else next) with its delta-v (here " +
                     "and vacuum, m/s), burn time and TWR from the game's delta-v calculator, and the vessel's total delta-v. When the " +
                     "calculator has no values yet, delta_v_ready is false with a reason and the delta-v values are null. " +
                     "include adds: stages (per stage, first to fire first: start/end mass t, delta-v here/sea level/vacuum, TWR " +
                     "here/sea level/vacuum, burn time), resources (name, amount, max per resource), body (radius_m, gm_m3ps2, " +
                     "rotation_period_s, atmosphere_top_m or null, soi_m or null for the Sun). Outside a ready flight scene: not_ready.")]
        public async Task<string> GetFlight(
            [Description("Extra parts: any of stages, resources, body")] string[]? include = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new();
            if (include is not null)
                args["include"] = new JsonArray(include.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());
            return (await bridge.CallAsync("flight", "get", args, cancellationToken)).ToJson();
        }
    }
}
