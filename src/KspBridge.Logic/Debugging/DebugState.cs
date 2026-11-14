using System;
using System.Collections.Generic;
using System.IO;

namespace KspBridge.Debugging
{
    /// <summary>
    /// Verification-only debug state (#7). Off unless GameData/KspBridge/debug.cfg has KSPBRIDGE_DEBUG { enabled = true }:
    /// that file is not deployed by the build, so a build never turns it off. Released by debug_clear, a scene change and
    /// reload_mod (a new logic assembly starts with fresh statics).
    /// </summary>
    public static class DebugState
    {
        public const string LockId = "kspbridge_debug";

        public static volatile bool Enabled;

        public static void LoadFlag()
        {
            Enabled = false;
            try
            {
                string path = KSPUtil.ApplicationRootPath + "GameData/KspBridge/debug.cfg";
                ConfigNode n = File.Exists(path) ? ConfigNode.Load(path)?.GetNode("KSPBRIDGE_DEBUG") : null;
                Enabled = n != null && bool.TryParse(n.GetValue("enabled"), out bool on) && on;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[KspBridge] debug.cfg not read: " + e.Message);
            }
            if (Enabled)
            {
                UnityEngine.Debug.Log("[KspBridge] debug tools enabled");
            }
        }

        public static void Require()
        {
            if (!Enabled)
            {
                throw new ApiException(409, "debug_disabled", "create GameData/KspBridge/debug.cfg with KSPBRIDGE_DEBUG { enabled = true } and reload_mod");
            }
        }

        // KSP's EvtDelegate reads evt.Target.GetType(), which throws for static methods: an instance handler like FlightScene.
        private sealed class SceneHook
        {
            public void OnScene(GameScenes s) => Clear();
        }

        private static SceneHook _hooked;

        /// <summary>Only when enabled: with the flag off the start path is unchanged.</summary>
        public static void Hook()
        {
            if (!Enabled || _hooked != null)
            {
                return;
            }
            _hooked = new SceneHook();
            GameEvents.onGameSceneLoadRequested.Add(_hooked.OnScene);
        }

        /// <summary>Releases the debug state even when removing the handler throws (the exception still reaches the caller).</summary>
        public static void Unhook()
        {
            try
            {
                if (_hooked != null)
                {
                    GameEvents.onGameSceneLoadRequested.Remove(_hooked.OnScene);
                    _hooked = null;   // only after a successful Remove: a failed one can be retried by the next Unhook
                }
            }
            finally
            {
                Clear();
            }
        }

        /// <summary>debug_input_lock: the mask of the kspbridge_debug control lock, null when not set.</summary>
        public static ulong? InputLockMask;

        /// <summary>debug_input_locks_error: InputLocks.Describe throws.</summary>
        public static volatile bool InputLocksError;

        /// <summary>debug_add_part_timeout: add_part's Finish wait is 0. Read on the HTTP thread (add_part runs there).</summary>
        public static volatile bool AddPartTimeout;

        /// <summary>debug_stage_delay: StageRequest holds its confirmation this long after the first send. Main thread only
        /// (volatile double does not compile).</summary>
        public static double StageDelayS;

        public sealed class Hold
        {
            public readonly uint PartId;
            public readonly float Throttle;

            public Hold(uint partId, float throttle)
            {
                PartId = partId;
                Throttle = throttle;
            }
        }

        /// <summary>debug_throttle_hold: written to the engine's currentThrottle every FixedUpdate before Coordinator.Tick.</summary>
        public static volatile Hold ThrottleHold;

        /// <summary>debug_dock_reject: the next N dock attempts stop 2 m before contact and count as failed (design 0032
        /// decision 34). Main thread only (the dock mode reads and decrements it).</summary>
        public static int DockRejectLeft;

