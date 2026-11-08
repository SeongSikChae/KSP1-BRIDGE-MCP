namespace KspBridge.Flight
{
    /// <summary>
    /// Holds the throttle set through the API: re-applied to the vessel's control state on every fly-by-wire pass,
    /// so stray throttle key input (e.g. Shift reaching the game window) cannot change it. Main thread only.
    /// </summary>
    public static class ThrottleLock
    {
        private static Vessel _vessel;
        private static float _value;
        public static bool IsActive => _vessel != null;

        public static bool Active(Vessel v) => _vessel != null && _vessel == v;

        /// <summary>The held value (meaningful only while active).</summary>
        public static float Value => _value;

        public static void Set(Vessel v, float value)
        {
            if (_vessel != v)
            {
                Clear();
                _vessel = v;
                v.OnFlyByWire += Apply;
            }
            _value = value;
        }

        public static void Clear()
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= Apply;
            }
            _vessel = null;
        }

        private static void Apply(FlightCtrlState st)
        {
            st.mainThrottle = _value;
            FlightInputHandler.state.mainThrottle = _value; // keep the gauge and the next input pass in sync
        }
    }
}
