using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Ascent.Tests.Readings;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class AscendLogicTests
    {
        private static AscendArgs Args(double target = 80000) => new AscendArgs { ApoapsisM = target, Heading = 90 };

        private static AscendLogic Logic(AscendArgs args = null) => new AscendLogic(args ?? Args(), Kerbin(70, 70));

        [TestMethod]
        public void ThrottleFromTheApoapsis()
        {
            Assert.AreEqual(1.0, Logic().Tick(Kerbin(1000, 20000)).Throttle, 1e-9);
            Assert.AreEqual(0.5, Logic().Tick(Kerbin(1000, 78000)).Throttle, 1e-9);
            Assert.AreEqual(0.05, Logic().Tick(Kerbin(1000, 79999)).Throttle, 1e-9);
            AscendCommand at = Logic().Tick(Kerbin(1000, 80000));
            Assert.AreEqual(0.0, at.Throttle);
            Assert.IsFalse(at.WantThrust);
        }

        [TestMethod]
        public void ThrottleFromTheDynamicPressure()
        {
            AscentReading r = Kerbin(5000, 20000);
            r.DynamicPressureKpa = 16;
            Assert.AreEqual(1.0, Logic().Tick(r).Throttle, 1e-9);
            r.DynamicPressureKpa = 18;
            Assert.AreEqual(0.5, Logic().Tick(r).Throttle, 1e-9);
            r.DynamicPressureKpa = 25;
            AscendCommand c = Logic().Tick(r);
            Assert.AreEqual(0.0, c.Throttle);
            Assert.IsTrue(c.WantThrust, "thrust is wanted before the pressure limit");
            r.AtmosphereTop = null;
            Assert.AreEqual(1.0, Logic().Tick(r).Throttle, 1e-9, "no pressure limit without air");
        }

        [TestMethod]
        public void ReachThenRefillInsideAndOutsideTheAtmosphere()
        {
            AscendLogic logic = Logic();
            Assert.AreEqual("climb", Tick(logic, Kerbin(30000, 70000)).phase);
            Assert.AreEqual("coast", Tick(logic, Kerbin(40000, 80100)).phase);
            (string phase, double throttle) coast = Tick(logic, Kerbin(45000, 79500));
            Assert.AreEqual("coast", coast.phase);
            Assert.AreEqual(0.0, coast.throttle);
            (string phase, double throttle) refill = Tick(logic, Kerbin(50000, 79100));
            Assert.AreEqual("climb", refill.phase);
            Assert.IsTrue(refill.throttle > 0);
            Assert.AreEqual("climb", Tick(logic, Kerbin(55000, 79500)).phase, "refilling goes on up to the target");
            Assert.AreEqual("coast", Tick(logic, Kerbin(56000, 80000)).phase);
            AscentReading above = Kerbin(71000, 79000);
            above.Thrust = 0;
            AscendCommand outside = logic.Tick(above);
            Assert.IsTrue(outside.WantThrust, "refills above the atmosphere too");
            Assert.IsFalse(outside.Done);
        }

        [TestMethod]
        public void Completion()
        {
            AscendLogic logic = Logic();
            logic.Tick(Kerbin(40000, 80000));
            AscentReading r = Kerbin(70500, 79500);
            r.Thrust = 0;
            AscendCommand done = logic.Tick(r);
            Assert.IsTrue(done.Done);
            Assert.AreEqual(0.0, done.Throttle);

            AscendLogic burning = Logic();
            burning.Tick(Kerbin(40000, 80000));
            AscentReading srb = Kerbin(70500, 80500);
            srb.Thrust = 50;
            Assert.IsFalse(burning.Tick(srb).Done, "a solid booster still burning");

            AscendLogic inside = Logic();
            inside.Tick(Kerbin(40000, 80000));
            AscentReading air = Kerbin(69000, 80000);
            air.Thrust = 0;
            Assert.IsFalse(inside.Tick(air).Done);

            AscendLogic open = Logic();
            AscentReading escape = Kerbin(80000, -1);
            escape.Eccentricity = 1.2;
            escape.Thrust = 0;
            Assert.IsTrue(open.Tick(escape).Done, "an open orbit counts as reached");
        }

        [TestMethod]
        public void CoastInTheAtmosphereFollowsTheSurfaceVelocity()
        {
            AscendLogic logic = Logic();
            AscentReading r = Kerbin(40000, 81000);
            r.SurfaceVelocity = AoaLimit.Direction(20, 90, Up, North, East) * 1500;
            AscendCommand c = logic.Tick(r);
            Assert.AreEqual(20.0, c.PitchDeg, 1e-6);
            Assert.AreEqual(90.0, c.HeadingDeg, 1e-6);
        }

        [TestMethod]
        public void StartsStraightUp()
        {
            AscendCommand c = Logic().Tick(Kerbin(100, 100));
            Assert.AreEqual(90.0, c.PitchDeg, 1e-6);
        }

        [TestMethod]
        public void StartFailures()
        {
            AscentReading pad = Kerbin(70, 70);
            Assert.IsNull(Logic().StartFailure(pad));
            Assert.IsNotNull(new AscendLogic(Args(60000), pad).StartFailure(pad));
            pad.Latitude = 30;
            AscendArgs low = new AscendArgs { ApoapsisM = 80000, InclinationDeg = 10 };
            Assert.IsNotNull(new AscendLogic(low, pad).StartFailure(pad));
        }

        [TestMethod]
        public void ControlLossFails()
        {
            AscendLogic logic = Logic();
            AscentReading r = Kerbin(5000, 6000, 10);
            r.DynamicPressureKpa = 8;
            r.SurfaceVelocity = Up * 250;
            r.Nose = East;
            Assert.IsNull(logic.Tick(r).Failure);
            r.UT = 13;
            StringAssert.StartsWith(logic.Tick(r).Failure, "control lost");
        }

        [TestMethod]
        public void ControlLossUnderThrustBelowOneKpa()
        {
            // D017: tumbling near the pad under thrust, slowed below 1 kPa (game check of the first verification).
            AscendLogic logic = Logic();
            AscentReading r = Kerbin(130, 130, 1.5);
            r.DynamicPressureKpa = 0.3;
            r.SurfaceVelocity = Up * 20;
            r.Nose = -Up;
            r.Thrust = 198;
            Assert.IsNull(logic.Tick(r).Failure);
            r.UT = 4.5;
            StringAssert.StartsWith(logic.Tick(r).Failure, "control lost");

            // Without thrust below 1 kPa it does not count.
            logic = Logic();
            r = Kerbin(130, 130, 1.5);
            r.DynamicPressureKpa = 0.3;
            r.SurfaceVelocity = Up * 20;
            r.Nose = -Up;
            r.Thrust = 0;
            Assert.IsNull(logic.Tick(r).Failure);
            r.UT = 10;
            Assert.IsNull(logic.Tick(r).Failure);
        }

        [TestMethod]
        public void EnginesExhaustedFails()
        {
            AscentReading r = Kerbin(20000, 30000);
            r.Engines = new List<EngineInfo> { Spent(2, 1) };
            r.NextStage = 1;
            StringAssert.StartsWith(Logic().Tick(r).Failure, "engines exhausted");
            r.Engines = new List<EngineInfo> { Spent(3, 2), Idle(2, 1) };
            r.NextStage = 2;
            AscendCommand c = Logic().Tick(r);
            Assert.IsNull(c.Failure);
            Assert.AreEqual(StageReason.NoRunningEngine, c.Stage);
        }

        [TestMethod]
        public void InclinationSteersTheHeading()
        {
            AscendLogic logic = new AscendLogic(new AscendArgs { ApoapsisM = 80000, InclinationDeg = 90 }, Kerbin(70, 70));
            AscentReading r = Kerbin(30000, 40000);
            r.SurfaceVelocity = new Vec(0, 0, 0);
            AscendCommand c = logic.Tick(r);
            Assert.IsTrue(c.HeadingDeg > 350, "north, leaning west against the rotation: " + c.HeadingDeg);
        }

        [TestMethod]
        public void RecordsAndProgress()
        {
            AscendLogic logic = Logic();
            AscentReading r = Kerbin(5000, 6000);
            r.DynamicPressureKpa = 12;
            r.SurfaceVelocity = AoaLimit.Direction(80, 90, Up, North, East) * 300;
            logic.Tick(r);
            Assert.AreEqual(12.0, logic.MaxQKpa);
            Assert.AreEqual(10.0, logic.MaxAoaDeg.Value, 1e-6);
            Dictionary<string, object> p = logic.Progress(2);
            foreach (string key in new[] { "phase", "target_apoapsis_m", "apoapsis_m", "pitch_deg", "heading_deg", "nose_error_deg", "throttle", "q_kpa", "aoa_deg", "stages" })
                Assert.IsTrue(p.ContainsKey(key), key);
            Assert.AreEqual(2, p["stages"]);
        }

        private static (string phase, double throttle) Tick(AscendLogic logic, AscentReading r)
        {
            AscendCommand c = logic.Tick(r);
            return (logic.Phase, c.Throttle);
        }
    }
}
