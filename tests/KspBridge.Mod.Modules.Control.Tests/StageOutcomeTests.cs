using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Control.Tests
{
    [TestClass]
    public sealed class StageOutcomeTests
    {
        [TestMethod]
        public void Before_NeedsANextStage()
        {
            Assert.AreEqual("no stages left", StageOutcome.Before(null));
            Assert.IsNull(StageOutcome.Before(0));
            Assert.IsNull(StageOutcome.Before(7));
        }

        [TestMethod]
        public void After_RefusedByTheGame()
            => Assert.AreEqual("the game refused staging", StageOutcome.After(3, accepted: false, activatedParts: 0));

        [TestMethod]
        public void After_NothingActivated()
            => Assert.AreEqual("no part of stage 3 was activated", StageOutcome.After(3, accepted: true, activatedParts: 0));

        [TestMethod]
        public void After_Success() => Assert.IsNull(StageOutcome.After(0, accepted: true, activatedParts: 2));
    }
}
