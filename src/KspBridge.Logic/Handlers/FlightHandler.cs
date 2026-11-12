using KspBridge.Flight;
using System;
using System.Collections.Generic;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the flight tools.</summary>
    public static class FlightHandler
    {
        private static readonly Telemetry _t = new Telemetry();

        public static IEnumerable<Route> Routes()
        {
            yield return new Route("GET", "/api/flight", (m, b, q) => Describe(Guard.ActiveVessel()));
        }

        /// <summary>get_flight: field names match the condition variables; NaN (not known) is null.</summary>
        private static object Describe(Vessel v)
        {
            _t.Capture(v, Planetarium.GetUniversalTime());
            return new Dictionary<string, object>
            {
                ["vessel"] = new Dictionary<string, object>
                {
                    ["id"] = v.persistentId,
                    ["name"] = v.vesselName,
                    ["situation"] = _t.Str["situation"],
                    ["met"] = N("met"),
                },
                ["flight"] = Group("altitude", "altitude_radar", "vertical_speed", "surface_speed", "orbital_speed",
                    "pitch", "heading", "roll", "dynamic_pressure"),
                ["orbit"] = With(Group("apoapsis", "periapsis", "time_to_apoapsis", "time_to_periapsis", "eccentricity", "inclination"),
                    "body", v.mainBody.bodyName),
                ["propulsion"] = With(Group("stage", "stage_dv", "total_dv", "twr", "throttle", "thrust", "ignitable_engines"),
                    "staging", _t.Str["staging"]),
                // unit 3: Coordinator.Describe (plan id, step, mode, step_time, attitude_error, warp, event_seq, epoch)
                ["plan"] = new Dictionary<string, object> { ["state"] = "idle" },
            };
        }

        private static Dictionary<string, object> Group(params string[] names)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (string n in names)
            {
                d[n] = N(n);
            }
            return d;
        }

        private static Dictionary<string, object> With(Dictionary<string, object> d, string key, object value)
        {
            d[key] = value;
            return d;
        }

        /// <summary>The variable rounded for the response; null when not known (NaN) or not finite.</summary>
        private static object N(string name)
        {
            double x = _t.Num[name];
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                return null;
            }
            return Math.Round(x, 3);
        }
    }
}
