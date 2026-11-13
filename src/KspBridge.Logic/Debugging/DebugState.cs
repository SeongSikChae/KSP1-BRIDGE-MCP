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
                ["input_lock"] = InputLockMask.HasValue
                    ? new Dictionary<string, object> { ["id"] = LockId, ["mask"] = "0x" + InputLockMask.Value.ToString("X") }
                    : null,
                ["input_locks_error"] = InputLocksError,
            };
        }
    }
}
