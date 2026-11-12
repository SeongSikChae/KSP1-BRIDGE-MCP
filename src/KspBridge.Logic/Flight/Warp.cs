namespace KspBridge.Flight
{
    /// <summary>Time warp for coast and circularize waits. Stub until unit 9; only the Coordinator calls it.</summary>
    public static class Warp
    {
        /// <summary>True while a warp we requested is in effect.</summary>
        public static bool Requested => false;

        public static void Request(Vessel v, double untilUt)
        {
        }

        /// <summary>Ends a warp we requested (a user's warp is left alone).</summary>
        public static void Stop()
        {
        }

        /// <summary>Matches the internal state to the game's actual warp.</summary>
        public static void Sync()
        {
        }

        /// <summary>Clears the user-cancel latch.</summary>
        public static void ClearLatch()
        {
        }
    }
}
