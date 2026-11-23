using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeStartTests
    {
        private static NodeStartReading Ready() => new NodeStartReading
        {
            Now = 100,
            HasNode = true,
            NodeUt = 1000,
            HasAfter = true,
            BurnSeconds = 40,
        };

        [TestMethod]
        public void StartsHalfTheBurnBeforeTheNode()
        {
            NodeStartResult s = NodeStart.Judge(Ready());
            Assert.IsNull(s.Failure);
            Assert.AreEqual(1000, s.NodeUt);
            Assert.AreEqual(40, s.BurnSeconds);
            Assert.AreEqual(980, s.StartUt);
        }

        [TestMethod]
        public void OnlyTheStartPassedIsNotAFailure()
        {
            NodeStartReading r = Ready();
            r.Now = 990;
            Assert.IsNull(NodeStart.Judge(r).Failure);
        }

        [TestMethod]
        public void Failures()
        {
            NodeStartReading r = Ready();
            r.Unavailable = "maneuver nodes are locked";
            Assert.AreEqual("maneuver nodes are locked", NodeStart.Judge(r).Failure);
            r = Ready();
            r.HasNode = false;
            StringAssert.Contains(NodeStart.Judge(r).Failure, "no maneuver node");
            r = Ready();
            r.OnSurface = true;
            StringAssert.Contains(NodeStart.Judge(r).Failure, "launch pad");
            r = Ready();
            r.Now = 1000;
            StringAssert.Contains(NodeStart.Judge(r).Failure, "has passed");
            r = Ready();
            r.HasAfter = false;
            StringAssert.Contains(NodeStart.Judge(r).Failure, "no orbit after");
            r = Ready();
            r.BurnSeconds = null;
            StringAssert.Contains(NodeStart.Judge(r).Failure, "cannot be estimated");
        }
    }
}
