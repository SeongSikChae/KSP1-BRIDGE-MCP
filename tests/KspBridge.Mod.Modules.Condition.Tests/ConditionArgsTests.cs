using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition.Tests
{
    /// <summary>wait_until parameters (spec C2).</summary>
    [TestClass]
    public sealed class ConditionArgsTests
    {
        private const string High = "{\"value\":\"altitude_m\",\"op\":\">=\",\"to\":1000}";

        private static ConditionSet Parse(string json) => ConditionArgs.Parse(Json.ParseObject(json));

        private static string One(string condition) => "{\"all\":[" + condition + "]}";

        private static void Rejects(string json, string part)
        {
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => Parse(json));
            Assert.AreEqual("bad_request", ex.Code);
            StringAssert.Contains(ex.Message, part);
        }

        [TestMethod]
        public void All_And_Any_KeepTheirConditionsInOrder()
        {
            ConditionSet all = Parse("{\"all\":[" + High + ",{\"value\":\"situation\",\"op\":\"==\",\"to\":\"FLYING\"}]}");
            Assert.AreEqual("all", all.Mode);
            Assert.AreEqual(2, all.Conditions.Count);
            Assert.AreEqual("altitude_m", all.Conditions[0].Value.Name);
            Assert.AreEqual(1000.0, all.Conditions[0].To);
            Assert.AreEqual("FLYING", all.Conditions[1].To);

            ConditionSet any = Parse("{\"any\":[{\"value\":\"no_thrust\",\"op\":\"==\",\"to\":false}]}");
            Assert.AreEqual("any", any.Mode);
            Assert.AreEqual(false, any.Conditions[0].To);
        }

        [TestMethod]
        public void EveryValue_IsAccepted_WithItsOwnComparisons()
        {
            foreach (ConditionValue value in ConditionValues.All)
            {
                string[] good = value.Kind == ConditionKind.Number ? new[] { ">=", "<=" } : new[] { "==" };
                string to = value.Kind == ConditionKind.Number ? "1" : value.Kind == ConditionKind.Flag ? "true" : "\"LANDED\"";
                foreach (string op in new[] { ">=", "<=", "==", ">", "<", "!=" })
                {
                    string json = One("{\"value\":\"" + value.Name + "\",\"op\":\"" + op + "\",\"to\":" + to + "}");
                    if (good.Contains(op))
                        Assert.AreEqual(op, Parse(json).Conditions[0].Op);
                    else
                        Rejects(json, "'" + value.Name + "' is compared with");
                }
            }
        }

        [TestMethod]
        public void EverySituationOfTheGame_IsAccepted()
        {
            CollectionAssert.AreEquivalent(
                new[] { "LANDED", "SPLASHED", "PRELAUNCH", "FLYING", "SUB_ORBITAL", "ORBITING", "ESCAPING", "DOCKED" },
                ConditionValues.Situations.ToArray());
            foreach (string name in ConditionValues.Situations)
                Assert.AreEqual(name, Parse(One("{\"value\":\"situation\",\"op\":\"==\",\"to\":\"" + name + "\"}")).Conditions[0].To);
        }

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"all\":null}")]
        [DataRow("{\"all\":[" + High + "],\"any\":[" + High + "]}")]
        public void NeitherOrBothLists_AreBadRequest(string json) => Rejects(json, "either 'all' or 'any'");

        [TestMethod]
        [DataRow("{\"all\":[]}", "must not be empty")]
        [DataRow("{\"any\":[]}", "must not be empty")]
        [DataRow("{\"all\":5}", "'all' must be an array")]
        [DataRow("{\"all\":[5]}", "all[0] must be an object")]
        [DataRow("{\"any\":[" + High + ",\"x\"]}", "any[1] must be an object")]
        public void BadLists_AreBadRequest(string json, string part) => Rejects(json, part);

        [TestMethod]
        [DataRow("{\"op\":\">=\",\"to\":1}", "'value' must be a string")]
        [DataRow("{\"value\":5,\"op\":\">=\",\"to\":1}", "'value' must be a string")]
        [DataRow("{\"value\":\"speed\",\"op\":\">=\",\"to\":1}", "unknown value 'speed'")]
        [DataRow("{\"value\":\"altitude_m\",\"to\":1}", "'op' must be a string")]
        [DataRow("{\"value\":\"altitude_m\",\"op\":\">=\"}", "'to' must be a number")]
        [DataRow("{\"value\":\"altitude_m\",\"op\":\">=\",\"to\":null}", "'to' must be a number")]
        [DataRow("{\"value\":\"altitude_m\",\"op\":\">=\",\"to\":\"1000\"}", "'to' must be a number")]
        [DataRow("{\"value\":\"no_thrust\",\"op\":\"==\",\"to\":1}", "'to' must be true or false")]
        [DataRow("{\"value\":\"no_thrust\",\"op\":\"==\"}", "'to' must be true or false")]
        [DataRow("{\"value\":\"situation\",\"op\":\"==\",\"to\":\"landed\"}", "'to' must be one of")]
        [DataRow("{\"value\":\"situation\",\"op\":\"==\",\"to\":true}", "'to' must be one of")]
        [DataRow("{\"value\":\"altitude_m\",\"op\":\">=\",\"to\":1,\"unit\":\"km\"}", "unknown field 'unit'")]
        public void BadConditions_AreBadRequest(string condition, string part) => Rejects(One(condition), part);
    }
}
