using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// switch_vessel (design 0035) and the vessel source it shares with list_vessels. One switch at a time; its result is
    /// get_status reset.switch. Not handed over on reload_mod (design decision 3). Main thread only. Game event handlers are
    /// instance methods: KSP's EvtDelegate reads evt.Target.GetType(), which throws for static methods (same as FlightReset).
    /// </summary>
    public sealed class VesselSwitch
    {
        private const float InSceneSeconds = 30f;
        private const float ReloadSeconds = 300f;

        private static VesselSwitch _hooked;
        private static uint? _targetId;                       // null: no switch pending
        private static string _path;                          // in_scene | reload
        private static float _deadline;                       // Time.realtimeSinceStartup limit of the pending switch
        private static Dictionary<string, object> _status;    // get_status reset.switch (last switch); null before any

        /// <summary>list_vessels and switch_vessel read the same vessels (design 0035 decision 9).</summary>
        internal static IEnumerable<Vessel> Candidates() => FlightGlobals.Vessels;

        /// <summary>The vessel list_vessels measures distances from: the active vessel in FLIGHT (409 not_in_flight /
        /// no_active_vessel as before); null in SPACECENTER and TRACKSTATION, which have no active vessel.</summary>
        internal static Vessel ListScopeActive()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s == GameScenes.SPACECENTER || s == GameScenes.TRACKSTATION)
            {
                FlightReset.RequireSceneReady();
                return null;
            }
            return Guard.ActiveVessel();
        }

        public static void Hook()
        {
            Unhook();
            VesselSwitch s = new VesselSwitch();
            GameEvents.onGameSceneLoadRequested.Add(s.OnLoadRequested);
            _hooked = s;
        }

        /// <summary>Removes the handler and clears the state: a pending switch is not handed over (the game still finishes it;
        /// get_flight shows the result).</summary>
        public static void Unhook()
        {
            VesselSwitch s = _hooked;
            _hooked = null;
            _targetId = null;
            _status = null;
            if (s != null)
            {
                GameEvents.onGameSceneLoadRequested.Remove(s.OnLoadRequested);
            }
        }

        /// <summary>An in-scene switch never loads a scene; a reload switch loads FLIGHT only.</summary>
        private void OnLoadRequested(GameScenes scene)
        {
            if (_targetId != null && (_path == "in_scene" || scene != GameScenes.FLIGHT))
            {
                Finish("failed", "scene_changed", null);
            }
        }

        /// <summary>POST api/flight/switch. Every pre-check runs before anything changes (design 0035 error table order).</summary>
        /// <summary>409 switch_pending while a switch waits (switch_vessel, open_tracking_station).</summary>
        internal static void RequireNotPending()
        {
            if (_targetId != null)
            {
                throw new ApiException(409, "switch_pending", "a vessel switch is in progress (get_status reset.switch)");
            }
        }

        public static object Switch(RequestBody b)
        {
            double idArg = b.GetDouble("vessel_id");
            if (idArg != Math.Floor(idArg) || idArg <= 0 || idArg > uint.MaxValue)
            {
                throw ApiException.BadRequest("vessel_id must be a vessel id from list_vessels");
            }
            FlightReset.RequireNoPendingRecovery();
            RequireNotPending();
            FlightReset.RequireSceneReady();
            FlightReset.RequireStockGame("switch_vessel");
            GameScenes scene = HighLogic.LoadedScene;
            bool flight = scene == GameScenes.FLIGHT;
            if (!flight && scene != GameScenes.SPACECENTER && scene != GameScenes.TRACKSTATION)
            {
                throw ApiException.SceneNotAllowed("switch_vessel");
            }
            Vessel active = flight ? Guard.ActiveVessel() : null;   // 409 no_active_vessel
            if (Coordinator.Running)
            {
                throw new ApiException(409, "plan_running", "a flight plan is running; abort_flight first");
            }
            uint id = (uint)idArg;
            Vessel target = Candidates().FirstOrDefault(x => x != null && x.persistentId == id && x.state != Vessel.State.DEAD)
                ?? throw new ApiException(404, "vessel_not_found", "no vessel with id " + id + " (use list_vessels)");
            if (target.vesselType == VesselType.Flag || target.vesselType == VesselType.SpaceObject
                || target.DiscoveryInfo == null || target.DiscoveryInfo.Level != DiscoveryLevels.Owned)
            {
                throw new ApiException(409, "not_switchable", target.vesselName + " (" + target.vesselType + ") cannot be flown");
            }
            if (target == active)
            {
                _status = Row(target, "done", null, false);
                _status["already_active"] = true;
                return new Dictionary<string, object>
                {
                    ["accepted"] = true, ["vessel"] = target.vesselName, ["id"] = target.persistentId, ["already_active"] = true,
                };
            }
            string path = flight && target.loaded ? "in_scene" : "reload";
            if (path == "reload" && flight)
            {
                // the active vessel goes on rails and is saved: refuse what the game's save check refuses (decision 2)
                ClearToSaveStatus c = FlightGlobals.ClearToSave(false);   // no game log
                if (c != ClearToSaveStatus.CLEAR)
                {
                    throw new ApiException(409, "switch_unavailable", "cannot leave " + active.vesselName + " now: " + c);
                }
            }
            bool warpLowered = LowerWarp();
            Control.ManualControl.StopPulse("switch");   // no plan runs (checked above): only a pulse can be going
            Begin(target, path, warpLowered);
            string lowered = warpLowered ? " (time warp was stopped)" : "";
            try
            {
                if (!flight)
                {
                    // the Tracking Station's Fly order (design 0035: game behaviour)
                    int idx = FlightGlobals.Vessels.IndexOf(target);
                    FlightReset.Save();
                    FlightDriver.StartAndFocusVessel("persistent", idx);
                }
                else if (path == "in_scene" ? !FlightGlobals.ForceSetActiveVessel(target)
                    : !FlightGlobals.SetActiveVessel(target))   // unloaded: the game saves and calls StartAndFocusVessel itself
                {
                    Finish("failed", "switch_refused", null);
                    throw new ApiException(409, "switch_refused", "the game refused to switch to " + target.vesselName + lowered);
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception e)
            {
                Finish("failed", e.GetType().Name + ": " + e.Message, null);
                throw new ApiException(500, "switch_failed", "switching to " + target.vesselName + " failed: " + e.Message + lowered);
            }
            return new Dictionary<string, object>
            {
                ["accepted"] = true, ["vessel"] = target.vesselName, ["id"] = target.persistentId, ["path"] = path,
                ["warp_lowered"] = warpLowered,
            };
        }

        /// <summary>BridgeLogic.FixedUpdate after FlightReset.Tick, and StatusCopy (FixedUpdate does not run at timeScale 0).
        /// Never throws.</summary>
        public static void Tick()
        {
            if (_targetId == null)
            {
                return;
            }
            try
            {
                bool ready = HighLogic.LoadedScene == GameScenes.FLIGHT && FlightReset.SceneReady;
                Vessel v = ready ? FlightGlobals.ActiveVessel : null;
                bool onTarget = v != null && v.persistentId == _targetId;
                if (Time.realtimeSinceStartup > _deadline)
                {
                    // e.g. a paused game: the switch happened but unpacking waits for physics
                    if (onTarget)
                    {
                        Finish("done", null, !v.packed);
                    }
                    else
                    {
                        Finish("failed", "timeout", null);
                    }
                    return;
                }
                if (!ready)
                {
                    return;
                }
                if (_path == "in_scene" && !Candidates().Any(x => x != null && x.persistentId == _targetId))
                {
                    Finish("failed", "vessel_lost", null);
                }
                else if (onTarget && !v.packed)
                {
                    Finish("done", null, true);
                }
                else if (_path == "reload" && v != null && !onTarget)
                {
                    Finish("failed", "wrong_vessel", null);
                }
            }
            catch (Exception e)
            {
                Finish("failed", e.GetType().Name + ": " + e.Message, null);
            }
        }

        /// <summary>get_status reset.switch: judged first, then a copy (the response is serialized on the HTTP thread).</summary>
        internal static Dictionary<string, object> StatusCopy()
        {
            Tick();
            return _status == null ? null : new Dictionary<string, object>(_status);
        }

        private static void Begin(Vessel target, string path, bool warpLowered)
        {
            _targetId = target.persistentId;
            _path = path;
            _deadline = Time.realtimeSinceStartup + (path == "in_scene" ? InSceneSeconds : ReloadSeconds);
            _status = Row(target, "pending", path, warpLowered);
            FlightEvents.Add("switch_started", -1, new Dictionary<string, object>
            {
                ["vessel_id"] = target.persistentId, ["vessel"] = target.vesselName, ["path"] = path,
            });
        }

        private static void Finish(string state, string error, bool? unpacked)
        {
            if (_targetId == null)
            {
                return;
            }
            uint id = _targetId.Value;
            _targetId = null;
            if (_status != null)
            {
                _status["state"] = state;
                _status["error"] = error;
                _status["unpacked"] = unpacked;
            }
            Dictionary<string, object> data = new Dictionary<string, object> { ["vessel_id"] = id, ["path"] = _path };
            if (error != null)
            {
                data["error"] = error;
            }
            FlightEvents.Add(state == "done" ? "switch_done" : "switch_failed", -1, data);
        }

        private static Dictionary<string, object> Row(Vessel v, string state, string path, bool warpLowered) =>
            new Dictionary<string, object>
            {
                ["state"] = state,
                ["vessel"] = v.vesselName,
                ["id"] = v.persistentId,
                ["path"] = path,
                ["warp_lowered"] = warpLowered,
                ["unpacked"] = null,
                ["error"] = null,
            };

        /// <summary>Stops a pending warp-to, a non-zero rate, or a rails rate still easing down (as Warp.Stop judges it).</summary>
        private static bool LowerWarp()
        {
            TimeWarp w = TimeWarp.fetch;
            if (w == null)
            {
                return false;
            }
            bool easing = TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1f;
            bool warping = Warp.Pending || TimeWarp.CurrentRateIndex > 0 || easing;
            if (warping)
            {
                TimeWarpControl.StopAll(w);
            }
            return warping;
        }
    }
}
