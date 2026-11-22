using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition.Tests
{
    /// <summary>Judging conditions and their summary (spec C2, C3, C4).</summary>
    [TestClass]
    public sealed class ConditionSetTests
    {
        private static Condition Number(string name, string op, double to) => new Condition(ConditionValues.Find(name), op, to);

        private static ConditionReading Reading(object altitude = null, object mass = null, string situation = "FLYING", double elapsed = 0,
            bool noThrust = false) => new ConditionReading
            {
                Flight = new Dictionary<string, object>
                {
                    ["situation"] = situation,
                    ["position"] = new Dictionary<string, object> { ["altitude_m"] = altitude },
                    ["mass_t"] = mass,
                },
                ElapsedS = elapsed,
                NoThrust = noThrust,
            };

        [TestMethod]
        [DataRow(">=", 999.9, false)]
        [DataRow(">=", 1000.0, true)]
        [DataRow(">=", 1000.1, true)]
        [DataRow("<=", 999.9, true)]
        [DataRow("<=", 1000.0, true)]
        [DataRow("<=", 1000.1, false)]
        [DataRow(">=", double.NaN, false)]
        [DataRow("<=", double.NaN, false)]
        [DataRow(">=", double.PositiveInfinity, true)]
        public void Numbers_IncludeTheEqualValue_AndNaNNeverMatches(string op, double actual, bool expected)
            => Assert.AreEqual(expected, Number("altitude_m", op, 1000).Met(actual));

        [TestMethod]
        public void MissingValue_NeverMatches()
        {
            Assert.IsFalse(Number("apoapsis_m", ">=", -1e9).Met(null));
            Assert.IsFalse(Number("apoapsis_m", "<=", 1e9).Met(null));
            Assert.IsFalse(new Condition(ConditionValues.Find("situation"), "==", "FLYING").Met(null));
        }

        [TestMethod]
        public void NamesAndFlags_AreEqualOrNot()
        {
            Condition landed = new Condition(ConditionValues.Find("situation"), "==", "LANDED");
            Assert.IsTrue(landed.Met("LANDED"));
            Assert.IsFalse(landed.Met("SPLASHED"));
            Condition running = new Condition(ConditionValues.Find("no_thrust"), "==", false);
            Assert.IsTrue(running.Met(false));
            Assert.IsFalse(running.Met(true));
        }

        [TestMethod]
        [DataRow("all", 1000.0, 5.0, true)]
        [DataRow("all", 1000.0, 6.0, false)]
        [DataRow("all", 900.0, 6.0, false)]
        [DataRow("any", 1000.0, 5.0, true)]
        [DataRow("any", 1000.0, 6.0, true)]
        [DataRow("any", 900.0, 5.0, true)]
        [DataRow("any", 900.0, 6.0, false)]
        public void AllNeedsEvery_AnyNeedsOne(string mode, double altitude, double mass, bool expected)
        {
            ConditionSet set = new ConditionSet(mode, new[] { Number("altitude_m", ">=", 1000), Number("mass_t", "<=", 5) });
            Assert.AreEqual(expected, set.Judge(Reading(altitude, mass)).Met);
        }

        [TestMethod]
        public void Summary_ShowsEveryCondition_WithItsValueAndWhetherItHolds()
        {
            ConditionSet set = new ConditionSet("any", new[] { Number("altitude_m", ">=", 1000), Number("elapsed_s", ">=", 5) });
            ConditionOutcome outcome = set.Judge(Reading(altitude: null, elapsed: 6));

            Assert.IsTrue(outcome.Met);
            Assert.AreEqual("any", outcome.Summary["mode"]);
            List<Dictionary<string, object>> rows = ((List<object>)outcome.Summary["conditions"]).Cast<Dictionary<string, object>>().ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("altitude_m", rows[0]["value"]);
            Assert.AreEqual(">=", rows[0]["op"]);
            Assert.AreEqual(1000.0, rows[0]["to"]);
            Assert.IsNull(rows[0]["actual"]);
            Assert.AreEqual(false, rows[0]["met"]);
            Assert.AreEqual(6.0, rows[1]["actual"]);
            Assert.AreEqual(true, rows[1]["met"]);
        }

        [TestMethod]
        public void OnlyWhatTheConditionsUse_IsNeeded()
        {
            ConditionSet elapsed = new ConditionSet("all", new[] { Number("elapsed_s", ">=", 5) });
            Assert.IsFalse(elapsed.UsesFlight);
            Assert.IsFalse(elapsed.UsesNoThrust);
            Assert.IsTrue(elapsed.Judge(new ConditionReading { ElapsedS = 5 }).Met);

            ConditionSet thrust = new ConditionSet("all", new[] { new Condition(ConditionValues.Find("no_thrust"), "==", true) });
            Assert.IsFalse(thrust.UsesFlight);
            Assert.IsTrue(thrust.UsesNoThrust);
            Assert.IsTrue(thrust.Judge(new ConditionReading { NoThrust = true }).Met);

            Assert.IsTrue(new ConditionSet("all", new[] { Number("mass_t", ">=", 5) }).UsesFlight);
        }
    }
}
