using System;
using System.Collections.Generic;
using System.Linq;
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

        // Flight-scene recovery waiting for the space center (VesselRetrieval order: save, load SPACECENTER, recover 8 frames later).
        private static Guid? _pendingId;                        // vessel to recover; null while none is pending
        private static bool _loaded;                            // the space center's GUIReady arrived
        private static float _deadline;                         // realtime: request → GUIReady 300 s, GUIReady → recovery 30 s
        private static Dictionary<string, object> _recovery;    // get_status reset.recovery (last flight-scene recovery)

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
            _pendingId = null;      // not handed over (0011): the vessel stays at the space center, recover_vessel(site) handles it
            _loaded = false;
            _recovery = null;
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
            if (_pendingId == null)
            {
                return;
            }
            if (scene != GameScenes.SPACECENTER)
            {
                Fail("scene_changed");
                return;
            }
            _loaded = false;                                      // our request or a repeat: wait for this load
            _deadline = Time.realtimeSinceStartup + 300f;
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
            if (_pendingId == null || scene != _requestedScene)
            {
                return;
            }
            if (scene == GameScenes.SPACECENTER)
            {
                _loaded = true;
                _deadline = Time.realtimeSinceStartup + 30f;      // the recovery limit starts once loaded
            }
            else
            {
                Fail("scene_changed");
            }
        }

        private static void Fail(string error)
        {
            if (_recovery != null)
            {
                _recovery["state"] = "failed";
                _recovery["error"] = error;
            }
            _pendingId = null;
            _loaded = false;
        }

        /// <summary>Called from Tick and Status, so a timeout is found even when FixedUpdate does not run (timeScale 0).</summary>
        private static void CheckDeadline()
        {
            if (_pendingId != null && Time.realtimeSinceStartup > _deadline)
            {
                Fail("timeout");
            }
        }

        /// <summary>
        /// BridgeLogic.FixedUpdate, after Coordinator.Tick. Recovers 8 render frames after the space center's GUIReady
        /// (VesselRetrieval: 8 × yield return null after sceneLoaded; GUIReady is sceneLoaded + 1), judged by the frame difference
        /// rather than the number of FixedUpdates, i.e. before the vessel markers spawn. Never throws.
        /// </summary>
        public static void Tick()
        {
            CheckDeadline();
            // _sceneLoading: a repeated SPACECENTER request makes it wait for that load to finish
            if (_pendingId == null || !_loaded || _sceneLoading || Time.frameCount - _readyFrame < 8)
            {
                return;
            }
            Guid id = _pendingId.Value;
            bool fired = false;   // once the recovery event (rewards) may have gone out, save even after a failure
            try
            {
                Vessel v = FlightGlobals.Vessels.FirstOrDefault(x => x.id == id);
                if (v == null)
                {
                    Fail("vessel_not_found");
                    return;
                }
                Dictionary<string, object> r = Measure(v.protoVessel, () =>
                {
                    fired = true;
                    GameEvents.onVesselRecovered.Fire(v.protoVessel, true);   // quick: VesselRecovery makes no result dialog
                    UnityEngine.Object.DestroyImmediate(v.gameObject);
                });
                Save();
                Safe(RefreshKscMarkers);   // in case FixedUpdate came late, after the markers spawned
                r["state"] = "done";
                _recovery = r;
                _pendingId = null;
                _loaded = false;
            }
            catch (Exception e)
            {
                if (fired)
                {
                    Safe(Save);
                }
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>The active vessel, like the recover button: the same checks, then VesselRetrieval's order (save, load the space
        /// center, recover there) with quick recovery so no result dialog waits for the player.</summary>
        private static object RecoverActive()
        {
            Vessel v = Guard.ActiveVessel();
            if (!v.IsRecoverable)
            {
                throw new ApiException(409, "not_recoverable", "the active vessel must be landed or splashed on "
                    + FlightGlobals.GetHomeBodyName() + " (situation " + v.situation + ")");
            }
            RequireLeave();
            RequireSave();
            if (Coordinator.Running)
            {
                Coordinator.Abort("recover", null, null);
            }
            Control.ManualControl.StopPulse("recover");   // outside Running: a plan and a pulse never run together
            Save();   // throws before anything is pending
            _pendingId = v.id;
            _loaded = false;
            _deadline = Time.realtimeSinceStartup + 300f;
            _recovery = new Dictionary<string, object> { ["state"] = "pending", ["vessel"] = v.vesselName, ["id"] = v.persistentId };
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return new Dictionary<string, object> { ["accepted"] = true, ["vessel"] = v.vesselName };
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

        internal static void RequireNoPendingRecovery()
        {
            if (_pendingId != null)
            {
                throw new ApiException(409, "recovery_pending", "a vessel recovery is in progress (get_status reset.recovery)");
            }
        }

        private static void RequireSave()
        {
            if (!HighLogic.CurrentGame.Parameters.Flight.CanAutoSave)
            {
                throw new ApiException(409, "save_disabled", "this game does not allow saving the persistent file");
            }
        }

        /// <summary>The public 3-argument save on every path: the start-scene overload is internal, and a stock game does not use
        /// the start scene (Game.Updated uses it only for mission games, which RequireStockGame refuses).</summary>
        private static void Save() => GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);

        /// <summary>Logs instead of throwing, so a failing side step (marker refresh, save after a failure) hides nothing.</summary>
        private static void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] flight reset: " + e);
            }
        }

        /// <summary>
        /// Space center vessel markers: KSCVesselMarkers spawns them Awake + 15 frames (DelayedCallback(15, SpawnVesselMarkers)) and
        /// listens to onVesselWillDestroy only, so a recovery (RecoverVesselFromFlight, DestroyImmediate) needs a refresh. Refreshing
        /// (Clear + Spawn) before the first spawn would duplicate it, so the refresh waits until 30 frames after the scene became
        /// ready, at least 1 frame like the game. Known difference: with a facility screen open (markers hidden) the refresh may draw
        /// them again (display only).
        /// </summary>
        private static void RefreshKscMarkers()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                return;
            }
            KSP.UI.Screens.KSCVesselMarkers m = KSP.UI.Screens.KSCVesselMarkers.fetch;
            if (m == null)
            {
                return;
            }
            int wait = Math.Max(1, 30 - (Time.frameCount - _readyFrame));
            m.StartCoroutine(CallbackUtil.DelayedCallback(wait, m.RefreshMarkers));
        }

        /// <summary>Same checks as the flight scene's Space Center and recover buttons (AltimeterSliderButtons).</summary>
        private static void RequireLeave()
        {
            ClearToSaveStatus s = FlightGlobals.ClearToSave(false);   // no game log: the reason goes into the response
            if (s != ClearToSaveStatus.CLEAR)
            {
                throw new ApiException(409, "leave_unavailable", "cannot leave now: " + s);
            }
            if (!HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter)
            {
                throw new ApiException(409, "leave_unavailable", "leaving to the space center is disabled in this game");
            }
        }

        /// <summary>Like the Space Center button: the vessel stays where it is. CanAutoSave is not checked, as the button does not
        /// (nothing is recovered, so a failed save cannot bring a vessel back).</summary>
        public static object Leave()
        {
            RequireNoPendingRecovery();
            RequireSceneReady();
            Guard.ActiveVessel();
            RequireStockGame("leave_flight");
            RequireLeave();
            if (Coordinator.Running)
            {
                Coordinator.Abort("leave", null, null);
            }
            Control.ManualControl.StopPulse("leave");
            Save();
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        /// <summary>recover_vessel. The flight scene is not read for site ("" included).</summary>
        public static object Recover(RequestBody b)
        {
            RequireNoPendingRecovery();
            RequireSceneReady();
            RequireStockGame("recover_vessel");   // no_game before the scene check
            GameScenes s = HighLogic.LoadedScene;
            if (s == GameScenes.FLIGHT)
            {
                return RecoverActive();
            }
            if (s != GameScenes.EDITOR && s != GameScenes.SPACECENTER)
            {
                throw ApiException.SceneNotAllowed("recover_vessel");
            }
            string site = b.Has("site") ? b.GetString("site") : null;
            if (string.IsNullOrWhiteSpace(site))
            {
                throw ApiException.BadRequest("site is required outside the flight scene");
            }
            return new Dictionary<string, object> { ["recovered"] = RecoverAtSite(site.Trim(), true) };
        }

        /// <summary>Launch sites only: facility launch sites (LaunchPad, Runway) or added launch sites. IsFacilityOrLaunchSite is
        /// not used: it accepts any facility.</summary>
        private static void RequireLaunchSite(string site)
        {
            PSystemSetup ps = PSystemSetup.Instance;
            bool ok = ps.SpaceCenterFacilityLaunchSites.Any(f => f.name == site || f.facilityName == site) || ps.IsLaunchSite(site, out _);
            if (!ok)
            {
                throw ApiException.BadRequest("unknown launch site '" + site + "'");
            }
        }

        /// <summary>The changes over the recovery (contract rewards included); null when the game has no such currency. crew is read
        /// before the recovery, which sends the crew back to the roster. id is the persistentId get_flight and get_status use.</summary>
        private static Dictionary<string, object> Measure(ProtoVessel pv, Action recover)
        {
            double? f0 = Funding.Instance?.Funds;
            float? s0 = ResearchAndDevelopment.Instance?.Science;
            float? r0 = Reputation.Instance?.reputation;
            List<string> crew = pv.GetVesselCrew().Select(c => c.name).ToList();
            string name = pv.vesselName;
            uint id = pv.persistentId;
            recover();
            return new Dictionary<string, object>
            {
                ["vessel"] = name,
                ["id"] = id,
                ["crew"] = crew,
                ["funds"] = Funding.Instance?.Funds - f0,
                ["science"] = ResearchAndDevelopment.Instance?.Science - s0,
                ["reputation"] = Reputation.Instance?.reputation - r0,
            };
        }

        /// <summary>
        /// Recovers every vessel landed at site in the current scene, like KSCVesselMarkers.RecoverVessel (quick: no result dialog),
        /// then saves. exact: landedAt must equal site (recover_vessel); otherwise the game's substring match (FindVesselsLandedAt,
        /// as LaunchSiteClear) for clear_site. Once a recovery event may have gone out, the save runs even after an exception.
        /// </summary>
        internal static List<Dictionary<string, object>> RecoverAtSite(string site, bool exact)
        {
            RequireLaunchSite(site);
            // VesselRecovery.OnVesselRecovered reads SpaceCenter.Instance.cb first and the event swallows the exception: recovering
            // without it would delete the vessel without funds or crew handling.
            if (SpaceCenter.Instance == null)
            {
                throw new ApiException(409, "not_supported", "vessel recovery needs the space center, which is not loaded in scene " + HighLogic.LoadedScene);
            }
            RequireSave();
            FlightState state = HighLogic.CurrentGame.flightState;
            List<ProtoVessel> found = ShipConstruction.FindVesselsLandedAt(state, site);
            if (exact)
            {
                found = found.Where(pv => pv.landedAt == site).ToList();
            }
            List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();
            bool attempted = false;
            try
            {
                foreach (ProtoVessel pv in found)
                {
                    attempted = true;
                    results.Add(Measure(pv, () => ShipConstruction.RecoverVesselFromFlight(pv, state, true)));
                }
            }
            finally
            {
                if (attempted)
                {
                    Save();   // the launch loads persistent
                    Safe(RefreshKscMarkers);
                }
            }
            return results;
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
            Control.ManualControl.StopPulse("revert");
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
            CheckDeadline();
            // Judged only in a loaded flight scene (LoadedScene changes when the load is requested).
            bool flight = HighLogic.LoadedScene == GameScenes.FLIGHT && !_sceneLoading && FlightGlobals.ActiveVessel != null;
            bool stock = IsStockGame();
            Vessel v = flight ? FlightGlobals.ActiveVessel : null;
            bool canRecover = flight && stock && _pendingId == null && v.IsRecoverable
                && FlightGlobals.ClearToSave(false) == ClearToSaveStatus.CLEAR   // no game log on every poll
                && HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter
                && HighLogic.CurrentGame.Parameters.Flight.CanAutoSave;
            EditorFacility facility = EditorFacility.None;
            List<string> launch = new List<string>();
            List<string> editor = new List<string>();
            if (flight && !stock)
            {
                launch.Add("not_supported");
            }
            else if (flight)
            {
                launch = RevertBlockers(false, out _);
                editor = RevertBlockers(true, out facility);
            }
            bool canRevertLaunch = flight && stock && launch.Count == 0;
            bool canRevertEditor = flight && stock && editor.Count == 0;
            return new Dictionary<string, object>
            {
                ["scene_ready"] = !_sceneLoading,
                ["requested_scene"] = _requestedScene?.ToString(),
                ["loading_seconds"] = _sceneLoading ? (object)(double)(Time.realtimeSinceStartup - _loadStarted) : null,
                ["can_recover"] = canRecover,
                ["can_revert_launch"] = canRevertLaunch,
                ["can_revert_editor"] = canRevertEditor,
                ["revert_editor_facility"] = canRevertEditor ? facility.ToString() : null,
                ["revert_unavailable"] = flight ? launch.Concat(editor).Distinct().ToList() : null,
                // a copy: the response is serialized on the HTTP thread (HttpServer) while Fail edits _recovery on the main thread
                ["recovery"] = _recovery == null ? null : new Dictionary<string, object>(_recovery),
            };
        }
    }
}
