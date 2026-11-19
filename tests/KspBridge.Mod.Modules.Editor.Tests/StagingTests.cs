using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor.Tests
{
    [TestClass]
    public sealed class StagingTests
    {
        private static List<StageEntry> Entries(params (uint id, int stage, int index, uint group)[] items)
            => items.Select(i => new StageEntry(i.id, i.stage, i.index, i.group)).ToList();

        [TestMethod]
        public void RequireTarget_EmptyShip_OnlyZero()
        {
            List<StageEntry> none = new List<StageEntry>();
            Assert.AreEqual(0, Staging.NewStage(none));
            Assert.AreEqual(0, Staging.RequireTarget(none, null));
            Assert.AreEqual(0, Staging.RequireTarget(none, 0));
            Assert.ThrowsExactly<BridgeException>(() => Staging.RequireTarget(none, 1));
            Assert.ThrowsExactly<BridgeException>(() => Staging.RequireTarget(none, -1));
        }

        [TestMethod]
        public void RequireTarget_ExistingOrNewFirst()
        {
            List<StageEntry> e = Entries((1, 0, 0, 1), (2, 1, 0, 2));
            Assert.AreEqual(2, Staging.RequireTarget(e, null), "default is a new stage that fires first");
            Assert.AreEqual(1, Staging.RequireTarget(e, 1));
            Assert.AreEqual(2, Staging.RequireTarget(e, 2));
            Assert.ThrowsExactly<BridgeException>(() => Staging.RequireTarget(e, 3));
            Assert.ThrowsExactly<BridgeException>(() => Staging.RequireTarget(e, 1.5));
        }

        [TestMethod]
        public void Move_PutsGroupAtEndOfStage()
        {
            List<StageEntry> e = Entries((1, 0, 0, 1), (2, 1, 0, 2), (3, 1, 1, 3), (4, 1, 1, 3));
            Staging.Move(e, new List<uint> { 1 }, 1);
            Assert.AreEqual(1, e.Single(x => x.Id == 1).Stage);
            Assert.AreEqual(2, e.Single(x => x.Id == 1).Index);
        }

        [TestMethod]
        public void Swap_ExchangesTwoExistingStages()
        {
            List<StageEntry> e = Entries((1, 0, 0, 1), (2, 1, 0, 2), (3, 2, 0, 3));
            Staging.Swap(e, 0, 2);
            Assert.AreEqual(2, e.Single(x => x.Id == 1).Stage);
            Assert.AreEqual(0, e.Single(x => x.Id == 3).Stage);
            Assert.AreEqual(1, e.Single(x => x.Id == 2).Stage);
            Assert.ThrowsExactly<BridgeException>(() => Staging.Swap(e, 1, 1), "same stage");
            Assert.ThrowsExactly<BridgeException>(() => Staging.Swap(e, 1, 5), "missing stage");
            Assert.ThrowsExactly<BridgeException>(() => Staging.Swap(e, 3, 1), "new-stage number is not an existing stage");
        }

        [TestMethod]
        public void Normalize_RemovesGapsAfterRemoval()
        {
            List<StageEntry> e = Entries((1, 0, 0, 1), (3, 2, 0, 3), (4, 5, 0, 4));
            Staging.Normalize(e);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, e.Select(x => x.Stage).ToArray());
            Assert.IsNull(Staging.NormalizationError(e));
        }

        [TestMethod]
        public void Normalize_SymmetryPartnersShareIndex()
        {
            // Stage 7 of Kerbal X as the game numbers it: engine pairs share an index, clamps (3) share one.
            List<StageEntry> e = Entries((10, 7, 5, 10), (11, 7, 6, 10), (20, 7, 0, 20), (21, 7, 1, 20), (30, 7, 3, 30), (31, 7, 3, 30), (32, 7, 4, 30), (40, 7, 9, 40));
            Staging.Normalize(e);
            Assert.AreEqual(0, e.Single(x => x.Id == 20).Index);
            Assert.AreEqual(0, e.Single(x => x.Id == 21).Index);
            Assert.AreEqual(1, e.Single(x => x.Id == 30).Index);
            Assert.AreEqual(1, e.Single(x => x.Id == 32).Index);
            Assert.AreEqual(2, e.Single(x => x.Id == 10).Index);
            Assert.AreEqual(2, e.Single(x => x.Id == 11).Index);
            Assert.AreEqual(3, e.Single(x => x.Id == 40).Index);
            Assert.IsNull(Staging.NormalizationError(e));
        }

        [TestMethod]
        public void NormalizationError_FindsGapsAndIndexProblems()
        {
            Assert.IsNotNull(Staging.NormalizationError(Entries((1, 0, 0, 1), (2, 2, 0, 2))), "gap");
            Assert.IsNotNull(Staging.NormalizationError(Entries((1, 1, 0, 1))), "does not start at 0");
            Assert.IsNotNull(Staging.NormalizationError(Entries((1, 0, 0, 1), (2, 0, 0, 2))), "two groups share an index");
            Assert.IsNotNull(Staging.NormalizationError(Entries((1, 0, 0, 1), (2, 0, 1, 1))), "partners with different indexes");
            Assert.IsNotNull(Staging.NormalizationError(Entries((1, 0, 1, 1))), "index does not start at 0");
            Assert.IsNull(Staging.NormalizationError(Entries((1, 0, 0, 1), (2, 0, 0, 1), (3, 0, 1, 3))), "partners share 0, next is 1");
            Assert.IsNull(Staging.NormalizationError(new List<StageEntry>()));
        }

        private static Dictionary<uint, int> M(params (uint id, int stage)[] items) => items.ToDictionary(i => i.id, i => i.stage);

        [TestMethod]
        public void StageMismatch_MatchesWhenGameStagesAsWritten()
        {
            Assert.IsNull(Staging.StageMismatch(M((1, 0), (2, 1)), M((1, 0), (2, 1)), new[] { 2u }, new uint[0]));
        }

        [TestMethod]
        public void StageMismatch_NewPartTheGameDidNotStageIsDropped()
        {
            // Heat shield written into the new first stage 2; the game does not stage it.
            Assert.IsNull(Staging.StageMismatch(M((1, 0), (2, 1), (9, 2)), M((1, 0), (2, 1)), new[] { 9u }, new uint[0]));
            // Replacing the only part of stage 0 by an unstaged part: the game deletes stage 0 and renumbers.
            Assert.IsNull(Staging.StageMismatch(M((9, 0), (2, 1), (3, 2)), M((2, 0), (3, 1)), new[] { 9u }, new uint[0]));
        }

        [TestMethod]
        public void StageMismatch_RequestedStageOnUnstagedPartIsRefused()
        {
            StringAssert.Contains(Staging.StageMismatch(M((1, 0), (9, 0)), M((1, 0)), new[] { 9u }, new[] { 9u }), "not staged");
        }

        [TestMethod]
        public void StageMismatch_FindsWrongOrderAndLostOrUnexpectedParts()
        {
            Assert.IsNotNull(Staging.StageMismatch(M((1, 0), (2, 1)), M((1, 1), (2, 0)), new uint[0], new uint[0]), "order swapped");
            Assert.IsNotNull(Staging.StageMismatch(M((1, 0), (2, 1)), M((1, 0), (2, 0)), new[] { 2u }, new uint[0]), "new part merged into stage 0");
            Assert.IsNotNull(Staging.StageMismatch(M((1, 0), (2, 1)), M((2, 0)), new uint[0], new uint[0]), "existing staged part lost");
            Assert.IsNotNull(Staging.StageMismatch(M((1, 0)), M((1, 0), (5, 0)), new uint[0], new uint[0]), "unexpected staged part");
        }

        [TestMethod]
        public void Group_HighestStageFirst()
        {
            List<KeyValuePair<int, List<StageEntry>>> g = Staging.Group(Entries((1, 0, 0, 1), (2, 2, 0, 2), (3, 1, 0, 3)));
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, g.Select(x => x.Key).ToArray());
        }
    }
}
