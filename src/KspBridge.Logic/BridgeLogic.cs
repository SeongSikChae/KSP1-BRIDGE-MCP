using System;
using System.Collections.Generic;
using KspBridge.Attitude;
using KspBridge.Flight;
using UnityEngine;

namespace KspBridge
{
    /// <summary>Entry point of the replaceable logic assembly (created by the loader's LogicHost).</summary>
    public sealed class BridgeLogic : ILogic
    {
        private EventData<Vessel>.OnEvent _onVesselChange;
        private EventData<GameScenes>.OnEvent _onSceneLoad;

        /// <summary>True after Stop: requests already queued for this (replaced) logic are refused instead of re-hooking state no one can release.</summary>
        public static volatile bool Stopped;

        public string Version => Routes.Version;

        public IEnumerable<Route> Start(LogicContext context)
        {
            Stopped = false;
            Settings.Reload();
            _onVesselChange = _ => ReleaseAll();
            _onSceneLoad = _ => ReleaseAll();
            GameEvents.onVesselChange.Add(_onVesselChange);
            GameEvents.onGameSceneLoadRequested.Add(_onSceneLoad);
            SasMonitor.Hook();
            FlightRecorder.Hook();
            BroadcastEvents.Hook();
            AnomalyMonitor.Hook();
            return Routes.Build(context.Dispatcher);
        }

        public void FixedUpdate()
        {
            try
            {
                AttitudeController.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] attitude: " + e);
                AttitudeController.Release();
            }
            try
            {
                NodeExecutor.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] node executor: " + e);
                NodeExecutor.Cancel("error");
            }
            try
            {
                AscentGuidance.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] ascent guidance: " + e);
                AscentGuidance.Cancel("error");
            }
            try
            {
                AnomalyMonitor.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] anomaly monitor: " + e);
            }
            try
            {
                TriggerManager.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] triggers: " + e);
            }
            try
            {
                SasMonitor.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] sas monitor: " + e);
                SasMonitor.Clear();
            }
            try
            {
                FlightRecorder.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] flight recorder: " + e);
            }
            try
            {
                BroadcastEvents.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] broadcast events: " + e);
            }
            try
            {
                Handlers.WarpHandler.Tick();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] scheduled warp: " + e);
            }
        }

        public IList<string> Stop()
        {
            Stopped = true;
            List<string> released = new List<string>();
            if (NodeExecutor.Running)
            {
                released.Add("node execution");
            }
            NodeExecutor.Cancel("reloaded");
            if (AscentGuidance.Running)
            {
                released.Add("ascent guidance");
            }
            AscentGuidance.Cancel("reloaded");
            int triggers = TriggerManager.Clear();
            if (triggers > 0)
            {
                released.Add(triggers + " trigger(s)");
            }
            if (ThrottleLock.IsActive)
            {
                released.Add("throttle hold");
            }
            ThrottleLock.Clear();
            if (AttitudeController.IsActive)
            {
                released.Add("attitude hold");
            }
            AttitudeController.Release();
            if (SasMonitor.IsActive)
            {
                released.Add("sas mode hold");
            }
            SasMonitor.Clear();
            SasMonitor.Unhook();
            FlightRecorder.Stop();
            BroadcastEvents.Unhook();
            AnomalyMonitor.Unhook();
            if (_onVesselChange != null)
            {
                GameEvents.onVesselChange.Remove(_onVesselChange);
                GameEvents.onGameSceneLoadRequested.Remove(_onSceneLoad);
                _onVesselChange = null;
                _onSceneLoad = null;
            }
            return released;
        }

        private static void ReleaseAll()
        {
            NodeExecutor.Cancel("vessel_changed");
            AscentGuidance.Cancel("vessel_changed");
            TriggerManager.Clear();
            ThrottleLock.Clear();
            AttitudeController.Release();
            SasMonitor.Clear();
        }
    }
}
