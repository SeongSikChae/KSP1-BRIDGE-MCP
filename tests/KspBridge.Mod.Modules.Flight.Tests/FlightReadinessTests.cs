using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Flight.Tests
{
    [TestClass]
    public sealed class FlightReadinessTests
    {
        private static FlightReadinessInput Ready() => new FlightReadinessInput
        {
            GameLoaded = true,
            SceneName = "FLIGHT",
            InFlight = true,
            HasActiveVessel = true,
        };

        [TestMethod]
        public void Ready_IsNull()
        {
            Assert.IsNull(FlightReadiness.Judge(Ready()));
        }

        [TestMethod]
        public void NoGame_IsNotReady()
        {
            FlightReadinessInput input = Ready();
            input.GameLoaded = false;
            input.SceneName = "MAINMENU";
            StringAssert.Contains(FlightReadiness.Judge(input), "no game");
        }

        [TestMethod]
        public void SceneLoading_UsesTheCoreReason()
        {
            FlightReadinessInput input = Ready();
            input.CoreBlockReason = "scene FLIGHT is loading; poll get_status until ready=true";
            Assert.AreEqual(input.CoreBlockReason, FlightReadiness.Judge(input));
        }

        [TestMethod]
        public void NotFlight_NamesTheScene()
        {
            FlightReadinessInput input = Ready();
            input.InFlight = false;
            input.SceneName = "SPACECENTER";
            string reason = FlightReadiness.Judge(input);
            StringAssert.Contains(reason, "flight scene");
            StringAssert.Contains(reason, "SPACECENTER");
        }

        [TestMethod]
        public void FlightNotReady_UsesTheCoreReason()
        {
            FlightReadinessInput input = Ready();
            input.CoreBlockReason = "flight scene is not ready";
            Assert.AreEqual("flight scene is not ready", FlightReadiness.Judge(input));
        }

        [TestMethod]
        public void NoActiveVessel_IsNotReady()
        {
            FlightReadinessInput input = Ready();
            input.HasActiveVessel = false;
            StringAssert.Contains(FlightReadiness.Judge(input), "no active vessel");
        }
    }
}
