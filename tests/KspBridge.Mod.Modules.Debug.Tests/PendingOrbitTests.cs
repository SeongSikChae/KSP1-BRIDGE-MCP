using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class PendingOrbitTests
    {
        [TestMethod]
        public void Apply_KeepsTheResult()
        {
            PendingOrbit p = new PendingOrbit(1, 100, null);
            p.Apply("done");
            Assert.AreEqual(OrbitState.Applied, p.State);
            Assert.AreEqual("done", p.Result);
            Assert.IsFalse(p.Expire(200), "an applied request does not time out");
        }

        [TestMethod]
        public void Expire_After10Seconds_IsTimedOut()
        {
            PendingOrbit p = new PendingOrbit(1, 100, null);
            Assert.IsFalse(p.Expire(109.9));
            Assert.AreEqual(OrbitState.Waiting, p.State);
            Assert.IsTrue(p.Expire(110));
            Assert.AreEqual(OrbitState.TimedOut, p.State);
            StringAssert.Contains(p.Reason, "10");
            Assert.ThrowsExactly<InvalidOperationException>(() => p.Apply("late"));
        }

        [TestMethod]
        public void Cancel_StopsWaiting_AndKeepsAnEndedState()
        {
            PendingOrbit p = new PendingOrbit(1, 100, null);
            p.Cancel("the scene changed to SPACECENTER");
            Assert.AreEqual(OrbitState.Cancelled, p.State);
            StringAssert.Contains(p.Reason, "SPACECENTER");

            PendingOrbit applied = new PendingOrbit(2, 100, null);
            applied.Apply("done");
            applied.Cancel("too late");
            Assert.AreEqual(OrbitState.Applied, applied.State);
        }

        [TestMethod]
        public void NewRequest_WaitsForTheOneInProgress()
        {
            PendingOrbit p = new PendingOrbit(1, 100, null);
            Assert.IsFalse(p.CanReplace(105), "still waiting");
            Assert.IsTrue(p.CanReplace(110), "past its deadline");

            PendingOrbit ended = new PendingOrbit(2, 100, null);
            ended.Apply("done");
            Assert.IsTrue(ended.CanReplace(101));
        }
    }
}
