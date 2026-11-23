using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class ControlLossRuleTests
    {
        [TestMethod]
        public void FailsAfterThreeSeconds()
        {
            ControlLossRule rule = new ControlLossRule();
            Assert.IsNull(rule.Update(10, true, 5, 0, 40));
            Assert.IsNull(rule.Update(12.9, true, 5, 0, 40));
            Assert.IsNotNull(rule.Update(13, true, 5, 0, 40));
        }

        [TestMethod]
        public void ResetsWhenTheStateBreaks()
        {
            ControlLossRule rule = new ControlLossRule();
            Assert.IsNull(rule.Update(10, true, 5, 0, 40));
            Assert.IsNull(rule.Update(12, true, 5, 0, 30));   // exactly 30° is not over it
            Assert.IsNull(rule.Update(13, true, 5, 0, 40));
            Assert.IsNull(rule.Update(15.9, true, 5, 0, 40));
            Assert.IsNotNull(rule.Update(16, true, 5, 0, 40));
        }

        [TestMethod]
        public void OnlyInTheAtmosphereAtOneKpaWithoutThrust()
        {
            ControlLossRule rule = new ControlLossRule();
            Assert.IsNull(rule.Update(0, false, 5, 0, 90));
            Assert.IsNull(rule.Update(10, false, 5, 0, 90));
            Assert.IsNull(rule.Update(20, true, 0.9, 0, 90));
            Assert.IsNull(rule.Update(30, true, 0.9, 0.009, 90));   // below 0.01 kN is no thrust
            Assert.IsNull(rule.Update(40, true, 1, 0, 90));
            Assert.IsNotNull(rule.Update(43, true, 1, 0, 90));
        }

        [TestMethod]
        public void CountsBelowOneKpaWhileThrusting()
        {
            // D017: a vessel tumbling under thrust near the pad (game check: about 25 m/s, under 1 kPa).
            ControlLossRule rule = new ControlLossRule();
            Assert.IsNull(rule.Update(1.5, true, 0.3, 198, 120));
            Assert.IsNull(rule.Update(4.4, true, 0.1, 198, 60));
            Assert.IsNotNull(rule.Update(4.5, true, 0.2, 198, 90));
        }

        [TestMethod]
        public void ThrustDoesNotCountAboveTheAtmosphere()
        {
            ControlLossRule rule = new ControlLossRule();
            Assert.IsNull(rule.Update(0, false, 0, 1500, 90));
            Assert.IsNull(rule.Update(10, false, 0, 1500, 90));
        }
    }
}
