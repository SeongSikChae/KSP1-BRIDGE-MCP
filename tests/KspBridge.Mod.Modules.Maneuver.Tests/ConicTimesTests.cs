using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class ConicTimesTests
    {
        [TestMethod]
        public void ClosedOrbit()
        {
            // Period 1000 s; 100 s after the periapsis at UT 5000.
            Assert.AreEqual(5900, ConicTimes.NextPeriapsis(5000, 0.2, 1000, 100).Value, 1e-9);
            Assert.AreEqual(5400, ConicTimes.NextApoapsis(5000, 0.2, 1000, 100).Value, 1e-9);
            Assert.AreEqual(5400, ConicTimes.NextPeriapsis(5000, 0.2, 1000, 600).Value, 1e-9);
            Assert.AreEqual(5900, ConicTimes.NextApoapsis(5000, 0.2, 1000, 600).Value, 1e-9);
        }

        [TestMethod]
        public void AtThePointItselfIsOnePeriodLater()
        {
            Assert.AreEqual(6000, ConicTimes.NextPeriapsis(5000, 0.2, 1000, 0).Value, 1e-9);
            Assert.AreEqual(6000, ConicTimes.NextApoapsis(5000, 0.2, 1000, 500).Value, 1e-9);
            Assert.AreEqual(6000, ConicTimes.NextApoapsis(5000, 0.2, 1000, 500.0005).Value, 1e-3, "within the 1 ms slack");
        }

        [TestMethod]
        public void TimeSincePeriapsisOutsideOnePeriodIsFolded()
        {
            Assert.AreEqual(5100, ConicTimes.NextPeriapsis(5000, 0.2, 1000, -100).Value, 1e-9);
            Assert.AreEqual(5900, ConicTimes.NextPeriapsis(5000, 0.2, 1000, 2100).Value, 1e-9);
        }

        [TestMethod]
        public void OpenOrbit()
        {
            Assert.AreEqual(5050, ConicTimes.NextPeriapsis(5000, 1.3, 0, -50).Value, 1e-9);
            Assert.IsNull(ConicTimes.NextPeriapsis(5000, 1.3, 0, 10), "periapsis behind");
            Assert.IsNull(ConicTimes.NextApoapsis(5000, 1.3, 0, -50), "no apoapsis");
        }
    }
}
