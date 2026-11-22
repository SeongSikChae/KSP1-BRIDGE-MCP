using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition
{
    internal enum ConditionKind
    {
        /// <summary>A number; compared with &gt;= or &lt;=.</summary>
        Number,

        /// <summary>One of a set of names; compared with ==.</summary>
        Name,

        /// <summary>true or false; compared with ==.</summary>
        Flag,
    }

    /// <summary>A value a condition may compare: its name, kind, unit, and where the get_flight answer has it.</summary>
    internal sealed class ConditionValue
    {
        public ConditionValue(string name, ConditionKind kind, string unit, params string[] path)
        {
            Name = name;
            Kind = kind;
            Unit = unit;
            Path = path;
        }

        public string Name { get; }
        public ConditionKind Kind { get; }
        public string Unit { get; }

        /// <summary>Keys from the top of the get_flight answer; empty when the value does not come from it.</summary>
        public IReadOnlyList<string> Path { get; }
    }

    /// <summary>What a condition set compares on one physics frame. Only the parts the conditions use need to be filled.</summary>
    internal sealed class ConditionReading
    {
        /// <summary>FlightReport.Build of the vessel, so a value is exactly what get_flight answers.</summary>
        public Dictionary<string, object> Flight;

        public double ElapsedS;
        public bool NoThrust;

        /// <summary>The value now: double, string or bool; null when the vessel has none (apoapsis of an open orbit).</summary>
        public object Get(ConditionValue value)
        {
            if (value.Name == ConditionValues.Elapsed)
                return ElapsedS;
            if (value.Name == ConditionValues.NoThrust)
                return NoThrust;
            object found = Flight;
            foreach (string key in value.Path)
                found = ((Dictionary<string, object>)found)[key];
            return found;
        }
    }

    /// <summary>The table of condition values (spec C2).</summary>
    internal static class ConditionValues
    {
        public const string Elapsed = "elapsed_s";
        public const string NoThrust = "no_thrust";
        public const string Situation = "situation";

        public static readonly IReadOnlyList<ConditionValue> All = new[]
        {
            Number("altitude_m", "m above sea level", "position"),
            Number("terrain_altitude_m", "m above ground or water", "position"),
            Number("surface_mps", "m/s", "speed"),
            Number("orbital_mps", "m/s", "speed"),
            Number("vertical_mps", "m/s, up +", "speed"),
            Number("horizontal_surface_mps", "m/s", "speed"),
            Number("apoapsis_m", "m above sea level, no value on open orbits", "orbit"),
            Number("periapsis_m", "m above sea level", "orbit"),
            Number("time_to_apoapsis_s", "s, no value on open orbits", "orbit"),
            Number("time_to_periapsis_s", "s", "orbit"),
            Number("inclination_deg", "deg", "orbit"),
            Number("eccentricity", null, "orbit"),
            Number("pitch_deg", "deg", "attitude"),
            Number("heading_deg", "deg", "attitude"),
            Number("roll_deg", "deg", "attitude"),
            Number("mass_t", "t"),
            Number("thrust_kn", "current thrust, kN"),
            Number("twr", "current thrust / weight here"),
            new ConditionValue(Elapsed, ConditionKind.Number, "game seconds since this step began"),
            new ConditionValue(Situation, ConditionKind.Name, null, Situation),
            new ConditionValue(NoThrust, ConditionKind.Flag, null),
        };

        /// <summary>Situation names the game has.</summary>
        public static readonly IReadOnlyList<string> Situations = Enum.GetNames(typeof(Vessel.Situations));

        public static ConditionValue Find(string name) => All.FirstOrDefault(v => v.Name == name);

        private static ConditionValue Number(string name, string unit, string group = null)
            => group == null
                ? new ConditionValue(name, ConditionKind.Number, unit, name)
                : new ConditionValue(name, ConditionKind.Number, unit, group, name);
    }
}
