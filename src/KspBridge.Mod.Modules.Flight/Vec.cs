using System;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Pure 3D vector for the flight calculations (no Unity types, so they run in unit tests).</summary>
    public readonly struct Vec
    {
        public Vec(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static readonly Vec Zero = new Vec(0, 0, 0);

        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator -(Vec a) => new Vec(-a.X, -a.Y, -a.Z);
        public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k, a.Z * k);
        public static Vec operator *(double k, Vec a) => a * k;

        public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public double Length => Math.Sqrt(Dot(this, this));

        /// <summary>Unit vector, or zero when the length is zero.</summary>
        public Vec Normalized()
        {
            double length = Length;
            return length == 0 ? Zero : this * (1.0 / length);
        }

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }
}
