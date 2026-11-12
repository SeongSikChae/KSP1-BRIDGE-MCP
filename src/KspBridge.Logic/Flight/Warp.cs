using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>Rails time warp for coast waits. Only the Coordinator calls it (tick order f, step change, abort, submit, stop).
    /// Events cover our own warps only (warp_started, warp_stopped, warp_unavailable); the player's warps raise none.</summary>
    public static class Warp
    {
        // WarpTo only sets setAutoWarp; the next TimeWarp.Update starts the autoWarp coroutine, which keeps autoWarpEngaged true
        // until it ends. The WARPTO_LOCK input lock is not used: a user cancel removes it one frame late (delay_unlock).
        private static readonly FieldInfo AutoWarpEngagedField =
            typeof(TimeWarp).GetField("autoWarpEngaged", BindingFlags.Instance | BindingFlags.NonPublic);

        // The player's rails/physics switch (private bool setMode(Modes)); looked up by parameter type, null checked at use.
        private static readonly MethodInfo SetModeMethod = typeof(TimeWarp).GetMethod("setMode",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TimeWarp.Modes) }, null);

        private static bool _wasActive;    // our warp was in effect at the last Sync
        private static bool _requested;    // our WarpTo is alive
        private static bool _cancelled;    // user-cancel latch: no new request within the step
        private static string _unavailable; // reason already reported in this step (warp_unavailable once per step and reason)
        private static double _untilUt = double.NaN;

        /// <summary>True while a warp we requested is in effect.</summary>
        public static bool Requested => _requested;

        /// <summary>User-cancel latch (reload handover, unit 11).</summary>
        public static bool Cancelled => _cancelled;

        /// <summary>Rails warp rate raised (physics warp is not counted and never stopped by us).</summary>
        public static bool On => TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex > 0;

        /// <summary>Warp in effect: rails rate raised, or a warp-to pending or running (its rate may still be easing in or out
        /// with the index at 0). get_flight plan.warp.</summary>
        public static bool Active => On || Pending;

        /// <summary>WarpTo waiting for the next TimeWarp.Update, or autoWarp running (rate not raised yet counts too).</summary>
        internal static bool Pending
        {
            get
            {
                TimeWarp w = TimeWarp.fetch;
                return w != null && (w.setAutoWarp || (AutoWarpEngagedField != null && (bool)AutoWarpEngagedField.GetValue(w)));
            }
        }

        /// <summary>Every tick (order f): our warp ending without our Stop latches (user cancel, or WarpTo stopping by itself).</summary>
        public static void Sync()
        {
            bool active = Active;
            if (_requested && _wasActive && !active)
            {
                _requested = false;
                _cancelled = true;
                Debug.Log("[KspBridge] warp ended without our Stop (user cancel or WarpTo end): latched for this step");
                Stopped();
            }
            _wasActive = _requested && active;
        }

        public static void Request(Vessel v, double targetUt)
        {
            if (_cancelled)
            {
                return;
            }
            bool allowed = v.altitude > (v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0)
                && FlightInputHandler.state.mainThrottle == 0
                && EngineModel.ThrustSum(v) <= 0
                && !AutoStager.Pending;   // staging runs tick by tick: never on rails
            if (!allowed || targetUt - Planetarium.GetUniversalTime() < 15)
            {
                if (_requested)
                {
                    Stop();   // the usual release point (no latch)
                }
                return;
            }
            if (_requested)
            {
                return;
            }
            // The player's warp is theirs: a warp-to (WarpTo would be ignored anyway), rails or physics rate raised.
            if (Pending || On || (TimeWarp.WarpMode == TimeWarp.Modes.LOW && TimeWarp.CurrentRateIndex > 0))
            {
                Unavailable("player_warp");
                return;
            }
            if (AutoWarpEngagedField == null)
            {
                Unavailable("not_supported");   // a running autoWarp would look stopped before its rate rises
                return;
            }
            // WarpTo warps in the current mode: physics mode at rate 0 would give a slow physics warp.
            if (TimeWarp.WarpMode == TimeWarp.Modes.LOW && !SetMode(TimeWarp.fetch, TimeWarp.Modes.HIGH))
            {
                Unavailable(SetModeMethod == null ? "not_supported" : "mode_switch_refused");
                return;
            }
            TimeWarp.fetch.WarpTo(targetUt - 10);   // floor: stops 10 s before even if one tick jumps the 15 s boundary
            _requested = true;
            _wasActive = true;
            _untilUt = targetUt;
            Debug.Log("[KspBridge] warp requested until UT " + targetUt.ToString("F2"));
            Event("warp_started", null);
        }

        /// <summary>Releases our warp, or at a step change and abort any rails warp and pending WarpTo; leaves physics warp alone.</summary>
        public static void Stop()
        {
            bool ours = _requested;
            _requested = false;
            _wasActive = false;
            bool easing = TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1f;   // index 0, rate still easing down
            if (!ours && !On && !Pending && !easing)
            {
                return;
            }
            TimeWarp w = TimeWarp.fetch;
            if (w != null)
            {
                w.setAutoWarp = false;   // WarpTo starts on the next Update: cancel before it too
                w.CancelAutoWarp();      // autoWarp coroutine and its input lock
            }
            if (On || easing)
            {
                TimeWarp.SetRate(0, true);
            }
            Debug.Log("[KspBridge] warp stopped by the plan (" + (ours ? "ours" : "not requested by us") + ")");
            if (ours)
            {
                Stopped();   // no latch: our own release
            }
        }

        /// <summary>Clears the user-cancel latch (step change, new plan).</summary>
        public static void ClearLatch()
        {
            _cancelled = false;
            _unavailable = null;
        }

        public static void LoadLatch(bool cancelled) => _cancelled = cancelled;

        /// <summary>The game's own rails/physics switch; false when it refuses (rate too high) or is not available.</summary>
        internal static bool SetMode(TimeWarp w, TimeWarp.Modes mode)
        {
            return SetModeMethod != null && (bool)SetModeMethod.Invoke(w, new object[] { mode });
        }

        internal static bool CanSetMode => SetModeMethod != null;

        private static void Stopped()
        {
            Event("warp_stopped", null);
            _untilUt = double.NaN;
        }

        /// <summary>Why a coast step waits without our warp; once per step and reason, so the cause is visible over MCP.</summary>
        private static void Unavailable(string reason)
        {
            if (_unavailable == reason)
            {
                return;
            }
            _unavailable = reason;
            Debug.Log("[KspBridge] warp not requested: " + reason);
            Event("warp_unavailable", reason);
        }

        private static void Event(string type, string reason)
        {
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["rate"] = (double)TimeWarp.CurrentRate,
                ["until_ut"] = double.IsNaN(_untilUt) ? null : (object)System.Math.Round(_untilUt, 2),
            };
            if (reason != null)
            {
                d["reason"] = reason;
            }
            FlightEvents.Add(type, Coordinator.StepIndex, d);
        }
    }
}
