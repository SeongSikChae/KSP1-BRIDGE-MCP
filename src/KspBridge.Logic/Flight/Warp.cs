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

        // The private judges WarpTo itself uses (TimeWarp.Update → setRate(1) → getMaxOnRailsRateIdx; getMaxWarpRateForTravel).
        // If either gives rate index 0, autoWarpTo ends after one FixedUpdate: calling them first means we never send a WarpTo
        // that KSP ends by itself (which Sync would read as a user cancel).
        private static readonly MethodInfo RailsRateMethod = typeof(TimeWarp).GetMethod("getMaxOnRailsRateIdx",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(int), typeof(bool), typeof(ClearToSaveStatus).MakeByRefType() }, null);
        private static readonly MethodInfo TravelRateMethod = typeof(TimeWarp).GetMethod("getMaxWarpRateForTravel",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(double), typeof(double), typeof(double), typeof(int).MakeByRefType() }, null);

        private static bool _wasActive;    // our warp was in effect at the last Sync
        private static string _refusal;    // last KSP refusal logged (log once per change)
        private static bool _requested;    // our WarpTo is alive
        private static bool _cancelled;    // user-cancel latch: no new request within the step
        private static bool _resumeUsed;   // one re-request per step after an unexpected end (design 0048 decision 2)
        private static bool _resumePending; // the next warp_started is that re-request
        private static bool _resumable;    // the step waits through CircularizeMode.WarpGate (execute_node, circularize, approach); coast is not
        private static readonly HashSet<string> _unavailable = new HashSet<string>(); // reasons reported in this step (once per step and reason)
        private static double _untilUt = double.NaN;
        private static bool _physics;      // #22 decision 11: our physics warp ×2 is in effect
        private const int PhysicsRateIndex = 1;   // ×2

        /// <summary>True while a warp we requested is in effect.</summary>
        public static bool Requested => _requested || _physics;

        /// <summary>User-cancel latch (reload handover, unit 11).</summary>
        public static bool Cancelled => _cancelled;

        /// <summary>Rails warp rate raised (physics warp is not counted and never stopped by us).</summary>
        public static bool On => TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex > 0;

        /// <summary>Warp in effect: rails rate raised, or a warp-to pending or running (its rate may still be easing in or out
        /// with the index at 0). get_flight plan.warp.</summary>
        public static bool Active => On || Pending || _physics;

        /// <summary>WarpTo waiting for the next TimeWarp.Update, or autoWarp running (rate not raised yet counts too).</summary>
        internal static bool Pending
        {
            get
            {
                TimeWarp w = TimeWarp.fetch;
                return w != null && (w.setAutoWarp || (AutoWarpEngagedField != null && (bool)AutoWarpEngagedField.GetValue(w)));
            }
        }

        /// <summary>Every tick (order f): our warp ending without our Stop (user cancel, or WarpTo stopping by itself; the two
        /// cannot be told apart) allows one re-request in a wait step that goes through CircularizeMode.WarpGate; a second such
        /// end, or any such end in another step (coast), latches as a user cancel.</summary>
        public static void Sync()
        {
            bool active = On || Pending;   // rails only; our physics warp is checked below
            Vessel av = FlightGlobals.ActiveVessel;
            bool belowTop = av != null && av.mainBody.atmosphere && av.altitude <= RailsFloor(av.mainBody);
            if (_requested && _wasActive && !active && belowTop)
            {
                _requested = false;   // #22 decision 11: KSP's altitude limit ended our rails at the atmosphere top, no latch
                Debug.Log("[KspBridge] warp ended at the atmosphere top (no latch)");
                Stopped("atmosphere", "rails");
            }
            else if (_requested && _wasActive && !active)
            {
                _requested = false;
                if (_resumeUsed || !_resumable)
                {
                    _cancelled = true;
                    Debug.Log("[KspBridge] warp ended without our Stop: latched for this step (user cancel)");
                }
                else
                {
                    _resumeUsed = true;
                    _resumePending = true;
                    Debug.Log("[KspBridge] warp ended without our Stop (user cancel or WarpTo end): one re-request allowed in this step");
                }
                Stopped("unexpected", "rails");
            }
            _wasActive = _requested && active;
            if (_physics && (TimeWarp.WarpMode != TimeWarp.Modes.LOW || TimeWarp.CurrentRateIndex != PhysicsRateIndex))
            {
                _physics = false;   // the rate was changed by the player: theirs from now on, latched for this step
                _cancelled = true;
                Debug.Log("[KspBridge] physics warp changed by the player: released and latched for this step");
                Stopped("unexpected", "physics");
            }
        }

        public static void Request(Vessel v, double targetUt) => Request(v, targetUt, false);

        /// <summary>physicsOk (#22 decision 11, land free fall): in the atmosphere our physics warp ×2 toward targetUt instead of
        /// rails. Our rails ending because the vessel went below the atmosphere top is no user cancel (ended_by atmosphere).</summary>
        public static void Request(Vessel v, double targetUt, bool physicsOk, string physicsWhy = null, bool physicsKeepOk = false,
            double physicsAttDeg = double.NaN, double physicsRate = double.NaN)
        {
            if (_cancelled)
            {
                return;
            }
            bool inAtmosphere = v.mainBody.atmosphere && v.altitude <= v.mainBody.atmosphereDepth;
            if (_requested && v.mainBody.atmosphere && v.altitude <= RailsFloor(v.mainBody) && !inAtmosphere)
            {
                StopRails("atmosphere");   // the lowest rails rate's altitude limit reached just above the atmosphere top
                return;
            }
            if (inAtmosphere)
            {
                if (_requested)
                {
                    StopRails("atmosphere");   // the atmosphere top crossed: our end, no latch
                }
                RequestPhysics(v, targetUt, physicsOk, physicsWhy, physicsKeepOk, physicsAttDeg, physicsRate);
                return;
            }
            if (_physics)
            {
                // DIAG (warp flapping, task a3b09a7f): left the atmosphere, with the altitude it was judged from
                StopPhysics("plan", "path: above the atmosphere, altitude "
                    + v.altitude.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " / top "
                    + v.mainBody.atmosphereDepth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "; "
                    + PhysicsDiag(physicsWhy, FlightInputHandler.state.mainThrottle, EngineModel.ThrustSum(v), targetUt - Planetarium.GetUniversalTime()));
            }
            // #22 decision 11 (U5b): the same floor as the stop above, so no band where it starts and stops every tick
            bool allowed = v.altitude > (v.mainBody.atmosphere ? RailsFloor(v.mainBody) : 0)
                && FlightInputHandler.state.mainThrottle == 0
                && EngineModel.ThrustSum(v) <= 0
                && !AutoStager.BlocksWarp;   // staging runs tick by tick: never on rails (a flameout pause alone does not block, #13)
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
            if (AutoWarpEngagedField == null || RailsRateMethod == null || TravelRateMethod == null)
            {
                Unavailable("not_supported");   // a running autoWarp would look stopped before its rate rises
                return;
            }
            // After the player-warp check: the rate index is 0 here, so rails index 1 > 0 is exactly WarpTo's first setRate(1).
            string refusal = KspRefusal(TimeWarp.fetch, targetUt - 10);
            if (refusal != _refusal)
            {
                _refusal = refusal;
                if (refusal != null)
                {
                    Debug.Log("[KspBridge] warp waits: KSP refuses (" + refusal + ")");
                }
            }
            if (refusal != null)
            {
                return;   // not requested yet; judged again next tick
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
            Debug.Log("[KspBridge] warp requested until UT " + targetUt.ToString("F2") + (_resumePending ? " (re-request)" : ""));
            Event("warp_started", null, _resumePending
                ? new Dictionary<string, object> { ["resumed"] = true, ["mode"] = "rails" }
                : new Dictionary<string, object> { ["mode"] = "rails" });
            _resumePending = false;
        }

        /// <summary>Releases our warp, or at a step change and abort any rails warp and pending WarpTo; leaves the player's physics
        /// warp alone (ours, #22 decision 11, is dropped).</summary>
        public static void Stop(string why = null)
        {
            StopRails("plan");
            if (_physics)
            {
                // DIAG (warp flapping, task a3b09a7f): the per-tick caller (Coordinator, no warp target this tick) names itself and
                // passes the mode's values; the one-shot callers (step change, abort, plan end, reload) pass nothing. Only static
                // reads here, so the release itself cannot fail on the diagnostic.
                StopPhysics("plan", "path: Stop, " + (why ?? "step change, abort, plan end or reload") + ", throttle "
                    + FlightInputHandler.state.mainThrottle.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ", ut "
                    + Planetarium.GetUniversalTime().ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void StopRails(string endedBy)
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
                Stopped(endedBy, "rails");   // no latch: our own release (plan) or the atmosphere top
            }
        }


        /// <summary>#22 decision 11: our physics warp ×2 in the atmosphere (land free fall). Kept while physicsOk (steady
        /// attitude), throttle 0, no thrust or staging and targetUt not reached; dropped back to ×1 otherwise. The player's
        /// warp is never taken over.</summary>
        private static void RequestPhysics(Vessel v, double targetUt, bool physicsOk, string physicsWhy, bool physicsKeepOk,
            double physicsAttDeg, double physicsRate)
        {
            float throttle = FlightInputHandler.state.mainThrottle;
            double thrust = EngineModel.ThrustSum(v);
            bool staging = AutoStager.BlocksWarp;
            double left = targetUt - Planetarium.GetUniversalTime();
            // 0070: ours on -> the mode's keep bound (which includes the start bound); off -> the start bound
            bool attitudeOk = _physics ? (physicsKeepOk || physicsOk) : physicsOk;
            bool ok = attitudeOk && throttle == 0 && thrust <= 0 && !staging && left > 0;
            if (ok && _physics)
            {
                // DIAG (0070): the largest values kept at ×2 (the stopping tick is left out: its values are in the stop line)
                if (!double.IsNaN(physicsAttDeg))
                {
                    _peakAtt = System.Math.Max(_peakAtt, physicsAttDeg);
                }
                if (!double.IsNaN(physicsRate))
                {
                    _peakRate = System.Math.Max(_peakRate, physicsRate);
                }
            }
            if (!ok)
            {
                if (_physics)
                {
                    // DIAG (warp flapping, task a3b09a7f): which condition failed, with the values it was judged from
                    string failed = (attitudeOk ? "" : " attitude") + (throttle == 0 ? "" : " throttle") + (thrust <= 0 ? "" : " thrust")
                        + (staging ? " staging" : "") + (left > 0 ? "" : " target_reached");
                    StopPhysics("plan", "path: conditions, failed:" + failed + "; " + PhysicsDiag(physicsWhy, throttle, thrust, left));
                }
                return;
            }
            if (_physics)
            {
                return;
            }
            if (Pending || On || (TimeWarp.WarpMode == TimeWarp.Modes.LOW && TimeWarp.CurrentRateIndex > 0))
            {
                Unavailable("player_warp");
                return;
            }
            if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && !SetMode(TimeWarp.fetch, TimeWarp.Modes.LOW))
            {
                Unavailable(SetModeMethod == null ? "not_supported" : "mode_switch_refused");
                return;
            }
            TimeWarp.SetRate(PhysicsRateIndex, true);
            _physics = true;
            _untilUt = targetUt;
            _peakAtt = -1;   // DIAG (0070): no ×2 tick yet
            _peakRate = -1;
            _peakSinceUt = Planetarium.GetUniversalTime();
            Debug.Log("[KspBridge] physics warp x" + TimeWarp.CurrentRate.ToString("F0") + " requested until UT " + targetUt.ToString("F2")
                + " (" + PhysicsDiag(physicsWhy, throttle, thrust, left) + ")");   // DIAG (task a3b09a7f)
            Event("warp_started", null, new Dictionary<string, object> { ["mode"] = "physics" });
        }

        private static void StopPhysics(string endedBy, string diag = null)
        {
            _physics = false;
            if (TimeWarp.WarpMode == TimeWarp.Modes.LOW && TimeWarp.CurrentRateIndex == PhysicsRateIndex)
            {
                TimeWarp.SetRate(0, true);
            }
            Debug.Log("[KspBridge] physics warp stopped (" + endedBy + (diag != null ? "; " + diag : "") + "; " + PeakDiag() + ")");
            Stopped(endedBy, "physics");
        }

        // DIAG (0070): the largest attitude error / angular rate kept during our current ×2 run (-1 = none yet), from its start UT
        private static double _peakAtt = -1, _peakRate = -1, _peakSinceUt = double.NaN;

        private static string PeakDiag()
        {
            System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
            string over = double.IsNaN(_peakSinceUt) ? "?" : (Planetarium.GetUniversalTime() - _peakSinceUt).ToString("F2", c);
            return "x2 peak " + (_peakAtt < 0 ? "att none" : "att " + _peakAtt.ToString("F2", c) + " deg") + ", "
                + (_peakRate < 0 ? "rate none" : "rate " + _peakRate.ToString("F4", c) + " rad/s") + " over " + over + " s";
        }


        /// <summary>DIAG (warp flapping, task a3b09a7f): the mode's attitude values, the other conditions and this tick's length
        /// and rate, for the physics warp start/stop log lines.</summary>
        private static string PhysicsDiag(string physicsWhy, float throttle, double thrust, double left) =>
            (physicsWhy ?? "no mode values") + ", throttle " + throttle.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + ", thrust " + thrust.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + ", left " + left.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " s, dt "
            + Time.fixedDeltaTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ", rate x"
            + TimeWarp.CurrentRate.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ", ut "
            + Planetarium.GetUniversalTime().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The lowest altitude where KSP still allows the first rails rate: the atmosphere top or that rate's altitude
        /// limit, whichever is higher (U5b: KSP ended our rails a little above 70 km).</summary>
        private static double RailsFloor(CelestialBody body)
        {
            double limit = TimeWarp.fetch != null ? TimeWarp.fetch.GetAltitudeLimit(1, body) : 0;
            return System.Math.Max(body.atmosphereDepth, limit) + 100;
        }

        /// <summary>Clears the user-cancel latch (step change, new plan).</summary>
        public static void ClearLatch()
        {
            _cancelled = false;
            _resumeUsed = false;
            _resumePending = false;
            _resumable = false;
            _unavailable.Clear();
            _refusal = null;
        }

        /// <summary>Why KSP would end a WarpTo(warpUt) at once (rate index stays 0), or null when it would warp.</summary>
        private static string KspRefusal(TimeWarp w, double warpUt)
        {
            // As TimeWarp.Update passes WarpTo's defaults (8, 2.5); 1 s margin for the time until KSP's Update judges it.
            object[] travel = { warpUt - Planetarium.GetUniversalTime() - 1, 8.0, 2.5, 0 };
            TravelRateMethod.Invoke(w, travel);
            if ((int)travel[3] <= 0)
            {
                return "too_close";
            }
            object[] rails = { 1, true, ClearToSaveStatus.CLEAR };
            if ((int)RailsRateMethod.Invoke(w, rails) <= 0)
            {
                return rails[2].ToString();
            }
            return null;
        }

        public static void LoadLatch(bool cancelled) => _cancelled = cancelled;

        /// <summary>The game's own rails/physics switch; false when it refuses (rate too high) or is not available.</summary>
        internal static bool SetMode(TimeWarp w, TimeWarp.Modes mode)
        {
            return SetModeMethod != null && (bool)SetModeMethod.Invoke(w, new object[] { mode });
        }

        internal static bool CanSetMode => SetModeMethod != null;

        /// <summary>warp_stopped with who ended it (plan | unexpected) and the time left to until_ut then.</summary>
        private static void Stopped(string endedBy, string mode)
        {
            double remaining = _untilUt - Planetarium.GetUniversalTime();
            Event("warp_stopped", null, new Dictionary<string, object>
            {
                ["ended_by"] = endedBy,
                ["mode"] = mode,
                ["remaining_s"] = double.IsNaN(remaining) ? null : (object)System.Math.Round(remaining, 1),
            });
            _untilUt = double.NaN;
        }

        /// <summary>Marks the current step as a wait step that may re-request once (CircularizeMode.WarpGate, every tick).</summary>
        public static void MarkResumable() => _resumable = true;

        /// <summary>A wait step stayed unaligned too long (design 0048 decision 1): warp_unavailable align_wait with the attitude
        /// error (deg, null when unknown) and the angular rate (rad/s) of that tick, so the failing gate is visible over MCP.</summary>
        public static void AlignWait(double attitudeErrorDeg, double angularRate) =>
            Unavailable("align_wait", new Dictionary<string, object>
            {
                ["attitude_error"] = double.IsNaN(attitudeErrorDeg) ? null : (object)System.Math.Round(attitudeErrorDeg, 3),
                ["angular_rate"] = System.Math.Round(angularRate, 4),
            });

        /// <summary>Why a coast step waits without our warp; once per step and reason, so the cause is visible over MCP.</summary>
        private static void Unavailable(string reason, Dictionary<string, object> extra = null)
        {
            if (!_unavailable.Add(reason))
            {
                return;
            }
            Debug.Log("[KspBridge] warp not requested: " + reason);
            Event("warp_unavailable", reason, extra);
        }

        private static void Event(string type, string reason, Dictionary<string, object> extra = null)
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
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> kv in extra)
                {
                    d[kv.Key] = kv.Value;
                }
            }
            FlightEvents.Add(type, Coordinator.StepIndex, d);
        }
    }
}
