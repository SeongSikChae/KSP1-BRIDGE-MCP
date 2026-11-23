using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>Game values the readiness judgement looks at; read by the module, judged here without the game.</summary>
    internal struct NodeReadinessInput
    {
        public bool GameLoaded;

        /// <summary>The core's block reason (FlightSession.BlockReason()): scene loading, flight scene not ready.</summary>
        public string CoreBlockReason;

        public string SceneName;
        public bool InFlight;
        public bool HasActiveVessel;

        /// <summary>The game has facility levels (ScenarioUpgradeableFacilities.Instance); without them both checks count as unlocked.</summary>
        public bool HasFacilityLevels;

        /// <summary>The tracking station level gives patched conics (GameVariables.GetOrbitDisplayMode).</summary>
        public bool TrackingAllowsConics;

        /// <summary>The mission control level unlocks flight planning (GameVariables.UnlockedFlightPlanning).</summary>
        public bool FlightPlanningUnlocked;

        /// <summary>The active vessel has its orbit solver (Vessel.patchedConicSolver).</summary>
        public bool HasSolver;
    }

    /// <summary>
    /// Whether the node tools may work now (spec C2-C4, D008): the scene, the active vessel and nodes being usable. Only the core's
    /// scene block and game state are looked at; input locks follow window focus and dialogs, so they are ignored.
    /// </summary>
    internal static class NodeAvailability
    {
        /// <summary>Null when ready, otherwise the not_ready reason.</summary>
        public static string Judge(NodeReadinessInput input)
        {
            if (!input.GameLoaded)
                return "no game is loaded (now " + input.SceneName + "); load a save first";
            if (input.CoreBlockReason != null)
                return input.CoreBlockReason;
            if (!input.InFlight)
                return "maneuver nodes need the flight scene (now " + input.SceneName + ")";
            if (!input.HasActiveVessel)
                return "there is no active vessel";
            return Usable(input);
        }

        /// <summary>Null when nodes are usable (spec terms "노드 사용 가능"), otherwise why not.</summary>
        public static string Usable(NodeReadinessInput input)
        {
            if (input.HasFacilityLevels)
            {
                List<string> missing = new List<string>();
                if (!input.TrackingAllowsConics)
                    missing.Add("the tracking station level does not give orbit prediction (patched conics)");
                if (!input.FlightPlanningUnlocked)
                    missing.Add("the mission control level does not unlock flight planning");
                if (missing.Count > 0)
                    return "maneuver nodes are locked: " + string.Join("; ", missing) + "; upgrade the facility";
            }
            if (!input.HasSolver)
                return "the active vessel has no orbit solver yet";
            return null;
        }
    }
}
