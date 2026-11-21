using System;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Surface-relative attitude in degrees, as the navball shows it.</summary>
    public readonly struct AttitudeAngles
    {
        public AttitudeAngles(double pitch, double heading, double roll)
        {
            Pitch = pitch;
            Heading = heading;
            Roll = roll;
        }

        /// <summary>Nose above the horizon, −90..90.</summary>
        public double Pitch { get; }

        /// <summary>Nose direction clockwise from north, 0..360.</summary>
        public double Heading { get; }

        /// <summary>Bank around the nose, −180..180; right wing down is positive.</summary>
        public double Roll { get; }
    }

    /// <summary>
    /// Pitch, heading and roll from the vessel's surface frame (the game's up, north, east) and the reference transform's nose
    /// (transform.up) and top (−transform.forward), the same reference the navball uses. Pure vectors, so it is unit-tested.
    /// When the nose points straight up or down, heading and roll are undefined and given as 0.
    /// </summary>
    public static class Attitude
    {
        private const double Degenerate = 1e-9;

        public static AttitudeAngles Compute(Vec up, Vec north, Vec east, Vec nose, Vec top)
        {
            up = up.Normalized();
            north = north.Normalized();
            east = east.Normalized();
            nose = nose.Normalized();
            top = top.Normalized();

            double sinPitch = Math.Max(-1, Math.Min(1, Vec.Dot(nose, up)));
            double pitch = Math.Asin(sinPitch) * 180 / Math.PI;

            double noseNorth = Vec.Dot(nose, north);
            double noseEast = Vec.Dot(nose, east);
            if (noseNorth * noseNorth + noseEast * noseEast < Degenerate)
                return new AttitudeAngles(pitch, 0, 0);

            double heading = Math.Atan2(noseEast, noseNorth) * 180 / Math.PI;
            if (heading < 0)
                heading += 360;

            // Right of the nose in the horizontal plane (facing north, right is east), and the zenith seen across the nose.
            Vec right = (north * -noseEast + east * noseNorth).Normalized();
            Vec level = (up - nose * Vec.Dot(up, nose)).Normalized();
            double roll = Math.Atan2(Vec.Dot(top, right), Vec.Dot(top, level)) * 180 / Math.PI;

            return new AttitudeAngles(pitch, heading, roll);
        }
    }
}
