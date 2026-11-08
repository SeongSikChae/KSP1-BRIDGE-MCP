using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class FlightLogTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "list_flights", ReadOnly = true)]
        [Description("Recorded flights of this save, newest first: id, vessel, crew, reason (recording, recovered, destroyed, reverted, switched, scene_exit, reload, incomplete), max_alt, max_g, parts_lost, crew_lost, touchdown (vz, vh, tilt at the last contact), checks (prediction vs reality: brake = predicted stop distance vs actual distance with end_vz/mean_tilt/mean_f_ratio; thrust = actual/predicted thrust ratio; dv = used vs game delta-v per stage). Read the checks of earlier flights before planning a landing or a burn.")]
        public Task<string> ListFlights(
            [Description("Filter by vessel name (substring)")] string? vessel = null,
            [Description("Maximum flights (default 20)")] int limit = 20,
            CancellationToken ct = default)
        {
            List<string> q = [$"limit={limit}"];
            if (!string.IsNullOrEmpty(vessel))
            {
                q.Add("vessel=" + Uri.EscapeDataString(vessel));
            }
            return client.SendAsync(HttpMethod.Get, "api/flights?" + string.Join("&", q), null, ct);
        }

        [McpServerTool(Name = "get_flight_log", ReadOnly = true)]
        [Description("Lines of one recorded flight (id from list_flights). Line t: start (vessel, crew, stages delta-v), s (sample: met, alt, agl, vz, vh, vs, pitch, hdg, roll, tilt, m, thr, f/f_pred kN, acc, stop, margin, sas, steer, g, stage; every 0.5 s, 0.1 s while thrusting below 1 km, 10 s on rails), e (event type: stage, trigger, control, sas, situation, impact, part_lost, crew, warp, stages, check), end (summary). Cut it down with from_met/to_met or around=<event type> (±window s around each such event), step (s between samples), types (default s,e). Over 400 samples the step is raised (downsampled_step).")]
        public Task<string> GetFlightLog(
            [Description("Flight id from list_flights")] string id,
            [Description("From mission time (s)")] double? fromMet = null,
            [Description("To mission time (s)")] double? toMet = null,
            [Description("Event type to center windows on, e.g. situation, impact, trigger, check")] string? around = null,
            [Description("Window half-width in s for around (default 10)")] double window = 10,
            [Description("Minimum seconds between samples")] double? step = null,
            [Description("Line types: s, e or s,e (default)")] string? types = null,
            CancellationToken ct = default)
        {
            List<string> q = [];
            void Add(string name, double? value)
            {
                if (value.HasValue)
                {
                    q.Add(name + "=" + value.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            Add("from_met", fromMet);
            Add("to_met", toMet);
            if (!string.IsNullOrEmpty(around))
            {
                q.Add("around=" + Uri.EscapeDataString(around));
                Add("window", window);
            }
            Add("step", step);
            if (!string.IsNullOrEmpty(types))
            {
                q.Add("types=" + Uri.EscapeDataString(types));
            }
            string path = $"api/flights/{Uri.EscapeDataString(id)}/log" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
            return client.SendAsync(HttpMethod.Get, path, null, ct);
        }
    }
}
