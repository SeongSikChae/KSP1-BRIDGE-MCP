using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    /// <summary>Readings around Kerbin for the rule tests. Frame: up +Z, north +Y, east +X.</summary>
    internal static class Readings
    {
        public const double KerbinRadius = 600000;
        public const double KerbinGm = 3.5316e12;
        public const double KerbinAtmosphere = 70000;

        public static readonly Vec Up = new Vec(0, 0, 1);
        public static readonly Vec North = new Vec(0, 1, 0);
        public static readonly Vec East = new Vec(1, 0, 0);

        public static AscentReading Kerbin(double altitude = 100, double apoapsis = 100, double ut = 0)
            => new AscentReading
            {
                UT = ut,
                Altitude = altitude,
                AtmosphereTop = KerbinAtmosphere,
                BodyRadius = KerbinRadius,
                Gm = KerbinGm,
                SoiRadius = 84159286,
                ApA = apoapsis,
                PeA = -500000,
                Eccentricity = 0.9,
                SemiMajorAxis = 400000,
                TimeToAp = 60,
                VerticalSpeed = 100,
                Latitude = 0,
                Up = Up,
                North = North,
                East = East,
                OrbitVelocity = East * 175,
                SurfaceVelocity = Up * 100,
                Nose = Up,
                Mass = 10,
                Thrust = 200,
                MaxThrustVac = 250,
                NextStage = 1,
                Engines = new List<EngineInfo> { Running(2, -1) },
            };

        /// <summary>A circular orbit state at <paramref name="altitude"/> moving east with <paramref name="short"/> m/s still missing.</summary>
        public static AscentReading Orbit(double altitude, double @short, double ut = 0)
        {
            double vc = Math.Sqrt(KerbinGm / (KerbinRadius + altitude));
            AscentReading r = Kerbin(altitude, altitude, ut);
            r.Eccentricity = 0.01;
            r.SemiMajorAxis = KerbinRadius + altitude - 5000;
            r.PeA = altitude - 10000;
            r.OrbitVelocity = East * (vc - @short);
            r.SurfaceVelocity = r.OrbitVelocity;
            r.VerticalSpeed = 0;
            r.Nose = East;
            r.DynamicPressureKpa = 0;
            return r;
        }

        public static EngineInfo Running(int stage, int separation)
            => new EngineInfo { Operational = true, Ignited = true, Stage = stage, Separation = separation, Propellant = true, StagingOn = true };

        public static EngineInfo Spent(int stage, int separation)
            => new EngineInfo { FlameOut = true, Ignited = true, Stage = stage, Separation = separation, Propellant = false, StagingOn = true };

        public static EngineInfo Idle(int stage, int separation)
            => new EngineInfo { Stage = stage, Separation = separation, Propellant = true, StagingOn = true };
    }
}