        /// <summary>The part's first ModuleEngines (multi-mode engines: first mode only); null when the part is not on the vessel
        /// or has no engine. A plain loop: Tick calls it every physics tick.</summary>
        public static ModuleEngines FindEngine(Vessel v, uint partId)
        {
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].persistentId == partId)
                {
                    return parts[i].FindModuleImplementing<ModuleEngines>();
                }
            }
            return null;
        }

        /// <summary>For the routes: 404 instead of null.</summary>
        public static ModuleEngines Engine(Vessel v, uint partId)
            => FindEngine(v, partId) ?? throw ApiException.NotFound("engine part " + partId + " not found on the active vessel");

        /// <summary>FixedUpdate, before Coordinator.Tick reads currentThrottle (AutoStager.KeptThrottleDown). Never throws.</summary>
        public static void Tick()
        {
            Hold h = ThrottleHold;
            if (h == null)
            {
                return;
            }
            try
            {
                if (!Flight.Coordinator.Running)
                {
                    // set only during a plan (the route checks): the plan completed or was aborted, stop driving the engine
                    ThrottleHold = null;
                    UnityEngine.Debug.Log("[KspBridge] debug throttle hold released: the flight plan ended");
                    return;
                }
                if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null)
                {
                    return;   // no vessel this tick: keep holding
                }
                ModuleEngines e = FindEngine(FlightGlobals.ActiveVessel, h.PartId);
                if (e == null)
                {
                    ThrottleHold = null;
                    UnityEngine.Debug.Log("[KspBridge] debug throttle hold released: engine part " + h.PartId + " not on the active vessel");
                    return;
                }
                _heldEngine = e;
                _heldBefore = e.currentThrottle;
                e.currentThrottle = h.Throttle;
            }
            catch (Exception ex)
            {
                ThrottleHold = null;
                UnityEngine.Debug.LogError("[KspBridge] debug throttle hold released: " + ex);
            }
        }

        // The engine written by this tick's Tick and its value before the write; AfterTick undoes the write when the plan ended
        // in the same tick. Main thread only.
        private static ModuleEngines _heldEngine;
        private static float _heldBefore;

        /// <summary>FixedUpdate, right after Coordinator.Tick: when the plan ended in this tick, put back the engine's value from
        /// before this tick's write and release, so the hold value does not stay for the ending tick. Never throws.</summary>
        public static void AfterTick()
        {
            ModuleEngines e = _heldEngine;
            _heldEngine = null;
            if (e == null || ThrottleHold == null)
            {
                return;
            }
            try
            {
                if (Flight.Coordinator.Running)
                {
                    return;
                }
                e.currentThrottle = _heldBefore;
                ThrottleHold = null;
                UnityEngine.Debug.Log("[KspBridge] debug throttle hold released: the flight plan ended in this tick (currentThrottle back to " + _heldBefore + ")");
            }
            catch (Exception ex)
            {
                ThrottleHold = null;
                UnityEngine.Debug.LogError("[KspBridge] debug throttle hold released: " + ex);
            }
        }

        /// <summary>debug_reload_fault: set by the tool, spent by the next Coordinator.Stop, released by Clear. Main thread only.</summary>
        public static int StartFailures, HandoverFailures;

        /// <summary>Coordinator.Stop, first thing: read and reset at once, so neither the Stop order nor the restarted previous
        /// logic (same assembly, same statics) can see them again.</summary>
        public static void TakeReloadFault(out int start, out int handover)
        {
            start = StartFailures;
            handover = HandoverFailures;
            StartFailures = HandoverFailures = 0;
        }

        /// <summary>Marks the handover so the next StartCore fails startFailures times (the loader hands the same string on).</summary>
        public static Dictionary<string, object> Tag(Dictionary<string, object> h, int startFailures)
        {
            if (startFailures > 0)
            {
                h["debug_fault"] = new Dictionary<string, object> { ["start_failures"] = (double)startFailures };
            }
            return h;
        }

        /// <summary>StartCore, right after LoadFlag: a handed-over start failure is spent here. Never throws on a bad handover
        /// (Handover.Parse handles that as Incompatible); throws only when a debug_fault asks for it and the flag is on
        /// (with the flag off the start path is unchanged; Handover.Parse ignores the extra key).</summary>
        public static void ConsumeStartFault(LogicContext context)
        {
            if (!Enabled || string.IsNullOrEmpty(context.Handover))
            {
                return;
            }
            Dictionary<string, object> h;
            try
            {
                h = Json.Json.Parse(context.Handover) as Dictionary<string, object>;
            }
            catch
            {
                return;
            }
            if (h == null || !(h.TryGetValue("debug_fault", out object f) && f is Dictionary<string, object> fault)
                || !(fault.TryGetValue("start_failures", out object n) && n is double left && left >= 1))
            {
                return;
            }
            if (left > 1)
            {
                fault["start_failures"] = left - 1;
            }
            else
            {
                h.Remove("debug_fault");
            }
            context.Handover = Json.Json.Serialize(h);   // the loader restarts the previous logic with this string
            throw new InvalidOperationException("debug: logic start failure injected (" + (left - 1) + " left)");
        }

        public static void SetInputLock(ulong? mask)
        {
            InputLockManager.RemoveControlLock(LockId);
            InputLockMask = mask;
            if (mask.HasValue)
            {
                InputLockManager.SetControlLock((ControlTypes)mask.Value, LockId);
            }
        }

        /// <summary>Releases every debug state, the reload faults included. The plain fields first, so a failing lock removal
        /// (the only game call, and only when a lock was set: none with the flag off) cannot leave them set.</summary>
        public static void Clear()
        {
            InputLocksError = false;
            AddPartTimeout = false;
            StageDelayS = 0;
            ThrottleHold = null;
            DockRejectLeft = 0;
            StartFailures = HandoverFailures = 0;
            if (InputLockMask.HasValue)
            {
                SetInputLock(null);
            }
        }

        /// <summary>get_status.debug: null with the flag off.</summary>
        public static Dictionary<string, object> Status()
        {
            if (!Enabled)
            {
                return null;
            }
            return new Dictionary<string, object>
            {
                ["stage_delay_s"] = StageDelayS,
                ["dock_reject_left"] = DockRejectLeft,
                ["throttle_hold"] = ThrottleHold is Hold h
                    ? new Dictionary<string, object> { ["part_id"] = (double)h.PartId, ["throttle"] = (double)h.Throttle }
                    : null,
                ["input_lock"] = InputLockMask.HasValue
                    ? new Dictionary<string, object> { ["id"] = LockId, ["mask"] = "0x" + InputLockMask.Value.ToString("X") }
                    : null,
                ["input_locks_error"] = InputLocksError,
                ["add_part_timeout"] = AddPartTimeout,
                ["reload_fault"] = new Dictionary<string, object>
                {
                    ["start_failures"] = (double)StartFailures,
                    ["handover_failures"] = (double)HandoverFailures,
                },
            };
        }
    }
}
