using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>
    /// The active vessel's values on one physics frame (spec C6). A value type so 180,000 samples carry no per-object header
    /// and are not garbage-collected one by one.
    /// </summary>
    internal struct Sample
    {
        /// <summary>Game time (UT, s).</summary>
        public double Ut;

        public double Throttle;
        public bool Sas;
        public bool Rcs;

        public double PitchInput;
        public double YawInput;
        public double RollInput;

        public double MassT;
        public double ThrustKn;

        public double PitchDeg;
        public double HeadingDeg;
        public double RollDeg;

        public double PitchRateDps;
        public double YawRateDps;
        public double RollRateDps;

        /// <summary>Last activated stage; null before staging (StageRef rule).</summary>
        public int? Stage;

        public double AltitudeM;
        public double VerticalSpeedMps;
        public double SurfaceSpeedMps;
        public double OrbitalSpeedMps;
    }

    /// <summary>One answer item of a sample: its name, whether it is summarised as numbers or as value changes, and how to read it.</summary>
    internal sealed class SampleItem
    {
        private SampleItem(string name, Func<Sample, double> number, Func<Sample, object> discrete)
        {
            Name = name;
            Number = number;
            Discrete = discrete;
        }

        public string Name { get; }

        /// <summary>Reader for a numeric item; null for a discrete one.</summary>
        public Func<Sample, double> Number { get; }

        /// <summary>Reader for a discrete item (bool, int or null); null for a numeric one.</summary>
        public Func<Sample, object> Discrete { get; }

        public bool IsDiscrete => Discrete != null;

        public object Value(Sample s) => IsDiscrete ? Discrete(s) : Number(s);

        public static SampleItem Num(string name, Func<Sample, double> read) => new SampleItem(name, read, null);

        public static SampleItem Disc(string name, Func<Sample, object> read) => new SampleItem(name, null, read);
    }

    /// <summary>The item table (plan.md "표본 항목"); ut and t_s are fixed columns of the sample list, not items.</summary>
    internal static class SampleItems
    {
        public static readonly IReadOnlyList<SampleItem> All = new List<SampleItem>
        {
            SampleItem.Num("throttle", s => s.Throttle),
            SampleItem.Disc("sas", s => s.Sas),
            SampleItem.Disc("rcs", s => s.Rcs),
            SampleItem.Num("pitch_input", s => s.PitchInput),
            SampleItem.Num("yaw_input", s => s.YawInput),
            SampleItem.Num("roll_input", s => s.RollInput),
            SampleItem.Num("mass_t", s => s.MassT),
            SampleItem.Num("thrust_kn", s => s.ThrustKn),
            SampleItem.Num("pitch_deg", s => s.PitchDeg),
            SampleItem.Num("heading_deg", s => s.HeadingDeg),
            SampleItem.Num("roll_deg", s => s.RollDeg),
            SampleItem.Num("pitch_rate_dps", s => s.PitchRateDps),
            SampleItem.Num("yaw_rate_dps", s => s.YawRateDps),
            SampleItem.Num("roll_rate_dps", s => s.RollRateDps),
            SampleItem.Disc("stage", s => s.Stage),
            SampleItem.Num("altitude_m", s => s.AltitudeM),
            SampleItem.Num("vertical_speed_mps", s => s.VerticalSpeedMps),
            SampleItem.Num("surface_speed_mps", s => s.SurfaceSpeedMps),
            SampleItem.Num("orbital_speed_mps", s => s.OrbitalSpeedMps),
        };

        public static SampleItem Find(string name)
        {
            foreach (SampleItem item in All)
            {
                if (item.Name == name)
                    return item;
            }
            return null;
        }
    }
}
