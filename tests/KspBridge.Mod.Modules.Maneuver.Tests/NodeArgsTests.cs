using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Maneuver.Tests.TestData;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeArgsTests
    {
        private static void Bad(Action a, string contains)
        {
            try
            {
                a();
            }
            catch (BridgeException ex)
            {
                StringAssert.Contains(ex.Message, contains);
                return;
            }
            Assert.Fail("expected bad_request containing '" + contains + "'");
        }

        private static Dictionary<string, object> Goal(params (string, object)[] pairs) => Args(pairs);

        [TestMethod]
        public void CreateWithComponents()
        {
            CreateNodeArgs a = NodeArgs.Create(Args(("ut", 1000.0), ("prograde_mps", 10.0)));
            Assert.AreEqual(NodeTimeKind.Ut, a.Time);
            Assert.AreEqual(1000.0, a.TimeValue);
            Assert.AreEqual(NodeGoalKind.Components, a.Goal);
            Assert.AreEqual(10.0, a.ProgradeMps);
            Assert.AreEqual(0.0, a.NormalMps);
            Assert.AreEqual(0.0, a.RadialMps);
            a = NodeArgs.Create(Args(("in_s", 600.0), ("normal_mps", -5.0), ("radial_mps", 2.0)));
            Assert.AreEqual(NodeTimeKind.InSeconds, a.Time);
            Assert.AreEqual(-5.0, a.NormalMps);
            Assert.AreEqual(2.0, a.RadialMps);
        }

        [TestMethod]
        public void CreateWithGoal()
        {
            CreateNodeArgs a = NodeArgs.Create(Args(("at", "periapsis"), ("goal", Goal(("apoapsis_m", 300000.0)))));
            Assert.AreEqual(NodeTimeKind.Periapsis, a.Time);
            Assert.AreEqual(NodeGoalKind.Apoapsis, a.Goal);
            Assert.AreEqual(300000.0, a.GoalValue);
            a = NodeArgs.Create(Args(("at", "apoapsis"), ("goal", Goal(("circular", true)))));
            Assert.AreEqual(NodeTimeKind.Apoapsis, a.Time);
            Assert.AreEqual(NodeGoalKind.Circular, a.Goal);
            a = NodeArgs.Create(Args(("in_s", 10.0), ("goal", Goal(("periapsis_m", 0.0)))));
            Assert.AreEqual(NodeGoalKind.Periapsis, a.Goal);
            Assert.AreEqual(0.0, a.GoalValue);
        }

        [TestMethod]
        public void CreateTimeRules()
        {
            Bad(() => NodeArgs.Create(Args(("prograde_mps", 1.0))), "exactly one time");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("in_s", 1.0), ("prograde_mps", 1.0))), "exactly one time");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("at", "apoapsis"), ("prograde_mps", 1.0))), "exactly one time");
            Bad(() => NodeArgs.Create(Args(("in_s", 0.0), ("prograde_mps", 1.0))), "'in_s' must be > 0");
            Bad(() => NodeArgs.Create(Args(("at", "node"), ("prograde_mps", 1.0))), "'at' must be apoapsis or periapsis");
        }

        [TestMethod]
        public void CreateDeltaVRules()
        {
            Bad(() => NodeArgs.Create(Args(("ut", 1.0))), "give delta-v components");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("prograde_mps", 0.0), ("normal_mps", 0.0))), "all 0");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("prograde_mps", 1.0), ("goal", Goal(("circular", true))))), "not both");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal()))), "exactly one goal");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal(("apoapsis_m", 1.0), ("periapsis_m", 1.0))))), "exactly one goal");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal(("circular", false))))), "only true");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal(("height", 1.0))))), "unknown parameter 'goal.height'");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", 5.0))), "'goal' must be an object");
        }

        [TestMethod]
        public void CreateKindsAndUnknownKeys()
        {
            Bad(() => NodeArgs.Create(Args(("ut", "soon"), ("prograde_mps", 1.0))), "'ut' must be a number");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("prograde_mps", true))), "'prograde_mps' must be a number");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal(("apoapsis_m", "high"))))), "'goal.apoapsis_m' must be a number");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("goal", Goal(("circular", "yes"))))), "must be a boolean");
            Bad(() => NodeArgs.Create(Args(("ut", 1.0), ("prograde_mps", 1.0), ("dv", 1.0))), "unknown parameter 'dv'");
            Bad(() => NodeArgs.Create(Args(("ut", double.NaN), ("prograde_mps", 1.0))), "finite");
        }

        [TestMethod]
        public void Delete()
        {
            Assert.AreEqual(0, NodeArgs.Delete(Args(("index", 0.0))).Index);
            Assert.IsTrue(NodeArgs.Delete(Args(("all", true))).All);
            Bad(() => NodeArgs.Delete(Args()), "exactly one");
            Bad(() => NodeArgs.Delete(Args(("index", 0.0), ("all", true))), "exactly one");
            Bad(() => NodeArgs.Delete(Args(("all", false))), "only true");
            Bad(() => NodeArgs.Delete(Args(("index", -1.0))), "integer >= 0");
            Bad(() => NodeArgs.Delete(Args(("index", 1.5))), "integer >= 0");
            Bad(() => NodeArgs.Delete(Args(("index", "first"))), "must be a number");
            Bad(() => NodeArgs.Delete(Args(("index", 0.0), ("node", 1.0))), "unknown parameter 'node'");
        }

        [TestMethod]
        public void Execute()
        {
            ExecuteNodeArgs e = NodeArgs.Execute(Args());
            Assert.AreEqual(0.5, e.ToleranceMps);
            Assert.IsTrue(e.AutoStage);
            e = NodeArgs.Execute(Args(("tolerance_mps", 100.0), ("auto_stage", false)));
            Assert.AreEqual(100.0, e.ToleranceMps);
            Assert.IsFalse(e.AutoStage);
            Bad(() => NodeArgs.Execute(Args(("tolerance_mps", 0.0))), "'tolerance_mps' must be > 0 and <= 100");
            Bad(() => NodeArgs.Execute(Args(("tolerance_mps", 100.5))), "'tolerance_mps' must be > 0 and <= 100");
            Bad(() => NodeArgs.Execute(Args(("auto_stage", "no"))), "must be a boolean");
            Bad(() => NodeArgs.Execute(Args(("lead_s", 1.0))), "unknown parameter 'lead_s'");
        }
    }
}
