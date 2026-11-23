using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class AoaLimitTests
    {
        [TestMethod]
        public void LimitWidensAtLowPressure()
        {
            Assert.IsNull(AoaLimit.LimitDeg(5, 1));
            Assert.IsNull(AoaLimit.LimitDeg(5, 0));
            Assert.AreEqual(25.0, AoaLimit.LimitDeg(5, 2).Value, 1e-9);
            Assert.AreEqual(5.0, AoaLimit.LimitDeg(5, 10).Value, 1e-9);
            Assert.AreEqual(5.0, AoaLimit.LimitDeg(5, 20).Value, 1e-9);
        }

        [TestMethod]
        public void PullsTowardTheSurfaceVelocity()
        {
            Vec velocity = AoaLimit.Direction(60, 90, Readings.Up, Readings.North, Readings.East) * 300;
            Vec target = AoaLimit.Direction(30, 90, Readings.Up, Readings.North, Readings.East);
            Vec limited = AoaLimit.Limit(target, velocity, 20, 5);
            Assert.AreEqual(5.0, AoaLimit.AngleDeg(limited, velocity), 1e-6);
            AoaLimit.PitchHeading(limited, Readings.Up, Readings.North, Readings.East, 0, out double pitch, out double heading);
            Assert.AreEqual(55.0, pitch, 1e-6);
            Assert.AreEqual(90.0, heading, 1e-6);
            // Inside the limit, no limit at low pressure, or too slow: unchanged.
            Assert.AreEqual(0.0, AoaLimit.AngleDeg(AoaLimit.Limit(target, velocity, 2, 30), target), 1e-6);
            Assert.AreEqual(0.0, AoaLimit.AngleDeg(AoaLimit.Limit(target, velocity, 0.5, 5), target), 1e-6);
            Assert.AreEqual(0.0, AoaLimit.AngleDeg(AoaLimit.Limit(target, velocity * 0.001, 20, 5), target), 1e-6);
        }

        [TestMethod]
        public void PitchHeadingRoundTrip()
        {
            foreach (double heading in new[] { 0.0, 45, 90, 200, 359 })
            {
                foreach (double pitch in new[] { -45.0, 0, 30, 89 })
                {
                    Vec d = AoaLimit.Direction(pitch, heading, Readings.Up, Readings.North, Readings.East);
                    AoaLimit.PitchHeading(d, Readings.Up, Readings.North, Readings.East, -1, out double p, out double h);
                    Assert.AreEqual(pitch, p, 1e-6);
                    Assert.AreEqual(heading, h, 1e-6);
                }
            }
            AoaLimit.PitchHeading(Readings.Up, Readings.Up, Readings.North, Readings.East, 77, out double up, out double fallback);
            Assert.AreEqual(90.0, up, 1e-9);
            Assert.AreEqual(77.0, fallback);
        }
    }
}
