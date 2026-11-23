using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Ascent.Tests.Readings;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    /// <summary>The shared burn rule of circularize and execute_node, fed a delta-v vector directly.</summary>
    [TestClass]
    public sealed class BurnControlTests
    {
        private static BurnControl Control(double tolerance = 0.5, bool autoStage = true) => new BurnControl(tolerance, autoStage);

        private static AscentReading Reading() => Orbit(80000, 0);

        [TestMethod]
        public void PointsAlongTheDeltaV()
        {
            BurnCommand c = Control().Tick(North * 50, true, Reading());
            Assert.AreEqual(0.0, c.PitchDeg, 1e-9);
            Assert.AreEqual(0.0, c.HeadingDeg, 1e-9);
            c = Control().Tick(Up * 50, true, Reading());
            Assert.AreEqual(90.0, c.PitchDeg, 1e-9);
        }

        [TestMethod]
        public void WaitingTurnsButNeitherThrustsNorLocksNorCompletes()
        {
            BurnControl control = Control();
            AscentReading r = Reading();
            BurnCommand c = control.Tick(East * 0.1, false, r);
            Assert.IsFalse(c.Done, "below the tolerance but not burning yet");
            Assert.IsFalse(c.WantThrust);
            Assert.AreEqual(0.0, c.Throttle);
            c = control.Tick(North * 5, false, r);
            Assert.AreEqual(0.0, c.HeadingDeg, 1e-9, "not locked while waiting");
        }

        [TestMethod]
        public void LocksBelowTenMetresPerSecond()
        {
            BurnControl control = Control();
            AscentReading r = Reading();
            BurnCommand first = control.Tick(East * 8, true, r);
            BurnCommand next = control.Tick(North * 7, true, r);
            Assert.AreEqual(first.HeadingDeg, next.HeadingDeg, 1e-9);
            Assert.AreEqual(90.0, next.HeadingDeg, 1e-9);
        }

        [TestMethod]
        public void GateAndThrottle()
        {
            AscentReading r = Reading();
            r.Nose = Up;
            BurnCommand off = Control().Tick(East * 100, true, r);
            Assert.IsTrue(off.WantThrust);
            Assert.AreEqual(0.0, off.Throttle, "more than 5° off");
            r.Nose = East;
            Assert.AreEqual(1.0, Control().Tick(East * 100, true, r).Throttle, 1e-9);
            Assert.AreEqual(20 / (25.0 * 2), Control().Tick(East * 20, true, r).Throttle, 1e-9);
            Assert.AreEqual(BurnControl.MinThrottle, Control().Tick(East * 1, true, r).Throttle, 1e-9);
            r.MaxThrustVac = 0;
            Assert.AreEqual(1.0, Control().Tick(East * 20, true, r).Throttle, "no running engine: full throttle");
        }

        [TestMethod]
        public void DoneAtTheTolerance()
        {
            BurnCommand c = Control(1).Tick(East * 0.9, true, Reading());
            Assert.IsTrue(c.Done);
            Assert.AreEqual(0.0, c.Throttle);
            Assert.IsFalse(Control(0.5).Tick(East * 0.9, true, Reading()).Done);
        }

        [TestMethod]
        public void DoneWhenItGrowsAgainWhileThrusting()
        {
            BurnControl control = Control();
            AscentReading r = Reading();
            Assert.IsFalse(control.Tick(East * 5, true, r).Done);
            Assert.IsFalse(control.Tick(East * 3, true, r).Done);
            Assert.IsFalse(control.Tick(East * 3.04, true, r).Done, "within 0.05 m/s");
            Assert.IsTrue(control.Tick(East * 3.1, true, r).Done);
        }

        [TestMethod]
        public void AFrameWithoutThrustDropsTheMinimum()
        {
            BurnControl control = Control();
            AscentReading r = Reading();
            control.Tick(East * 3, true, r);
            AscentReading gap = Reading();
            gap.Thrust = 0;
            Assert.IsFalse(control.Tick(East * 4, true, gap).Done);
            Assert.IsFalse(control.Tick(East * 4.5, true, r).Done, "a new minimum starts after the gap");
            Assert.IsTrue(control.Tick(East * 4.6, true, r).Done);
        }

        [TestMethod]
        public void EnginesExhaustedOnlyWhileThrustIsWanted()
        {
            AscentReading r = Reading();
            r.Engines = new List<EngineInfo> { Spent(2, 1) };
            r.NextStage = 1;
            StringAssert.StartsWith(Control().Tick(East * 50, true, r).Failure, "engines exhausted");
            Assert.IsNull(Control().Tick(East * 50, false, r).Failure);
        }

        [TestMethod]
        public void StagingReasonWhileBurning()
        {
            AscentReading r = Reading();
            r.Engines = new List<EngineInfo> { Spent(2, 1), Idle(1, -1) };
            r.NextStage = 1;
            Assert.AreEqual(StageReason.NoRunningEngine, Control().Tick(East * 50, true, r).Stage);
            Assert.AreEqual(StageReason.None, Control(0.5, false).Tick(East * 50, true, r).Stage, "auto_stage false");
            Assert.AreEqual(StageReason.None, Control().Tick(East * 50, false, r).Stage, "waiting");
        }

        [TestMethod]
        public void NoseErrorAndLast()
        {
            BurnControl control = Control();
            Assert.IsNull(control.Last);
            AscentReading r = Reading();
            r.Nose = North;
            control.Tick(East * 50, true, r);
            Assert.AreEqual(90.0, control.NoseError, 1e-9);
            Assert.IsNotNull(control.Last);
        }
    }
}
