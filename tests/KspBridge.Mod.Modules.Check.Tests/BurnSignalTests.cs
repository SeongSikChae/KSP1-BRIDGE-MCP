using KspBridge.Mod.Modules.Check;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace KspBridge.Mod.Modules.Check.Tests
{
    [TestClass]
    public sealed class BurnSignalTests
    {
        private static ShipData Ship(params uint[] engines)
        {
            ShipData ship = new ShipData();
            foreach (uint id in engines)
                ship.Engines.Add(new EngineData { PartId = id, Staged = true });
            return ship;
        }

        private static StageCalc Stage(ShipData ship, int number)
        {
            StageCalc calc = new StageCalc();
            ship.Stages[number] = calc;
            return calc;
        }

        private static void Segment(StageCalc stage, double start, double end, params uint[] engines)
            => stage.Segments.Add(new BurnSegment { StartMass = start, EndMass = end, Engines = engines.ToList() });

        private static EngineData E(ShipData ship, uint id) => ship.Engines.First(e => e.PartId == id);

        [TestMethod]
        public void SegmentWithMassDrop_Burns()
        {
            ShipData ship = Ship(1);
            Segment(Stage(ship, 1), 6.0, 2.5, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 1 }, E(ship, 1).BurnStages.ToArray());
        }

        [TestMethod]
        public void LeftoverSegmentWithoutMassDrop_DoesNotBurn()
        {
            // The spent Terrier of the test two-stage rocket: stage 1 burns, stage 0 keeps it active with a leftover burn
            // whose start and end mass are the same float value.
            ShipData ship = Ship(1);
            Segment(Stage(ship, 1), 6.0, 1.596000075, 1);
            Segment(Stage(ship, 0), 1.596000075, 1.596000075, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 1 }, E(ship, 1).BurnStages.ToArray());
        }

        [TestMethod]
        public void EngineRunningOutFirst_StillBurnsInThatStage()
        {
            // Engine 2 runs dry at the end of the first segment and is not in the next one.
            ShipData ship = Ship(1, 2);
            StageCalc stage = Stage(ship, 2);
            Segment(stage, 10.0, 7.0, 1, 2);
            Segment(stage, 7.0, 5.0, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 2 }, E(ship, 1).BurnStages.ToArray());
            CollectionAssert.AreEquivalent(new[] { 2 }, E(ship, 2).BurnStages.ToArray());
        }

        [TestMethod]
        public void StageWithoutSegments_DoesNotBurn()
        {
            // A stage the calculator skipped (its segments were cleared before the calculation, D074).
            ShipData ship = Ship(1);
            Segment(Stage(ship, 2), 8.0, 4.0, 1);
            Stage(ship, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 2 }, E(ship, 1).BurnStages.ToArray());
        }

        [TestMethod]
        public void StageMissingFromCalculator_DoesNotBurn()
        {
            ShipData ship = Ship(1);
            Segment(Stage(ship, 3), 8.0, 4.0, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 3 }, E(ship, 1).BurnStages.ToArray());
            Assert.IsFalse(E(ship, 1).BurnStages.Contains(2));
        }

        [TestMethod]
        public void Fill_ReplacesEarlierBurnStages()
        {
            ShipData ship = Ship(1);
            E(ship, 1).BurnStages.Add(5);
            Segment(Stage(ship, 1), 6.0, 2.5, 1);

            BurnSignal.Fill(ship);

            CollectionAssert.AreEquivalent(new[] { 1 }, E(ship, 1).BurnStages.ToArray());
        }
    }
}
