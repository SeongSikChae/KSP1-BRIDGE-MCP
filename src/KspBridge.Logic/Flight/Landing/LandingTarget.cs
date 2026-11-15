using System;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// Landing target argument (#22, docs 0060~0062): latitude+longitude, or a vessel id. Parsing checks the form only
    /// (submit and the reload re-parse alike); a vessel target is resolved when the land step starts (design decision 9).
    /// </summary>
    public sealed class LandingTarget
    {
        public static readonly string[] Args = { "latitude", "longitude", "vessel" };

        /// <summary>NaN for a vessel target.</summary>
        public double Latitude { get; }

        /// <summary>NaN for a vessel target; -180..180.</summary>
        public double Longitude { get; }

        /// <summary>persistentId; NaN for a coordinate target.</summary>
        public double VesselId { get; }

        public bool IsVessel => !double.IsNaN(VesselId);

        private LandingTarget(double latitude, double longitude, double vesselId)
        {
            Latitude = latitude;
            Longitude = longitude;
            VesselId = vesselId;
        }

        /// <summary>null = no target. A present key with null, a non-number, NaN or an out-of-range value, mixed coordinate
        /// and vessel keys, or only one of latitude/longitude throws through invalid(path, reason).</summary>
        internal static LandingTarget Parse(Func<string, bool> has, Func<string, object> get, string path,
                                            Func<string, string, Exception> invalid)
        {
            bool lat = has("latitude");
            bool lon = has("longitude");
            bool ves = has("vessel");
            if (!lat && !lon && !ves)
            {
                return null;
            }
            if (ves && (lat || lon))
            {
                throw invalid(path, "give latitude+longitude or vessel, not both");
            }
            if (ves)
            {
                double id = Number(get("vessel"), path + ".vessel", 0, uint.MaxValue, invalid);
                if (id != Math.Floor(id))
                {
                    throw invalid(path + ".vessel", "must be an integer vessel id");
                }
                return new LandingTarget(double.NaN, double.NaN, id);
            }
            if (lat != lon)
            {
                throw invalid(path, "latitude and longitude go together");
            }
            return new LandingTarget(Number(get("latitude"), path + ".latitude", -90, 90, invalid),
                                     Number(get("longitude"), path + ".longitude", -180, 180, invalid), double.NaN);
        }

        /// <summary>Longitude wrapped into -180..180.</summary>
        internal static double Wrap180(double lon)
        {
            lon %= 360;
            return lon > 180 ? lon - 360 : lon < -180 ? lon + 360 : lon;
        }

        /// <summary>null is not a default here: a present key must hold a number in [min, max].</summary>
        private static double Number(object o, string path, double min, double max, Func<string, string, Exception> invalid)
        {
            if (!(o is double d) || double.IsNaN(d) || d < min || d > max)
            {
                throw invalid(path, "must be a number in [" + min + ", " + max + "]");
            }
            return d;
        }
    }
}
