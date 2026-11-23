using KspBridge.Mod.Modules.Flight;
using System;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Directions in the surface frame, and the angle of attack limit of spec C3.</summary>
    internal static class AoaLimit
    {
        /// <summary>At or below this dynamic pressure (kPa) there is no limit, and above it the limit widens as 10 ÷ q.</summary>
        public const double FreeQKpa = 1;

        public const double MinSpeed = 1;

        private const double Rad = Math.PI / 180;
        private const double Degenerate = 1e-9;

        /// <summary>The unit vector of a pitch (up +) and heading (clockwise from north).</summary>
        public static Vec Direction(double pitchDeg, double headingDeg, Vec up, Vec north, Vec east)
        {
            double cp = Math.Cos(pitchDeg * Rad);
            Vec ahead = north * Math.Cos(headingDeg * Rad) + east * Math.Sin(headingDeg * Rad);
            return (up * Math.Sin(pitchDeg * Rad) + ahead * cp).Normalized();
        }

        /// <summary>Pitch and heading of a direction; the heading is <paramref name="fallbackHeading"/> when it points straight up or down.</summary>
        public static void PitchHeading(Vec direction, Vec up, Vec north, Vec east, double fallbackHeading, out double pitchDeg, out double headingDeg)
        {
            Vec d = direction.Normalized();
            double vertical = Vec.Dot(d, up);
            pitchDeg = Math.Asin(Math.Max(-1, Math.Min(1, vertical))) / Rad;
            double n = Vec.Dot(d, north);
            double e = Vec.Dot(d, east);
            headingDeg = Math.Sqrt(n * n + e * e) < Degenerate ? fallbackHeading : LaunchAzimuth.Normalize(Math.Atan2(e, n) / Rad);
        }

        public static double AngleDeg(Vec a, Vec b)
        {
            double la = a.Length;
            double lb = b.Length;
            if (la < Degenerate || lb < Degenerate)
                return 0;
            return Math.Acos(Math.Max(-1, Math.Min(1, Vec.Dot(a, b) / (la * lb)))) / Rad;
        }

        /// <summary>The limit (°) at a dynamic pressure: max_aoa_deg × max(1, 10 ÷ q); null when there is none (q ≤ 1 kPa).</summary>
        public static double? LimitDeg(double maxAoaDeg, double qKpa)
            => qKpa > FreeQKpa ? maxAoaDeg * Math.Max(1, 10 / qKpa) : (double?)null;

        /// <summary>
        /// <paramref name="target"/> pulled toward the surface velocity until it is within the limit; unchanged when there is no limit
        /// or the surface speed is below 1 m/s.
        /// </summary>
        public static Vec Limit(Vec target, Vec surfaceVelocity, double qKpa, double maxAoaDeg)
        {
            double? limit = LimitDeg(maxAoaDeg, qKpa);
            if (!limit.HasValue || surfaceVelocity.Length < MinSpeed)
                return target.Normalized();
            Vec t = target.Normalized();
            Vec v = surfaceVelocity.Normalized();
            if (AngleDeg(t, v) <= limit.Value)
                return t;
            Vec side = t - v * Vec.Dot(t, v);
            if (side.Length < Degenerate)
                return v;   // straight against the velocity: no side to turn to
            side = side.Normalized();
            return (v * Math.Cos(limit.Value * Rad) + side * Math.Sin(limit.Value * Rad)).Normalized();
        }
    }
}
