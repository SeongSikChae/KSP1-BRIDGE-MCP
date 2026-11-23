using System;
using System.Globalization;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>The heading toward an inclination (spec C3, C7, D007). Angles in degrees, headings clockwise from north.</summary>
    public static class LaunchAzimuth
    {
        /// <summary>How far an inclination may be below the latitude (or above 180° − latitude) and still be flown (spec C7).</summary>
        public const double LatitudeSlackDeg = 0.5;

        /// <summary>Below this share of the target speed the velocity still needed only points the heading roughly (spec C3).</summary>
        public const double SmallShare = 0.05;

        private const double Rad = Math.PI / 180;

        /// <summary>Why the inclination cannot be flown from the latitude, or null.</summary>
        public static string StartFailure(double inclinationDeg, double latitudeDeg)
        {
            double lat = Math.Abs(latitudeDeg);
            if (inclinationDeg < lat - LatitudeSlackDeg || inclinationDeg > 180 - lat + LatitudeSlackDeg)
            {
                return "an inclination of " + inclinationDeg.ToString("0.###", CultureInfo.InvariantCulture) + "° cannot be reached from latitude " +
                       latitudeDeg.ToString("0.###", CultureInfo.InvariantCulture) + "°";
            }
            return null;
        }

        /// <summary>
        /// β with sin β = cos(inclination) ÷ cos(latitude): the solution with a north component of 0 or more for a north launch, 0 or
        /// less for a south launch. Without a solution the sine is clamped to ±1, which gives due east (inclination below the
        /// latitude) or due west (above 180° − latitude) for either launch: the fallback rule (spec C3, D018).
        /// </summary>
        public static double Inertial(double inclinationDeg, double latitudeDeg, bool north)
        {
            double s = Math.Cos(inclinationDeg * Rad) / Math.Cos(latitudeDeg * Rad);
            double beta = Math.Asin(Math.Max(-1, Math.Min(1, s))) / Rad;   // −90..90: north component cos β ≥ 0
            return Normalize(north ? beta : 180 - beta);
        }

        /// <summary>
        /// β for circularize (spec C8, D016): of the two solutions the one closer to the current horizontal orbital velocity; the
        /// north one on a tie or without a horizontal velocity.
        /// </summary>
        public static double Nearest(double inclinationDeg, double latitudeDeg, double northSpeed, double eastSpeed)
        {
            double north = Inertial(inclinationDeg, latitudeDeg, true);
            double south = Inertial(inclinationDeg, latitudeDeg, false);
            if (northSpeed * northSpeed + eastSpeed * eastSpeed < 1e-12)
                return north;
            double current = Math.Atan2(eastSpeed, northSpeed) / Rad;
            return Gap(south, current) < Gap(north, current) ? south : north;
        }

        private static double Gap(double a, double b)
        {
            double d = Math.Abs(Normalize(a) - Normalize(b));
            return d > 180 ? 360 - d : d;
        }

        /// <summary>
        /// The heading of (target horizontal orbital velocity − current horizontal orbital velocity); the inertial heading when what is
        /// left is below 5% of the target speed.
        /// </summary>
        public static double Heading(double inclinationDeg, double latitudeDeg, bool north, double northSpeed, double eastSpeed, double targetSpeed)
        {
            double beta = Inertial(inclinationDeg, latitudeDeg, north);
            double dn = targetSpeed * Math.Cos(beta * Rad) - northSpeed;
            double de = targetSpeed * Math.Sin(beta * Rad) - eastSpeed;
            if (Math.Sqrt(dn * dn + de * de) < SmallShare * targetSpeed)
                return beta;
            return Normalize(Math.Atan2(de, dn) / Rad);
        }

        public static double Normalize(double heading)
        {
            double h = heading % 360;
            if (h < 0)
                h += 360;
            return h >= 360 ? 0 : h;
        }
    }
}
