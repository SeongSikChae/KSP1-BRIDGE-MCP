namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>
    /// Decides debug_save like the game's F5 default rule (pure). A destroyed active vessel is refused first because
    /// FlightGlobals.ClearToSave reports CLEAR for it. Then only ladder, moving over the surface and about to crash block;
    /// in the atmosphere and under acceleration pass. GameSettings.CAN_ALWAYS_QUICKSAVE is not followed.
    /// </summary>
    public static class QuickSaveCheck
    {
        /// <summary>Null when the game may be saved, otherwise the reason.</summary>
        public static string Decide(bool activeDead, string clearToSaveResult)
        {
            if (activeDead)
                return "the active vessel is destroyed";
            switch (clearToSaveResult)
            {
                case "NOT_WHILE_ON_A_LADDER":
                case "NOT_WHILE_MOVING_OVER_SURFACE":
                case "NOT_WHILE_ABOUT_TO_CRASH":
                    return "the game does not allow saving now (" + clearToSaveResult + ")";
                default:
                    return null;
            }
        }
    }
}
