using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor.Tests
{
    [TestClass]
    public sealed class EditorQueryTests
    {
        [TestMethod]
        [DataRow("Kerbal X")]
        [DataRow("bridge-test-1")]
        [DataRow("로켓 1")]
        [DataRow("a.b")]
        public void CraftNameError_AcceptsNormalNames(string name)
        {
            Assert.IsNull(EditorQuery.CraftNameError(name));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(" lead")]
        [DataRow("trail ")]
        [DataRow("a/b")]
        [DataRow("a\\b")]
        [DataRow("c:x")]
        [DataRow("what?")]
        [DataRow("a\tb")]
        [DataRow("..")]
        [DataRow("up..x")]
        [DataRow("dot.")]
        public void CraftNameError_RejectsUnsafeNames(string name)
        {
            Assert.IsNotNull(EditorQuery.CraftNameError(name));
        }

        [TestMethod]
        public void RequireCraftName_ThrowsBadRequest()
        {
            BridgeException e = Assert.ThrowsExactly<BridgeException>(() => EditorQuery.RequireCraftName("a/b"));
            Assert.AreEqual("bad_request", e.Code);
        }

        [TestMethod]
        public void MatchesPart_FiltersByCategoryAndSearchIgnoringCase()
        {
            Assert.IsTrue(EditorQuery.MatchesPart("liquidEngine2", "LV-T45 \"Swivel\"", "Engine", null, null));
            Assert.IsTrue(EditorQuery.MatchesPart("liquidEngine2", "LV-T45 \"Swivel\"", "Engine", "engine", "swivel"));
            Assert.IsTrue(EditorQuery.MatchesPart("liquidEngine2", "LV-T45 \"Swivel\"", "Engine", "", "LIQUIDENGINE"));
            Assert.IsFalse(EditorQuery.MatchesPart("liquidEngine2", "LV-T45 \"Swivel\"", "Engine", "FuelTank", null));
            Assert.IsFalse(EditorQuery.MatchesPart("liquidEngine2", "LV-T45 \"Swivel\"", "Engine", null, "reliant"));
            Assert.IsFalse(EditorQuery.MatchesPart("x", null, "Pods", null, "y"));
        }

        [TestMethod]
        public void Paginate_UsesDefaultLimitAndReportsTotal()
        {
            List<int> items = Enumerable.Range(0, 120).ToList();

            Page<int> page = EditorQuery.Paginate(items, null, null);

            Assert.AreEqual(120, page.Total);
            Assert.AreEqual(0, page.Offset);
            CollectionAssert.AreEqual(Enumerable.Range(0, EditorQuery.DefaultLimit).ToList(), page.Items);
        }

        [TestMethod]
        public void Paginate_PagesDoNotOverlap()
        {
            List<int> items = Enumerable.Range(0, 25).ToList();

            Page<int> first = EditorQuery.Paginate(items, 10, 0);
            Page<int> second = EditorQuery.Paginate(items, 10, 10);
            Page<int> last = EditorQuery.Paginate(items, 10, 20);

            Assert.AreEqual(0, first.Items.Intersect(second.Items).Count());
            CollectionAssert.AreEqual(Enumerable.Range(20, 5).ToList(), last.Items);
            Assert.AreEqual(25, first.Items.Concat(second.Items).Concat(last.Items).Distinct().Count());
        }

        [TestMethod]
        public void Paginate_CapsLimitAndAllowsOffsetPastEnd()
        {
            List<int> items = Enumerable.Range(0, 500).ToList();

            Assert.AreEqual(EditorQuery.MaxLimit, EditorQuery.Paginate(items, 1000, 0).Items.Count);
            Page<int> beyond = EditorQuery.Paginate(items, 10, 900);
            Assert.AreEqual(0, beyond.Items.Count);
            Assert.AreEqual(500, beyond.Total);
        }

        [TestMethod]
        [DataRow(0.0, 0.0)]
        [DataRow(10.0, -1.0)]
        [DataRow(1.5, 0.0)]
        [DataRow(10.0, 0.5)]
        public void Paginate_RejectsBadArguments(double limit, double offset)
        {
            BridgeException e = Assert.ThrowsExactly<BridgeException>(() => EditorQuery.Paginate(new List<int> { 1 }, limit, offset));
            Assert.AreEqual("bad_request", e.Code);
        }

        [TestMethod]
        public void MissingParts_EmptyWhenAllInstalled()
        {
            List<string> missing = EditorQuery.MissingParts(new[] { "mk1pod.v2", "fuelTank", "fuelTank" }, _ => true);
            Assert.AreEqual(0, missing.Count);
        }

        [TestMethod]
        public void MissingParts_ListsEachMissingNameOnceInOrder()
        {
            var installed = new HashSet<string> { "mk1pod.v2" };
            List<string> missing = EditorQuery.MissingParts(
                new[] { "zModPart", "mk1pod.v2", "aModPart", "zModPart" }, installed.Contains);
            CollectionAssert.AreEqual(new[] { "aModPart", "zModPart" }, missing);
        }

        [TestMethod]
        public void MissingParts_ReportsUnnamedPartNodes()
        {
            List<string> missing = EditorQuery.MissingParts(new[] { "mk1pod.v2", "", null }, _ => true);
            CollectionAssert.AreEqual(new[] { "(unnamed)" }, missing);
        }
    }
}
