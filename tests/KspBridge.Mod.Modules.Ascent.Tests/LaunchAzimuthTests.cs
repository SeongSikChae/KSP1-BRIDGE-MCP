using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class LaunchAzimuthTests
    {
        [TestMethod]
        public void InertialAtTheEquator()
        {
            Assert.AreEqual(90.0, LaunchAzimuth.Inertial(0, 0, true), 1e-9);
            Assert.AreEqual(90.0, LaunchAzimuth.Inertial(0, 0, false), 1e-9);
            Assert.AreEqual(45.0, LaunchAzimuth.Inertial(45, 0, true), 1e-9);
            Assert.AreEqual(135.0, LaunchAzimuth.Inertial(45, 0, false), 1e-9);
            Assert.AreEqual(0.0, LaunchAzimuth.Inertial(90, 0, true), 1e-9);
            Assert.AreEqual(180.0, LaunchAzimuth.Inertial(90, 0, false), 1e-9);
            Assert.AreEqual(315.0, LaunchAzimuth.Inertial(135, 0, true), 1e-9);
            Assert.AreEqual(225.0, LaunchAzimuth.Inertial(135, 0, false), 1e-9);
            Assert.AreEqual(270.0, LaunchAzimuth.Inertial(180, 0, true), 1e-9);
        }

        [TestMethod]
        public void InertialOffTheEquator()
        {
            double expected = Math.Asin(Math.Cos(45 * Math.PI / 180) / Math.Cos(30 * Math.PI / 180)) * 180 / Math.PI;
            Assert.AreEqual(expected, LaunchAzimuth.Inertial(45, 30, true), 1e-9);
            // Inside the slack the nearest reachable inclination is flown: due east.
            Assert.AreEqual(90.0, LaunchAzimuth.Inertial(0, -0.0972, true), 1e-9);
        }

        [TestMethod]
        public void FallbackWithoutASolution()
        {
            // spec C3 fallback (D018): below |latitude| due east, above 180° − |latitude| due west, for either launch.
            Assert.AreEqual(90.0, LaunchAzimuth.Inertial(0, -0.1, true), 1e-9);
            Assert.AreEqual(90.0, LaunchAzimuth.Inertial(0, -0.1, false), 1e-9);
            Assert.AreEqual(270.0, LaunchAzimuth.Inertial(179.95, -0.1, true), 1e-9);
            Assert.AreEqual(270.0, LaunchAzimuth.Inertial(179.95, -0.1, false), 1e-9);
            Assert.AreEqual(90.0, LaunchAzimuth.Nearest(10, 20, 100, 2000), 1e-9);
            Assert.AreEqual(270.0, LaunchAzimuth.Nearest(170, 20, 100, -2000), 1e-9);
        }

        [TestMethod]
        public void NearestToTheCurrentVelocity()
        {
            // Inclination 45 at the equator: 45° (north) and 135° (south).
            Assert.AreEqual(45.0, LaunchAzimuth.Nearest(45, 0, 1600, 1600), 1e-9);
            Assert.AreEqual(135.0, LaunchAzimuth.Nearest(45, 0, -1600, 1600), 1e-9);
            Assert.AreEqual(45.0, LaunchAzimuth.Nearest(45, 0, 10, 2000), 1e-9);
            Assert.AreEqual(135.0, LaunchAzimuth.Nearest(45, 0, -10, 2000), 1e-9);
            // A tie or no horizontal velocity: the north one.
            Assert.AreEqual(45.0, LaunchAzimuth.Nearest(45, 0, 0, 2000), 1e-9);
            Assert.AreEqual(45.0, LaunchAzimuth.Nearest(45, 0, 0, 0), 1e-9);
            // Retrograde 135 at the equator: 315° (north) and 225° (south).
            Assert.AreEqual(315.0, LaunchAzimuth.Nearest(135, 0, 1500, -1500), 1e-9);
            Assert.AreEqual(225.0, LaunchAzimuth.Nearest(135, 0, -1500, -1500), 1e-9);
        }

        [TestMethod]
        public void HeadingTakesOutTheSpeedAlreadyThere()
        {
            // Polar from the pad: the rotation's 175 m/s east is taken out, so the heading leans west of north.
            double h = LaunchAzimuth.Heading(90, 0, true, 0, 175, 2280);
            Assert.AreEqual(LaunchAzimuth.Normalize(Math.Atan2(-175, 2280) * 180 / Math.PI), h, 1e-9);
            Assert.IsTrue(h > 350 && h < 360);
            // East at the equator: nothing to correct.
            Assert.AreEqual(90.0, LaunchAzimuth.Heading(0, 0, true, 0, 175, 2280), 1e-9);
            // What is left is below 5% of the target speed: the inertial heading.
            Assert.AreEqual(45.0, LaunchAzimuth.Heading(45, 0, true, 1550, 1600, 2280), 1e-9);
        }

        [TestMethod]
        public void StartFailure()
        {
            Assert.IsNull(LaunchAzimuth.StartFailure(0, -0.0972));
            Assert.IsNull(LaunchAzimuth.StartFailure(9.6, 10));
            Assert.IsNotNull(LaunchAzimuth.StartFailure(5, 10));
            Assert.IsNull(LaunchAzimuth.StartFailure(170.4, 10));
            Assert.IsNotNull(LaunchAzimuth.StartFailure(175, -10));
            Assert.IsNull(LaunchAzimuth.StartFailure(90, 80));
        }
    }
}
