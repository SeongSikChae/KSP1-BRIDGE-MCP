using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// Plan-wide watch for abnormal flight (flip, lost control, overheat, oscillation, wasted thrust). Main thread only.
    /// </summary>
    public static class FlightMonitor
    {
        /// <summary>A plan or step "monitor" object: only the keys the JSON gave (null = inherit from the plan, then the defaults).</summary>
        public sealed class Settings
        {
            private static readonly string[] Keys =
            {
                "enabled", "abort", "flip_aoa_deg", "flip_min_q_kpa", "flip_for_s", "lost_error_deg", "lost_for_s",
                "waste_error_deg", "waste_for_s", "osc_flips", "osc_min_rate", "temp_warn", "temp_abort",
            };

            public bool? Enabled, Abort;
            public double? FlipAoaDeg, FlipMinQ, FlipForS, LostErrorDeg, LostForS, WasteErrorDeg, WasteForS, OscFlips, OscMinRate, TempWarn, TempAbort;

            /// <summary>Partial settings from a "monitor" object; null when the key is absent. 400 invalid_plan on unknown keys or bad values.</summary>
            public static Settings Parse(object raw, string path)
            {
                if (raw == null)
                {
                    return null;
                }
                Dictionary<string, object> o = raw as Dictionary<string, object> ?? throw Plan.Invalid(path, "must be an object");
                Plan.UnknownKeys(o, Keys, path);
                Settings s = new Settings
                {
                    Enabled = Bool(o, "enabled", path),
                    Abort = Bool(o, "abort", path),
                    FlipAoaDeg = Num(o, "flip_aoa_deg", path, 1, 180),
                    FlipMinQ = Num(o, "flip_min_q_kpa", path, 0, 1000),
                    FlipForS = Num(o, "flip_for_s", path, 0.1, 600),
                    LostErrorDeg = Num(o, "lost_error_deg", path, 1, 180),
                    LostForS = Num(o, "lost_for_s", path, 0.1, 600),
                    WasteErrorDeg = Num(o, "waste_error_deg", path, 1, 180),
                    WasteForS = Num(o, "waste_for_s", path, 0.1, 600),
                    OscFlips = Num(o, "osc_flips", path, 2, 100),
                    OscMinRate = Num(o, "osc_min_rate", path, 0.001, 10),
                    TempWarn = Num(o, "temp_warn", path, 0.1, 1),
                    TempAbort = Num(o, "temp_abort", path, 0.1, 2),
                };
                if (s.TempWarn > s.TempAbort)   // both given in this object
                {
                    throw Plan.Invalid(path, "temp_warn must not exceed temp_abort");
                }
                return s;
            }

            private static bool? Bool(Dictionary<string, object> o, string name, string path) =>
                o.TryGetValue(name, out object x) && x != null ? Plan.OptionalBool(o, name, path, false) : (bool?)null;

            private static double? Num(Dictionary<string, object> o, string name, string path, double min, double max) =>
                o.TryGetValue(name, out object x) && x != null ? Plan.OptionalNumber(o, name, path, min, max, 0) : (double?)null;
        }

        /// <summary>The settings in force for a step: step → plan → defaults. TempWarn is capped at TempAbort.</summary>
        public sealed class Effective
        {
            public bool Enabled, Abort;
            public double FlipAoaDeg, FlipMinQ, FlipForS, LostErrorDeg, LostForS, WasteErrorDeg, WasteForS, OscFlips, OscMinRate, TempWarn, TempAbort;

            public static Effective Merge(Settings step, Settings plan)
            {
                Effective e = new Effective
                {
                    Enabled = step?.Enabled ?? plan?.Enabled ?? true,
                    Abort = step?.Abort ?? plan?.Abort ?? true,
                    FlipAoaDeg = step?.FlipAoaDeg ?? plan?.FlipAoaDeg ?? 30,
                    FlipMinQ = step?.FlipMinQ ?? plan?.FlipMinQ ?? 5,
                    FlipForS = step?.FlipForS ?? plan?.FlipForS ?? 2,
                    LostErrorDeg = step?.LostErrorDeg ?? plan?.LostErrorDeg ?? 30,
                    LostForS = step?.LostForS ?? plan?.LostForS ?? 5,
                    WasteErrorDeg = step?.WasteErrorDeg ?? plan?.WasteErrorDeg ?? 10,
                    WasteForS = step?.WasteForS ?? plan?.WasteForS ?? 5,
                    OscFlips = step?.OscFlips ?? plan?.OscFlips ?? 6,
                    OscMinRate = step?.OscMinRate ?? plan?.OscMinRate ?? 0.05,
                    TempWarn = step?.TempWarn ?? plan?.TempWarn ?? 0.8,
                    TempAbort = step?.TempAbort ?? plan?.TempAbort ?? 0.95,
                };
                e.TempWarn = Math.Min(e.TempWarn, e.TempAbort);
                return e;
            }
        }
    }
}
