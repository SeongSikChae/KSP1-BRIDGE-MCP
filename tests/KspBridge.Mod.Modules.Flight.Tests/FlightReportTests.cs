using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Flight.Tests
{
    [TestClass]
    public sealed class FlightReportTests
    {
        private static FlightData Pad() => new FlightData
        {
            Vessel = "Kerbal X",
            Situation = "PRELAUNCH",
            Body = "Kerbin",
            UT = 1000,
            Latitude = -0.0972,
            Longitude = -74.5577 - 360,
            Altitude = 70,
            TerrainHeight = 65,
            Ocean = true,
            OrbitalSpeed = 174,
            ApA = -500000,
            PeA = -600000,
            Eccentricity = 0.99,
            Period = 1000,
            TimeToAp = 10,
            TimeToPe = 20,
            Up = new Vec(0, 1, 0),
            North = new Vec(0, 0, 1),
            East = new Vec(1, 0, 0),
            Nose = new Vec(0, 1, 0),
            Top = new Vec(0, 0, -1),
            Throttle = 0.5,
            Mass = 100,
            Thrust = 0,
            Gravity = 9.81,
            CurrentStage = 3,
            StagedParts = new List<StagedPart> { new StagedPart(2, false), new StagedPart(1, false), new StagedPart(0, false) },
            DeltaVReady = true,
            Stages = new List<StageCalc>
            {
                new StageCalc { Stage = 0, DvActual = 900, DvAsl = 800, DvVac = 1000, TwrActual = 0.9, BurnTime = 60 },
                new StageCalc { Stage = 2, DvActual = 2000, DvAsl = 2000, DvVac = 2300, TwrActual = 1.5, BurnTime = 90 },
                new StageCalc { Stage = 1, DvActual = 1200, DvAsl = 1100, DvVac = 1400, TwrActual = 1.1, BurnTime = 70 },
            },
            TotalDvActual = 4100,
            TotalDvVac = 4700,
            Resources = new List<ResourceTotal>
            {
                new ResourceTotal { Name = "Oxidizer", Amount = 1100, Max = 1100 },
                new ResourceTotal { Name = "LiquidFuel", Amount = 900, Max = 900 },
            },
            BodyInfo = new BodyData
            {
                Name = "Kerbin", Radius = 600000, Gm = 3.5316e12, RotationPeriod = 21549.425, AtmosphereTop = 70000,
                SphereOfInfluence = 84159286,
            },
        };

        private static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) => (Dictionary<string, object>)d[key];

        private static readonly string[] DefaultKeys =
        {
            "vessel", "situation", "body", "ut", "met_s", "position", "speed", "orbit", "attitude", "control", "mass_t", "thrust_kn",
            "twr", "stage", "delta_v_ready", "delta_v_not_ready_reason",
        };

        [TestMethod]
        public void DefaultAnswer_HasTheSummaryKeysOnly()
        {
            Dictionary<string, object> a = FlightReport.Build(Pad(), FlightInclude.None);
            CollectionAssert.AreEquivalent(DefaultKeys, a.Keys.ToArray());
        }

        [TestMethod]
        public void Include_AddsOnlyTheAskedParts_AndKeepsTheSummary()
        {
            Dictionary<string, object> plain = FlightReport.Build(Pad(), FlightInclude.None);
            Dictionary<string, object> all = FlightReport.Build(Pad(), FlightInclude.Stages | FlightInclude.Resources | FlightInclude.Body);

            CollectionAssert.AreEquivalent(DefaultKeys.Concat(new[] { "stages", "resources", "body_info" }).ToArray(), all.Keys.ToArray());
            foreach (string key in DefaultKeys)
                Assert.AreEqual(Json.Write(plain[key]), Json.Write(all[key]), key);
        }

        [TestMethod]
        public void Position_TerrainHeightAndLongitude()
        {
            Dictionary<string, object> p = Obj(FlightReport.Build(Pad(), FlightInclude.None), "position");
            Assert.AreEqual(5.0, (double)p["terrain_altitude_m"], 1e-9);
            Assert.AreEqual(-74.5577, (double)p["longitude_deg"], 1e-9);
        }

        [TestMethod]
        public void TerrainBelowSeaLevel_IsWaterOnOceanBodies_AndGroundElsewhere()
        {
            FlightData sea = Pad();
            sea.TerrainHeight = -300;
            Assert.AreEqual(70.0, (double)Obj(FlightReport.Build(sea, FlightInclude.None), "position")["terrain_altitude_m"], 1e-9);

            FlightData dry = Pad();
            dry.Ocean = false;
            dry.TerrainHeight = -300;
            Assert.AreEqual(370.0, (double)Obj(FlightReport.Build(dry, FlightInclude.None), "position")["terrain_altitude_m"], 1e-9);
        }

        [TestMethod]
        public void ClosedOrbit_HasApoapsisAndPeriod_OpenOrbit_HasNulls()
        {
            Dictionary<string, object> closed = Obj(FlightReport.Build(Pad(), FlightInclude.None), "orbit");
            Assert.AreEqual(1000.0, (double)closed["period_s"], 1e-9);
            Assert.IsNotNull(closed["apoapsis_m"]);

            FlightData escape = Pad();
            escape.Eccentricity = 1.2;
            Dictionary<string, object> open = Obj(FlightReport.Build(escape, FlightInclude.None), "orbit");
            Assert.IsNull(open["period_s"]);
            Assert.IsNull(open["apoapsis_m"]);
            Assert.IsNull(open["time_to_apoapsis_s"]);
            Assert.IsNotNull(open["periapsis_m"]);
            Assert.IsNotNull(open["time_to_periapsis_s"]);
        }

        [TestMethod]
        public void Twr_IsThrustOverWeight_AndZeroWithoutMassOrGravity()
        {
            Assert.AreEqual(2.0, FlightReport.Twr(1962, 100, 9.81), 1e-9);
            Assert.AreEqual(0.0, FlightReport.Twr(100, 0, 9.81));
            Assert.AreEqual(0.0, FlightReport.Twr(100, 1, 0));

            FlightData burning = Pad();
            burning.Thrust = 981;
            Assert.AreEqual(1.0, (double)FlightReport.Build(burning, FlightInclude.None)["twr"], 1e-9);
        }

        [TestMethod]
        public void Stage_BeforeLaunch_ShowsTheNextStageValues()
        {
            Dictionary<string, object> s = Obj(FlightReport.Build(Pad(), FlightInclude.None), "stage");
            Assert.IsNull(s["last_activated"]);
            Assert.AreEqual(2, s["next"]);
            Assert.AreEqual(2, s["reference"]);
            Assert.AreEqual(2000.0, (double)s["delta_v_mps"], 1e-9);
            Assert.AreEqual(2300.0, (double)s["delta_v_vac_mps"], 1e-9);
            Assert.AreEqual(90.0, (double)s["burn_time_s"], 1e-9);
            Assert.AreEqual(1.5, (double)s["twr"], 1e-9);
            Assert.AreEqual(4100.0, (double)s["total_delta_v_mps"], 1e-9);
            Assert.AreEqual(4700.0, (double)s["total_delta_v_vac_mps"], 1e-9);
        }

        [TestMethod]
        public void Stage_WithoutCalculatorValues_IsZero()
        {
            FlightData d = Pad();
            d.StagedParts.Add(new StagedPart(3, false));   // a stage with no engine: the calculator has nothing for 3
            Dictionary<string, object> s = Obj(FlightReport.Build(d, FlightInclude.None), "stage");
            Assert.AreEqual(3, s["reference"]);
            Assert.AreEqual(0.0, (double)s["delta_v_mps"]);
            Assert.AreEqual(0.0, (double)s["burn_time_s"]);
            Assert.AreEqual(0.0, (double)s["twr"]);
        }

        [TestMethod]
        public void CalculatorNotReady_DeltaVIsNullWithReason_RestIsNormal()
        {
            FlightData d = Pad();
            d.DeltaVReady = false;
            d.DeltaVNotReadyReason = "the game's delta-v calculator has not finished its first calculation yet";

            Dictionary<string, object> a = FlightReport.Build(d, FlightInclude.Stages);
            Dictionary<string, object> s = Obj(a, "stage");

            Assert.IsFalse((bool)a["delta_v_ready"]);
            Assert.AreEqual(d.DeltaVNotReadyReason, a["delta_v_not_ready_reason"]);
            foreach (string key in new[] { "delta_v_mps", "delta_v_vac_mps", "burn_time_s", "twr", "total_delta_v_mps", "total_delta_v_vac_mps" })
                Assert.IsNull(s[key], key);
            Assert.IsNull(a["stages"]);
            Assert.AreEqual(2, s["reference"], "stage numbers do not need the calculator");
            Assert.AreEqual(100.0, (double)a["mass_t"]);
            Assert.AreEqual("PRELAUNCH", a["situation"]);
        }

        [TestMethod]
        public void StageTable_IsCalculatorStagesAtOrBelowTheReference_FirstToFireFirst()
        {
            FlightData d = Pad();
            d.Stages.Add(new StageCalc { Stage = 5, DvActual = 1 });   // above the reference: already gone
            List<object> table = (List<object>)FlightReport.Build(d, FlightInclude.Stages)["stages"];

            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, table.Select(t => (int)((Dictionary<string, object>)t)["stage"]).ToArray());
            Dictionary<string, object> first = (Dictionary<string, object>)table[0];
            Assert.AreEqual(2000.0, (double)first["delta_v_asl_mps"], 1e-9);
            Assert.AreEqual(2300.0, (double)first["delta_v_vac_mps"], 1e-9);
        }

        [TestMethod]
        public void Resources_AreSortedByName()
        {
            List<object> list = (List<object>)FlightReport.Build(Pad(), FlightInclude.Resources)["resources"];
            CollectionAssert.AreEqual(new[] { "LiquidFuel", "Oxidizer" },
                list.Select(r => (string)((Dictionary<string, object>)r)["name"]).ToArray());
            Dictionary<string, object> fuel = (Dictionary<string, object>)list[0];
            Assert.AreEqual(900.0, (double)fuel["amount"]);
            Assert.AreEqual(900.0, (double)fuel["max"]);
        }

        [TestMethod]
        public void BodyInfo_HasTheConstants_AndSunSoiIsNull()
        {
            Dictionary<string, object> b = Obj(FlightReport.Build(Pad(), FlightInclude.Body), "body_info");
            Assert.AreEqual(600000.0, (double)b["radius_m"]);
            Assert.AreEqual(3.5316e12, (double)b["gm_m3ps2"]);
            Assert.AreEqual(70000.0, (double)b["atmosphere_top_m"]);
            Assert.IsNotNull(b["soi_m"]);

            FlightData sun = Pad();
            sun.BodyInfo.AtmosphereTop = null;
            sun.BodyInfo.SphereOfInfluence = double.PositiveInfinity;
            Dictionary<string, object> s = Obj(FlightReport.Build(sun, FlightInclude.Body), "body_info");
            Assert.IsNull(s["soi_m"]);
            Assert.IsNull(s["atmosphere_top_m"]);
        }

        [TestMethod]
        public void ParseInclude_KnownNames_AndBadInput()
        {
            Assert.AreEqual(FlightInclude.None, FlightReport.ParseInclude(null));
            Assert.AreEqual(FlightInclude.Stages | FlightInclude.Body,
                FlightReport.ParseInclude(new List<object> { "stages", "body" }));

            Assert.AreEqual("bad_request", Assert.ThrowsExactly<BridgeException>(
                () => FlightReport.ParseInclude(new List<object> { "orbit" })).Code);
            Assert.AreEqual("bad_request", Assert.ThrowsExactly<BridgeException>(
                () => FlightReport.ParseInclude(new List<object> { 3.0 })).Code);
            Assert.AreEqual("bad_request", Assert.ThrowsExactly<BridgeException>(
                () => FlightReport.ParseInclude("stages")).Code);
        }

        [TestMethod]
        public void Attitude_OnThePad_IsPitch90()
        {
            Dictionary<string, object> att = Obj(FlightReport.Build(Pad(), FlightInclude.None), "attitude");
            Assert.AreEqual(90.0, (double)att["pitch_deg"], 1e-9);
        }
    }
}
