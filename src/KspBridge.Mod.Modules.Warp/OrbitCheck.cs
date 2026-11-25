using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>The active vessel's orbit at one physics frame (R01 C01-E1). Read the way get_flight reads it.</summary>
    internal sealed class OrbitRecord
    {
        public double Ut;

        /// <summary>Null on an open orbit.</summary>
        public double? ApoapsisM;

        public double PeriapsisM;
        public double Eccentricity;
        public double SemiMajorAxisM;

        /// <summary>Same rule as get_flight: an eccentricity of 1 or more has no apoapsis.</summary>
        public static OrbitRecord From(double ut, double apoapsis, double periapsis, double eccentricity, double semiMajorAxis)
            => new OrbitRecord
            {
                Ut = ut,
                ApoapsisM = eccentricity < 1 ? apoapsis : (double?)null,
                PeriapsisM = periapsis,
                Eccentricity = eccentricity,
                SemiMajorAxisM = semiMajorAxis,
            };

        public Dictionary<string, object> ToEvent() => new Dictionary<string, object>
        {
            ["ut"] = Ut,
            ["apoapsis_m"] = ApoapsisM,
            ["periapsis_m"] = PeriapsisM,
            ["eccentricity"] = Eccentricity,
            ["semi_major_axis_m"] = SemiMajorAxisM,
        };
    }

    /// <summary>What the orbit check reads from the game in one physics frame.</summary>
    internal sealed class CheckReading
    {
        public OrbitRecord Orbit;

        /// <summary>Engine thrust, as get_flight's thrust_kn.</summary>
        public double Thrust;

        /// <summary>The RCS action group is on, as get_flight's control.rcs.</summary>
        public bool Rcs;

        /// <summary>The vessel is above the top of the atmosphere (always when the body has none).</summary>
        public bool AboveAtmosphere = true;

        public int PartCount;

        /// <summary>The body the orbit is around.</summary>
        public string Body;

        /// <summary>Landed, splashed or on the pad.</summary>
        public bool Landed;
    }

    /// <summary>
    /// Whether a warp step kept the orbit (D01_01, D01_02): the start orbit, the reasons the check could not be made, and the
    /// comparison at the end. Free of the game.
    /// </summary>
    internal sealed class OrbitCheck
    {
        public const double LimitM = 10;

        public const string Thrust = "thrust";
        public const string Rcs = "rcs";
        public const string Atmosphere = "atmosphere";
        public const string PartsLost = "parts_lost";
        public const string SoiChange = "soi_change";
        public const string Landed = "landed";

        private readonly List<string> reasons = new List<string>();
        private CheckReading start;

        public OrbitRecord StartOrbit => start?.Orbit;

        public OrbitRecord EndOrbit { get; private set; }

        /// <summary>The reasons the orbit is not judged, in the order they broke, each once.</summary>
        public IReadOnlyList<string> Reasons => reasons;

        public bool Judged => reasons.Count == 0;

        /// <summary>The first physics frame of the step.</summary>
        public void Begin(CheckReading reading)
        {
            start = reading;
            Observe(reading);
        }

        /// <summary>One physics frame, the first and last included.</summary>
        public void Observe(CheckReading now)
        {
            if (start == null)
                return;
            if (now.Thrust != 0)
                Add(Thrust);
            if (now.Rcs)
                Add(Rcs);
            if (!now.AboveAtmosphere)
                Add(Atmosphere);
            if (now.PartCount < start.PartCount)
                Add(PartsLost);
            if (now.Body != start.Body)
                Add(SoiChange);
            if (now.Landed)
                Add(Landed);
        }

        /// <summary>The last frame; returns the failure message when the check was made and the orbit changed, otherwise null.</summary>
        public string Complete(CheckReading now)
        {
            Observe(now);
            EndOrbit = now.Orbit;
            if (start == null || !Judged)
                return null;
            OrbitRecord a = start.Orbit;
            OrbitRecord b = now.Orbit;
            bool changed = Math.Abs(a.PeriapsisM - b.PeriapsisM) > LimitM;
            if (a.ApoapsisM.HasValue != b.ApoapsisM.HasValue)
                changed = true;
            else if (a.ApoapsisM.HasValue && Math.Abs(a.ApoapsisM.Value - b.ApoapsisM.Value) > LimitM)
                changed = true;
            if (!changed)
                return null;
            return "the orbit changed during warp without thrust: apoapsis " + Format(a.ApoapsisM) + " → " + Format(b.ApoapsisM)
                   + " m, periapsis " + Format(a.PeriapsisM) + " → " + Format(b.PeriapsisM) + " m (limit 10 m)";
        }

        private void Add(string reason)
        {
            if (!reasons.Contains(reason))
                reasons.Add(reason);
        }

        private static string Format(double? value)
            => value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "none";
    }
}
