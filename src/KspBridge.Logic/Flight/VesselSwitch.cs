using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// switch_vessel and the vessel source it shares with list_vessels (design 0035). Main thread only.
    /// </summary>
    public sealed class VesselSwitch
    {
        /// <summary>list_vessels and switch_vessel read the same vessels (design 0035 decision 9).</summary>
        internal static IEnumerable<Vessel> Candidates() => FlightGlobals.Vessels;

        /// <summary>The vessel list_vessels measures distances from: the active vessel in FLIGHT (409 not_in_flight /
        /// no_active_vessel as before); null in SPACECENTER and TRACKSTATION, which have no active vessel.</summary>
        internal static Vessel ListScopeActive()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s == GameScenes.SPACECENTER || s == GameScenes.TRACKSTATION)
            {
                FlightReset.RequireSceneReady();
                return null;
            }
            return Guard.ActiveVessel();
        }
    }
}
