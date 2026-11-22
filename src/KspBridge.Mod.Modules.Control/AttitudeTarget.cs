using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Reference directions an attitude can follow.</summary>
    internal enum AttitudeDirection
    {
        Prograde,
        Retrograde,
        Normal,
        Antinormal,
        RadialOut,
        RadialIn,
        SurfacePrograde,
        SurfaceRetrograde,
    }

    /// <summary>What the target directions are computed from; world vectors, any one consistent frame.</summary>
    internal struct FlightFrame
    {
        public Vec Up;
        public Vec North;
        public Vec East;
        public Vec OrbitVelocity;
        public Vec SurfaceVelocity;

        /// <summary>Orbit normal (the game's Orbit.h.xzy).</summary>
        public Vec OrbitNormal;
    }

    /// <summary>Where the nose should point and, when a roll is asked for, where the vessel's top should point.</summary>
    internal readonly struct TargetVectors
    {
        public TargetVectors(Vec nose, Vec? top)
        {
            Defined = true;
            Nose = nose;
            Top = top;
        }

        /// <summary>False when the direction cannot be determined now (D014); the rest is then unused.</summary>
        public bool Defined { get; }

        public Vec Nose { get; }
        public Vec? Top { get; }
    }

    /// <summary>An attitude to hold: pitch and heading with an optional roll (flight-observation's surface attitude), or a direction.</summary>
    internal sealed class AttitudeTarget
    {
        /// <summary>Below this speed (m/s) a velocity direction counts as undefined (D014).</summary>
        public const double MinSpeed = 1;

        private const double Degenerate = 1e-6;
        private const double DegToRad = Math.PI / 180;

        private AttitudeTarget()
        {
        }

        public double Pitch { get; private set; }
        public double Heading { get; private set; }
        public double? Roll { get; private set; }
        public AttitudeDirection? Direction { get; private set; }

        public static AttitudeTarget PitchHeading(double pitch, double heading, double? roll)
            => new AttitudeTarget { Pitch = pitch, Heading = heading, Roll = roll };

        public static AttitudeTarget Along(AttitudeDirection direction) => new AttitudeTarget { Direction = direction };

        public Dictionary<string, object> Describe()
        {
            if (Direction.HasValue)
                return new Dictionary<string, object> { ["direction"] = ControlArgs.DirectionName(Direction.Value) };
            return new Dictionary<string, object>
            {
                ["pitch_deg"] = Pitch,
                ["heading_deg"] = Heading,
                ["roll_deg"] = Roll,
            };
        }

        public TargetVectors Resolve(FlightFrame frame)
            => Direction.HasValue ? ResolveDirection(Direction.Value, frame) : ResolvePitchHeading(frame);

        /// <summary>Defined at pitch ±90° too: the level top turns with the pitch and the right side comes from the heading.</summary>
        private TargetVectors ResolvePitchHeading(FlightFrame frame)
        {
            double sinPitch = Math.Sin(Pitch * DegToRad);
            double cosPitch = Math.Cos(Pitch * DegToRad);
            double sinHeading = Math.Sin(Heading * DegToRad);
            double cosHeading = Math.Cos(Heading * DegToRad);

            Vec ahead = frame.North * cosHeading + frame.East * sinHeading;
            Vec nose = frame.Up * sinPitch + ahead * cosPitch;
            if (!Roll.HasValue)
                return new TargetVectors(nose.Normalized(), null);

            Vec level = frame.Up * cosPitch - ahead * sinPitch;
            Vec right = frame.East * cosHeading - frame.North * sinHeading;
            Vec top = level * Math.Cos(Roll.Value * DegToRad) + right * Math.Sin(Roll.Value * DegToRad);
            return new TargetVectors(nose.Normalized(), top.Normalized());
        }

        private static TargetVectors ResolveDirection(AttitudeDirection direction, FlightFrame frame)
        {
            switch (direction)
            {
                case AttitudeDirection.SurfacePrograde:
                    return Velocity(frame.SurfaceVelocity, 1);
                case AttitudeDirection.SurfaceRetrograde:
                    return Velocity(frame.SurfaceVelocity, -1);
                case AttitudeDirection.Prograde:
                    return Velocity(frame.OrbitVelocity, 1);
                case AttitudeDirection.Retrograde:
                    return Velocity(frame.OrbitVelocity, -1);
            }

            if (frame.OrbitVelocity.Length < MinSpeed)
                return default;
            switch (direction)
            {
                case AttitudeDirection.Normal:
                    return Unit(frame.OrbitNormal);
                case AttitudeDirection.Antinormal:
                    return Unit(-frame.OrbitNormal);
                case AttitudeDirection.RadialOut:
                    return Unit(RadialOut(frame));
                case AttitudeDirection.RadialIn:
                    return Unit(-RadialOut(frame));
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        private static TargetVectors Velocity(Vec velocity, double sign)
            => velocity.Length < MinSpeed ? default : Unit(velocity * sign);

        /// <summary>
        /// Up without its part along the orbital velocity, as the navball draws it (D017). The game's AutopilotMode.RadialOut is not
        /// used: that name is the opposite of the real direction.
        /// </summary>
        private static Vec RadialOut(FlightFrame frame)
        {
            Vec up = frame.Up.Normalized();
            Vec along = frame.OrbitVelocity.Normalized();
            return up - along * Vec.Dot(up, along);
        }

        private static TargetVectors Unit(Vec v) => v.Length < Degenerate ? default : new TargetVectors(v.Normalized(), null);
    }
}
