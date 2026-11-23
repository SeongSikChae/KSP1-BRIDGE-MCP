using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Ascent.Tests.Readings;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class CircularizeLogicTests
    {
        private static CircularizeLogic Logic(double tolerance = 0.5) => new CircularizeLogic(new CircularizeArgs { ToleranceMps = tolerance });

        [TestMethod]
        public void StartFailures()
        {
            AscentReading r = Orbit(80000, 100);
            Assert.IsNull(Logic().StartFailure(r));
            r.OnSurface = true;
            Assert.IsNotNull(Logic().StartFailure(r));
            r = Orbit(80000, 100);
            r.Eccentricity = 1.1;
            Assert.IsNotNull(Logic().StartFailure(r));
            r = Orbit(80000, 100);
            r.ApA = 90000000;
            Assert.IsNotNull(Logic().StartFailure(r));
            r = Orbit(80000, 100);
            r.ApA = 60000;
            Assert.IsNotNull(Logic().StartFailure(r));
        }

        [TestMethod]
        public void InclinationStartFailure()
        {
            CircularizeLogic logic = new CircularizeLogic(new CircularizeArgs { InclinationDeg = 5 });
            AscentReading r = Orbit(80000, 100);
            r.Latitude = 10;
            Assert.IsNotNull(logic.StartFailure(r));
            r.Latitude = 4;
            Assert.IsNull(logic.StartFailure(r));
            r.Latitude = 5.4;   // inside the 0.5° slack
            Assert.IsNull(logic.StartFailure(r));
        }

        [TestMethod]
        public void InclinationTurnsThePlane()
        {
            // An equatorial orbit moving east (slightly north) 100 m/s short, target inclination 45 at the equator: the circular
            // velocity points along β = 45°, so the burn also turns the plane (D016).
            AscentReading r = Orbit(80000, 100);
            double vc = Math.Sqrt(KerbinGm / (KerbinRadius + 80000));
            r.OrbitVelocity = East * (vc - 100) + North * 10;
            r.TimeToAp = 0;

            CircularizeCommand plain = Logic().Tick(r);
            CircularizeLogic logic = new CircularizeLogic(new CircularizeArgs { InclinationDeg = 45 });
            CircularizeCommand c = logic.Tick(r);

            double s = Math.Sqrt(0.5);
            double de = vc * s - (vc - 100), dn = vc * s - 10;
            Assert.AreEqual(Math.Sqrt(de * de + dn * dn), logic.RemainingDv, 1e-6);
            Assert.AreEqual(LaunchAzimuth.Normalize(Math.Atan2(de, dn) * 180 / Math.PI), c.HeadingDeg, 1e-6);
            Assert.AreEqual(0.0, c.PitchDeg, 1e-6);
            Assert.IsTrue(c.HeadingDeg > 300 && plain.HeadingDeg > 80 && plain.HeadingDeg < 100);
        }

        [TestMethod]
        public void InclinationFallbackKeepsBurning()
        {
            // At latitude 20 an inclination of 10 cannot be flown: due east (spec C3 fallback, D018), no failure.
            AscentReading r = Orbit(80000, 100);
            r.Latitude = 20;
            r.TimeToAp = 0;
            CircularizeLogic logic = new CircularizeLogic(new CircularizeArgs { InclinationDeg = 10 });
            CircularizeCommand c = logic.Tick(r);
            Assert.IsNull(c.Failure);
            Assert.AreEqual(90.0, c.HeadingDeg, 1e-6);
            Assert.AreEqual(100.0, logic.RemainingDv, 1e-6);
        }

        [TestMethod]
        public void WaitsForTheCenteredStart()
        {
            CircularizeLogic logic = Logic();
            AscentReading r = Orbit(80000, 100, ut: 1000);
            r.TimeToAp = 60;
            r.Stages = null;   // 25 m/s² from 250 kN on 10 t
            CircularizeCommand c = logic.Tick(r);
            Assert.AreEqual("wait", logic.Phase);
            Assert.AreEqual(0.0, c.Throttle);
            Assert.IsFalse(c.WantThrust);
            Assert.AreEqual(1000 + 60 - logic.BurnSeconds.Value / 2, logic.StartUt, 1e-9);
            double start = logic.StartUt;
            r.UT = start;
            r.TimeToAp = 60 - (start - 1000);
            logic.Tick(r);
            Assert.AreEqual("burn", logic.Phase);
            Assert.AreEqual(start, logic.BurnStartUt.Value, 1e-9);
        }

        [TestMethod]
        public void BurnsAtOnceWhenGoingDown()
        {
            CircularizeLogic logic = Logic();
            AscentReading r = Orbit(80000, 100);
            r.TimeToAp = 1000;
            r.VerticalSpeed = -5;
            logic.Tick(r);
            Assert.AreEqual("burn", logic.Phase);
        }

        [TestMethod]
        public void GateAndThrottle()
        {
            CircularizeLogic logic = Logic();
            AscentReading r = Orbit(80000, 100);
            r.TimeToAp = 0;
            r.Nose = Up;
            CircularizeCommand off = logic.Tick(r);
            Assert.IsTrue(off.WantThrust, "thrust is wanted while the nose turns (D010)");
            Assert.AreEqual(0.0, off.Throttle);
            r.Nose = East;
            Assert.AreEqual(1.0, logic.Tick(r).Throttle, 1e-9);
            AscentReading near = Orbit(80000, 20);
            near.TimeToAp = 0;
            Assert.AreEqual(20 / (25.0 * 2), Logic().Tick(near).Throttle, 1e-6);
            AscentReading lightless = Orbit(80000, 100);
            lightless.TimeToAp = 0;
            lightless.MaxThrustVac = 0;
            Assert.AreEqual(1.0, Logic().Tick(lightless).Throttle);
        }

        [TestMethod]
        public void DoneWithinTolerance()
        {
            AscentReading r = Orbit(80000, 0.3);
            r.TimeToAp = 0;
            CircularizeCommand c = Logic().Tick(r);
            Assert.IsTrue(c.Done);
            Assert.AreEqual(0.0, c.Throttle);
        }

        [TestMethod]
        public void LocksTheDirectionBelowTenMetresPerSecond()
        {
            CircularizeLogic logic = Logic();
            AscentReading r = Orbit(80000, 8);
            r.TimeToAp = 0;
            CircularizeCommand first = logic.Tick(r);
            r.OrbitVelocity = r.OrbitVelocity + Up * 5;   // the Δv direction would tilt down now
            CircularizeCommand next = logic.Tick(r);
            Assert.AreEqual(first.PitchDeg, next.PitchDeg, 1e-9);
            Assert.AreEqual(first.HeadingDeg, next.HeadingDeg, 1e-9);
        }

        [TestMethod]
        public void DoneWhenItGrowsAgainBelowTen()
        {
            CircularizeLogic logic = Logic();
            Assert.IsFalse(logic.Tick(Burning(5)).Done);
            Assert.IsFalse(logic.Tick(Burning(3)).Done);
            Assert.IsTrue(logic.Tick(Burning(3.1)).Done);
        }

        [TestMethod]
        public void StagingAboveTenIsNotDone()
        {
            CircularizeLogic logic = Logic();
            logic.Tick(Burning(30));
            AscentReading gap = Burning(31);
            gap.Thrust = 0;
            Assert.IsFalse(logic.Tick(gap).Done);
            Assert.IsFalse(logic.Tick(Burning(32)).Done);
            Assert.IsFalse(logic.Tick(Burning(29)).Done);
        }

        [TestMethod]
        public void StagingBelowTenThenShrinkingIsNotDone()
        {
            CircularizeLogic logic = Logic();
            logic.Tick(Burning(9));
            AscentReading gap = Burning(9.5);
            gap.Thrust = 0;
            Assert.IsFalse(logic.Tick(gap).Done, "no thrust: the minimum is dropped");
            Assert.IsFalse(logic.Tick(Burning(12)).Done, "above 10 m/s");
            Assert.IsFalse(logic.Tick(Burning(9.8)).Done, "shrinking again after the relight");
            Assert.IsFalse(logic.Tick(Burning(9.5)).Done);
            Assert.IsTrue(logic.Tick(Burning(9.6)).Done);
        }

        [TestMethod]
        public void EnginesExhausted()
        {
            AscentReading r = Burning(50);
            r.Engines = new List<EngineInfo> { Spent(2, 1) };
            r.NextStage = 1;
            StringAssert.StartsWith(Logic().Tick(r).Failure, "engines exhausted");
            AscentReading waiting = Orbit(80000, 50);
            waiting.TimeToAp = 600;
            waiting.Engines = r.Engines;
            waiting.NextStage = 1;
            Assert.IsNull(Logic().Tick(waiting).Failure, "no thrust wanted while waiting");
        }

        [TestMethod]
        public void Progress()
        {
            CircularizeLogic logic = Logic();
            logic.Tick(Burning(50));
            Dictionary<string, object> p = logic.Progress(1);
            foreach (string key in new[] { "phase", "start_ut", "burn_time_s", "remaining_dv_mps", "nose_error_deg", "throttle", "stages" })
                Assert.IsTrue(p.ContainsKey(key), key);
            Assert.AreEqual(50.0, (double)p["remaining_dv_mps"], 1e-6);
        }

        [TestMethod]
        public void ApoapsisDeltaVFeedsTheEstimate()
        {
            CircularizeLogic logic = Logic();
            AscentReading r = Orbit(80000, 100);
            r.ApA = 80000;
            r.SemiMajorAxis = KerbinRadius + 40000;
            r.TimeToAp = 600;
            r.Stages = null;
            logic.Tick(r);
            double rAp = KerbinRadius + 80000;
            double dv = Math.Sqrt(KerbinGm / rAp) - Math.Sqrt(KerbinGm * (2 / rAp - 1 / r.SemiMajorAxis));
            Assert.AreEqual(dv / 25.0, logic.BurnSeconds.Value, 1e-6);
        }

        private static AscentReading Burning(double left)
        {
            AscentReading r = Orbit(80000, left);
            r.TimeToAp = 0;
            return r;
        }
    }
}
