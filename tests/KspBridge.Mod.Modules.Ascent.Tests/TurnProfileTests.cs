using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class TurnProfileTests
    {
        private static TurnProfile Kerbin(double start = 70, double target = 80000)
            => new TurnProfile(start, target, Readings.KerbinAtmosphere, null, null);

        [TestMethod]
        public void Defaults()
        {
            TurnProfile p = Kerbin();
            Assert.AreEqual(570.0, p.Start);
            Assert.AreEqual(45500.0, p.End, 1e-9);
            TurnProfile airless = new TurnProfile(1000, 15000, null, null, null);
            Assert.AreEqual(1050.0, airless.Start);
            Assert.AreEqual(1050 + (15000 - 1050) * 0.1, airless.End, 1e-9);
            TurnProfile given = new TurnProfile(70, 80000, 70000, 1000, 30000);
            Assert.AreEqual(1070.0, given.Start);
            Assert.AreEqual(30000.0, given.End);
        }

        [TestMethod]
        public void PitchUsesTheSmallerShare()
        {
            TurnProfile p = Kerbin();
            Assert.AreEqual(90.0, p.Pitch(100, 100), 1e-9);
            Assert.AreEqual(0.0, p.Pitch(50000, 75000), 1e-9);
            double f = (20000 - 570) / (45500 - 570.0);
            Assert.AreEqual(90 - 90 * Math.Pow(f, 0.6), p.Pitch(20000, 70000), 1e-9);
            // The apoapsis still low: less turned than the altitude alone would give (the overheating case of D002).
            double apShare = (41000 - 570) / (70000 - 570.0);
            Assert.AreEqual(90 - 90 * Math.Pow(apShare, 0.6), p.Pitch(40000, 41000), 1e-9);
            Assert.IsTrue(p.Pitch(40000, 41000) > p.Pitch(40000, 70000));
        }

        [TestMethod]
        public void PitchWithoutAirUsesAltitudeOnly()
        {
            TurnProfile p = new TurnProfile(1000, 15000, null, null, null);
            Assert.AreEqual(0.0, p.Pitch(p.End, 1100), 1e-9);
        }

        [TestMethod]
        public void Sag()
        {
            TurnProfile p = Kerbin();
            Assert.AreEqual(0.0, p.Sag(false, 60000, 5, -10));   // apoapsis still in the atmosphere
            Assert.AreEqual(0.0, p.Sag(true, 75000, 5, -10));    // already reached
            Assert.AreEqual(0.0, p.Sag(false, 75000, 30, 10));
            Assert.AreEqual(10.0, p.Sag(false, 75000, 15, 10), 1e-9);
            Assert.AreEqual(20.0, p.Sag(false, 75000, 0, 10), 1e-9);
            Assert.AreEqual(20.0, p.Sag(false, 75000, 100, -1), 1e-9);
        }

        [TestMethod]
        public void StartFailures()
        {
            Assert.IsNull(Kerbin().StartFailure(600000, 84159286));
            Assert.IsNotNull(Kerbin(target: 60000).StartFailure(600000, 84159286));
            Assert.IsNotNull(Kerbin(target: 70000).StartFailure(600000, 84159286));
            Assert.IsNotNull(Kerbin(target: 90000000).StartFailure(600000, 84159286));
            Assert.IsNotNull(new TurnProfile(70, 80000, 70000, null, 500).StartFailure(600000, 84159286));
        }
    }
}
