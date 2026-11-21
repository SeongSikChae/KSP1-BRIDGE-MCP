using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class SaveNameTests
    {
        private static string BadRequest(string raw, bool forSave)
        {
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => SaveName.Parse(raw, forSave));
            Assert.AreEqual("bad_request", ex.Code);
            return ex.Message;
        }

        [TestMethod]
        public void Omitted_IsQuicksave()
        {
            Assert.AreEqual("quicksave", SaveName.Parse(null, forSave: true));
            Assert.AreEqual("quicksave", SaveName.Parse(null, forSave: false));
        }

        [TestMethod]
        public void AllowedCharacters_Pass()
        {
            Assert.AreEqual("Orbit test_1-a", SaveName.Parse("Orbit test_1-a", forSave: true));
            Assert.AreEqual(new string('a', 64), SaveName.Parse(new string('a', 64), forSave: true));
        }

        [TestMethod]
        public void Empty_IsBadRequest() => StringAssert.Contains(BadRequest("", true), "empty");

        [TestMethod]
        public void PathCharacters_AreBadRequest()
        {
            StringAssert.Contains(BadRequest("../x", true), "'.'");
            BadRequest("a\\b", false);
            BadRequest("a:b", false);
            BadRequest("t1.sfs", false);
        }

        [TestMethod]
        public void TooLong_IsBadRequest() => StringAssert.Contains(BadRequest(new string('a', 65), true), "64");

        [TestMethod]
        public void Persistent_IsRefusedForSaveOnly()
        {
            StringAssert.Contains(BadRequest("persistent", true), "persistent");
            BadRequest("Persistent", true);
            Assert.AreEqual("persistent", SaveName.Parse("persistent", forSave: false));
        }
    }
}
