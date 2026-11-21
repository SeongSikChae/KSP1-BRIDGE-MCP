using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Flight.Tests
{
    /// <summary>
    /// Inputs follow the game's behaviour checked by decompiling: firing a stage makes its parts ACTIVE (force_activate), the
    /// flight starts with currentStage = highest icon stage + 1 (BeginFlight), SetShipOrbit leaves currentStage alone, and a
    /// fired separator removes its own icon so it is not in the staged list.
    /// </summary>
    [TestClass]
    public sealed class StageRefTests
    {
        private static StagedPart Idle(int stage) => new StagedPart(stage, activated: false);

        private static StagedPart Fired(int stage) => new StagedPart(stage, activated: true);

        [TestMethod]
        public void BeforeLaunch_NoLastActivated_NextIsHighestStage()
        {
            StageRefResult r = StageRef.Judge(5, new[] { Idle(4), Idle(4), Idle(3), Idle(1), Idle(0) });
            Assert.IsNull(r.LastActivated);
            Assert.AreEqual(4, r.Next);
            Assert.AreEqual(4, r.Reference);
        }

        [TestMethod]
        public void MovedToOrbitWithoutStaging_IsLikeBeforeLaunch()
        {
            StageRefResult r = StageRef.Judge(3, new[] { Idle(2), Idle(1), Idle(0) });
            Assert.IsNull(r.LastActivated);
            Assert.AreEqual(2, r.Reference);
        }

        [TestMethod]
        public void IgnitedOnThePad_HeldByClamps_LastActivatedIsCurrentStage()
        {
            StageRefResult r = StageRef.Judge(4, new[] { Fired(4), Fired(4), Idle(3), Idle(0) });
            Assert.AreEqual(4, r.LastActivated);
            Assert.AreEqual(3, r.Next);
            Assert.AreEqual(4, r.Reference);
        }

        [TestMethod]
        public void BoostersDropped_EarlierEngineStillBurning_StageRemains()
        {
            // Main engine fired in stage 3, stage 2 (radial decouplers) fired and dropped the boosters; the fired main engine
            // keeps the top group (number 2) alive.
            StageRefResult r = StageRef.Judge(2, new[] { Fired(3), Idle(1), Idle(0) });
            Assert.AreEqual(2, r.LastActivated);
            Assert.AreEqual(1, r.Next);
            Assert.AreEqual(2, r.Reference);
        }

        [TestMethod]
        public void AllFiredPartsGone_NoLastActivated_ReferenceIsNext()
        {
            // Whatever currentStage became after the empty group was deleted, it is not used.
            StageRefResult r = StageRef.Judge(1, new[] { Idle(1), Idle(0) });
            Assert.IsNull(r.LastActivated);
            Assert.AreEqual(1, r.Next);
            Assert.AreEqual(1, r.Reference);
        }

        [TestMethod]
        public void OnlyAFiredSeparatorLeft_IsNotCounted()
        {
            // The separator that fired is still on the vessel but has no icon, so the snapshot does not list it.
            StageRefResult r = StageRef.Judge(1, new[] { Idle(0) });
            Assert.IsNull(r.LastActivated);
            Assert.AreEqual(0, r.Next);
        }

        [TestMethod]
        public void LastStageFired_NoNext()
        {
            StageRefResult r = StageRef.Judge(0, new[] { Fired(0) });
            Assert.AreEqual(0, r.LastActivated);
            Assert.IsNull(r.Next);
            Assert.AreEqual(0, r.Reference);
        }

        [TestMethod]
        public void NoStagedParts_NothingToShow()
        {
            StageRefResult r = StageRef.Judge(0, new StagedPart[0]);
            Assert.IsNull(r.LastActivated);
            Assert.IsNull(r.Next);
            Assert.IsNull(r.Reference);
        }
    }
}
