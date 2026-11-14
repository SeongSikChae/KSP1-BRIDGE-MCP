using KspBridge.Debugging;
using KspBridge.Flight;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the debug tools (#7): POST /api/debug/&lt;name&gt; on the main thread, 409 debug_disabled with the flag off.</summary>
    public static class DebugHandler
    {
        public static IEnumerable<Route> Routes()
        {
            yield return Post("clear", b =>
            {
                DebugState.Clear();
                return DebugState.Status();
            });
            // mask is a hex string: JSON numbers are double and lose bits above 2^53
            yield return Post("input_lock", b =>
            {
                DebugState.SetInputLock(b.Has("mask") ? ParseMask(b.GetString("mask")) : (ulong?)null);
                return DebugState.Status();
            });
            yield return Post("dock_reject", b =>
            {
                double n = b.GetDouble("count");
                if (n != Math.Floor(n) || n < 0 || n > 10)
                {
                    throw ApiException.BadRequest("count must be an integer 0..10 (0 releases)");
                }
                DebugState.DockRejectLeft = (int)n;
                return DebugState.Status();
            });
            yield return Post("input_locks_error", b =>
            {
                DebugState.InputLocksError = b.GetBool("on");
                return DebugState.Status();
            });
            yield return Post("stage_delay", b =>
            {
                double s = b.GetDouble("seconds");
                if (!(s >= 0 && s <= MaxStageDelayS))
                {
                    throw ApiException.BadRequest("seconds must be 0.." + MaxStageDelayS + " (0 releases)");
                }
                DebugState.StageDelayS = s;
                return DebugState.Status();
            });
            yield return Post("engine_stop", b =>
            {
                ModuleEngines e = DebugState.Engine(Guard.ActiveVessel(), PartId(b));
                string mode = b.GetString("mode");
                if (mode == "shutdown")
                {
                    e.Shutdown();
                }
                else if (mode == "flameout")
                {
                    e.Flameout("debug");
                }
                else
                {
                    throw ApiException.BadRequest("mode must be shutdown or flameout");
                }
                return new Dictionary<string, object>
                {
                    ["part_id"] = (double)e.part.persistentId,
                    ["ignited"] = e.EngineIgnited,
                    ["flameout"] = e.flameout,
                };
            });
            yield return Post("throttle_hold", b =>
            {
                if (!b.Has("throttle"))
                {
                    DebugState.ThrottleHold = null;
                    return DebugState.Status();
                }
                uint id = PartId(b);
                DebugState.Engine(Guard.ActiveVessel(), id);   // validate now
                if (!Coordinator.Running)
                {
                    // the hold is for a plan's staging wait; Tick releases it when the plan ends
                    throw new ApiException(409, "no_plan", "no running flight plan: submit one first, the hold ends with it");
                }
                DebugState.ThrottleHold = new DebugState.Hold(id, UnityEngine.Mathf.Clamp01((float)b.GetDouble("throttle")));
                return DebugState.Status();
            });
            yield return Post("reload_fault", b =>
            {
                int start = Count(b, "start_failures", 2);
                int handover = Count(b, "handover_failures", 3);
                if (start >= 1 && handover == 3)
                {
                    // stage 3 fails the minimal handover too, so no string carries the start failure to the next logic
                    throw ApiException.BadRequest("start_failures needs a handover: use handover_failures 0..2 with it");
                }
                DebugState.StartFailures = start;
                DebugState.HandoverFailures = handover;
                return DebugState.Status();
            });
            yield return Post("add_part_timeout", b =>
            {
                DebugState.AddPartTimeout = b.GetBool("on");
                return DebugState.Status();
            });
        }

        private static int Count(RequestBody b, string name, int max)
        {
            double v = b.GetDouble(name, 0);
            if (!(v >= 0 && v <= max) || v != Math.Floor(v))
            {
                throw ApiException.BadRequest(name + " must be an integer 0.." + max);
            }
            return (int)v;
        }

        /// <summary>Upper bound of debug_stage_delay: the failure deadline grows by it, so a plan never waits longer than this.</summary>
        private const double MaxStageDelayS = 60;

        /// <summary>A part persistentId: an integer 0..uint.MaxValue, else 400 (no silent truncation to another id).</summary>
        private static uint PartId(RequestBody b)
        {
            double v = b.GetDouble("part_id");
            if (!(v >= 0 && v <= uint.MaxValue) || v != Math.Floor(v))
            {
                throw ApiException.BadRequest("part_id must be an integer part id (persistentId) 0.." + uint.MaxValue);
            }
            return (uint)v;
        }

        private static ulong ParseMask(string s)
        {
            string hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
            if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong m))
            {
                throw ApiException.BadRequest("mask must be a hex string like 0x8");
            }
            return m;   // 0 allowed: a lock with no controls (0008 controls: [])
        }

        private static Route Post(string name, Func<RequestBody, object> f)
            => new Route("POST", "/api/debug/" + name, (m, b, q) =>
            {
                DebugState.Require();
                return f(b);
            });
    }
}
