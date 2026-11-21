using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class VesselSwitchCheckTests
    {
        private static VesselFacts Target() => new VesselFacts { Found = true, Loaded = true, Owned = true, Name = "Aeris 3A" };

        [TestMethod]
        public void LoadedOwnedClear_Proceeds()
            => Assert.AreEqual(SwitchOutcome.Proceed, VesselSwitchCheck.Decide(Target(), false, "CLEAR").Outcome);

        [TestMethod]
        public void NotFound_IsRejected()
        {
            Assert.AreEqual(SwitchOutcome.Reject, VesselSwitchCheck.Decide(new VesselFacts(), true, null).Outcome);
            Assert.AreEqual(SwitchOutcome.Reject, VesselSwitchCheck.Decide(null, true, null).Outcome);
        }

        [TestMethod]
        public void ActiveTarget_IsAlready()
        {
            VesselFacts t = Target();
            t.Active = true;
            t.Loaded = false;
            Assert.AreEqual(SwitchOutcome.Already, VesselSwitchCheck.Decide(t, false, "NOT_IN_ATMOSPHERE").Outcome);
        }

        [TestMethod]
        public void NotLoaded_IsRejectedEvenWithForce()
        {
            VesselFacts t = Target();
            t.Loaded = false;
            SwitchDecision d = VesselSwitchCheck.Decide(t, true, null);
            Assert.AreEqual(SwitchOutcome.Reject, d.Outcome);
            StringAssert.Contains(d.Reason, "not loaded");
        }

        [TestMethod]
        public void NotOwned_IsRejectedWhateverForce()
        {
            VesselFacts t = Target();
            t.Owned = false;
            Assert.AreEqual(SwitchOutcome.Reject, VesselSwitchCheck.Decide(t, false, "CLEAR").Outcome);
            SwitchDecision forced = VesselSwitchCheck.Decide(t, true, null);
            Assert.AreEqual(SwitchOutcome.Reject, forced.Outcome);
            StringAssert.Contains(forced.Reason, "not owned");
        }

        [TestMethod]
        public void NotClear_WithoutForce_IsRejectedWithResultName()
        {
            foreach (string result in new[] { "NOT_IN_ATMOSPHERE", "NOT_UNDER_ACCELERATION", "NOT_WHILE_MOVING_OVER_SURFACE",
                         "NOT_WHILE_ABOUT_TO_CRASH", "NOT_WHILE_ON_A_LADDER" })
            {
                SwitchDecision d = VesselSwitchCheck.Decide(Target(), false, result);
                Assert.AreEqual(SwitchOutcome.Reject, d.Outcome);
                StringAssert.Contains(d.Reason, result);
            }
        }

        [TestMethod]
        public void NotClear_WithForce_Proceeds()
            => Assert.AreEqual(SwitchOutcome.Proceed, VesselSwitchCheck.Decide(Target(), true, "NOT_IN_ATMOSPHERE").Outcome);

        [TestMethod]
        public void Order_LoadedBeforeOwnedBeforeClear()
        {
            VesselFacts t = Target();
            t.Loaded = false;
            t.Owned = false;
            StringAssert.Contains(VesselSwitchCheck.Decide(t, false, "NOT_IN_ATMOSPHERE").Reason, "not loaded");

            VesselFacts u = Target();
            u.Owned = false;
            StringAssert.Contains(VesselSwitchCheck.Decide(u, false, "NOT_IN_ATMOSPHERE").Reason, "not owned");
        }
    }
}
