using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Measure.Tests
{
    [TestClass]
    public sealed class MeasureArgsTests
    {
        private static Dictionary<string, object> Limit(object value) => new Dictionary<string, object> { ["sample_limit"] = value };

        [TestMethod]
        public void SampleLimit_DefaultsTo30000()
        {
            Assert.AreEqual(30000, MeasureArgs.SampleLimit(new Dictionary<string, object>()));
            Assert.AreEqual(30000, MeasureArgs.SampleLimit(Limit(null)));
        }

        [TestMethod]
        public void SampleLimit_AcceptsTheBounds()
        {
            Assert.AreEqual(1, MeasureArgs.SampleLimit(Limit(1.0)));
            Assert.AreEqual(180000, MeasureArgs.SampleLimit(Limit(180000.0)));
        }

        [TestMethod]
        public void SampleLimit_RejectsOutOfRangeAndNonIntegers()
        {
            foreach (object bad in new object[] { 0.0, -1.0, 180001.0, 1.5, "10", true })
            {
                BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => MeasureArgs.SampleLimit(Limit(bad)), "value " + bad);
                Assert.AreEqual("bad_request", ex.Code);
            }
        }

        private static MeasureReadinessInput Ready() => new MeasureReadinessInput
        {
            GameLoaded = true,
            SceneName = "FLIGHT",
            InFlight = true,
            HasActiveVessel = true,
        };

        [TestMethod]
        public void Readiness_ReadyIsNull()
        {
            Assert.IsNull(MeasureArgs.Readiness(Ready()));
        }

        [TestMethod]
        public void Readiness_NoGame()
        {
            MeasureReadinessInput input = Ready();
            input.GameLoaded = false;
            input.SceneName = "MAINMENU";
            StringAssert.Contains(MeasureArgs.Readiness(input), "no game");
        }

        [TestMethod]
        public void Readiness_SceneLoadingUsesTheCoreReason()
        {
            MeasureReadinessInput input = Ready();
            input.CoreBlockReason = "scene FLIGHT is loading; poll get_status until ready=true";
            Assert.AreEqual(input.CoreBlockReason, MeasureArgs.Readiness(input));
        }

        [TestMethod]
        public void Readiness_NotFlightNamesTheScene()
        {
            MeasureReadinessInput input = Ready();
            input.InFlight = false;
            input.SceneName = "SPACECENTER";
            string reason = MeasureArgs.Readiness(input);
            StringAssert.Contains(reason, "measure_start");
            StringAssert.Contains(reason, "SPACECENTER");
        }

        [TestMethod]
        public void Readiness_NoActiveVessel()
        {
            MeasureReadinessInput input = Ready();
            input.HasActiveVessel = false;
            StringAssert.Contains(MeasureArgs.Readiness(input), "no active vessel");
        }

        [TestMethod]
        public void EndReason_FlightToFlightIsNewFlight()
        {
            Assert.AreEqual("new_flight", MeasureArgs.EndReason(MeasureEvent.SceneLoadRequested, inFlight: true, requestedFlight: true));
        }

        [TestMethod]
        public void EndReason_OtherSceneLoadsAreSceneChange()
        {
            Assert.AreEqual("scene_change", MeasureArgs.EndReason(MeasureEvent.SceneLoadRequested, inFlight: true, requestedFlight: false));
            Assert.AreEqual("scene_change", MeasureArgs.EndReason(MeasureEvent.SceneLoadRequested, inFlight: false, requestedFlight: true));
        }

        [TestMethod]
        public void EndReason_VesselChange()
        {
            Assert.AreEqual("vessel_change", MeasureArgs.EndReason(MeasureEvent.VesselChange, inFlight: true, requestedFlight: false));
        }
    }
}
