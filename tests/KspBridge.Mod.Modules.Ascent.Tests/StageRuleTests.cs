using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using static KspBridge.Mod.Modules.Ascent.Tests.Readings;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    /// <summary>
    /// The stock Kerbal X (Ships/VAB/Kerbal X.craft): the Mainsail and six LV-T45 light in stage 7, the LV-T45 pairs are dropped by
    /// stages 6, 5 and 4, the Mainsail by 3, the Poodle lights in 2 and is dropped by 1; 0 is the parachute.
    /// </summary>
    [TestClass]
    public sealed class StageRuleTests
    {
        private static StageReason Judge(int? next, List<EngineInfo> engines, bool want = true, bool auto = true)
            => StageRule.Judge(want, auto, next, engines);

        private static List<EngineInfo> Pad() => new List<EngineInfo>
        {
            Idle(7, 3), Idle(7, 6), Idle(7, 6), Idle(7, 5), Idle(7, 5), Idle(7, 4), Idle(7, 4), Idle(2, 1),
        };

        [TestMethod]
        public void KerbalXSequence()
        {
            Assert.AreEqual(StageReason.NoRunningEngine, Judge(7, Pad()));

            List<EngineInfo> lit = new List<EngineInfo>
            {
                Running(7, 3), Running(7, 6), Running(7, 6), Running(7, 5), Running(7, 5), Running(7, 4), Running(7, 4), Idle(2, 1),
            };
            Assert.AreEqual(StageReason.None, Judge(6, lit));

            lit[1] = Spent(7, 6);
            Assert.AreEqual(StageReason.None, Judge(6, lit), "one booster of the pair still runs");
            lit[2] = Spent(7, 6);
            Assert.AreEqual(StageReason.BoostersOut, Judge(6, lit));

            List<EngineInfo> core = new List<EngineInfo> { Running(7, 3), Idle(2, 1) };
            Assert.AreEqual(StageReason.None, Judge(3, core));
            core[0] = Spent(7, 3);
            Assert.AreEqual(StageReason.NoRunningEngine, Judge(3, core), "a spent core is no_running_engine, not boosters_out (D015)");

            List<EngineInfo> upper = new List<EngineInfo> { Idle(2, 1) };
            Assert.AreEqual(StageReason.NoRunningEngine, Judge(2, upper));

            upper[0] = Running(2, 1);
            Assert.AreEqual(StageReason.None, Judge(1, upper), "stage 1 would drop the running Poodle");
            upper[0] = Spent(2, 1);
            Assert.AreEqual(StageReason.None, Judge(1, upper), "nothing usable after stage 1: the capsule separation is left alone");
            Assert.IsTrue(StageRule.Exhausted(true, true, 1, upper));

            Assert.AreEqual(StageReason.None, Judge(0, new List<EngineInfo>()));
            Assert.IsTrue(StageRule.Exhausted(true, true, 0, new List<EngineInfo>()));
        }

        [TestMethod]
        public void NothingWithoutWantOrAutoStage()
        {
            Assert.AreEqual(StageReason.None, Judge(7, Pad(), want: false));
            Assert.IsFalse(StageRule.Exhausted(false, true, 7, Pad()));
            Assert.AreEqual(StageReason.None, Judge(7, Pad(), auto: false));
            Assert.IsTrue(StageRule.Exhausted(true, false, 7, Pad()));
            Assert.AreEqual(StageReason.None, Judge(null, Pad()));
            Assert.IsTrue(StageRule.Exhausted(true, true, null, Pad()));
            Assert.IsFalse(StageRule.Exhausted(true, true, 7, Pad()));
        }

        [TestMethod]
        public void UsableEngines()
        {
            Assert.IsTrue(StageRule.Usable(Idle(2, 1), 3));
            Assert.IsTrue(StageRule.Usable(Running(5, -1), 3));
            Assert.IsFalse(StageRule.Usable(Idle(4, 1), 3), "its stage is past: a shut down engine does not light by staging");
            Assert.IsFalse(StageRule.Usable(Idle(2, 2), 3), "dropped by the stage that lights it");
            Assert.IsFalse(StageRule.Usable(Idle(2, 3), 3), "dropped by the next stage");
            Assert.IsFalse(StageRule.Usable(Spent(5, -1), 3));
            EngineInfo dry = Idle(2, 1);
            dry.Propellant = false;
            Assert.IsFalse(StageRule.Usable(dry, 3));
            EngineInfo off = Idle(2, 1);
            off.StagingOn = false;
            Assert.IsFalse(StageRule.Usable(off, 3));
        }

        [TestMethod]
        public void UnignitedEngineInTheDroppedGroupBlocksBoostersOut()
        {
            List<EngineInfo> engines = new List<EngineInfo> { Running(5, -1), Spent(5, 4), Idle(3, 4) };
            Assert.AreEqual(StageReason.None, Judge(4, engines));
        }

        [TestMethod]
        public void Names()
        {
            Assert.AreEqual("boosters_out", StageRule.Name(StageReason.BoostersOut));
            Assert.AreEqual("no_running_engine", StageRule.Name(StageReason.NoRunningEngine));
            Assert.IsNull(StageRule.Name(StageReason.None));
        }
    }
}
