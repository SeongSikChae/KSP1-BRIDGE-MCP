using System;
using UnityEngine;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// Attitude target of a mode: where the nose (ReferenceTransform up) points, optionally with a roll angle.
    /// A readonly struct: modes that compute a new target every tick (gravity_turn, circularize) allocate nothing.
    /// "No target" is a null AttitudeTarget? (ModeCommand.Attitude).
    /// </summary>
    public readonly struct AttitudeTarget
    {
        private enum Kind
        {
            PitchHeading,
            Reference,
            Vector,
        }

        private readonly Kind _kind;
        private readonly double _pitch;
        private readonly double _heading;
        private readonly double _roll;      // NaN = roll not held
        private readonly string _reference;
        private readonly Vector3d _vector;  // world direction

        private AttitudeTarget(Kind kind, double pitch, double heading, double roll, string reference, Vector3d vector)
        {
            _kind = kind;
            _pitch = pitch;
            _heading = heading;
            _roll = roll;
            _reference = reference;
            _vector = vector;
        }

        /// <summary>Pitch above the horizon and heading from north (deg), optional roll (deg, NaN = not held).</summary>
        public static AttitudeTarget PitchHeading(double pitch, double heading, double roll = double.NaN) =>
            new AttitudeTarget(Kind.PitchHeading, pitch, heading, roll, null, Vector3d.zero);

        /// <summary>prograde, retrograde, surface_prograde, normal, antinormal, radial_in, radial_out.</summary>
        public static AttitudeTarget Reference(string reference, double roll = double.NaN) =>
            new AttitudeTarget(Kind.Reference, double.NaN, double.NaN, roll, reference, Vector3d.zero);

        public static AttitudeTarget SurfacePrograde() => Reference("surface_prograde");

        /// <summary>A fixed world direction (no roll).</summary>
        public static AttitudeTarget Vector(Vector3d worldDirection) =>
            new AttitudeTarget(Kind.Vector, double.NaN, double.NaN, double.NaN, null, worldDirection.normalized);

        /// <summary>The direction `to`, but at most limitDeg away from `from` (rotated from `from` toward `to`). No roll.</summary>
        public static AttitudeTarget RotateTowards(Vector3d from, Vector3d to, double limitDeg) =>
            Vector(Vector3.RotateTowards(from.normalized, to.normalized, (float)(limitDeg * Math.PI / 180), 0f));

        public bool HasRoll => !double.IsNaN(_roll);

        /// <summary>Surface frame of the vessel: forward = north, up = away from the body, right = east.</summary>
        public static Quaternion SurfaceRotation(Vessel v)
        {
            Vector3d com = v.CoMD;
            Vector3d up = (com - v.mainBody.position).normalized;
            Vector3d north = Vector3d.Exclude(up, v.mainBody.position + (Vector3d)v.mainBody.transform.up * v.mainBody.Radius - com).normalized;
            return Quaternion.LookRotation(north, up);
        }

        /// <summary>World rotation whose up (nose) is at pitch/heading and whose roll is the surface roll (inverse of Telemetry's attitude).</summary>
        public static Quaternion PitchHeadingRotation(Quaternion surface, double pitch, double heading, double roll) =>
            surface * Quaternion.Euler(-(float)pitch, (float)heading, (float)roll) * Quaternion.Euler(90, 0, 0);

        /// <summary>World direction the nose should point at.</summary>
        public Vector3d Direction(Vessel v)
        {
            switch (_kind)
            {
                case Kind.PitchHeading:
                    return PitchHeadingRotation(SurfaceRotation(v), _pitch, _heading, 0) * Vector3.up;
                case Kind.Vector:
                    return _vector;
                default:
                    return ReferenceDirection(v, _reference);
            }
        }

        /// <summary>Full target rotation for a held roll (HasRoll only): the direction's pitch/heading with the roll angle.</summary>
        public Quaternion RollRotation(Vessel v)
        {
            Quaternion surface = SurfaceRotation(v);
            double pitch = _pitch, heading = _heading;
            if (_kind != Kind.PitchHeading)
            {
                Vector3 local = Quaternion.Inverse(surface) * (Vector3)Direction(v);   // x = east, y = up, z = north
                pitch = Math.Asin(Mathf.Clamp(local.y, -1, 1)) * 180 / Math.PI;
                heading = Math.Atan2(local.x, local.z) * 180 / Math.PI;
            }
            return PitchHeadingRotation(surface, pitch, heading, _roll);
        }

        private static Vector3d ReferenceDirection(Vessel v, string reference)
        {
            Vector3d prograde = v.obt_velocity.normalized;
            switch (reference)
            {
                case "prograde": return prograde;
                case "retrograde": return -prograde;
                case "surface_prograde": return v.srf_velocity.normalized;
                case "normal": return v.orbit.GetOrbitNormal().xzy.normalized;   // angular momentum, Orbit frame → world
                case "antinormal": return -v.orbit.GetOrbitNormal().xzy.normalized;
                default:
                    Vector3d up = (v.CoMD - v.mainBody.position).normalized;
                    Vector3d radialOut = Vector3d.Exclude(prograde, up).normalized;
                    return reference == "radial_in" ? -radialOut : radialOut;
            }
        }
    }
}
