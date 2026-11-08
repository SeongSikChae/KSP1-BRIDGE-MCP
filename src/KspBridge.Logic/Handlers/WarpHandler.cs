using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace KspBridge.Handlers
{
    /// <summary>Time warp: stock auto-warp to a time, direct rate (rails/physics), stop.</summary>
    public static class WarpHandler
    {
        private static readonly MethodInfo SetModeMethod =
            typeof(TimeWarp).GetMethod("setMode", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo AutoWarpEngagedField =
            typeof(TimeWarp).GetField("autoWarpEngaged", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MaxRailsMethod =
            typeof(TimeWarp).GetMethod("getMaxOnRailsRateIdx", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Stop UT of the last warp_to started here (null when finished or cancelled).</summary>
        private static double? _stopUt;

        /// <summary>POST /api/warp/to</summary>
        public static object To(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            TimeWarp tw = Require();
            string target = body.GetString("target");
            double lead = body.GetDouble("lead", 30);
            if (lead < 0)
            {
                throw ApiException.BadRequest("lead must be >= 0");
            }
            double now = Planetarium.GetUniversalTime();
            double targetUt = TargetUt(v, target, body, now);
            double stopUt = targetUt - lead;
            if (stopUt <= now + 1)
            {
                throw ApiException.BadRequest("stop time " + stopUt.ToString("F1") + " is not in the future (now " + now.ToString("F1") + ", target " + targetUt.ToString("F1") + ", lead " + lead + ")");
            }
            // CancelAutoWarp does not stop the running stock coroutine at once; a second WarpTo in the same
            // window would leave two coroutines (the old one can stop at its own target). Stock refuses too.
            if (AutoWarpActive(tw))
            {
                throw new ApiException(409, "warp_busy", "an auto-warp is in progress (call stop_warp first)");
            }

            // Not possible yet (engines burning, in the atmosphere, ...): schedule it; Tick starts it as soon as it is allowed.
            string waitingFor = FlightInputHandler.state.mainThrottle > 0 ? "throttle_above_0" : null;
            if (waitingFor == null && MaxRails(tw, out string reason) == 0)
            {
                waitingFor = reason ?? "altitude_limit";
            }
            if (waitingFor != null)
            {
                _pending = new Pending
                {
                    VesselId = v.id,
                    Target = target,
                    Lead = lead,
                    FixedUt = target == "ut" ? (double?)targetUt : null,
                };
                _stopUt = null;
                return new Dictionary<string, object>
                {
                    ["target"] = target,
                    ["scheduled"] = true,
                    ["waiting_for"] = waitingFor,
                    ["target_ut"] = targetUt,
                    ["stop_ut"] = stopUt,
                    ["now_ut"] = now,
                    ["note"] = "starts when the throttle is 0 and rails warp is allowed; target time is recomputed then",
                };
            }

            _pending = null;
            Start(tw, stopUt);
            return new Dictionary<string, object>
            {
                ["target"] = target,
                ["target_ut"] = targetUt,
                ["stop_ut"] = stopUt,
                ["now_ut"] = now,
                ["duration"] = stopUt - now,
            };
        }

        private sealed class Pending
        {
            public Guid VesselId;
            public string Target;
            public double Lead;
            public double? FixedUt;
        }

        /// <summary>warp_to scheduled until the throttle is 0 and rails warp is allowed (null = none).</summary>
        private static Pending _pending;

        private static void Start(TimeWarp tw, double stopUt)
        {
            if (TimeWarp.WarpMode != TimeWarp.Modes.HIGH)
            {
                TimeWarp.SetRate(0, true, false);
                SetMode(tw, TimeWarp.Modes.HIGH);
            }
            tw.WarpTo(stopUt);
            _stopUt = stopUt;
        }

        /// <summary>Every physics tick: starts a scheduled warp_to once it is allowed (target time recomputed now).</summary>
        internal static void Tick()
        {
            Pending p = _pending;
            if (p == null)
            {
                return;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            TimeWarp tw = TimeWarp.fetch;
            if (v == null || v.id != p.VesselId || tw == null)
            {
                _pending = null;
                return;
            }
            if (FlightInputHandler.state.mainThrottle > 0 || AutoWarpActive(tw) || MaxRails(tw, out _) == 0)
            {
                return;
            }
            _pending = null;
            double now = Planetarium.GetUniversalTime();
            double targetUt;
            try
            {
                targetUt = p.FixedUt ?? TargetUt(v, p.Target, null, now);
            }
            catch (ApiException e)
            {
                UnityEngine.Debug.LogWarning("[KspBridge] scheduled warp dropped: " + e.Message);
                return;
            }
            double stopUt = targetUt - p.Lead;
            if (stopUt <= now + 1)
            {
                UnityEngine.Debug.LogWarning("[KspBridge] scheduled warp dropped: stop time already passed");
                return;
            }
            Start(tw, stopUt);
        }

        /// <summary>POST /api/warp</summary>
        public static object Set(RequestBody body)
        {
            Guard.ActiveVessel();
            TimeWarp tw = Require();
            double rate = body.GetDouble("rate");
            bool physics = body.Has("physics") && body.GetBool("physics");
            float[] rates = physics ? tw.physicsWarpRates : tw.warpRates;
            int index = Array.FindIndex(rates, r => Math.Abs(r - rate) < 1e-3);
            if (index < 0)
            {
                throw ApiException.BadRequest((physics ? "physics" : "rails") + " rate must be one of: " + string.Join(", ", rates.Select(r => r.ToString("0.##"))));
            }

            CancelAuto(tw);
            _stopUt = null;
            _pending = null;
            TimeWarp.Modes mode = physics ? TimeWarp.Modes.LOW : TimeWarp.Modes.HIGH;
            if (TimeWarp.WarpMode != mode)
            {
                TimeWarp.SetRate(0, true, false);
                SetMode(tw, mode);
            }
            TimeWarp.SetRate(index, false);
            int actual = TimeWarp.CurrentRateIndex;
            return new Dictionary<string, object>
            {
                ["requested"] = rate,
                ["rate"] = (double)rates[Math.Min(actual, rates.Length - 1)],
                ["index"] = actual,
                ["mode"] = ModeName(),
                ["limited"] = actual < index,
            };
        }

        /// <summary>POST /api/warp/stop</summary>
        public static object Stop()
        {
            Guard.ActiveVessel();
            TimeWarp tw = Require();
            double before = TimeWarp.CurrentRate;
            StopNow(tw);
            return new Dictionary<string, object>
            {
                ["rate_before"] = before,
                ["rate"] = (double)TimeWarp.CurrentRate,
            };
        }

        /// <summary>Cancels auto-warp and drops to 1x instantly (also used by triggers).</summary>
        internal static void StopNow(TimeWarp tw = null)
        {
            tw = tw ?? TimeWarp.fetch;
            if (tw == null)
            {
                return;
            }
            CancelAuto(tw);
            TimeWarp.SetRate(0, true, false);
            _stopUt = null;
            _pending = null;
        }

        /// <summary>Cancels a running auto-warp and one requested but not yet started (setAutoWarp is read in TimeWarp.Update).</summary>
        private static void CancelAuto(TimeWarp tw)
        {
            tw.setAutoWarp = false;
            tw.CancelAutoWarp();
        }

        private static bool AutoWarpActive(TimeWarp tw)
            => tw.setAutoWarp || (AutoWarpEngagedField != null && (bool)AutoWarpEngagedField.GetValue(tw));

        /// <summary>"warp" block of GET /api/vessel.</summary>
        internal static Dictionary<string, object> Describe(Vessel v)
        {
            TimeWarp tw = TimeWarp.fetch;
            if (tw == null)
            {
                return null;
            }
            if (_stopUt.HasValue && ((TimeWarp.CurrentRateIndex == 0 && !AutoWarpActive(tw)) || Planetarium.GetUniversalTime() >= _stopUt.Value))
            {
                _stopUt = null; // auto-warp finished (or was stopped from the game UI)
            }
            return new Dictionary<string, object>
            {
                ["rate"] = (double)TimeWarp.CurrentRate,
                ["index"] = TimeWarp.CurrentRateIndex,
                ["mode"] = ModeName(),
                ["stop_ut"] = _stopUt,
                ["scheduled"] = _pending == null ? null : new Dictionary<string, object>
                {
                    ["target"] = _pending.Target,
                    ["lead"] = _pending.Lead,
                    ["ut"] = _pending.FixedUt,
                },
                ["max_rails_rate"] = (double)tw.warpRates[MaxRails(tw, out string reason)],
                ["rails_limit"] = reason,
            };
        }

        /// <summary>
        /// Highest rails rate index the stock limiter allows now (getMaxOnRailsRateIdx: altitude/Pe mode per
        /// GameSettings.ORBIT_WARP_MAXRATE_MODE, atmosphere, acceleration, imminent crash/SOI); reason null when clear.
        /// </summary>
        private static int MaxRails(TimeWarp tw, out string reason)
        {
            reason = null;
            if (MaxRailsMethod == null)
            {
                return tw.warpRates.Length - 1;
            }
            object[] args = { tw.warpRates.Length - 1, true, null };
            int index = (int)MaxRailsMethod.Invoke(tw, args);
            string r = args[2]?.ToString();
            reason = r == null || r == "CLEAR" ? null : r.ToLowerInvariant();
            return Math.Max(0, Math.Min(index, tw.warpRates.Length - 1));
        }

        private static double TargetUt(Vessel v, string target, RequestBody body, double now)
        {
            Orbit o = v.orbit;
            switch (target)
            {
                case "ut":
                    return body.GetDouble("ut");
                case "ap":
                    if (o.eccentricity >= 1.0)
                    {
                        throw new ApiException(409, "no_target", "no apoapsis (escape trajectory)");
                    }
                    return now + o.timeToAp;
                case "pe":
                    return now + o.timeToPe;
                case "node":
                    if (v.patchedConicSolver == null || v.patchedConicSolver.maneuverNodes.Count == 0)
                    {
                        throw new ApiException(409, "no_target", "there is no maneuver node");
                    }
                    return v.patchedConicSolver.maneuverNodes[0].UT;
                case "soi":
                    // Walk the patches: with a maneuver node the first patch ends in MANEUVER and the SOI change is later.
                    for (Orbit p = o; p != null; p = p.nextPatch)
                    {
                        if (p.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER || p.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)
                        {
                            return p.EndUT;
                        }
                        if (p.patchEndTransition == Orbit.PatchTransitionType.FINAL || p.patchEndTransition == Orbit.PatchTransitionType.INITIAL)
                        {
                            break;
                        }
                    }
                    throw new ApiException(409, "no_target", "no SOI change on the predicted path");
                default:
                    throw ApiException.BadRequest("target must be ut, ap, pe, node or soi");
            }
        }

        private static TimeWarp Require()
        {
            return TimeWarp.fetch ?? throw new ApiException(503, "not_ready", "time warp is not available yet");
        }

        private static void SetMode(TimeWarp tw, TimeWarp.Modes mode)
        {
            if (SetModeMethod != null)
            {
                SetModeMethod.Invoke(tw, new object[] { mode });
            }
            else
            {
                tw.Mode = mode;
            }
        }

        private static string ModeName() => TimeWarp.WarpMode == TimeWarp.Modes.LOW ? "physics" : "rails";
    }
}
