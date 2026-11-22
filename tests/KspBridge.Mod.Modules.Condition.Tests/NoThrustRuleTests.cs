using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Condition.Tests
{
    /// <summary>The no-thrust rule (spec terms, C8).</summary>
    [TestClass]
    public sealed class NoThrustRuleTests
    {
        [TestMethod]
        [DataRow(1.0, 1, 0, true, "the only engine is out")]
        [DataRow(0.01, 3, 0, true, "every engine is out")]
        [DataRow(0.0, 1, 0, false, "nothing is asked for")]
        [DataRow(1.0, 0, 0, false, "no engine was ignited, or the spent stage is gone")]
        [DataRow(1.0, 1, 1, false, "another engine still runs")]
        [DataRow(1.0, 0, 2, false, "all engines run")]
        public void Judge(double throttle, int flamedOut, int operational, bool expected, string why)
            => Assert.AreEqual(expected,
                NoThrustRule.Judge(new EngineState { Throttle = throttle, FlamedOut = flamedOut, Operational = operational }), why);
    }
}
