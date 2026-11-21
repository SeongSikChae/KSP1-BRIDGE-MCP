namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Game values the readiness judgement looks at; read by <see cref="FlightModule"/>, judged here without the game.</summary>
    public struct FlightReadinessInput
    {
        public bool GameLoaded;

        /// <summary>The core's block reason (FlightSession.BlockReason()): scene loading, flight scene not ready.</summary>
        public string CoreBlockReason;

        public string SceneName;
        public bool InFlight;
        public bool HasActiveVessel;
    }

    /// <summary>
    /// Whether get_flight may read the game now (C10). Only the core's scene block and game state are looked at; input locks
    /// follow window focus and dialogs, not whether the flight scene works, so they are deliberately ignored.
    /// </summary>
    public static class FlightReadiness
    {
        /// <summary>Null when ready, otherwise the not_ready reason.</summary>
        public static string Judge(FlightReadinessInput input)
        {
            if (!input.GameLoaded)
                return "no game is loaded (now " + input.SceneName + "); load a save first";
            if (input.CoreBlockReason != null)
                return input.CoreBlockReason;
            if (!input.InFlight)
                return "get_flight needs the flight scene (now " + input.SceneName + ")";
            if (!input.HasActiveVessel)
                return "there is no active vessel";
            return null;
        }
    }
}
