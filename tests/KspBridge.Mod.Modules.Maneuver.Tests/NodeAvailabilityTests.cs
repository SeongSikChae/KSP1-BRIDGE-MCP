using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeAvailabilityTests
    {
        private static NodeReadinessInput Ready() => new NodeReadinessInput
        {
            GameLoaded = true,
            SceneName = "FLIGHT",
            InFlight = true,
            HasActiveVessel = true,
            HasFacilityLevels = true,
            TrackingAllowsConics = true,
            FlightPlanningUnlocked = true,
            HasSolver = true,
        };

        [TestMethod]
        public void ReadyIsNull() => Assert.IsNull(NodeAvailability.Judge(Ready()));

        [TestMethod]
        public void SceneAndVessel()
        {
            NodeReadinessInput i = Ready();
            i.GameLoaded = false;
            StringAssert.Contains(NodeAvailability.Judge(i), "no game is loaded");
            i = Ready();
            i.CoreBlockReason = "the scene is loading";
            Assert.AreEqual("the scene is loading", NodeAvailability.Judge(i));
            i = Ready();
            i.InFlight = false;
            i.SceneName = "SPACECENTER";
            StringAssert.Contains(NodeAvailability.Judge(i), "flight scene (now SPACECENTER)");
            i = Ready();
            i.HasActiveVessel = false;
            StringAssert.Contains(NodeAvailability.Judge(i), "no active vessel");
        }

        [TestMethod]
        public void FacilitiesNameWhatIsMissing()
        {
            NodeReadinessInput i = Ready();
            i.TrackingAllowsConics = false;
            string why = NodeAvailability.Judge(i);
            StringAssert.Contains(why, "tracking station");
            Assert.IsFalse(why.Contains("mission control"));
            i = Ready();
            i.FlightPlanningUnlocked = false;
            why = NodeAvailability.Judge(i);
            StringAssert.Contains(why, "mission control");
            Assert.IsFalse(why.Contains("tracking station"));
            i.TrackingAllowsConics = false;
            why = NodeAvailability.Judge(i);
            StringAssert.Contains(why, "tracking station");
            StringAssert.Contains(why, "mission control");
        }

        [TestMethod]
        public void NoFacilityLevelsCountsAsUnlocked()
        {
            NodeReadinessInput i = Ready();
            i.HasFacilityLevels = false;
            i.TrackingAllowsConics = false;
            i.FlightPlanningUnlocked = false;
            Assert.IsNull(NodeAvailability.Judge(i));
        }

        [TestMethod]
        public void NoSolver()
        {
            NodeReadinessInput i = Ready();
            i.HasSolver = false;
            StringAssert.Contains(NodeAvailability.Judge(i), "no orbit solver");
        }
    }
}
