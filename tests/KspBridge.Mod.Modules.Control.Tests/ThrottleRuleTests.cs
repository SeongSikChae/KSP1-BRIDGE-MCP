using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Control.Tests
{
    [TestClass]
    public sealed class ThrottleRuleTests
    {
        [TestMethod]
        [DataRow(EndReasons.PlannerAbort, true)]
        [DataRow(EndReasons.StepFailed, true)]
        [DataRow(EndReasons.Timeout, true)]
        [DataRow(EndReasons.Reload, true)]
        [DataRow(EndReasons.Completed, false)]
        [DataRow(EndReasons.NewFlight, false)]
        [DataRow(EndReasons.SceneChange, false)]
        [DataRow(EndReasons.VesselChange, false)]
        public void CutsOnlyOnReasonsThatKeepTheVessel(string reason, bool expected)
        {
            Assert.AreEqual(expected, ThrottleRule.ShouldCut(reason, sameVessel: true));
            Assert.IsFalse(ThrottleRule.ShouldCut(reason, sameVessel: false));
        }
    }
}
