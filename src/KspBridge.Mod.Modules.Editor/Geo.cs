using KspBridge.Mod.Core;
using System;
using System.Globalization;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>KSP-independent 3D vector (Unity left-handed axes: x right, y up, z forward).</summary>
    public readonly struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 Up = new Vec3(0, 1, 0);
        public static readonly Vec3 Right = new Vec3(1, 0, 0);
        public static readonly Vec3 Forward = new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Same formula as UnityEngine.Vector3.Cross.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b)
            => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public double Length => Math.Sqrt(Dot(this, this));

        /// <summary>Unit vector, or zero when the length is (near) zero.</summary>
        public Vec3 Normalized
        {
            get
            {
                double length = Length;
                return length < 1e-9 ? Zero : this * (1.0 / length);
            }
        }

        public bool ApproxEquals(Vec3 other, double tolerance) => (this - other).Length <= tolerance;

        public override string ToString() => Geo.Format(this);
    }

    /// <summary>KSP-independent unit quaternion with Unity's conventions (multiplication order, LookRotation, Euler).</summary>
    public readonly struct Quat
    {
        public Quat(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        /// <summary>a * b: rotate by b first, then by a (Unity order).</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotates a vector (q * v).</summary>
        public static Vec3 operator *(Quat q, Vec3 v)
        {
            Vec3 u = new Vec3(q.X, q.Y, q.Z);
            Vec3 t = 2.0 * Vec3.Cross(u, v);
            return v + q.W * t + Vec3.Cross(u, t);
        }

        public Quat Inverse => new Quat(-X, -Y, -Z, W);

        public Quat Normalized
        {
            get
            {
                double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
                return n < 1e-12 ? Identity : new Quat(X / n, Y / n, Z / n, W / n);
            }
        }

        /// <summary>Rotation of <paramref name="degrees"/> around <paramref name="axis"/> (Unity AngleAxis).</summary>
        public static Quat AngleAxis(double degrees, Vec3 axis)
        {
            Vec3 a = axis.Normalized;
            double half = degrees * Math.PI / 360.0;
            double s = Math.Sin(half);
            return new Quat(a.X * s, a.Y * s, a.Z * s, Math.Cos(half));
        }

        /// <summary>
        /// Shortest rotation taking direction <paramref name="from"/> to <paramref name="to"/>. When they point in opposite
        /// directions the axis is undefined (cross product zero); then the rotation is 180° around an axis perpendicular to
        /// <paramref name="from"/>: <paramref name="fallbackAxis"/> projected off <paramref name="from"/>, or local X, or Z.
        /// </summary>
        public static Quat FromTo(Vec3 from, Vec3 to, Vec3? fallbackAxis = null)
        {
            Vec3 f = from.Normalized;
            Vec3 t = to.Normalized;
            double dot = Vec3.Dot(f, t);
            if (dot > 1 - 1e-9)
                return Identity;
            if (dot < -1 + 1e-9)
            {
                Vec3 axis = Vec3.Zero;
                foreach (Vec3 candidate in new[] { fallbackAxis ?? Vec3.Zero, Vec3.Right, Vec3.Forward, Vec3.Up })
                {
                    axis = (candidate - f * Vec3.Dot(candidate, f)).Normalized;
                    if (axis.Length > 0.5)
                        break;
                }
                return AngleAxis(180, axis);
            }
            Vec3 c = Vec3.Cross(f, t);
            return new Quat(c.X, c.Y, c.Z, 1 + dot).Normalized;
        }

        /// <summary>
        /// Unity Quaternion.LookRotation: local +Z points along <paramref name="forward"/>, local +Y as close to
        /// <paramref name="up"/> as possible. When the two are parallel Unity falls back to FromTo(Z, forward).
        /// </summary>
        public static Quat LookRotation(Vec3 forward, Vec3 up)
        {
            Vec3 z = forward.Normalized;
            if (z.Length < 0.5)
                return Identity;
            Vec3 x = Vec3.Cross(up, z);
            if (x.Length < 1e-9)
                return FromTo(Vec3.Forward, z);
            x = x.Normalized;
            Vec3 y = Vec3.Cross(z, x);
            return FromBasis(x, y, z);
        }

        /// <summary>Rotation whose local axes map to the given orthonormal right/up/forward vectors.</summary>
        private static Quat FromBasis(Vec3 x, Vec3 y, Vec3 z)
        {
            double m00 = x.X, m01 = y.X, m02 = z.X;
            double m10 = x.Y, m11 = y.Y, m12 = z.Y;
            double m20 = x.Z, m21 = y.Z, m22 = z.Z;
            double trace = m00 + m11 + m22;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2;
                return new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s).Normalized;
            }
            if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
                return new Quat(0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s).Normalized;
            }
            if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
                return new Quat((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s).Normalized;
            }
            double s2 = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
            return new Quat((m02 + m20) / s2, (m12 + m21) / s2, 0.25 * s2, (m10 - m01) / s2).Normalized;
        }

        /// <summary>Unity Quaternion.Euler: rotate around Z, then X, then Y (degrees).</summary>
        public static Quat Euler(double x, double y, double z)
            => AngleAxis(y, Vec3.Up) * AngleAxis(x, Vec3.Right) * AngleAxis(z, Vec3.Forward);

        /// <summary>Angle in degrees between two rotations (0..180).</summary>
        public static double Angle(Quat a, Quat b)
        {
            double dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
            return Math.Acos(Math.Min(1.0, dot)) * 360.0 / Math.PI;
        }

        public override string ToString() => Geo.Format(this);
    }

    /// <summary>Reading and writing the vector and quaternion text forms used in craft files.</summary>
    public static class Geo
    {
        private static readonly char[] Separators = { ',', '|' };

        /// <summary>"x,y,z" (KSP also writes "x|y|z" in some places).</summary>
        public static Vec3 ParseVec(string text, string what)
        {
            double[] parts = ParseNumbers(text, 3, what);
            return new Vec3(parts[0], parts[1], parts[2]);
        }

        /// <summary>"x,y,z,w".</summary>
        public static Quat ParseQuat(string text, string what)
        {
            double[] parts = ParseNumbers(text, 4, what);
            return new Quat(parts[0], parts[1], parts[2], parts[3]);
        }

        public static string Format(Vec3 v) => Format(v, ",");

        /// <summary>Vector with another separator (attach node values use "|").</summary>
        public static string Format(Vec3 v, string separator) => Num(v.X) + separator + Num(v.Y) + separator + Num(v.Z);

        public static string Format(Quat q) => Num(q.X) + "," + Num(q.Y) + "," + Num(q.Z) + "," + Num(q.W);

        /// <summary>Number as the game writes floats: invariant culture, single precision.</summary>
        public static string Num(double value)
        {
            float f = (float)value;
            if (Math.Abs(f) < 1e-7f)
                f = 0f;
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        private static double[] ParseNumbers(string text, int count, string what)
        {
            if (text == null)
                throw BridgeException.BadRequest(what + " is missing");
            string[] tokens = text.Split(Separators);
            if (tokens.Length != count)
                throw BridgeException.BadRequest(what + " must have " + count + " numbers: '" + text + "'");
            double[] result = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (!double.TryParse(tokens[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    throw BridgeException.BadRequest(what + " has a bad number: '" + text + "'");
            }
            return result;
        }
    }
}
