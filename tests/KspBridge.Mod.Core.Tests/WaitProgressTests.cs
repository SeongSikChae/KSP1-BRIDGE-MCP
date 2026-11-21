using KspBridge.Mod.Modules.Wait;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace KspBridge.Mod.Core.Tests
{
    /// <summary>The wait step's progress report (target and time left), through the step type the module registers.</summary>
    [TestClass]
    public sealed class WaitProgressTests
    {
        private static IStepRun CreateWait(double seconds)
        {
            StepRegistry steps = new StepRegistry();
            EventLog events = new EventLog(() => 0);
            Coordinator coordinator = new Coordinator(steps, events, () => 0, () => null, () => null, () => 1);
            ModuleContext context = new ModuleContext("wait", new Router(null), steps, new Subscriptions(null), coordinator, events,
                _ => { }, new FlightSession());
            new WaitModule().Register(context);
            return steps.Find("wait").Create(new Dictionary<string, object> { ["seconds"] = seconds });
        }

        private static Dictionary<string, object> ProgressAt(IStepRun run, double startUt, double ut)
        {
            StepContext context = new StepContext(new Dictionary<string, object>()) { StartUT = startUt, UT = ut };
            return ((IStepProgress)run).Progress(context);
        }

        [TestMethod]
        public void ReportsTarget_AndTimeLeft()
        {
            IStepRun run = CreateWait(30);

            Dictionary<string, object> progress = ProgressAt(run, 100, 112);

            Assert.AreEqual(30.0, (double)progress["target_s"], 1e-9);
            Assert.AreEqual(18.0, (double)progress["remaining_s"], 1e-9);
        }

        [TestMethod]
        public void TimeLeft_NeverGoesBelowZero()
        {
            IStepRun run = CreateWait(5);

            Dictionary<string, object> progress = ProgressAt(run, 0, 9);

            Assert.AreEqual(0.0, (double)progress["remaining_s"], 1e-9);
        }
    }
}
