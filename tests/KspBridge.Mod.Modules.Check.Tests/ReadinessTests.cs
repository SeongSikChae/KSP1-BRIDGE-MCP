using KspBridge.Mod.Modules.Check;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Check.Tests
{
    [TestClass]
    public sealed class ReadinessTests
    {
        private static ReadinessInput Ready() => new ReadinessInput
        {
            GameLoaded = true,
            CoreBlockReason = null,
            SceneName = "EDITOR",
            EditorScene = true,
            HasShip = true,
            HasEditorDriver = true,
            RestartingEditor = false,
            FramesSinceLoad = int.MaxValue,
            AllPartsStarted = true,
            SortSignalFound = true,
            SortRunning = false,
        };

        [TestMethod]
        public void AllConditionsMet_IsReady() => Assert.IsNull(Readiness.Judge(Ready()));

        [TestMethod]
        public void ExactlySettleFrames_IsReady()
        {
            ReadinessInput input = Ready();
            input.FramesSinceLoad = Readiness.SettleFrames;
            Assert.IsNull(Readiness.Judge(input));
        }

        [TestMethod]
        public void NoGame_NotReady()
        {
            ReadinessInput input = Ready();
            input.GameLoaded = false;
            StringAssert.Contains(Readiness.Judge(input), "no game");
        }

        [TestMethod]
        public void CoreSceneLoad_NotReady_WithCoreReason()
        {
            ReadinessInput input = Ready();
            input.CoreBlockReason = "a scene load is in progress";
            Assert.AreEqual("a scene load is in progress", Readiness.Judge(input));
        }

        [TestMethod]
        public void NotEditorScene_NotReady()
        {
            ReadinessInput input = Ready();
            input.EditorScene = false;
            input.SceneName = "SPACECENTER";
            StringAssert.Contains(Readiness.Judge(input), "SPACECENTER");
        }

        [TestMethod]
        public void NoShip_NotReady()
        {
            ReadinessInput input = Ready();
            input.HasShip = false;
            Assert.IsNotNull(Readiness.Judge(input));
        }

        [TestMethod]
        public void NoEditorDriver_NotReady()
        {
            ReadinessInput input = Ready();
            input.HasEditorDriver = false;
            StringAssert.Contains(Readiness.Judge(input), "switching facility");
        }

        [TestMethod]
        public void InGameFacilitySwitch_NotReady()
        {
            ReadinessInput input = Ready();
            input.RestartingEditor = true;
            StringAssert.Contains(Readiness.Judge(input), "switching facility");
        }

        [TestMethod]
        public void RecentLoad_NotReady()
        {
            ReadinessInput input = Ready();
            input.FramesSinceLoad = Readiness.SettleFrames - 1;
            StringAssert.Contains(Readiness.Judge(input), "loading");
        }

        [TestMethod]
        public void PartsNotStarted_NotReady()
        {
            ReadinessInput input = Ready();
            input.AllPartsStarted = false;
            StringAssert.Contains(Readiness.Judge(input), "parts");
        }

        [TestMethod]
        public void SortSignalMissing_NotReady()
        {
            ReadinessInput input = Ready();
            input.SortSignalFound = false;
            StringAssert.Contains(Readiness.Judge(input), "sortRoutine");
        }

        [TestMethod]
        public void SortRunning_NotReady()
        {
            ReadinessInput input = Ready();
            input.SortRunning = true;
            StringAssert.Contains(Readiness.Judge(input), "sorting");
        }
    }
}
