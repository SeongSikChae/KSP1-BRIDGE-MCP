namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>Values of the vessel debug_switch_vessel targets (filled by DebugModule).</summary>
    public sealed class VesselFacts
    {
        public bool Found;
        public bool Active;

        /// <summary>Vessel.loaded: in the scene (physics or on rails), so the game switches without a scene reload.</summary>
        public bool Loaded;

        /// <summary>DiscoveryInfo.Level is Owned.</summary>
        public bool Owned;

        public string Name;
    }

    public enum SwitchOutcome
    {
        Already,
        Proceed,
        Reject,
    }

    public sealed class SwitchDecision
    {
        public SwitchOutcome Outcome { get; private set; }
        public string Reason { get; private set; }

        public static SwitchDecision Of(SwitchOutcome outcome, string reason = null) => new SwitchDecision { Outcome = outcome, Reason = reason };
    }

    /// <summary>
    /// Decides debug_switch_vessel (pure). Order: not found → already active → not loaded → not owned (whatever force says;
    /// the game's forced switch would skip that check) → with force false, the save check result must be CLEAR.
    /// </summary>
    public static class VesselSwitchCheck
    {
        public const string Clear = "CLEAR";

        /// <param name="clearToSaveResult">FlightGlobals.ClearToSave result name; only used when <paramref name="force"/> is false.</param>
        public static SwitchDecision Decide(VesselFacts target, bool force, string clearToSaveResult)
        {
            if (target == null || !target.Found)
                return SwitchDecision.Of(SwitchOutcome.Reject, "no vessel has this id");
            if (target.Active)
                return SwitchDecision.Of(SwitchOutcome.Already);
            if (!target.Loaded)
                return SwitchDecision.Of(SwitchOutcome.Reject, "vessel '" + target.Name + "' is not loaded (too far away); only vessels in " +
                                                               "the scene can be switched to without reloading the flight");
            if (!target.Owned)
                return SwitchDecision.Of(SwitchOutcome.Reject, "vessel '" + target.Name + "' is not owned by the player");
            if (!force && clearToSaveResult != Clear)
                return SwitchDecision.Of(SwitchOutcome.Reject, "the game does not allow switching now (" + clearToSaveResult + "); pass " +
                                                               "force=true to switch anyway");
            return SwitchDecision.Of(SwitchOutcome.Proceed);
        }
    }
}
