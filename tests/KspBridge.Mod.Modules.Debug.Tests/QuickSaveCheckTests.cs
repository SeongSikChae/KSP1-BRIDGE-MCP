using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class QuickSaveCheckTests
    {
        [TestMethod]
        public void DestroyedVessel_IsRefused_EvenWhenClear()
            => StringAssert.Contains(QuickSaveCheck.Decide(true, "CLEAR"), "destroyed");

        [TestMethod]
        public void LadderSurfaceCrash_AreRefused()
        {
            foreach (string result in new[] { "NOT_WHILE_ON_A_LADDER", "NOT_WHILE_MOVING_OVER_SURFACE", "NOT_WHILE_ABOUT_TO_CRASH" })
                StringAssert.Contains(QuickSaveCheck.Decide(false, result), result);
        }

        [TestMethod]
        public void AtmosphereAccelerationClear_Pass()
        {
            Assert.IsNull(QuickSaveCheck.Decide(false, "CLEAR"));
            Assert.IsNull(QuickSaveCheck.Decide(false, "NOT_IN_ATMOSPHERE"));
            Assert.IsNull(QuickSaveCheck.Decide(false, "NOT_UNDER_ACCELERATION"));
        }
    }
}
