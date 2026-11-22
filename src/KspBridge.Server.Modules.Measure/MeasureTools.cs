using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("measure")]

namespace KspBridge.Server.Modules.Measure
{
    /// <summary>Measurement for game checks (paired with the mod's measure module and its measure_start/measure_stop step types).</summary>
    [McpServerToolType]
    public sealed class MeasureTools
    {
        private const string Items =
            "Items (units): throttle (0-1), sas, rcs (on/off), pitch_input, yaw_input, roll_input (control input applied that frame, " +
            "-1..1; + commands the + rotation below), mass_t (t), thrust_kn (current total, kN), pitch_deg, heading_deg, roll_deg " +
            "(navball attitude, deg), pitch_rate_dps (+ = nose toward the vessel's top), yaw_rate_dps (+ = nose toward the vessel's " +
            "right), roll_rate_dps (+ = right wing down) (deg/s about the vessel's axes), stage (last activated stage, null before " +
            "staging), altitude_m (above sea level), vertical_speed_mps (up +), surface_speed_mps, orbital_speed_mps (m/s).";

        private readonly IBridgeClient bridge;

        public MeasureTools(IBridgeClient bridge) => this.bridge = bridge;

        [McpServerTool(Name = "measure_start")]
        [Description("Start a measurement: from now the mod records the active vessel's values once per physics frame, independent of " +
                     "MCP call timing, until measure_stop, a scene load (new_flight / scene_change), an active-vessel change " +
                     "(vessel_change), the sample limit (sample_limit) or an error (error). Only one measurement runs at a time " +
                     "(bad_request while one runs); starting a new one drops the previous ended one. Returns id, start_ut, " +
                     "sample_limit and vessel. Does not change the vessel or a running plan. For frame-exact boundaries use the plan " +
                     "steps measure_start / measure_stop instead. Needs a ready flight scene with an active vessel (not_ready otherwise). " +
                     Items)]
        public async Task<string> MeasureStart(
            [Description("Maximum number of samples, integer 1-180000 (default 30000); one sample per physics frame")] int? sample_limit = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new();
            if (sample_limit.HasValue)
                args["sample_limit"] = sample_limit.Value;
            return (await bridge.CallAsync("measure", "start", args, cancellationToken)).ToJson();
        }

        [McpServerTool(Name = "measure_stop")]
        [Description("End the running measurement with end reason stopped. Returns id, start_ut, end_ut, sample_count and end_reason. " +
                     "bad_request when no measurement is running.")]
        public async Task<string> MeasureStop(CancellationToken cancellationToken = default)
            => (await bridge.CallAsync("measure", "stop", new JsonObject(), cancellationToken)).ToJson();

        [McpServerTool(Name = "get_measurement", ReadOnly = true, Idempotent = true)]
        [Description("The running measurement (samples so far), otherwise the latest ended one; {\"measurement\": null} when there is " +
                     "none. Works in any scene. Answer: id, state (running/ended), vessel, start_ut, end_ut and end_reason (null while " +
                     "running; stopped, sample_limit, new_flight, scene_change, vessel_change or error), error (description, only for " +
                     "error), sample_count (whole measurement), sample_limit, range (from_s, to_s, sample_count in the range), " +
                     "frame_dt_s (min/max UT step between neighbouring samples) and summary per item over the range: numeric items " +
                     "first, last, min, min_t_s, max, max_t_s (earliest elapsed time), mean (arithmetic; heading and roll means are " +
                     "not corrected for wrap-around at 360 / ±180 deg); sas, rcs, stage: first, last, changes [{t_s, value}]. " +
                     "An empty range gives sample_count 0 and null summary. With samples=true adds samples: stride, columns (t_s, ut, " +
                     "then the items) and rows; more than max_samples samples in the range are thinned to every stride-th one. " +
                     "t_s is game seconds since start_ut. " + Items)]
        public async Task<string> GetMeasurement(
            [Description("Range start, elapsed game seconds since start_ut (inclusive; omit for no limit)")] double? from_s = null,
            [Description("Range end, elapsed game seconds since start_ut (inclusive; omit for no limit; must not be below from_s)")] double? to_s = null,
            [Description("Also return the sample list")] bool samples = false,
            [Description("Items of the sample list (default all); t_s and ut are always included")] string[]? items = null,
            [Description("Maximum rows of the sample list, integer 1-2000 (default 200)")] int? max_samples = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["samples"] = samples };
            if (from_s.HasValue)
                args["from_s"] = from_s.Value;
            if (to_s.HasValue)
                args["to_s"] = to_s.Value;
            if (items is not null)
                args["items"] = new JsonArray(items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());
            if (max_samples.HasValue)
                args["max_samples"] = max_samples.Value;
            return (await bridge.CallAsync("measure", "get", args, cancellationToken)).ToJson();
        }
    }
}
