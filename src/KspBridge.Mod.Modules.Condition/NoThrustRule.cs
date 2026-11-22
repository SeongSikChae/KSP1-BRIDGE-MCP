namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>Throttle and engine counts of a vessel on one physics frame.</summary>
    internal struct EngineState
    {
        public double Throttle;

        /// <summary>Engines the game marks as flamed out, whether or not they are still ignited (a spent solid booster stays so).</summary>
        public int FlamedOut;

        /// <summary>Engines the game calls operational (ignited and not flamed out).</summary>
        public int Operational;
    }

    /// <summary>
    /// No thrust (spec terms): the throttle asks for thrust, an engine has gone out and none is running. Not before the first
    /// ignition, not after the spent stage is gone, not at zero throttle.
    /// </summary>
    internal static class NoThrustRule
    {
        public static bool Judge(EngineState state) => state.Throttle > 0 && state.FlamedOut > 0 && state.Operational == 0;
    }
}
