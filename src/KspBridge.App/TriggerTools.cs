using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class TriggerTools(KspBridgeClient client)
    {
        internal const string Fields =
            "Fields: altitude_asl, altitude_terrain, apoapsis, periapsis, time_to_ap, speed_surface, speed_vertical, met, ut, g_force (vessel G now), g_load_max (highest crew accumulated G, 1 = blackout), " +
            "prediction fields thrust_kn, max_accel, twr, drag_accel, stop_time, stop_distance, stop_dv, time_to_impact, burn_margin " +
            "(numbers, op >= <= ==; prediction stop_*/time_to_impact/burn_margin are null unless descending), " +
            "situation (string, ==), stage_flameout (bool, ==; true only when EVERY ignited engine has flamed out), " +
            "any_flameout (bool, ==; true when at least one ignited engine has flamed out, e.g. radial SRBs empty while the core engine still burns).";

        [McpServerTool(Name = "add_trigger", Destructive = true)]
        [Description("Register a condition → actions trigger on the active vessel. The game checks it every physics tick and runs the actions in the same tick the condition becomes true " +
            "(use this for timing-critical steps such as cutting the throttle at a target apoapsis or ending a braking burn). One-shot by default. " +
            "Cleared when the active vessel or the scene changes. " + Fields + " " +
            "Actions (run in order): {type:'throttle', value:0..1}, {type:'stage'}, {type:'attitude', mode:'own'|'sas', pitch, heading, roll?}, {type:'release_attitude'}, " +
            "{type:'sas', on:bool}, {type:'action_group', group, on?} (on omitted = toggle).")]
        public Task<string> AddTrigger(
            string field,
            [Description(">=, <= or ==")] string op,
            [Description("Number, string (situation) or boolean (stage_flameout, any_flameout)")] JsonElement value,
            [Description("Array of action objects")] JsonElement actions,
            [Description("Optional label")] string? name = null,
            [Description("true = run every time the condition turns from false to true")] bool repeat = false,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/triggers", new { name, when = new { field, op, value = NormalizeValue(field, value) }, actions, repeat }, ct);

        /// <summary>
        /// The value parameter is untyped, so a client may send a number such as 378.0 as the string "378.0".
        /// Numeric strings become numbers for every field except situation; "true"/"false" become booleans.
        /// </summary>
        internal static object NormalizeValue(string field, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String || field == "situation")
            {
                return value;
            }
            string s = value.GetString()!.Trim();
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }
            if (bool.TryParse(s, out bool b))
            {
                return b;
            }
            return value;
        }

        [McpServerTool(Name = "list_triggers", ReadOnly = true)]
        [Description("Registered triggers with state (armed, fired, failed), fired_met, fired_value (field value when it fired) and error.")]
        public Task<string> ListTriggers(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/triggers", null, ct);

        [McpServerTool(Name = "remove_trigger")]
        [Description("Remove a trigger by id, or all triggers when id is omitted.")]
        public Task<string> RemoveTrigger([Description("Trigger id; omit to remove all")] int? id = null, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Delete, id is null ? "api/triggers" : $"api/triggers/{id}", null, ct);
    }
}
