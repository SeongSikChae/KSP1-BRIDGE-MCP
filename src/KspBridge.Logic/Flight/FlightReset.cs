using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Flight reset and recovery tools (revert_flight, leave_flight, recover_vessel, launch_craft clear_site) and the scene-load
    /// tracking they rely on. Main thread only. Game event handlers are instance methods: KSP's EvtDelegate reads
    /// evt.Target.GetType(), which throws for static methods (same as FlightScene).
    /// </summary>
    public sealed class FlightReset
    {
        private static FlightReset _hooked;

        // Scene-load tracking. HighLogic.LoadScene sets LoadedScene when the load is requested, so LoadedScene cannot tell
        // when a load has finished: a load counts as finished on the requested scene's ready signal (FLIGHT: onFlightReady,
        // which FlightDriver.PostInit fires after the post-init backup; other scenes: onLevelWasLoadedGUIReady).
        private static bool _sceneLoading;
        private static GameScenes? _requestedScene;   // last requested scene; null until a load is requested after start
        private static float _loadStarted;            // Time.realtimeSinceStartup of the last load request
        private static int _readyFrame;               // Time.frameCount when the requested scene became ready

        // Flight-scene recovery waiting for the space center (filled in by recover_vessel; null while none is pending).
        private static Guid? _pendingId;

        public static void Hook()
        {
            Unhook();
            FlightReset r = new FlightReset();
            GameEvents.onGameSceneLoadRequested.Add(r.OnLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(r.OnLevelReady);
            GameEvents.onFlightReady.Add(r.OnFlightReady);
            _hooked = r;
        }

        /// <summary>Removes the handlers and clears the tracking state (the next logic starts with nothing loading).</summary>
        public static void Unhook()
        {
            FlightReset r = _hooked;
            _hooked = null;
            _sceneLoading = false;
            _requestedScene = null;
            _pendingId = null;
            if (r == null)
            {
                return;
            }
            GameEvents.onGameSceneLoadRequested.Remove(r.OnLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(r.OnLevelReady);
            GameEvents.onFlightReady.Remove(r.OnFlightReady);
        }

        private void OnLoadRequested(GameScenes scene)
        {
            _sceneLoading = true;
            _requestedScene = scene;
            _loadStarted = Time.realtimeSinceStartup;
        }

        /// <summary>FlightDriver.PostInit fires it after PostInitState is made: only then is revert to launch this flight's.</summary>
        private void OnFlightReady()
        {
            if (_requestedScene == GameScenes.FLIGHT)
            {
                _sceneLoading = false;
                _readyFrame = Time.frameCount;
            }
        }

        /// <summary>Not fired for LOADINGBUFFER. FLIGHT is released by OnFlightReady. A GUIReady of another scene than the last
        /// requested one (e.g. FLIGHT after FlightDriver.Start requested SPACECENTER) is ignored.</summary>
        private void OnLevelReady(GameScenes scene)
        {
            if (scene == _requestedScene && scene != GameScenes.FLIGHT)
            {
                _sceneLoading = false;
                _readyFrame = Time.frameCount;
            }
        }

        internal static bool IsStockGame()
        {
            Game game = HighLogic.CurrentGame;
            return game != null && game.Mode != Game.Modes.MISSION && game.Mode != Game.Modes.MISSION_BUILDER
                && game.Mode != Game.Modes.SCENARIO_NON_RESUMABLE;
        }

        /// <summary>Same mode set as CraftFiles.Launch.</summary>
        internal static void RequireStockGame(string tool)
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "no_game", "load a save first");
            }
            if (!IsStockGame())
            {
                throw new ApiException(409, "not_supported", tool + " does not support " + HighLogic.CurrentGame.Mode + " games");
            }
        }

        internal static void RequireSceneReady()
        {
            if (_sceneLoading)
            {
                throw new ApiException(409, "scene_loading", "a scene is still loading; poll get_status until reset.scene_ready " +
                    "(if reset.loading_seconds keeps growing, reload_mod)");
            }
        }

        private static void RequireNoPendingRecovery()
        {
            if (_pendingId != null)
            {
                throw new ApiException(409, "recovery_pending", "a vessel recovery is in progress (get_status reset.recovery)");
            }
        }

        public static object Revert(string target)
        {
            RequireNoPendingRecovery();
            RequireSceneReady();          // FLIGHT is ready only after onFlightReady (post-init backup)
            Guard.ActiveVessel();         // 409 not_in_flight / no_active_vessel
            RequireStockGame("revert_flight");
            bool editor = target == "editor";
            if (!editor && target != "launch")
            {
                throw ApiException.BadRequest("target must be launch or editor");
            }
            List<string> why = RevertBlockers(editor, out EditorFacility facility);
            if (why.Count > 0)
            {
                throw new ApiException(409, "revert_unavailable", "cannot revert to " + target + ": " + string.Join(", ", why));
            }
            if (Coordinator.Running)
            {
                Coordinator.Abort("revert", null, null);
            }
            if (editor)
            {
                FlightDriver.RevertToPrelaunch(facility);
            }
            else
            {
                FlightDriver.RevertToLaunch();
            }
            Dictionary<string, object> r = new Dictionary<string, object> { ["accepted"] = true, ["target"] = target };
            if (editor)
            {
                r["facility"] = facility.ToString();
            }
            return r;
        }

        /// <summary>Same checks as PauseMenu.drawStockRevertOptions.</summary>
        internal static List<string> RevertBlockers(bool editor, out EditorFacility facility)
        {
            GameParameters.FlightParams p = HighLogic.CurrentGame.Parameters.Flight;
            List<string> why = new List<string>();
            facility = EditorFacility.None;
            if (!editor)
            {
                if (!FlightDriver.CanRevertToPostInit)
                {
                    why.Add("no_post_init_state");
                }
                if (!p.CanRestart)
                {
                    why.Add("restart_disabled");
                }
                return why;
            }
            if (!p.CanLeaveToEditor)
            {
                why.Add("leave_to_editor_disabled");
            }
            if (!FlightDriver.CanRevertToPrelaunch || ShipConstruction.ShipConfig == null)
            {
                why.Add("no_prelaunch_state");
            }
            else
            {
                facility = ShipConstruction.ShipType;
                if (facility != EditorFacility.VAB && facility != EditorFacility.SPH)
                {
                    why.Add("unknown_facility");
                }
            }
            return why;
        }

        /// <summary>get_status reset.</summary>
        public static Dictionary<string, object> Status()
        {
            return new Dictionary<string, object>
            {
                ["scene_ready"] = !_sceneLoading,
                ["requested_scene"] = _requestedScene?.ToString(),
                ["loading_seconds"] = _sceneLoading ? (object)(double)(Time.realtimeSinceStartup - _loadStarted) : null,
            };
        }
    }
}
