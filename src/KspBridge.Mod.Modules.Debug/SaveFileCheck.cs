using System;
using System.Globalization;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>Values read straight from a save file's ConfigNode (filled by DebugModule; no game object is built).</summary>
    public sealed class SaveFileFacts
    {
        /// <summary>The root has a GAME node.</summary>
        public bool HasGame;

        /// <summary>GAME/version ("major.minor.revision").</summary>
        public string Version;

        /// <summary>GAME/scene (GameScenes as an integer; FLIGHT is 7).</summary>
        public string Scene;

        /// <summary>GAME/FLIGHTSTATE exists.</summary>
        public bool HasFlightState;

        /// <summary>Number of VESSEL nodes under FLIGHTSTATE.</summary>
        public int VesselCount;

        /// <summary>FLIGHTSTATE/activeVessel.</summary>
        public string ActiveVessel;

        /// <summary>pid of the VESSEL node activeVessel points at; null when that node has no pid or does not exist.</summary>
        public string ActivePid;
    }

    /// <summary>
    /// Pre-check of a save before anything touches the game (pure). Order: damaged values → not a flight save → active vessel
    /// pid → game version. Values beyond these are left to the post-check of the loaded game (LoadedGameCheck).
    /// </summary>
    public static class SaveFileCheck
    {
        /// <summary>GameScenes.FLIGHT.</summary>
        public const int FlightScene = 7;

        /// <summary>Null when the file may be loaded, otherwise the reason.</summary>
        public static string Check(SaveFileFacts facts, int major, int minor, int revision)
        {
            if (facts == null || !facts.HasGame)
                return Damaged("there is no GAME node");
            if (!TryVersion(facts.Version, out int[] version))
                return Damaged("GAME/version '" + facts.Version + "' is not three integers joined by dots");
            if (!TryInt(facts.Scene, out int scene))
                return Damaged("GAME/scene '" + facts.Scene + "' is not an integer");
            int active = 0;
            if (facts.HasFlightState && !TryInt(facts.ActiveVessel, out active))
                return Damaged("FLIGHTSTATE/activeVessel '" + facts.ActiveVessel + "' is not an integer");

            if (scene != FlightScene)
                return NotFlight("it starts in scene " + scene + ", not FLIGHT (" + FlightScene + ")");
            if (!facts.HasFlightState)
                return NotFlight("there is no FLIGHTSTATE node");
            if (facts.VesselCount <= 0)
                return NotFlight("FLIGHTSTATE has no vessel");
            if (active < 0 || active >= facts.VesselCount)
                return NotFlight("FLIGHTSTATE/activeVessel " + active + " is not one of its " + facts.VesselCount + " vessels");

            if (facts.ActivePid == null)
                return Damaged("the active vessel has no pid");
            if (!TryGuid(facts.ActivePid, out _))
                return Damaged("the active vessel pid '" + facts.ActivePid + "' is not a GUID");

            if (version[0] != major || version[1] != minor || version[2] != revision)
                return "the save was written by game version " + version[0] + "." + version[1] + "." + version[2] + ", this game is " +
                       major + "." + minor + "." + revision + " (load it with the game's own load, which upgrades it)";
            return null;
        }

        public static bool TryGuid(string value, out Guid guid)
        {
            guid = Guid.Empty;
            if (value == null)
                return false;
            try
            {
                guid = new Guid(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static string Damaged(string why) => "the save file is damaged: " + why;

        private static string NotFlight(string why) => "not a save made in flight: " + why;

        private static bool TryInt(string value, out int result)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        private static bool TryVersion(string value, out int[] parts)
        {
            parts = null;
            if (value == null)
                return false;
            string[] split = value.Split('.');
            if (split.Length != 3)
                return false;
            int[] result = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!int.TryParse(split[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }
            parts = result;
            return true;
        }
    }
}
