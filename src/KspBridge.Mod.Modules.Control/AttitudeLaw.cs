using KspBridge.Mod.Modules.Flight;
using System;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>How far the vessel is from its target attitude. Vectors are in the reference transform's local axes.</summary>
    internal readonly struct AttitudeError
    {
        public AttitudeError(Vec rotation, double angleDeg, double? rollDeg)
        {
            Defined = true;
            Rotation = rotation;
            AngleDeg = angleDeg;
            RollDeg = rollDeg;
        }

        /// <summary>False when the target has no direction now; Rotation is then zero and only stops the turning.</summary>
        public bool Defined { get; }

        /// <summary>Rotation to make (rad): x and z turn the nose onto the target, y is the roll left after that.</summary>
        public Vec Rotation { get; }

        public double AngleDeg { get; }

        /// <summary>Null when the target has no roll.</summary>
        public double? RollDeg { get; }
    }

    /// <summary>
    /// Attitude control law, free of the game. Local axes: right +X, nose +Y, top −Z (the reference transform's). Rotations and
    /// rates follow v = ω × r. A control command c gives the torque −c × available torque on its axis (reaction wheels apply
    /// ReferenceTransform.rotation × −input × torque), with x = pitch, y = roll, z = yaw.
    /// </summary>
    internal static class AttitudeLaw
    {
        /// <summary>Time (s) in which the remaining angle would be closed at the wanted rate.</summary>
        public const double AngleTime = 2;

        /// <summary>Time (s) in which the rate error is corrected.</summary>
        public const double RateTime = 0.5;

        /// <summary>Fastest wanted turn rate (rad/s).</summary>
        public const double MaxRate = 20 * Math.PI / 180;

        private const double RadToDeg = 180 / Math.PI;
        private const double Degenerate = 1e-9;

        /// <summary>The vector's components on the vessel's local axes.</summary>
        public static Vec ToLocal(Vec v, Vec right, Vec nose, Vec top)
            => new Vec(Vec.Dot(v, right), Vec.Dot(v, nose), -Vec.Dot(v, top));

        /// <summary>Error from the local target vectors (nose and, when a roll is asked for, top).</summary>
        public static AttitudeError Error(Vec targetNose, Vec? targetTop)
        {
            Vec t = targetNose.Normalized();
            double s = Math.Sqrt(t.X * t.X + t.Z * t.Z);
            double angle = Math.Atan2(s, t.Y);
            Vec axis = s > Degenerate ? new Vec(t.Z / s, 0, -t.X / s) : new Vec(1, 0, 0);
            Vec rotation = axis * angle;

            if (!targetTop.HasValue)
                return new AttitudeError(rotation, angle * RadToDeg, null);

            Vec top = Rotate(new Vec(0, 0, -1), axis, angle);
            Vec wanted = targetTop.Value.Normalized();
            double roll = Math.Atan2(Vec.Dot(t, Cross(top, wanted)), Vec.Dot(top, wanted));
            return new AttitudeError(new Vec(rotation.X, roll, rotation.Z), angle * RadToDeg, roll * RadToDeg);
        }

        /// <summary>Torque the vessel can give on each axis, as the game's SAS takes it: the larger of the two sums, never negative (D018).</summary>
        public static Vec AvailableTorque(Vec positiveSum, Vec negativeSum)
            => new Vec(
                Math.Max(0, Math.Max(positiveSum.X, negativeSum.X)),
                Math.Max(0, Math.Max(positiveSum.Y, negativeSum.Y)),
                Math.Max(0, Math.Max(positiveSum.Z, negativeSum.Z)));

        /// <summary>Control command per axis (x pitch, y roll, z yaw), each in −1..1.</summary>
        public static Vec Command(Vec error, Vec rate, Vec torque, Vec inertia)
            => new Vec(
                Axis(error.X, rate.X, torque.X, inertia.X),
                Axis(error.Y, rate.Y, torque.Y, inertia.Y),
                Axis(error.Z, rate.Z, torque.Z, inertia.Z));

        public static bool Aligned(AttitudeError error, double rateDps, double toleranceDeg, double settleRateDps)
            => error.Defined
               && error.AngleDeg <= toleranceDeg
               && (!error.RollDeg.HasValue || Math.Abs(error.RollDeg.Value) <= toleranceDeg)
               && rateDps <= settleRateDps;

        public static double RateDps(Vec rate) => rate.Length * RadToDeg;

        public static Vec Cross(Vec a, Vec b)
            => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static double Axis(double error, double rate, double torque, double inertia)
        {
            if (!(torque > 0) || !(inertia > 0))
                return 0;   // nothing can turn this axis (D019)
            double size = Math.Abs(error);
            double acceleration = torque / inertia;
            double wanted = Math.Sign(error) * Math.Min(Math.Min(size / AngleTime, Math.Sqrt(acceleration * size)), MaxRate);
            double needed = inertia * (wanted - rate) / RateTime;
            return Math.Max(-1, Math.Min(1, -needed / torque));
        }

        private static Vec Rotate(Vec v, Vec axis, double angle)
        {
            double cos = Math.Cos(angle);
            return v * cos + Cross(axis, v) * Math.Sin(angle) + axis * (Vec.Dot(axis, v) * (1 - cos));
        }
    }
}
