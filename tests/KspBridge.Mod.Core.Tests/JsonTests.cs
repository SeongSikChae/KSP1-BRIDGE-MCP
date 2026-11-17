using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Core.Tests
{
    [TestClass]
    public sealed class JsonTests
    {
        [TestMethod]
        public void Parse_ReadsNestedValues()
        {
            Dictionary<string, object> obj = Json.ParseObject("{\"a\":1.5,\"b\":[true,null,\"x\\n\\u0041\"],\"c\":{\"d\":-2e3}}");

            Assert.AreEqual(1.5, obj["a"]);
            List<object> b = (List<object>)obj["b"];
            Assert.AreEqual(true, b[0]);
            Assert.IsNull(b[1]);
            Assert.AreEqual("x\nA", b[2]);
            Assert.AreEqual(-2000.0, ((Dictionary<string, object>)obj["c"])["d"]);
        }

        [TestMethod]
        public void Write_RoundTrips()
        {
            Dictionary<string, object> value = new Dictionary<string, object>
            {
                ["s"] = "q\"\\\t",
                ["i"] = 42,
                ["d"] = 0.25,
                ["n"] = null,
                ["l"] = new List<object> { 1.0, "two" },
                ["e"] = DayOfWeek.Monday,
            };

            string json = Json.Write(value);
            Dictionary<string, object> back = Json.ParseObject(json);

            Assert.AreEqual("q\"\\\t", back["s"]);
            Assert.AreEqual(42.0, back["i"]);
            Assert.AreEqual(0.25, back["d"]);
            Assert.IsNull(back["n"]);
            Assert.AreEqual("two", ((List<object>)back["l"])[1]);
            Assert.AreEqual("Monday", back["e"]);
            StringAssert.Contains(json, "\"i\":42");
        }

        [TestMethod]
        public void Write_NaN_IsNull() => Assert.AreEqual("null", Json.Write(double.NaN));

        [TestMethod]
        public void ParseObject_Empty_IsEmptyObject() => Assert.AreEqual(0, Json.ParseObject("  ").Count);

        [TestMethod]
        [DataRow("{\"a\":}")]
        [DataRow("{\"a\":1")]
        [DataRow("[1,2")]
        [DataRow("{} x")]
        [DataRow("\"open")]
        public void Parse_Invalid_Throws(string text) => Assert.ThrowsExactly<FormatException>(() => Json.Parse(text));

        [TestMethod]
        public void JsonArgs_WrongType_IsBadRequest()
        {
            Dictionary<string, object> obj = Json.ParseObject("{\"n\":\"x\"}");
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => obj.Num("n"));
            Assert.AreEqual("bad_request", ex.Code);
        }
    }
}
