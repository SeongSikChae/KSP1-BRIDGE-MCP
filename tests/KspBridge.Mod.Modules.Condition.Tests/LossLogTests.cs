using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition.Tests
{
    /// <summary>Collecting losses and their causes (spec C7, C11).</summary>
    [TestClass]
    public sealed class LossLogTests
    {
        private LossLog log;

        [TestInitialize]
        public void Init()
        {
            log = new LossLog();
            log.Start();
        }

        private static List<Dictionary<string, object>> Parts(Dictionary<string, object> data)
            => ((List<object>)data["parts"]).Cast<Dictionary<string, object>>().ToList();

        [TestMethod]
        public void Cause_IsJoined_WhetherItComesBeforeOrAfterTheLoss()
        {
            log.AddCause(1, "overheat");
            log.Add(1, "tank", Loss.Exploded);
            log.Add(2, "engine", Loss.Exploded);
            log.AddCause(2, "crash");
            log.Add(3, "wing", Loss.BrokeOff);

            LossReport report = log.Take();

            CollectionAssert.AreEqual(new[] { "tank", "engine", "wing" }, report.Losses.Select(l => l.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "exploded", "exploded", "broke_off" }, report.Losses.Select(l => l.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { "overheat", "crash", null }, report.Losses.Select(l => l.Cause).ToArray());
            Assert.IsFalse(report.VesselDestroyed);
        }

        [TestMethod]
        public void TheFirstCauseStands_AndACauseAloneIsNoLoss()
        {
            log.AddCause(1, "collision");
            log.AddCause(1, "crash");
            Assert.AreEqual(0, log.Take().Losses.Count);

            log.AddCause(1, "collision");
            log.AddCause(1, "crash");
            log.Add(1, "tank", Loss.Exploded);
            Assert.AreEqual("collision", log.Take().Losses.Single().Cause);
        }

        [TestMethod]
        public void Take_EmptiesTheLog_AndACauseDoesNotReachTheNextFrame()
        {
            log.Add(1, "tank", Loss.Exploded);
            log.AddCause(2, "crash");
            log.AddVesselDestroyed();
            LossReport first = log.Take();
            Assert.IsTrue(first.VesselDestroyed);
            Assert.AreEqual(1, first.Losses.Count);

            log.Add(2, "engine", Loss.Exploded);
            LossReport second = log.Take();
            Assert.IsFalse(second.VesselDestroyed);
            Assert.IsNull(second.Losses.Single().Cause);

            Assert.AreEqual(0, log.Take().Losses.Count);
        }

        [TestMethod]
        public void NothingIsCollected_BeforeStartAndAfterStop()
        {
            LossLog idle = new LossLog();
            Assert.IsFalse(idle.Running);
            idle.Add(1, "tank", Loss.Exploded);
            idle.AddVesselDestroyed();
            idle.AddCause(2, "crash");
            idle.Start();
            idle.Add(2, "engine", Loss.Exploded);
            LossReport report = idle.Take();
            Assert.IsFalse(report.VesselDestroyed);
            Assert.AreEqual("engine", report.Losses.Single().Name);
            Assert.IsNull(report.Losses.Single().Cause);

            idle.Stop();
            Assert.IsFalse(idle.Running);
            idle.Add(3, "wing", Loss.BrokeOff);
            idle.AddVesselDestroyed();
            LossReport after = idle.Take();
            Assert.AreEqual(0, after.Losses.Count);
            Assert.IsFalse(after.VesselDestroyed);
        }

        [TestMethod]
        public void LossesOfOnePlan_NeverReachTheNext()
        {
            log.Add(1, "tank", Loss.Exploded);
            log.AddVesselDestroyed();
            log.Stop();
            log.Start();
            Assert.AreEqual(0, log.Take().Losses.Count);

            log.Add(1, "tank", Loss.Exploded);
            log.AddVesselDestroyed();
            log.Start();
            LossReport report = log.Take();
            Assert.AreEqual(0, report.Losses.Count);
            Assert.IsFalse(report.VesselDestroyed);
        }

        [TestMethod]
        public void Event_CountsEveryLoss_AndListsTheFirstTen()
        {
            for (int i = 0; i < 12; i++)
                log.Add(i, "part" + i, Loss.Exploded);
            log.AddCause(0, "crash");

            LossReport report = log.Take();
            Dictionary<string, object> data = report.ToEvent();

            Assert.AreEqual(12, data["count"]);
            List<Dictionary<string, object>> parts = Parts(data);
            Assert.AreEqual(10, parts.Count);
            Assert.AreEqual("part0", parts[0]["name"]);
            Assert.AreEqual("exploded", parts[0]["kind"]);
            Assert.AreEqual("crash", parts[0]["cause"]);
            Assert.IsNull(parts[9]["cause"]);
            StringAssert.StartsWith(report.Describe(), "12 part(s) lost: part0 (exploded, crash), part1 (exploded)");
            StringAssert.EndsWith(report.Describe(), "part9 (exploded), ...");
        }

        [TestMethod]
        public void Describe_NamesTheDestroyedVessel_AndAnUnknownPart()
        {
            log.AddVesselDestroyed();
            Assert.AreEqual("the active vessel was destroyed", log.Take().Describe());

            log.AddVesselDestroyed();
            log.Add(1, null, Loss.Exploded);
            Assert.AreEqual("the active vessel was destroyed; 1 part(s) lost: unknown part (exploded)", log.Take().Describe());
        }
    }
}
