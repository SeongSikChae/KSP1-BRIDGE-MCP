using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeInfoTests
    {
        private static AfterOrbit After() => new AfterOrbit
        {
            Body = "Kerbin",
            ApoapsisM = 300000,
            PeriapsisM = 100000,
            Eccentricity = 0.125,
            InclinationDeg = 0.5,
            NextApoapsisUt = 2200,
            NextPeriapsisUt = 3400,
        };

        private static NodeInfo Node(int index) => new NodeInfo
        {
            Index = index,
            Ut = 1000,
            ProgradeMps = 30,
            NormalMps = 0,
            RadialMps = 40,
            After = After(),
            RemainingDvMps = index == 0 ? 20 : (double?)null,
        };

        [TestMethod]
        public void FirstNodeItem()
        {
            Dictionary<string, object> item = Node(0).Item(400, null, 10);
            Assert.AreEqual(0, item["index"]);
            Assert.AreEqual(1000.0, item["ut"]);
            Assert.AreEqual(600.0, item["in_s"]);
            Assert.AreEqual(30.0, item["prograde_mps"]);
            Assert.AreEqual(0.0, item["normal_mps"]);
            Assert.AreEqual(40.0, item["radial_mps"]);
            Assert.AreEqual(50.0, (double)item["dv_mps"], 1e-9);
            Assert.AreEqual(2.0, (double)item["burn_time_s"], 1e-9, "the first node: the 20 m/s left at 10 m/s²");
            Assert.AreEqual(999.0, (double)item["burn_start_ut"], 1e-9);
            Assert.AreEqual(20.0, item["remaining_dv_mps"]);
            var after = (Dictionary<string, object>)item["after"];
            Assert.AreEqual("Kerbin", after["body"]);
            Assert.AreEqual(300000.0, after["apoapsis_m"]);
            Assert.AreEqual(2200.0, after["next_apoapsis_ut"]);
            Assert.AreEqual(3400.0, after["next_periapsis_ut"]);
            Assert.IsFalse(item.ContainsKey("after_missing"));
        }

        [TestMethod]
        public void LaterNodeEstimatesItsWholeDeltaV()
        {
            Dictionary<string, object> item = Node(1).Item(400, null, 10);
            Assert.AreEqual(5.0, (double)item["burn_time_s"], 1e-9);
            Assert.AreEqual(997.5, (double)item["burn_start_ut"], 1e-9);
            Assert.AreEqual(-600.0, Node(1).Item(1600, null, 10)["in_s"], "negative when past");
            Assert.IsFalse(item.ContainsKey("remaining_dv_mps"));
        }

        [TestMethod]
        public void NoOrbitAfterTheNode()
        {
            NodeInfo n = Node(0);
            n.After = null;
            Dictionary<string, object> item = n.Item(400, null, 10);
            Assert.IsNull(item["after"]);
            Assert.AreEqual(NodeInfo.NoAfterReason, item["after_missing"]);
            Assert.IsNull(item["burn_time_s"]);
            Assert.IsNull(item["burn_start_ut"]);
            Assert.IsNull(item["remaining_dv_mps"]);
        }

        [TestMethod]
        public void NoEstimate()
        {
            Dictionary<string, object> item = Node(1).Item(400, null, 0);
            Assert.IsNull(item["burn_time_s"]);
            Assert.IsNull(item["burn_start_ut"]);
        }

        [TestMethod]
        public void OpenOrbitAfter()
        {
            NodeInfo n = Node(1);
            n.After.ApoapsisM = null;
            n.After.NextApoapsisUt = null;
            var after = (Dictionary<string, object>)n.Item(400, null, 10)["after"];
            Assert.IsNull(after["apoapsis_m"]);
            Assert.IsNull(after["next_apoapsis_ut"]);
        }
    }
}
