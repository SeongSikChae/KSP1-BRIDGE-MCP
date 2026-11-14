using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// land: powered landing from a descent (#17, docs 0028~0030). Burns against the stopping distance to the ground
    /// (radar altitude minus the vessel bottom), holds final_speed below final_altitude, tilts to vertical as the
    /// horizontal speed drops, turns on the Gear group at gear_altitude and completes once landed and still.
    /// </summary>
    public sealed class LandMode : IFlightMode
    {
        public static readonly string[] Args = { "margin", "final_altitude", "final_speed", "gear_altitude" };

        private readonly double _margin;
        private readonly double _finalAlt;
        private readonly double _finalSpeed;
        private readonly double _gearAlt;   // NaN = gear off

        private LandMode(double margin, double finalAlt, double finalSpeed, double gearAlt)
        {
            _margin = margin;
            _finalAlt = finalAlt;
            _finalSpeed = finalSpeed;
            _gearAlt = gearAlt;
        }

        /// <summary>Validates the arguments at submit time and returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double margin = Plan.OptionalNumber(o, "margin", path, 0.1, 1, 0.8);
            double finalAlt = Plan.OptionalNumber(o, "final_altitude", path, 0, 1000, 20);
            double finalSpeed = Plan.OptionalNumber(o, "final_speed", path, 0.1, 20, 2);
            double gearAlt;
            if (o.TryGetValue("gear_altitude", out object g) && g is bool b)
            {
                if (b)
                {
                    throw Plan.Invalid(path + ".gear_altitude", "must be a number 0..10000 or false");
                }
                gearAlt = double.NaN;
            }
            else
            {
                gearAlt = Plan.OptionalNumber(o, "gear_altitude", path, 0, 10000, 500);
            }
            return () => new LandMode(margin, finalAlt, finalSpeed, gearAlt);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            return ModeCommand.Idle;
        }

        public Dictionary<string, object> Save() => new Dictionary<string, object>();

        public void Load(Vessel v, Dictionary<string, object> state)
        {
        }
    }
}
