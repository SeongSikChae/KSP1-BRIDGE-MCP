using System.Globalization;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Whether a stage step succeeded, judged from what the game did (D010); a failure reason, or null for success.</summary>
    internal static class StageOutcome
    {
        public const string NoStagesLeft = "no stages left";
        public const string Refused = "the game refused staging";

        /// <summary>Before staging: there must be a next stage.</summary>
        public static string Before(int? nextStage) => nextStage.HasValue ? null : NoStagesLeft;

        /// <summary>After staging: the game accepted it and at least one part of the stage was activated.</summary>
        public static string After(int stage, bool accepted, int activatedParts)
        {
            if (!accepted)
                return Refused;
            return activatedParts > 0 ? null : "no part of stage " + stage.ToString(CultureInfo.InvariantCulture) + " was activated";
        }
    }
}
