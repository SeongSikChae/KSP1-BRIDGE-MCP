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
            registry.Add(new ScopeStepType());
            ScopeStepType.Reset();
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

        [TestMethod]
        public void Hold_IsCalledBeforeTheStep_InLaterSteps_AndBetweenSteps()
        {
            List<string> calls = new List<string>();
            FakeHold hold = new FakeHold { OnControl = () => calls.Add("hold") };
            ScopeStepType.OnBegin = c =>
            {
                if (c.Plan.Hold == null)
                    c.Plan.SetHold(hold, "first");
            };
            ScopeStepType.OnControl = () => calls.Add("step");
            coordinator.Submit(Plan(ScopeStep(), ScopeStep(), Step(60)), replace: false);

            coordinator.Control(null);
            Assert.AreEqual(0, calls.Count, "no hold yet and the first step has not begun");

            Frame();   // step 0 begins and completes
            coordinator.Control(null);   // between steps
            CollectionAssert.AreEqual(new[] { "hold" }, calls);

            calls.Clear();
            ScopeStepType.Finish = false;
            Frame();   // step 1 begins and keeps running
            coordinator.Control(null);
            CollectionAssert.AreEqual(new[] { "hold", "step" }, calls);

            calls.Clear();
            ScopeStepType.Finish = true;
            Frame();
            Frame();   // the plain step runs
            coordinator.Control(null);
            CollectionAssert.AreEqual(new[] { "hold" }, calls);
        }

        [TestMethod]
        public void Hold_Replaced_Released_AndEndedByCompletion_LeaveEventsWithReasons()
        {
            FakeHold first = new FakeHold { Name = "a" };
            FakeHold second = new FakeHold { Name = "b" };
            int step = 0;
            ScopeStepType.OnBegin = c =>
            {
                switch (step++)
                {
                    case 0:
                        Assert.IsFalse(c.Plan.ReleaseHold("nothing"));
                        c.Plan.SetHold(first, "x");
                        break;
                    case 1:
                        c.Plan.SetHold(second, "attitude");
                        break;
                    case 2:
                        Assert.IsTrue(c.Plan.ReleaseHold("attitude_off"));
                        Assert.IsNull(c.Plan.Hold);
                        break;
                    default:
                        c.Plan.SetHold(first, "x");
                        break;
                }
            };
            coordinator.Submit(Plan(ScopeStep(), ScopeStep(), ScopeStep(), ScopeStep()), replace: false);
            Run(1);

            CollectionAssert.AreEqual(new[] { "attitude:a", "attitude_off:b", "completed:a" }, HoldReleases());
            string[] types = EventTypes();
            Assert.AreEqual("plan_completed", types[types.Length - 1], "the hold is released before the plan end event");
        }

        [TestMethod]
        [DataRow(EndReasons.PlannerAbort)]
        [DataRow(EndReasons.Reload)]
        [DataRow(EndReasons.VesselChange)]
        [DataRow(EndReasons.SceneChange)]
        [DataRow(EndReasons.NewFlight)]
        public void Hold_EndsWithThePlan_AndIsNotCalledAgain(string reason)
        {
            FakeHold hold = new FakeHold { Name = "a" };
            ScopeStepType.OnBegin = c => c.Plan.SetHold(hold, "x");
            coordinator.Submit(Plan(ScopeStep(), Step(60)), replace: false);
            Run(1);
            coordinator.Control(null);
            Assert.AreEqual(1, hold.Calls);

            coordinator.Abort(reason, null);
            coordinator.Control(null);

            Assert.AreEqual(1, hold.Calls);
            CollectionAssert.AreEqual(new[] { reason + ":a" }, HoldReleases());
        }

        [TestMethod]
        public void Hold_EndsOnTimeout_AndOnStepFailure()
        {
            ScopeStepType.OnBegin = c => c.Plan.SetHold(new FakeHold { Name = "a" }, "x");
            coordinator.Submit(Plan(ScopeStep(), Step(60, ",\"timeout_s\":1")), replace: false);
            Run(3);
            coordinator.Submit(Plan(ScopeStep(), Step(-1)), replace: false);
            Run(4);

            CollectionAssert.AreEqual(new[] { "timeout:a", "step_failed:a" }, HoldReleases());
        }

        [TestMethod]
        public void Summary_ShowsTheHold_OnlyWithProgress_AndTheSubmitAnswerIsNull()
        {
            ScopeStepType.OnBegin = c => c.Plan.SetHold(new FakeHold { Name = "a" }, "x");
            Dictionary<string, object> answer = coordinator.Submit(Plan(ScopeStep(), Step(1)), replace: false);
            Assert.IsTrue(answer.ContainsKey("hold"));
            Assert.IsNull(answer["hold"]);
            Assert.IsNull(answer["hold_error"]);

            Run(0.5);
            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();
            Assert.AreEqual("a", ((Dictionary<string, object>)current["hold"])["name"]);
            Assert.IsNull(current["hold_error"]);
            Assert.IsFalse(coordinator.CurrentSummary.ContainsKey("hold"), "get_status reads the plain summary");

            Run(3);
            Assert.IsFalse(coordinator.LastSummary.ContainsKey("hold"));
        }

        [TestMethod]
        public void HoldDescribeException_IsShown_AndThePlanKeepsRunning()
        {
            ScopeStepType.OnBegin = c => c.Plan.SetHold(new FakeHold { ThrowOnDescribe = true }, "x");
            coordinator.Submit(Plan(ScopeStep(), Step(1)), replace: false);
            Run(0.5);

            Dictionary<string, object> current = coordinator.CurrentSummaryWithProgress();
            Assert.IsNull(current["hold"]);
            StringAssert.Contains((string)current["hold_error"], "InvalidOperationException");

            Run(3);
            Assert.AreEqual(EndReasons.Completed, coordinator.LastSummary["end_reason"]);
        }

        [TestMethod]
        public void HoldException_FailsTheRunningStep()
        {
            FakeHold hold = new FakeHold();
            ScopeStepType.OnBegin = c => c.Plan.SetHold(hold, "x");
            coordinator.Submit(Plan(ScopeStep(), Step(60)), replace: false);
            Run(1);
            hold.OnControl = () => throw new System.InvalidOperationException("boom");

            coordinator.Control(null);
            Frame();

            Assert.AreEqual(EndReasons.StepFailed, coordinator.LastSummary["end_reason"]);
            StringAssert.Contains((string)coordinator.LastSummary["end_detail"], "control error: InvalidOperationException: boom");
        }

        [TestMethod]
        public void HoldException_BetweenSteps_FailsTheNextStep_WithoutBeginningIt()
        {
            FakeHold hold = new FakeHold { OnControl = () => throw new System.InvalidOperationException("boom") };
            int begun = 0;
            ScopeStepType.OnBegin = c =>
            {
                begun++;
                c.Plan.SetHold(hold, "x");
            };
            coordinator.Submit(Plan(ScopeStep(), ScopeStep()), replace: false);
            Frame();

            coordinator.Control(null);   // step 0 is done, step 1 has not begun
            Frame();

            Assert.AreEqual(1, begun);
            Assert.AreEqual(EndReasons.StepFailed, coordinator.LastSummary["end_reason"]);
            Assert.AreEqual(1, EventTypes().Count(t => t == "step_started"));
        }

        [TestMethod]
        public void BeginException_FailsOnlyThatPlan()
        {
            ScopeStepType.OnBegin = c => throw new System.InvalidOperationException("no vessel");
            coordinator.Submit(Plan(ScopeStep()), replace: false);
            Run(0.1);
            Assert.AreEqual(EndReasons.StepFailed, coordinator.LastSummary["end_reason"]);

            ScopeStepType.OnBegin = null;
            coordinator.Submit(Plan(ScopeStep()), replace: false);
            Run(1);
            Assert.AreEqual(EndReasons.Completed, coordinator.LastSummary["end_reason"]);
        }

        [TestMethod]
        public void EndActions_RunOnce_AfterTheHoldIsReleased_AndOnlyTheFirstOfAKey()
        {
            List<string> calls = new List<string>();
            int step = 0;
            ScopeStepType.OnBegin = c =>
            {
                int n = step++;
                c.Plan.SetHold(new FakeHold { Name = "h" + n }, "attitude");
                c.Plan.AtEnd("cut", r => calls.Add(n + ":" + r + ":" + HoldReleases().Length));
            };
            coordinator.Submit(Plan(ScopeStep(), ScopeStep(), Step(60)), replace: false);
            Run(1);

            coordinator.Abort(EndReasons.PlannerAbort, null);
            coordinator.Abort(EndReasons.PlannerAbort, null);

            CollectionAssert.AreEqual(new[] { "0:planner_abort:2" }, calls);
        }

        [TestMethod]
        public void EndActionException_IsRecorded_AndDoesNotStopTheEnd()
        {
            List<string> calls = new List<string>();
            ScopeStepType.OnBegin = c =>
            {
                c.Plan.AtEnd("bad", r => throw new System.InvalidOperationException("boom"));
                c.Plan.AtEnd("good", r => calls.Add(r));
            };
            coordinator.Submit(Plan(ScopeStep()), replace: false);
            Run(1);

            CollectionAssert.AreEqual(new[] { "completed" }, calls);
            CollectionAssert.AreEqual(new[] { "completed" }, ended);
            Dictionary<string, object> error = events.Since(0, 1000).Single(e => (string)e["type"] == "plan_end_error");
            Assert.AreEqual("bad", ((Dictionary<string, object>)error["data"])["key"]);
        }

        [TestMethod]
        public void PlanEndedInBegin_IsNotCompleted()
        {
            ScopeStepType.OnBegin = c => coordinator.Abort(EndReasons.VesselChange, "staging changed the vessel");
            coordinator.Submit(Plan(ScopeStep()), replace: false);
            Run(1);

            Assert.AreEqual(EndReasons.VesselChange, coordinator.LastSummary["end_reason"]);
            CollectionAssert.AreEqual(new[] { "plan_submitted", "step_started", "plan_aborted" }, EventTypes());
        }

        [TestMethod]
        public void ScopeOfAnEndedPlan_Throws()
        {
            IPlanScope scope = null;
            ScopeStepType.OnBegin = c => scope = c.Plan;
            coordinator.Submit(Plan(ScopeStep()), replace: false);
            Run(1);

            Assert.ThrowsExactly<System.InvalidOperationException>(() => scope.SetHold(new FakeHold(), "x"));
            Assert.ThrowsExactly<System.InvalidOperationException>(() => scope.ReleaseHold("x"));
            Assert.ThrowsExactly<System.InvalidOperationException>(() => scope.AtEnd("x", r => { }));
        }

        private void Frame()
        {
            ut += 0.02;
            coordinator.FixedUpdate();
        }

        private static string ScopeStep() => "{\"type\":\"scope\"}";

        private string[] HoldReleases() => events.Since(0, 1000).Where(e => (string)e["type"] == "hold_released")
            .Select(e => (Dictionary<string, object>)e["data"])
            .Select(d => d["reason"] + ":" + ((Dictionary<string, object>)d["hold"])?["name"]).ToArray();

        private sealed class FakeHold : IPlanHold
        {
            public string Name;
            public int Calls;
            public System.Action OnControl;
            public bool ThrowOnDescribe;

            public void Control(FlightCtrlState state, Vessel vessel)
            {
                Calls++;
                OnControl?.Invoke();
            }

            public Dictionary<string, object> Describe()
            {
                if (ThrowOnDescribe)
                    throw new System.InvalidOperationException("describe failed");
                return new Dictionary<string, object> { ["name"] = Name };
            }
        }

        /// <summary>Runs OnBegin with the context of the step and completes on the same frame (unless Finish is false).</summary>
        private sealed class ScopeStepType : IStepType
        {
            public static System.Action<StepContext> OnBegin;
            public static System.Action OnControl;
            public static bool Finish = true;

            public static void Reset()
            {
                OnBegin = null;
                OnControl = null;
                Finish = true;
            }

            public string Name => "scope";

            public string Describe() => "test step that uses the plan scope";

            public IStepRun Create(Dictionary<string, object> parameters) => new ScopeRun();
        }

        private sealed class ScopeRun : IStepRun
        {
            public void Begin(StepContext context) => ScopeStepType.OnBegin?.Invoke(context);

            public StepResult Judge(StepContext context) => ScopeStepType.Finish ? StepResult.Done : StepResult.Running;

            public void Control(FlightCtrlState state, StepContext context) => ScopeStepType.OnControl?.Invoke();

            public void End(string reason)
            {
            }
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
