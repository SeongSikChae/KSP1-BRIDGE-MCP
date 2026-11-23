using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    /// <summary>Kerbin values for the rule tests. Surface frame for readings: up +Z, north +Y, east +X.</summary>
    internal static class TestData
    {
        public const double Gm = 3.5316e12;
        public const double Radius = 600000;
        public const double Soi = 84159286;

        public static readonly Vec Up = new Vec(0, 0, 1);
        public static readonly Vec North = new Vec(0, 1, 0);
        public static readonly Vec East = new Vec(1, 0, 0);

        public static GoalBody Kerbin => new GoalBody { Gm = Gm, Radius = Radius, SoiRadius = Soi };

        public static double CircularSpeed(double altitude) => Math.Sqrt(Gm / (Radius + altitude));

        /// <summary>A reading in orbit with the nose east and one running engine (25 m/s² at full throttle).</summary>
        public static AscentReading Reading(double ut = 0) => new AscentReading
        {
            UT = ut,
            Altitude = 100000,
            BodyRadius = Radius,
            Gm = Gm,
            SoiRadius = Soi,
            Up = Up,
            North = North,
            East = East,
            OrbitVelocity = East * CircularSpeed(100000),
            Nose = East,
            Mass = 10,
            Thrust = 200,
            MaxThrustVac = 250,
            NextStage = 1,
            Engines = new List<EngineInfo>
            {
                new EngineInfo { Operational = true, Ignited = true, Stage = 2, Separation = -1, Propellant = true, StagingOn = true },
            },
        };

        public static Dictionary<string, object> Args(params (string Key, object Value)[] pairs)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach ((string key, object value) in pairs)
                d[key] = value;
            return d;
        }
    }
}
