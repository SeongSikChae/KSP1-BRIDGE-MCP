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

        public static void Unhook()
        {
            if (_hooked != null)
            {
                GameEvents.onGameSceneLoadRequested.Remove(_hooked.OnScene);
                _hooked = null;
            }
            Clear();
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
            if (h == null || !FlightGlobals.ready || FlightGlobals.ActiveVessel == null)
            {
                return;   // no vessel this tick: keep holding
            }
            try
            {
                ModuleEngines e = FindEngine(FlightGlobals.ActiveVessel, h.PartId);
                if (e == null)
                {
                    ThrottleHold = null;
                    UnityEngine.Debug.Log("[KspBridge] debug throttle hold released: engine part " + h.PartId + " not on the active vessel");
                    return;
                }
                e.currentThrottle = h.Throttle;
            }
            catch (Exception ex)
            {
                ThrottleHold = null;
                UnityEngine.Debug.LogError("[KspBridge] debug throttle hold released: " + ex);
            }
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

        /// <summary>Releases every debug state, the reload faults included.</summary>
        public static void Clear()
        {
            SetInputLock(null);
            InputLocksError = false;
            AddPartTimeout = false;
            StageDelayS = 0;
            ThrottleHold = null;
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
                ["throttle_hold"] = ThrottleHold is Hold h
                    ? new Dictionary<string, object> { ["part_id"] = (double)h.PartId, ["throttle"] = (double)h.Throttle }
                    : null,
                ["input_lock"] = InputLockMask.HasValue
                    ? new Dictionary<string, object> { ["id"] = LockId, ["mask"] = "0x" + InputLockMask.Value.ToString("X") }
                    : null,
                ["input_locks_error"] = InputLocksError,
                ["add_part_timeout"] = AddPartTimeout,
            };
        }
    }
}
