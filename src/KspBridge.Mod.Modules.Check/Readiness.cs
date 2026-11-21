namespace KspBridge.Mod.Modules.Check
{
    /// <summary>Game values the readiness judgement looks at; read by <see cref="CheckModule"/>, judged here without the game.</summary>
    public struct ReadinessInput
    {
        public bool GameLoaded;

        /// <summary>The core's scene-load block reason (FlightSession.BlockReason()); null when no scene load is in progress.</summary>
        public string CoreBlockReason;

        public string SceneName;
        public bool EditorScene;
        public bool HasShip;
        public bool HasEditorDriver;

        /// <summary>EditorDriver.restartingEditor: true from an in-game facility switch request until the new ship's onEditorStarted.</summary>
        public bool RestartingEditor;

        /// <summary>Frames since the last onEditorRestart/onEditorLoad; int.MaxValue when none happened in this editor scene.</summary>
        public int FramesSinceLoad;

        public bool AllPartsStarted;

        /// <summary>StageManager.sortRoutine was found by reflection (another game version may not have it).</summary>
        public bool SortSignalFound;

        public bool SortRunning;
    }

    /// <summary>
    /// Whether a check may start now (C10). The same settle judgement as the editor module (frames since the load, every part
    /// started, the stage manager's sort finished) plus the core's scene-load block and the in-game facility switch. Input locks
    /// are deliberately not looked at: they follow window focus and dialogs, not whether the editor works.
    /// </summary>
    public static class Readiness
    {
        public const int SettleFrames = 3;

        /// <summary>Null when ready, otherwise the not_ready reason.</summary>
        public static string Judge(ReadinessInput input)
        {
            if (!input.GameLoaded)
                return "no game is loaded (now " + input.SceneName + "); load a save first";
            if (input.CoreBlockReason != null)
                return input.CoreBlockReason;
            if (!input.EditorScene)
                return "check_ship needs the editor scene (now " + input.SceneName + "); call editor_enter";
            if (!input.HasShip)
                return "editor is starting; retry shortly";
            if (!input.HasEditorDriver || input.RestartingEditor)
                return "editor is switching facility; retry shortly";
            if (input.FramesSinceLoad < SettleFrames)
                return "editor is loading the ship; retry shortly";
            if (!input.AllPartsStarted)
                return "editor is starting the ship's parts; retry shortly";
            if (!input.SortSignalFound)
                return "cannot tell when the stage manager has finished (StageManager.sortRoutine not found in this game version); check_ship is unavailable";
            if (input.SortRunning)
                return "editor is sorting stages; retry shortly";
            return null;
        }
    }
}
