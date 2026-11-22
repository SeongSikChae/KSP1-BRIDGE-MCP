using KspBridge.Mod.Modules.Warp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Warp.Tests
{
    [TestClass]
    public sealed class WarpRuleTests
    {
        private static readonly float[] Rails = { 1, 5, 10, 50, 100, 1000, 10000, 100000 };
        private static readonly float[] Physics = { 1, 2, 3, 4 };

        [TestMethod]
        [DataRow(-10, 0)]
        [DataRow(0, 0)]
        [DataRow(2.999, 0)]
        [DataRow(3, 1)]
        [DataRow(3.999, 1)]
        [DataRow(4, 2)]
        [DataRow(12, 3)]
        [DataRow(22, 4)]
        [DataRow(201.9, 4)]
        [DataRow(202, 5)]
        [DataRow(2002, 6)]
        [DataRow(20001.9, 6)]
        [DataRow(20002, 7)]
        [DataRow(1e9, 7)]
        public void Rails_ByTheTimeLeft(double remaining, int index)
            => Assert.AreEqual(index, WarpRule.RailsWanted(remaining, null, Rails, null));

        [TestMethod]
        [DataRow(1, 0)]
        [DataRow(4.9, 0)]
        [DataRow(5, 1)]
        [DataRow(30, 2)]
        [DataRow(50, 3)]
        [DataRow(99999, 6)]
        [DataRow(1e7, 7)]
        public void Rails_HighestNotAboveMaxRate(double maxRate, int index)
            => Assert.AreEqual(index, WarpRule.RailsWanted(1e9, null, Rails, maxRate));

        [TestMethod]
        [DataRow(-5, 3)]
        [DataRow(0, 3)]
        [DataRow(144.9, 3)]
        [DataRow(145, 4)]
        [DataRow(300, 4)]
        [DataRow(774.9, 4)]
        [DataRow(775, 5)]
        [DataRow(7075, 6)]
        [DataRow(70074.9, 6)]
        [DataRow(70075, 7)]
        public void Rails_BeforeASphereOfInfluence(double soi, int index)
            => Assert.AreEqual(index, WarpRule.RailsWanted(1e6, soi, Rails, null));

        [TestMethod]
        [DataRow(0.59, 4, 0)]
        [DataRow(0.601, 4, 1)]
        [DataRow(0.8, 4, 2)]
        [DataRow(1, 4, 3)]
        [DataRow(100, 4, 3)]
        [DataRow(100, 3, 2)]
        [DataRow(100, 2, 1)]
        public void Physics_ByTheTimeLeftAndTheLimit(double remaining, int physicsRate, int index)
            => Assert.AreEqual(index, WarpRule.PhysicsWanted(remaining, Physics, physicsRate));

        [TestMethod]
        public void Physics_NoneWithoutPhysicsRate() => Assert.AreEqual(0, WarpRule.PhysicsWanted(100, Physics, null));

        private static WarpReading Now(bool rails, int index, double rate, bool settled = true)
            => new WarpReading { Rails = rails, Index = index, Rate = rate, Settled = settled };

        private static void Is(WarpCommand c, int index, bool instant, bool switchMode = false, bool rails = false)
        {
            Assert.IsTrue(c.Change);
            Assert.AreEqual(index, c.Index);
            Assert.AreEqual(instant, c.Instant);
            Assert.AreEqual(switchMode, c.SwitchMode);
            if (switchMode)
                Assert.AreEqual(rails, c.Rails);
        }

        [TestMethod]
        public void Decide_DownToOne_AtOnce()
        {
            Is(WarpRule.Decide(Now(true, 5, 1000), true, 0, Rails), 0, instant: true);
            Is(WarpRule.Decide(Now(true, 0, 37, settled: false), true, 0, Rails), 0, instant: true);
            Is(WarpRule.Decide(Now(true, 1, 1, settled: false), true, 0, Rails), 0, instant: true);
            Assert.IsFalse(WarpRule.Decide(Now(true, 0, 1), true, 0, Rails).Change);
            Assert.IsFalse(WarpRule.Decide(Now(false, 0, 1), true, 0, Rails).Change);
        }

        [TestMethod]
        public void Decide_DownAtOnce_UpThroughTheGame()
        {
            Is(WarpRule.Decide(Now(true, 6, 10000), true, 4, Rails), 4, instant: true);
            Is(WarpRule.Decide(Now(true, 2, 10), true, 4, Rails), 4, instant: false);
            Assert.IsFalse(WarpRule.Decide(Now(true, 4, 100), true, 4, Rails).Change);
        }

        [TestMethod]
        public void Decide_WhileTheGameChangesTheRate()
        {
            Assert.IsFalse(WarpRule.Decide(Now(true, 2, 7, settled: false), true, 4, Rails).Change, "not raised again");
            Assert.IsFalse(WarpRule.Decide(Now(true, 4, 60, settled: false), true, 4, Rails).Change, "on its way up");
            Is(WarpRule.Decide(Now(true, 4, 5000, settled: false), true, 4, Rails), 4, instant: true);
        }

        [TestMethod]
        public void Decide_ChangesTheMode()
        {
            Is(WarpRule.Decide(Now(true, 0, 1), false, 3, Physics), 3, instant: false, switchMode: true, rails: false);
            Is(WarpRule.Decide(Now(true, 5, 1000), false, 2, Physics), 2, instant: false, switchMode: true, rails: false);
            Is(WarpRule.Decide(Now(false, 3, 4), true, 7, Rails), 7, instant: false, switchMode: true, rails: true);
        }
    }
}
