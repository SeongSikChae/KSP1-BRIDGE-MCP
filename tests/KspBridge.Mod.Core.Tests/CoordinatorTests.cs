using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Core.Tests
{
    [TestClass]
    public sealed class CoordinatorTests
    {
        private double ut;
        private string block;
        private int session = 1;
        private StepRegistry registry;
        private EventLog events;
        private Coordinator coordinator;
        private readonly List<string> ended = new List<string>();
        private int started;

        [TestInitialize]
        public void Init()
        {
            registry = new StepRegistry();
            registry.Add(new FakeStepType());
            events = new EventLog(() => ut);
            coordinator = new Coordinator(registry, events, () => ut, () => null, () => block, () => session);
            coordinator.PlanStarted += () => started++;
            coordinator.PlanEnded += r => ended.Add(r);
        }

        private static Dictionary<string, object> Plan(params string[] steps)
            => Json.ParseObject("{\"steps\":[" + string.Join(",", steps) + "]}");

        private static string Step(double seconds, string extra = "")
            => "{\"type\":\"fake\",\"params\":{\"seconds\":" + seconds + "}" + extra + "}";

        private void Run(double untilUt, double dt = 0.02)
        {
            while (ut < untilUt)
            {
                ut += dt;
                coordinator.FixedUpdate();
            }
        }

        private string[] EventTypes() => events.Since(0, 1000).Select(e => (string)e["type"]).ToArray();

        [TestMethod]
        public void TwoSteps_CompleteInOrder_AndEmitEvents()
        {
            coordinator.Submit(Plan(Step(2), Step(3)), replace: false);
            Assert.AreEqual(1, started);

            Run(4);
            Assert.IsTrue(coordinator.Running);
            Assert.AreEqual(1, coordinator.CurrentSummary["step_index"]);

            Run(6);
            Assert.IsFalse(coordinator.Running);
            Assert.AreEqual("completed", coordinator.LastSummary["state"]);
            Assert.AreEqual(EndReasons.Completed, coordinator.LastSummary["end_reason"]);
            CollectionAssert.AreEqual(new[] { "completed" }, ended);
            CollectionAssert.AreEqual(
                new[] { "plan_submitted", "step_started", "step_completed", "step_started", "step_completed", "plan_completed" },
                EventTypes());
            Assert.AreEqual(1, coordinator.LastSession);
        }

        [TestMethod]
        public void Abort_EndsRunningStep_WithReason()
        {
            coordinator.Submit(Plan(Step(60)), replace: false);
            Run(1);
            FakeRun run = FakeStepType.Last;

            Dictionary<string, object> summary = coordinator.Abort(EndReasons.Reload, "test");

            Assert.AreEqual("aborted", summary["state"]);
            Assert.AreEqual(EndReasons.Reload, summary["end_reason"]);
            Assert.AreEqual(EndReasons.Reload, run.EndReason);
            CollectionAssert.AreEqual(new[] { EndReasons.Reload }, ended);
            Assert.IsNull(coordinator.Abort(EndReasons.PlannerAbort, null), "nothing left to abort");
        }

        [TestMethod]
        public void Timeout_EndsPlan()
        {
            coordinator.Submit(Plan(Step(60, ",\"timeout_s\":1")), replace: false);
            Run(2);
            Assert.AreEqual(EndReasons.Timeout, coordinator.LastSummary["end_reason"]);
        }

        [TestMethod]
        public void FailingStep_EndsPlanAsStepFailed()
        {
            coordinator.Submit(Plan(Step(-1)), replace: false);   // FakeRun fails on negative seconds
            Run(0.1);
            Assert.AreEqual(EndReasons.StepFailed, coordinator.LastSummary["end_reason"]);
            StringAssert.Contains((string)coordinator.LastSummary["end_detail"], "negative");
        }

        [TestMethod]
        public void ControlException_FailsStepOnNextFrame()
        {
            coordinator.Submit(Plan(Step(60)), replace: false);
            Run(0.1);
            FakeStepType.Last.ThrowOnControl = true;

            coordinator.Control(null);
            Run(0.2);

            Assert.AreEqual(EndReasons.StepFailed, coordinator.LastSummary["end_reason"]);
            StringAssert.Contains((string)coordinator.LastSummary["end_detail"], "control error");
        }

        [TestMethod]
        public void UnknownStepType_IsBadRequest()
        {
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(
                () => coordinator.Submit(Plan("{\"type\":\"nope\"}"), replace: false));
            Assert.AreEqual("bad_request", ex.Code);
            StringAssert.Contains(ex.Message, "list_step_types");
            Assert.IsFalse(coordinator.Running);
        }

        [TestMethod]
        public void Blocked_IsNotReady()
        {
            block = "scene loading";
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => coordinator.Submit(Plan(Step(1)), replace: false));
            Assert.AreEqual("not_ready", ex.Code);
        }

        [TestMethod]
        public void SecondSubmit_NeedsReplace()
        {
            coordinator.Submit(Plan(Step(60)), replace: false);
            Assert.ThrowsExactly<BridgeException>(() => coordinator.Submit(Plan(Step(1)), replace: false));

            Dictionary<string, object> second = coordinator.Submit(Plan(Step(1)), replace: true);

            Assert.AreEqual(2, second["id"]);
            Assert.AreEqual(EndReasons.PlannerAbort, coordinator.LastSummary["end_reason"]);
        }

        [TestMethod]
        public void LastSession_IsTheSessionThePlanRanIn()
        {
            coordinator.Submit(Plan(Step(60)), replace: false);
            session = 7;   // a scene change bumps the session before aborting
            coordinator.Abort(EndReasons.SceneChange, null);
            Assert.AreEqual(1, coordinator.LastSession);
        }

        private sealed class FakeStepType : IStepType
        {
            public static FakeRun Last;

            public string Name => "fake";

            public string Describe() => "test step";

            public IStepRun Create(Dictionary<string, object> parameters)
                => Last = new FakeRun(parameters.Num("seconds", required: true).Value);
        }

        private sealed class FakeRun : IStepRun
        {
            private readonly double seconds;

            public FakeRun(double seconds) => this.seconds = seconds;

            public string EndReason;
            public bool ThrowOnControl;

            public void Begin(StepContext context)
            {
            }

            public StepResult Judge(StepContext context)
            {
                if (seconds < 0)
                    return StepResult.Failed("negative seconds");
                return context.Elapsed >= seconds ? StepResult.Done : StepResult.Running;
            }

            public void Control(FlightCtrlState state, StepContext context)
            {
                if (ThrowOnControl)
                    throw new System.InvalidOperationException("boom");
            }

            public void End(string reason) => EndReason = reason;
        }
    }
}
