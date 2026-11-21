using KspBridge.Mod.Modules.Check;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Check.Tests
{
    [TestClass]
    public sealed class PendingCheckTests
    {
        [TestMethod]
        public void NewCheck_IsWaiting_AndBlocksReplacement()
        {
            PendingCheck check = new PendingCheck(1, 100);
            Assert.AreEqual(CheckState.Waiting, check.State);
            Assert.IsFalse(check.CanReplace(105));
        }

        [TestMethod]
        public void Complete_KeepsResult_AndAllowsReplacement()
        {
            PendingCheck check = new PendingCheck(1, 100);
            object result = new object();
            check.Complete(result);
            Assert.AreEqual(CheckState.Done, check.State);
            Assert.AreSame(result, check.Result);
            Assert.IsTrue(check.CanReplace(101));
        }

        [TestMethod]
        public void Expire_BeforeTimeout_StaysWaiting()
        {
            PendingCheck check = new PendingCheck(1, 100);
            Assert.IsFalse(check.Expire(100 + PendingCheck.TimeoutSeconds - 0.01));
            Assert.AreEqual(CheckState.Waiting, check.State);
        }

        [TestMethod]
        public void Expire_AtTimeout_TimesOut()
        {
            PendingCheck check = new PendingCheck(1, 100);
            Assert.IsTrue(check.Expire(100 + PendingCheck.TimeoutSeconds));
            Assert.AreEqual(CheckState.TimedOut, check.State);
            StringAssert.Contains(check.Reason, "10");
            Assert.IsFalse(check.Expire(200), "expires only once");
        }

        [TestMethod]
        public void WaitingPastDeadline_CanBeReplaced()
        {
            PendingCheck check = new PendingCheck(1, 100);
            Assert.IsTrue(check.CanReplace(100 + PendingCheck.TimeoutSeconds));
        }

        [TestMethod]
        public void Cancel_StopsWaiting_WithReason()
        {
            PendingCheck check = new PendingCheck(1, 100);
            check.Cancel("the scene changed");
            Assert.AreEqual(CheckState.Cancelled, check.State);
            Assert.AreEqual("the scene changed", check.Reason);
            Assert.IsTrue(check.CanReplace(100));
        }

        [TestMethod]
        public void Cancel_AfterDone_KeepsResult()
        {
            PendingCheck check = new PendingCheck(1, 100);
            check.Complete("r");
            check.Cancel("late");
            Assert.AreEqual(CheckState.Done, check.State);
        }

        [TestMethod]
        public void Complete_AfterTimeout_Throws()
        {
            PendingCheck check = new PendingCheck(1, 100);
            check.Expire(200);
            Assert.ThrowsExactly<InvalidOperationException>(() => check.Complete("r"));
        }
    }
}
