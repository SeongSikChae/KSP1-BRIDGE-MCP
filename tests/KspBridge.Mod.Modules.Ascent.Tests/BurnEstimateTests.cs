using KspBridge.Mod.Modules.Ascent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent.Tests
{
    [TestClass]
    public sealed class BurnEstimateTests
    {
        private static StageBurn Stage(double start, double end, double dv, double time)
            => new StageBurn { StartMass = start, EndMass = end, DvVac = dv, BurnTime = time };

        [TestMethod]
        public void InsideOneStage()
        {
            List<StageBurn> stages = new List<StageBurn> { Stage(10, 5, 1000, 100) };
            Assert.AreEqual(100.0, BurnEstimate.Seconds(1000, stages, 0).Value, 1e-9);
            double ve = 1000 / Math.Log(2);
            double end = 10 * Math.Exp(-500 / ve);
            Assert.AreEqual(100 * (10 - end) / 5, BurnEstimate.Seconds(500, stages, 0).Value, 1e-9);
            Assert.AreEqual(0.0, BurnEstimate.Seconds(0, stages, 0).Value);
        }

        [TestMethod]
        public void AcrossStages()
        {
            List<StageBurn> stages = new List<StageBurn> { Stage(10, 5, 1000, 100), Stage(0, 0, 0, 0), Stage(4, 2, 800, 60) };
            Assert.AreEqual(160.0, BurnEstimate.Seconds(1800, stages, 0).Value, 1e-9);
            Assert.AreEqual(160 + 200 / 2.0, BurnEstimate.Seconds(2000, stages, 2).Value, 1e-9);
            Assert.AreEqual(160.0, BurnEstimate.Seconds(2000, stages, 0).Value, 1e-9);
        }

        [TestMethod]
        public void WithoutTheCalculator()
        {
            Assert.AreEqual(50.0, BurnEstimate.Seconds(100, null, 2).Value, 1e-9);
            Assert.AreEqual(50.0, BurnEstimate.Seconds(100, new List<StageBurn>(), 2).Value, 1e-9);
            Assert.IsNull(BurnEstimate.Seconds(100, null, 0));
        }
    }
}
