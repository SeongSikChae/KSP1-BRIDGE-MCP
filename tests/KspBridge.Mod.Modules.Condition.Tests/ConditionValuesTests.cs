using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition.Tests
{
    /// <summary>Condition values are what get_flight answers (spec C2).</summary>
    [TestClass]
    public sealed class ConditionValuesTests
    {
        /// <summary>Every number is different, so a value taken from the wrong place shows.</summary>
        private static FlightData Data(double eccentricity = 0.25) => new FlightData
        {
            Vessel = "v",
            Situation = "FLYING",
            Body = "Kerbin",
            Altitude = 1001,
            TerrainHeight = 101,
            SurfaceSpeed = 12,
            OrbitalSpeed = 13,
            VerticalSpeed = 14,
            HorizontalSurfaceSpeed = 15,
            ApA = 16,
            PeA = 17,
            Inclination = 18,
            Eccentricity = eccentricity,
            Period = 19,
            TimeToAp = 20,
            TimeToPe = 21,
            Up = new Vec(0, 1, 0),
            North = new Vec(0, 0, 1),
            East = new Vec(1, 0, 0),
            Nose = new Vec(0.6, 0.8, 0),
            Top = new Vec(-0.8, 0.6, 0),
            Mass = 22,
            Thrust = 230,
            Gravity = 10,
            BodyInfo = new BodyData { Name = "Kerbin" },
        };

        private static ConditionReading Reading(FlightData data) => new ConditionReading
        {
            Flight = FlightReport.Build(data, FlightInclude.None),
            ElapsedS = 24,
            NoThrust = true,
        };

        private static object Get(FlightData data, string name) => Reading(data).Get(ConditionValues.Find(name));

        [TestMethod]
        [DataRow("altitude_m", 1001.0)]
        [DataRow("terrain_altitude_m", 900.0)]
        [DataRow("surface_mps", 12.0)]
        [DataRow("orbital_mps", 13.0)]
        [DataRow("vertical_mps", 14.0)]
        [DataRow("horizontal_surface_mps", 15.0)]
        [DataRow("apoapsis_m", 16.0)]
        [DataRow("periapsis_m", 17.0)]
        [DataRow("inclination_deg", 18.0)]
        [DataRow("eccentricity", 0.25)]
        [DataRow("time_to_apoapsis_s", 20.0)]
        [DataRow("time_to_periapsis_s", 21.0)]
        [DataRow("pitch_deg", 53.130102354155973)]
        [DataRow("heading_deg", 90.0)]
        [DataRow("roll_deg", 0.0)]
        [DataRow("mass_t", 22.0)]
        [DataRow("thrust_kn", 230.0)]
        [DataRow("twr", 230.0 / 220.0)]
        [DataRow("elapsed_s", 24.0)]
        public void Numbers(string name, double expected) => Assert.AreEqual(expected, (double)Get(Data(), name), 1e-9);

        [TestMethod]
        public void SituationAndNoThrust()
        {
            Assert.AreEqual("FLYING", Get(Data(), "situation"));
            Assert.AreEqual(true, Get(Data(), "no_thrust"));
        }

        [TestMethod]
        public void EveryTableValue_IsCoveredAbove_AndHasTheAnswersOwnName()
        {
            Assert.AreEqual(21, ConditionValues.All.Count);
            Assert.AreEqual(21, ConditionValues.All.Select(v => v.Name).Distinct().Count());
            foreach (ConditionValue value in ConditionValues.All.Where(v => v.Path.Count > 0))
                Assert.AreEqual(value.Name, value.Path.Last());
        }

        [TestMethod]
        public void OpenOrbit_HasNoApoapsis()
        {
            FlightData open = Data(eccentricity: 1.2);
            Assert.IsNull(Get(open, "apoapsis_m"));
            Assert.IsNull(Get(open, "time_to_apoapsis_s"));
            Assert.AreEqual(17.0, Get(open, "periapsis_m"));
        }

        [TestMethod]
        public void ValuesOutsideTheAnswer_DoNotNeedIt()
        {
            ConditionReading reading = new ConditionReading { ElapsedS = 3 };
            Assert.AreEqual(3.0, reading.Get(ConditionValues.Find("elapsed_s")));
            Assert.AreEqual(false, reading.Get(ConditionValues.Find("no_thrust")));
        }
    }
}
