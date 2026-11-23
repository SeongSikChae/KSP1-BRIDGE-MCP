using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class AscentArgsTests
    {
        private static AscendArgs Ascend(string json) => AscentArgs.Ascend(Json.ParseObject(json));

        private static CircularizeArgs Circularize(string json) => AscentArgs.Circularize(Json.ParseObject(json));

        private static void Bad(string json) => Assert.ThrowsExactly<BridgeException>(() => Ascend(json), json);

        [TestMethod]
        public void AscendDefaults()
        {
            AscendArgs a = Ascend("{\"apoapsis_m\":80000}");
            Assert.AreEqual(80000.0, a.ApoapsisM);
            Assert.AreEqual(90.0, a.Heading);
            Assert.IsNull(a.InclinationDeg);
            Assert.IsTrue(a.North);
            Assert.AreEqual(20.0, a.MaxQKpa);
            Assert.AreEqual(5.0, a.MaxAoaDeg);
            Assert.IsNull(a.TurnStartM);
            Assert.IsNull(a.TurnEndM);
            Assert.IsTrue(a.AutoStage);
        }

        [TestMethod]
        public void AscendValues()
        {
            AscendArgs a = Ascend("{\"apoapsis_m\":100000,\"inclination_deg\":45,\"launch\":\"south\",\"max_q_kpa\":15," +
                                  "\"max_aoa_deg\":90,\"turn_start_m\":0,\"turn_end_m\":40000,\"auto_stage\":false}");
            Assert.IsNull(a.Heading);
            Assert.AreEqual(45.0, a.InclinationDeg);
            Assert.IsFalse(a.North);
            Assert.AreEqual(15.0, a.MaxQKpa);
            Assert.AreEqual(90.0, a.MaxAoaDeg);
            Assert.AreEqual(0.0, a.TurnStartM);
            Assert.AreEqual(40000.0, a.TurnEndM);
            Assert.IsFalse(a.AutoStage);
            Assert.AreEqual(0.0, Ascend("{\"apoapsis_m\":80000,\"heading\":0}").Heading);
            Assert.IsTrue(Ascend("{\"apoapsis_m\":80000,\"inclination_deg\":0,\"launch\":\"north\"}").North);
            Assert.AreEqual(180.0, Ascend("{\"apoapsis_m\":80000,\"inclination_deg\":180}").InclinationDeg);
        }

        [TestMethod]
        public void AscendRejects()
        {
            Bad("{}");
            Bad("{\"apoapsis_m\":0}");
            Bad("{\"apoapsis_m\":\"80000\"}");
            Bad("{\"apoapsis_m\":80000,\"heading\":90,\"inclination_deg\":10}");
            Bad("{\"apoapsis_m\":80000,\"launch\":\"north\"}");
            Bad("{\"apoapsis_m\":80000,\"inclination_deg\":10,\"launch\":\"east\"}");
            Bad("{\"apoapsis_m\":80000,\"heading\":360}");
            Bad("{\"apoapsis_m\":80000,\"heading\":-1}");
            Bad("{\"apoapsis_m\":80000,\"inclination_deg\":181}");
            Bad("{\"apoapsis_m\":80000,\"max_q_kpa\":0}");
            Bad("{\"apoapsis_m\":80000,\"max_aoa_deg\":0}");
            Bad("{\"apoapsis_m\":80000,\"max_aoa_deg\":91}");
            Bad("{\"apoapsis_m\":80000,\"turn_start_m\":-1}");
            Bad("{\"apoapsis_m\":80000,\"turn_end_m\":0}");
            Bad("{\"apoapsis_m\":80000,\"auto_stage\":1}");
            Bad("{\"apoapsis_m\":80000,\"altitude\":1}");
        }

        [TestMethod]
        public void Circularization()
        {
            CircularizeArgs d = Circularize("{}");
            Assert.AreEqual(0.5, d.ToleranceMps);
            Assert.IsTrue(d.AutoStage);
            CircularizeArgs c = Circularize("{\"tolerance_mps\":100,\"auto_stage\":false}");
            Assert.AreEqual(100.0, c.ToleranceMps);
            Assert.IsFalse(c.AutoStage);
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"tolerance_mps\":0}"));
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"tolerance_mps\":100.5}"));
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"apoapsis_m\":1}"));
        }

        [TestMethod]
        public void CircularizationInclination()
        {
            Assert.IsNull(Circularize("{}").InclinationDeg);
            Assert.AreEqual(45.0, Circularize("{\"inclination_deg\":45}").InclinationDeg);
            Assert.AreEqual(0.0, Circularize("{\"inclination_deg\":0}").InclinationDeg);
            Assert.AreEqual(180.0, Circularize("{\"inclination_deg\":180}").InclinationDeg);
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"inclination_deg\":-1}"));
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"inclination_deg\":180.5}"));
            Assert.ThrowsExactly<BridgeException>(() => Circularize("{\"inclination_deg\":45,\"launch\":\"north\"}"));
        }
    }
}
