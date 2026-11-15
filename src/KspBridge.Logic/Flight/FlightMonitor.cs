using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Plan-wide watch for abnormal flight (flip, lost control, overheat, oscillation, wasted thrust). Main thread only.
    /// Alerts and clears are events (monitor_alert / monitor_clear) on level changes only; an abort item at level abort
    /// makes Tick return the plan_aborted message.
    /// </summary>
    public static class FlightMonitor
    {
        private const double WindowS = 10;

        private struct Sample
        {
            public double Ut, Err, Rate, Aoa, Temp;
            public Vector3 Omega;
        }

        private static readonly Queue<Sample> _window = new Queue<Sample>();
        private static readonly Dictionary<string, string> _level = new Dictionary<string, string>();   // kind → warn|abort
        /// <summary>control_lost: an error this much below the one the timer started from (or last restarted from) is a turn
        /// toward the target, not lost control; the timer restarts from there.</summary>
        private const double LostProgressDeg = 5;

        private static double _flipFor, _lostFor, _wasteFor;
        private static double _lostFrom = double.NaN;   // attitude_error the control_lost timer counts from
        private static double _lastUt = double.NaN;
        private static double _drift;            // attitude_drift: accumulated angle the nose moved away from the target by itself (deg)
        private static Vector3d? _driftPrev;     // nose direction at the last judged tick (null = none: the next tick adds nothing)
        private static double _oscMinRate = 0.05;
        private static bool _running;   // false after EndPlan: the summary shows no current alerts

        /// <summary>Every real plan tick after the abort check (not in the trial). cmd: the command in force (last tick's).
        /// Returns the abort message, or null.</summary>
        public static string Tick(Telemetry t, Vessel v, double ut, ModeCommand cmd, Effective s)
        {
            _running = true;
            _oscMinRate = s.OscMinRate;   // before the Enabled check: the summary counts with it either way
            double dt = double.IsNaN(_lastUt) ? 0 : Math.Max(0, ut - _lastUt);
            _lastUt = ut;
            if (!v.packed)
            {
                Add(new Sample
                {
                    Ut = ut, Err = t.Num["attitude_error"], Rate = t.Num["angular_rate"], Aoa = t.Num["aoa"], Temp = t.Num["temp_ratio"],
                    Omega = v.angularVelocity,
                });
            }
            if (!s.Enabled)
            {
                return null;
            }
            bool thrusting = t.Num["throttle"] > 0 && t.Num["thrust"] > 0;
            double err = t.Num["attitude_error"], q = t.Num["dynamic_pressure"], aoa = t.Num["aoa"], temp = t.Num["temp_ratio"];
            // keep the timers, add nothing: on rails (a rail tick spans a long dt), staging, an intended misalignment, no error
            bool pauseErr = v.packed || t.Str["staging"] != "none" || cmd.Aligning || double.IsNaN(err);

            // flipped: only while the commanded attitude itself flies along the surface velocity (land / retrograde descents excluded)
            bool inAtmo = v.mainBody.atmosphere && v.altitude < v.mainBody.atmosphereDepth;
            double cmdAoa = cmd.Attitude.HasValue && v.srfSpeed >= 1
                ? Vector3d.Angle(cmd.Attitude.Value.Direction(v), v.srf_velocity) : double.NaN;
            Accumulate(ref _flipFor, inAtmo && q >= s.FlipMinQ && cmdAoa <= s.FlipAoaDeg && aoa > s.FlipAoaDeg, v.packed, dt);
            bool lost = thrusting && err > s.LostErrorDeg;
            if (!pauseErr && lost)
            {
                if (double.IsNaN(_lostFrom) || _lostFor == 0)
                {
                    _lostFrom = err;
                }
                else if (err <= _lostFrom - LostProgressDeg)   // converging on the target: a turn, not lost control
                {
                    _lostFrom = err;
                    _lostFor = 0;
                }
            }
            Accumulate(ref _lostFor, lost, pauseErr, dt);
            Accumulate(ref _wasteFor, thrusting && err > s.WasteErrorDeg, pauseErr, dt);

            // attitude_drift: no thrust (or a forced land start still turning, thrust cut by cos) with a commanded attitude.
            // Paused (kept as is, previous nose dropped) on rails, while staging and when the error is not known.
            bool driftArmed = (!thrusting || cmd.Aligning) && cmd.Attitude.HasValue;
            bool driftPause = v.packed || t.Str["staging"] != "none" || double.IsNaN(err);
            string driftLevel = _level.TryGetValue("attitude_drift", out string dl) ? dl : null;   // unchanged while paused
            if (driftPause)
            {
                _driftPrev = null;
            }
            else if (!driftArmed)
            {
                ResetDrift();
                driftLevel = null;
            }
            else
            {
                Vector3d dir = cmd.Attitude.Value.Direction(v);
                Vector3d nose = v.ReferenceTransform.up;
                if (_driftPrev.HasValue)   // change of the nose against the current target only: target moves add nothing
                {
                    _drift = Math.Max(0, _drift + Vector3d.Angle(nose, dir) - Vector3d.Angle(_driftPrev.Value, dir));
                }
                _driftPrev = nose;
                if (err > s.DriftErrorDeg && _drift >= s.DriftGrowthDeg)
                {
                    driftLevel = "warn";
                }
                else if (err <= s.DriftErrorDeg)
                {
                    if (driftLevel != null)
                    {
                        ResetDrift();   // cleared: a new episode accumulates from 0
                    }
                    driftLevel = null;
                }
                // else: above the limit, not (or no longer) accumulated: keep the current level until it comes back under
            }

            string abortLevel = s.Abort ? "abort" : "warn";
            string abort = null;
            abort = Set("flipped", _flipFor >= s.FlipForS ? abortLevel : null, aoa, s.FlipAoaDeg,
                "aoa " + F(aoa) + " deg at q " + F(q) + " kPa for " + F(_flipFor) + " s") ?? abort;
            abort = Set("control_lost", _lostFor >= s.LostForS ? abortLevel : null, err, s.LostErrorDeg,
                "attitude_error " + F(err) + " deg under thrust for " + F(_lostFor) + " s") ?? abort;
            abort = Set("overheat", temp >= s.TempAbort ? abortLevel : temp >= s.TempWarn ? "warn" : null, temp,
                temp >= s.TempAbort ? s.TempAbort : s.TempWarn, "temp_ratio " + F(temp, 3)) ?? abort;
            Set("thrust_waste", _wasteFor >= s.WasteForS ? "warn" : null, err, s.WasteErrorDeg, null);
            int flips = MaxFlips(s.OscMinRate);
            bool oscOn = _level.ContainsKey("oscillation") ? flips >= s.OscFlips / 2 : flips >= s.OscFlips;   // hysteresis
            Set("oscillation", oscOn ? "warn" : null, flips, s.OscFlips, null);
            Set("attitude_drift", driftLevel, err, s.DriftErrorDeg, null);
            return abort;
        }

        private static void ResetDrift()
        {
            _drift = 0;
            _driftPrev = null;
        }

        /// <summary>Step change (NextStep, a replaced current step): clears every alert with monitor_clear and the timers;
        /// the sample window stays.</summary>
        public static void ResetStep()
        {
            foreach (string kind in new List<string>(_level.Keys))
            {
                Set(kind, null, double.NaN, double.NaN, null);
            }
            _flipFor = _lostFor = _wasteFor = 0;
            ResetDrift();
        }

        /// <summary>Plan end (abort, completion, release): alerts and timers cleared without events (plan_aborted /
        /// plan_completed tell the end); the window stays for get_flight.</summary>
        public static void EndPlan()
        {
            _level.Clear();
            _flipFor = _lostFor = _wasteFor = 0;
            ResetDrift();
            _running = false;
        }

        /// <summary>New plan: everything, the window included.</summary>
        public static void Reset()
        {
            EndPlan();
            _window.Clear();
            _lastUt = double.NaN;
        }

        /// <summary>get_flight plan.monitor: statistics of the last 10 s of samples (null when there are none). Values that never
        /// had a number in the window are null; alerts is empty once the plan has ended.</summary>
        public static Dictionary<string, object> Summary()
        {
            if (_window.Count == 0)
            {
                return null;
            }
            double firstUt = double.NaN, lastUt = double.NaN;
            double errMax = double.NaN, errSum = 0, rateMax = double.NaN, aoaMax = double.NaN, tempMax = double.NaN;
            int errN = 0;
            foreach (Sample s in _window)
            {
                if (double.IsNaN(firstUt))
                {
                    firstUt = s.Ut;
                }
                lastUt = s.Ut;
                if (!double.IsNaN(s.Err))
                {
                    errMax = Max(errMax, s.Err);
                    errSum += s.Err;
                    errN++;
                }
                rateMax = Max(rateMax, s.Rate);
                aoaMax = Max(aoaMax, s.Aoa);
                tempMax = Max(tempMax, s.Temp);
            }
            return new Dictionary<string, object>
            {
                ["window_s"] = Math.Round(lastUt - firstUt, 1),
                ["samples"] = (double)_window.Count,
                ["attitude_error_max"] = Num(errMax),
                ["attitude_error_mean"] = errN > 0 ? Num(errSum / errN) : null,
                ["angular_rate_max"] = Num(rateMax),
                ["sign_flips"] = (double)MaxFlips(_oscMinRate),
                ["aoa_max"] = Num(aoaMax),
                ["temp_ratio_max"] = Num(tempMax),
                ["alerts"] = _running ? new List<object>(_level.Keys) : new List<object>(),   // ended plan: no current alerts
            };
        }

        /// <summary>The larger of the two, ignoring a NaN x.</summary>
        private static double Max(double max, double x) => double.IsNaN(x) ? max : double.IsNaN(max) || x > max ? x : max;

        private static void Add(Sample s)
        {
            _window.Enqueue(s);
            while (_window.Count > 0 && _window.Peek().Ut < s.Ut - WindowS)
            {
                _window.Dequeue();
            }
        }

        private static void Accumulate(ref double acc, bool cond, bool pause, double dt)
        {
            if (pause)
            {
                return;
            }
            acc = cond ? acc + dt : 0;
        }

        /// <summary>Sets the level of one item (null = clear); an event only when it changes. Returns the abort message at level abort.</summary>
        private static string Set(string kind, string level, double value, double limit, string detail)
        {
            _level.TryGetValue(kind, out string current);
            if (level != current)
            {
                if (level == null)
                {
                    _level.Remove(kind);
                    FlightEvents.Add("monitor_clear", Coordinator.StepIndex, new Dictionary<string, object> { ["kind"] = kind });
                }
                else
                {
                    _level[kind] = level;
                    FlightEvents.Add("monitor_alert", Coordinator.StepIndex, new Dictionary<string, object>
                    {
                        ["kind"] = kind,
                        ["level"] = level,
                        ["value"] = Num(value),
                        ["limit"] = Num(limit),
                    });
                }
            }
            return level == "abort" ? kind + ": " + detail : null;
        }

        /// <summary>Sign changes per local axis over the window, counting only samples with |ω axis| ≥ minRate
        /// (a near-zero axis does not flip back and forth on noise). The largest of the three axes.</summary>
        private static int MaxFlips(double minRate)
        {
            int max = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                int last = 0, flips = 0;
                foreach (Sample s in _window)
                {
                    float w = s.Omega[axis];
                    if (Math.Abs(w) < minRate)
                    {
                        continue;
                    }
                    int sign = w > 0 ? 1 : -1;
                    if (last != 0 && sign != last)
                    {
                        flips++;
                    }
                    last = sign;
                }
                max = Math.Max(max, flips);
            }
            return max;
        }

        private static object Num(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, 3);

        private static string F(double x, int digits = 1) => double.IsNaN(x) ? "NaN" : x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A plan or step "monitor" object: only the keys the JSON gave (null = inherit from the plan, then the defaults).</summary>
        public sealed class Settings
        {
            private static readonly string[] Keys =
            {
                "enabled", "abort", "flip_aoa_deg", "flip_min_q_kpa", "flip_for_s", "lost_error_deg", "lost_for_s",
                "waste_error_deg", "waste_for_s", "osc_flips", "osc_min_rate", "temp_warn", "temp_abort",
                "drift_error_deg", "drift_growth_deg",
            };

            public bool? Enabled, Abort;
            public double? FlipAoaDeg, FlipMinQ, FlipForS, LostErrorDeg, LostForS, WasteErrorDeg, WasteForS, OscFlips, OscMinRate, TempWarn, TempAbort;
            public double? DriftErrorDeg, DriftGrowthDeg;

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
                    DriftErrorDeg = Num(o, "drift_error_deg", path, 1, 180),
                    DriftGrowthDeg = Num(o, "drift_growth_deg", path, 1, 180),
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
            public double DriftErrorDeg, DriftGrowthDeg;

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
                    DriftErrorDeg = step?.DriftErrorDeg ?? plan?.DriftErrorDeg ?? 30,
                    DriftGrowthDeg = step?.DriftGrowthDeg ?? plan?.DriftGrowthDeg ?? 20,
                };
                e.TempWarn = Math.Min(e.TempWarn, e.TempAbort);
                return e;
            }
        }
    }
}
