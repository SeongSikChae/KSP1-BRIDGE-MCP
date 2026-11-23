using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Maneuver.Tests.TestData;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class ExecuteNodeLogicTests
    {
        private static ExecuteNodeLogic Started(double startUt = 100, double tolerance = 0.5)
        {
            ExecuteNodeLogic logic = new ExecuteNodeLogic(new ExecuteNodeArgs { ToleranceMps = tolerance });
            NodeStartResult start = NodeStart.Judge(new NodeStartReading
            {
                Now = 0,
                HasNode = true,
                NodeUt = startUt + 2,
                HasAfter = true,
                BurnSeconds = 4,
            });
            logic.Start(start);
            return logic;
        }

        [TestMethod]
        public void TakesTheStartFromNodeStart()
        {
            ExecuteNodeLogic logic = Started(100);
            Assert.AreEqual(100, logic.StartUt);
            Assert.AreEqual(102, logic.NodeUt);
            Assert.AreEqual(4, logic.BurnSeconds);
        }

        [TestMethod]
        public void WaitsThenBurns()
        {
            ExecuteNodeLogic logic = Started(100);
            BurnCommand wait = logic.Tick(50, East * 100, Reading(50));
            Assert.AreEqual("wait", logic.Phase);
            Assert.IsFalse(wait.WantThrust);
            Assert.AreEqual(0.0, wait.Throttle);
            Assert.IsNull(logic.BurnStartUt);
            BurnCommand burn = logic.Tick(100, East * 100, Reading(100));
            Assert.AreEqual("burn", logic.Phase);
            Assert.IsTrue(burn.WantThrust);
            Assert.AreEqual(1.0, burn.Throttle, 1e-9);
            Assert.AreEqual(100, logic.BurnStartUt);
        }

        [TestMethod]
        public void BurnsAtOnceWhenTheStartHasPassed()
        {
            ExecuteNodeLogic logic = Started(100);
            logic.Tick(150, East * 100, Reading(150));
            Assert.AreEqual("burn", logic.Phase);
            Assert.AreEqual(150, logic.BurnStartUt);
        }

        [TestMethod]
        public void TheBurnIsTheSharedRule()
        {
            // Same delta-v and reading: the same command as BurnControl gives (D014 / spec C5 "같은 판정").
            ExecuteNodeLogic logic = Started(0, 1);
            BurnControl control = new BurnControl(1, true);
            foreach (double left in new[] { 100.0, 20.0, 8.0, 5.0, 5.2, 0.8 })
            {
                BurnCommand a = logic.Tick(10, North * left, Reading(10));
                BurnCommand b = control.Tick(North * left, true, Reading(10));
                Assert.AreEqual(b.Throttle, a.Throttle, 1e-12);
                Assert.AreEqual(b.PitchDeg, a.PitchDeg, 1e-12);
                Assert.AreEqual(b.HeadingDeg, a.HeadingDeg, 1e-12);
                Assert.AreEqual(b.Done, a.Done);
            }
        }

        [TestMethod]
        public void DoneWithinTheTolerance()
        {
            ExecuteNodeLogic logic = Started(0, 0.5);
            Assert.IsTrue(logic.Tick(10, East * 0.4, Reading(10)).Done);
            Assert.AreEqual(0.4, logic.RemainingDv, 1e-12);
        }

        [TestMethod]
        public void Progress()
        {
            ExecuteNodeLogic logic = Started(100);
            Dictionary<string, object> before = logic.Progress(0);
            Assert.AreEqual("wait", before["phase"]);
            Assert.IsNull(before["nose_error_deg"]);
            Assert.IsNull(before["throttle"]);
            logic.Tick(100, East * 100, Reading(100));
            Dictionary<string, object> p = logic.Progress(2);
            Assert.AreEqual("burn", p["phase"]);
            Assert.AreEqual(100.0, p["start_ut"]);
            Assert.AreEqual(4.0, p["burn_time_s"]);
            Assert.AreEqual(100.0, (double)p["remaining_dv_mps"], 1e-9);
            Assert.AreEqual(0.0, (double)p["nose_error_deg"], 1e-9);
            Assert.AreEqual(1.0, (double)p["throttle"], 1e-9);
            Assert.AreEqual(2, p["stages"]);
        }
    }
}
