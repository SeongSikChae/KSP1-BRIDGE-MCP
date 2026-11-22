using System.Collections.Generic;

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// Tracks scene loads and flight identity. A scene load or an active-vessel change ends the running plan with a reason
    /// and starts a new session id, so status never shows a previous flight's plan.
    /// HighLogic.LoadScene sets LoadedScene at request time, so a load counts as finished only on the requested scene's
    /// ready signal (FLIGHT: onFlightReady; others: onLevelWasLoadedGUIReady).
    /// </summary>
    public sealed class FlightSession
    {
        private ICoordinator coordinator;

        public int Id { get; private set; } = 1;
        public bool Loading { get; private set; }
        public GameScenes? RequestedScene { get; private set; }

        public string Scene => HighLogic.LoadedScene.ToString();

        public bool InFlight => HighLogic.LoadedScene == GameScenes.FLIGHT;

        /// <summary>Requests may touch the game: no scene load in progress and the scene is usable.</summary>
        public bool Ready => BlockReason() == null;

        public string BlockReason()
        {
            if (Loading)
                return "scene " + RequestedScene + " is loading; poll get_status until ready=true";
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER)
                return "game is loading";
            if (InFlight && (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null))
                return "flight scene is not ready";
            return null;
        }

        /// <summary>Why a plan cannot start now, or null.</summary>
        public string PlanBlockReason()
        {
            string reason = BlockReason();
            if (reason != null)
                return reason;
            if (!InFlight)
                return "plans run in the flight scene (now " + Scene + ")";
            // The game keeps a destroyed vessel active until the player leaves or another vessel takes over.
            return FlightGlobals.ActiveVessel.state == Vessel.State.DEAD
                ? "the active vessel is destroyed; revert the flight or switch to another vessel"
                : null;
        }

        public void Hook(Subscriptions subscriptions, ICoordinator coordinator)
        {
            this.coordinator = coordinator;
            subscriptions.Add(GameEvents.onGameSceneLoadRequested, OnLoadRequested);
            subscriptions.Add(GameEvents.onFlightReady, OnFlightReady);
            subscriptions.Add(GameEvents.onLevelWasLoadedGUIReady, OnLevelReady);
            subscriptions.Add(GameEvents.onVesselChange, OnVesselChange);
        }

        private void OnLoadRequested(GameScenes scene)
        {
            string reason = InFlight && scene == GameScenes.FLIGHT ? EndReasons.NewFlight : EndReasons.SceneChange;
            coordinator?.Abort(reason, "scene load requested: " + scene);
            Loading = true;
            RequestedScene = scene;
            Id++;
        }

        private void OnFlightReady()
        {
            if (RequestedScene == GameScenes.FLIGHT)
                Loading = false;
        }

        private void OnLevelReady(GameScenes scene)
        {
            if (scene == RequestedScene && scene != GameScenes.FLIGHT)
                Loading = false;
        }

        private void OnVesselChange(Vessel vessel)
        {
            if (coordinator != null && coordinator.Running)
                coordinator.Abort(EndReasons.VesselChange, "active vessel changed to " + (vessel != null ? vessel.GetDisplayName() : "none"));
            Id++;
        }

        public Dictionary<string, object> Snapshot() => new Dictionary<string, object>
        {
            ["id"] = Id,
            ["loading"] = Loading,
            ["requested_scene"] = RequestedScene?.ToString(),
        };

        public void Restore(Dictionary<string, object> snapshot)
        {
            if (snapshot == null)
                return;
            if (snapshot.TryGetValue("id", out object id) && id is double d)
                Id = (int)d;
            if (snapshot.TryGetValue("loading", out object loading) && loading is bool b)
                Loading = b;
            if (snapshot.TryGetValue("requested_scene", out object scene) && scene is string s
                && System.Enum.TryParse(s, out GameScenes parsed))
                RequestedScene = parsed;
        }
    }
}
