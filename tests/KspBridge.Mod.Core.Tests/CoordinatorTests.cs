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
            registry.Add(new ProgressStepType());
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

        private static string ProgressStep(double seconds, bool throws = false, string extra = "")
            => "{\"type\":\"progress\",\"params\":{\"seconds\":" + seconds + ",\"throws\":" + (throws ? "true" : "false") + "}" + extra + "}";

        [TestMethod]
        public void Submit_Answer_HasPlanElapsed_AndNullStepValues_BeforeTheStepBegins()
        {
            ut = 100;
            Dictionary<string, object> answer = coordinator.Submit(Plan(ProgressStep(10, extra: ",\"timeout_s\":20")), replace: false);

            Assert.AreEqual(0.0, (double)answer["elapsed_s"], 1e-9);
            Assert.IsNull(answer["step_elapsed_s"]);
            Assert.IsNull(answer["step_timeout_remaining_s"]);
            Assert.IsNull(answer["step_progress"]);
            Assert.IsNull(answer["step_progress_error"]);
        }

        [TestMethod]
        public void RunningStep_ShowsElapsed_TimeoutLeft_AndReportedProgress()
        {
            coordinator.Submit(Plan(ProgressStep(10, extra: ",\"timeout_s\":20")), replace: false);
            Run(3);

            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();

            Assert.AreEqual(ut, (double)current["elapsed_s"], 1e-9);
            double stepElapsed = (double)current["step_elapsed_s"];
            Assert.AreEqual(3, stepElapsed, 0.05);
            Assert.AreEqual(20 - stepElapsed, (double)current["step_timeout_remaining_s"], 1e-9);
            Dictionary<string, object> progress = (Dictionary<string, object>)current["step_progress"];
            Assert.AreEqual(10 - stepElapsed, (double)progress["left_s"], 1e-9, "the step sees the request's UT");
            Assert.IsNull(current["step_progress_error"]);
        }

        [TestMethod]
        public void StepWithoutTimeout_HasNullTimeoutLeft_AndStepWithoutReport_HasNullProgress()
        {
            coordinator.Submit(Plan(Step(10)), replace: false);
            Run(1);

            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();

            Assert.IsNotNull(current["step_elapsed_s"]);
            Assert.IsNull(current["step_timeout_remaining_s"]);
            Assert.IsNull(current["step_progress"]);
            Assert.IsNull(current["step_progress_error"]);
        }

        [TestMethod]
        public void BetweenSteps_StepValuesAreNull()
        {
            coordinator.Submit(Plan(ProgressStep(1), ProgressStep(5)), replace: false);
            while (coordinator.CurrentSummary == null || (int)coordinator.CurrentSummary["step_index"] == 0)
            {
                ut += 0.02;
                coordinator.FixedUpdate();
            }

            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();   // step 1 begins on the next frame

            Assert.AreEqual(1, current["step_index"]);
            Assert.IsNotNull(current["elapsed_s"]);
            Assert.IsNull(current["step_elapsed_s"]);
            Assert.IsNull(current["step_timeout_remaining_s"]);
            Assert.IsNull(current["step_progress"]);
        }

        [TestMethod]
        public void ProgressException_IsShown_AndThePlanKeepsRunning()
        {
            coordinator.Submit(Plan(ProgressStep(5, throws: true)), replace: false);
            Run(1);

            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();

            Assert.IsNull(current["step_progress"]);
            StringAssert.Contains((string)current["step_progress_error"], "InvalidOperationException");
            Assert.IsTrue(coordinator.Running);
            Run(6);
            Assert.AreEqual(EndReasons.Completed, coordinator.LastSummary["end_reason"]);
        }

        [TestMethod]
        public void EndedPlan_AndPlainSummary_HaveNoProgressValues()
        {
            coordinator.Submit(Plan(ProgressStep(5)), replace: false);
            Run(1);
            Assert.IsFalse(coordinator.CurrentSummary.ContainsKey("elapsed_s"), "get_status reads the plain summary");
            Assert.AreEqual(0, ProgressRun.Calls, "the plain summary never calls the progress report");

            Run(6);

            Assert.IsNull(coordinator.CurrentSummaryWithProgress());
            Assert.IsFalse(coordinator.LastSummary.ContainsKey("elapsed_s"));
            Assert.IsFalse(coordinator.LastSummary.ContainsKey("step_progress"));
        }

        private sealed class ProgressStepType : IStepType
        {
            public string Name => "progress";

            public string Describe() => "test step that reports progress";

            public IStepRun Create(Dictionary<string, object> parameters)
            {
                ProgressRun.Calls = 0;
                return new ProgressRun(parameters.Num("seconds", required: true).Value, parameters.Bool("throws"));
            }
        }

        private sealed class ProgressRun : IStepRun, IStepProgress
        {
            public static int Calls;

            private readonly double seconds;
            private readonly bool throws;

            public ProgressRun(double seconds, bool throws)
            {
                this.seconds = seconds;
                this.throws = throws;
            }

            public Dictionary<string, object> Progress(StepContext context)
            {
                Calls++;
                if (throws)
                    throw new System.InvalidOperationException("report failed");
                return new Dictionary<string, object> { ["left_s"] = seconds - context.Elapsed };
            }

            public void Begin(StepContext context)
            {
            }

            public StepResult Judge(StepContext context) => context.Elapsed >= seconds ? StepResult.Done : StepResult.Running;

            public void Control(FlightCtrlState state, StepContext context)
            {
            }

            public void End(string reason)
            {
            }
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
