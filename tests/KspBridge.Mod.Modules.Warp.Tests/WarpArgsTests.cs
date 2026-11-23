using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Warp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Warp.Tests
{
    [TestClass]
    public sealed class WarpArgsTests
    {
        private static WarpArgs Parse(string json) => WarpArgs.Parse(Json.ParseObject(json));

        [TestMethod]
        public void Goals()
        {
            WarpArgs seconds = Parse("{\"seconds\":600}");
            Assert.AreEqual(WarpGoal.Seconds, seconds.Goal);
            Assert.AreEqual(600.0, seconds.Value);
            Assert.IsNull(seconds.MaxRate);
            Assert.IsNull(seconds.PhysicsRate);

            WarpArgs ut = Parse("{\"ut\":-5}");
            Assert.AreEqual(WarpGoal.Ut, ut.Goal);
            Assert.AreEqual(-5.0, ut.Value);

            WarpArgs ap = Parse("{\"to\":\"apoapsis\",\"lead_s\":30}");
            Assert.AreEqual(WarpGoal.Apoapsis, ap.Goal);
            Assert.AreEqual(30.0, ap.Lead);

            WarpArgs pe = Parse("{\"to\":\"periapsis\"}");
            Assert.AreEqual(WarpGoal.Periapsis, pe.Goal);
            Assert.AreEqual(0.0, pe.Lead);

            WarpArgs node = Parse("{\"to\":\"node\",\"lead_s\":60}");
            Assert.AreEqual(WarpGoal.Node, node.Goal);
            Assert.AreEqual(60.0, node.Lead);
            Assert.AreEqual(0.0, Parse("{\"to\":\"node\"}").Lead);
        }

        [TestMethod]
        public void Rates()
        {
            WarpArgs a = Parse("{\"seconds\":0.5,\"max_rate\":1,\"physics_rate\":4}");
            Assert.AreEqual(1.0, a.MaxRate);
            Assert.AreEqual(4, a.PhysicsRate);
            Assert.AreEqual(2, Parse("{\"seconds\":1,\"physics_rate\":2}").PhysicsRate);
        }

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"seconds\":null}")]
        [DataRow("{\"max_rate\":10}")]
        [DataRow("{\"seconds\":1,\"ut\":5}")]
        [DataRow("{\"seconds\":1,\"to\":\"apoapsis\"}")]
        [DataRow("{\"ut\":1,\"to\":\"apoapsis\"}")]
        [DataRow("{\"seconds\":0}")]
        [DataRow("{\"seconds\":-1}")]
        [DataRow("{\"seconds\":\"1\"}")]
        [DataRow("{\"seconds\":true}")]
        [DataRow("{\"ut\":\"1\"}")]
        [DataRow("{\"to\":1}")]
        [DataRow("{\"to\":\"Apoapsis\"}")]
        [DataRow("{\"to\":\"maneuver\"}")]
        [DataRow("{\"to\":\"apoapsis\",\"lead_s\":-1}")]
        [DataRow("{\"to\":\"apoapsis\",\"lead_s\":\"1\"}")]
        [DataRow("{\"seconds\":1,\"lead_s\":1}")]
        [DataRow("{\"ut\":1,\"lead_s\":0}")]
        [DataRow("{\"seconds\":1,\"max_rate\":0.5}")]
        [DataRow("{\"seconds\":1,\"max_rate\":\"10\"}")]
        [DataRow("{\"seconds\":1,\"physics_rate\":1}")]
        [DataRow("{\"seconds\":1,\"physics_rate\":5}")]
        [DataRow("{\"seconds\":1,\"physics_rate\":2.5}")]
        [DataRow("{\"seconds\":1,\"rate\":5}")]
        public void BadInput_IsBadRequest(string json)
        {
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => Parse(json));
            Assert.AreEqual(BridgeException.BadRequest("x").Code, ex.Code);
        }

        [TestMethod]
        [DataRow("seconds")]
        [DataRow("ut")]
        [DataRow("max_rate")]
        public void NotFinite_IsBadRequest(string key)
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
            {
                var args = new System.Collections.Generic.Dictionary<string, object> { ["seconds"] = 1.0 };
                if (key == "ut")
                    args.Remove("seconds");
                args[key] = bad;
                Assert.ThrowsExactly<BridgeException>(() => WarpArgs.Parse(args));
            }
        }
    }
}
