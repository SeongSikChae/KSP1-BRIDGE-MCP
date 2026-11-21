using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>Values of the game GamePersistence.LoadGameCfg built from a save (filled by DebugModule).</summary>
    public sealed class LoadedGameFacts
    {
        public bool GameNull;
        public bool GameCompatible;
        public bool FlightStateNull;
        public bool FlightStateCompatible;

        /// <summary>vesselID of each flightState.protoVessels entry, in order.</summary>
        public IList<Guid> ProtoVesselIds = new List<Guid>();
    }

    /// <summary>
    /// Post-check of the game built from a save (pure). The game may drop crewless EVA vessels while loading and the active
    /// index it keeps is not reliable afterwards, so the vessel to open is found by the pid read in the pre-check.
    /// </summary>
    public static class LoadedGameCheck
    {
        /// <summary>Null and the position of the saved active vessel when the game can be started, otherwise the reason.</summary>
        public static string Check(LoadedGameFacts facts, Guid activePid, out int index)
        {
            index = -1;
            if (facts == null || facts.GameNull)
                return "the game could not build a game from the save";
            if (!facts.GameCompatible)
                return "the game marked the save as incompatible";
            if (facts.FlightStateNull)
                return "the game built no flight state from the save";
            if (!facts.FlightStateCompatible)
                return "the game marked the save's flight state as incompatible";
            for (int i = 0; i < facts.ProtoVesselIds.Count; i++)
            {
                if (facts.ProtoVesselIds[i] == activePid)
                {
                    index = i;
                    return null;
                }
            }
            return "the saved active vessel (pid " + activePid.ToString("N") + ") is not among the " + facts.ProtoVesselIds.Count +
                   " vessels the game loaded";
        }
    }
}
