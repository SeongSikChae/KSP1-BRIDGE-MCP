using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Core.Tests
{
    /// <summary>Plan guards and abort_on (spec C3, C5, C6, C9, C11).</summary>
    [TestClass]
    public sealed class CoordinatorGuardTests
    {
        private double ut;
        private StepRegistry registry;
        private EventLog events;
        private GuardRegistry guards;
        private Coordinator coordinator;
        private StubStepType step;

        [TestInitialize]
        public void Init()
        {
            registry = new StepRegistry();
            step = new StubStepType();
            registry.Add(step);
            events = new EventLog(() => ut);
            guards = new GuardRegistry();
            coordinator = new Coordinator(registry, events, () => ut, () => null, () => null, () => 1, guards);
        }

        private static string Step(string extra = "") => "{\"type\":\"stub\"" + extra + "}";

        private static Dictionary<string, object> Plan(string abortOn, params string[] steps)
            => Json.ParseObject("{\"steps\":[" + string.Join(",", steps) + "]" + (abortOn == null ? "" : ",\"abort_on\":" + abortOn) + "}");

        private void Frame()
        {
            ut += 0.02;
            coordinator.FixedUpdate();
        }

        private FakeGuard AddGuard(int priority = 0, params GuardOption[] options)
        {
            FakeGuard guard = new FakeGuard { Priority = priority, Options = options };
            guards.Add(guard);
            return guard;
        }

        private string[] EventTypes() => events.Since(0, 1000).Select(e => (string)e["type"]).ToArray();

        private object EndReason => coordinator.LastSummary["end_reason"];

        private string EndDetail => (string)coordinator.LastSummary["end_detail"];

        private static Dictionary<string, object> AbortOn(Dictionary<string, object> summary)
            => (Dictionary<string, object>)summary["abort_on"];

        // ---- abort_on

        [TestMethod]
        [DataRow(null)]
        [DataRow("null")]
        [DataRow("{}")]
        public void AbortOn_LeftOut_UsesTheDefaults(string abortOn)
        {
            AddGuard(0, new GuardOption("part_loss", true));
            AddGuard(1, new GuardOption("no_thrust", false));

            Dictionary<string, object> answer = coordinator.Submit(Plan(abortOn, Step()), replace: false);

            CollectionAssert.AreEquivalent(new[] { "part_loss", "no_thrust" }, AbortOn(answer).Keys.ToArray());
            Assert.AreEqual(true, AbortOn(answer)["part_loss"]);
            Assert.AreEqual(false, AbortOn(answer)["no_thrust"]);
        }

        [TestMethod]
        public void AbortOn_IsShownInEverySummary_AndReachesTheGuard()
        {
            FakeGuard guard = AddGuard(0, new GuardOption("part_loss", true), new GuardOption("no_thrust", false));
            List<string> seen = new List<string>();
            guard.OnCheck = on =>
            {
                seen.Add(on("part_loss") + "/" + on("no_thrust") + "/" + on("other"));
                return null;
            };
            coordinator.Submit(Plan("{\"part_loss\":false,\"no_thrust\":true}", Step()), replace: false);
            Assert.AreEqual(false, AbortOn(coordinator.CurrentSummary)["part_loss"]);
            Assert.AreEqual(true, AbortOn(coordinator.CurrentSummaryWithProgress())["no_thrust"]);

            Frame();
            step.Finish = true;
            Frame();

            Assert.AreEqual("False/True/False", seen[0]);
            Assert.AreEqual(false, AbortOn(coordinator.LastSummary)["part_loss"]);
            Assert.AreEqual(true, AbortOn(coordinator.LastSummary)["no_thrust"]);
        }

        [TestMethod]
        [DataRow("[]")]
        [DataRow("true")]
        [DataRow("{\"other\":true}")]
        [DataRow("{\"part_loss\":1}")]
        [DataRow("{\"part_loss\":\"true\"}")]
        [DataRow("{\"part_loss\":null}")]
        public void AbortOn_Bad_IsBadRequest_AndNothingStarts(string abortOn)
        {
            FakeGuard guard = AddGuard(0, new GuardOption("part_loss", true));

            BridgeException ex = Assert.ThrowsExactly<BridgeException>(
                () => coordinator.Submit(Plan(abortOn, Step()), replace: false));

            Assert.AreEqual("bad_request", ex.Code);
            Assert.IsFalse(coordinator.Running);
            Assert.AreEqual(0, guard.Started.Count);
        }

        [TestMethod]
        public void WithoutGuards_AnyKeyIsUnknown_AndTheSummaryIsEmpty()
        {
            Assert.ThrowsExactly<BridgeException>(() => coordinator.Submit(Plan("{\"part_loss\":true}", Step()), replace: false));
            Assert.AreEqual(0, AbortOn(coordinator.Submit(Plan(null, Step()), replace: false)).Count);
        }

        [TestMethod]
        public void TwoGuardsWithTheSameKey_AreRefused()
        {
            AddGuard(0, new GuardOption("part_loss", true));
            Assert.ThrowsExactly<InvalidOperationException>(() => AddGuard(1, new GuardOption("part_loss", true)));
            Assert.AreEqual(1, guards.Guards.Count);
        }

        // ---- judging

        [TestMethod]
        public void Trip_EndsThePlan_WithItsReasonAndDetail()
        {
            FakeGuard guard = AddGuard();
            coordinator.Submit(Plan(null, Step(), Step()), replace: false);
            Frame();
            guard.Trip = new GuardTrip(EndReasons.PartLoss, "2 parts lost");
            Frame();

            Assert.IsFalse(coordinator.Running);
            Assert.AreEqual(EndReasons.PartLoss, EndReason);
            Assert.AreEqual("2 parts lost", EndDetail);
            Assert.AreEqual("aborted", coordinator.LastSummary["state"]);
            Assert.AreEqual(EndReasons.PartLoss, step.EndReason);
            Dictionary<string, object> data = (Dictionary<string, object>)events.Since(0, 1000).Last()["data"];
            Assert.AreEqual("plan_aborted", events.Since(0, 1000).Last()["type"]);
            Assert.AreEqual("2 parts lost", data["detail"]);
        }

        [TestMethod]
        public void LowestPriorityNumberWins_AndEveryGuardIsAsked()
        {
            FakeGuard second = AddGuard(1);
            FakeGuard first = AddGuard(0);
            second.Trip = new GuardTrip(EndReasons.NoThrust, "b");
            first.Trip = new GuardTrip(EndReasons.VesselDestroyed, "a");
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();

            Assert.AreEqual(EndReasons.VesselDestroyed, EndReason);
            Assert.AreEqual(1, first.Checks);
            Assert.AreEqual(1, second.Checks);
        }

        [TestMethod]
        public void Trip_ComesBeforeTimeout_Completion_AndControlErrors()
        {
            FakeGuard guard = AddGuard();
            coordinator.Submit(Plan(null, Step(",\"timeout_s\":0.01")), replace: false);
            Frame();
            step.Finish = true;
            step.ThrowOnControl = true;
            coordinator.Control(null);
            guard.Trip = new GuardTrip(EndReasons.PartLoss, "x");
            Frame();

            Assert.AreEqual(EndReasons.PartLoss, EndReason);
        }

        [TestMethod]
        public void Trip_BetweenSteps_DoesNotBeginTheNextStep()
        {
            FakeGuard guard = AddGuard();
            step.Finish = true;
            coordinator.Submit(Plan(null, Step(), Step()), replace: false);
            Frame();
            Assert.AreEqual(1, step.Begun);
            guard.Trip = new GuardTrip(EndReasons.PartLoss, "x");
            Frame();

            Assert.AreEqual(1, step.Begun);
            Assert.AreEqual(EndReasons.PartLoss, EndReason);
            Assert.AreEqual(1, EventTypes().Count(t => t == "step_started"));
        }

        [TestMethod]
        public void TroubleOfTheLastStep_IsSeenBeforeThePlanCompletes()
        {
            FakeGuard guard = AddGuard();
            step.Finish = true;
            step.OnBegin = () => guard.Trip = new GuardTrip(EndReasons.PartLoss, "staging broke it");
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();

            Assert.AreEqual(EndReasons.PartLoss, EndReason);
            Dictionary<string, object> only = (Dictionary<string, object>)((List<object>)coordinator.LastSummary["steps"])[0];
            Assert.AreEqual("aborted", only["state"]);
            CollectionAssert.DoesNotContain(EventTypes(), "plan_completed");
        }

        [TestMethod]
        public void StepsBeforeTheLast_CompleteWithOneCheckPerFrame()
        {
            FakeGuard guard = AddGuard();
            step.Finish = true;
            coordinator.Submit(Plan(null, Step(), Step()), replace: false);
            Frame();
            Assert.AreEqual(1, guard.Checks);
            Frame();
            Assert.AreEqual(3, guard.Checks, "the last step is checked again before the plan completes");
            Assert.AreEqual(EndReasons.Completed, EndReason);
        }

        [TestMethod]
        public void Timeout_ComesBeforeTheStepsOwnCompletion()
        {
            coordinator.Submit(Plan(null, Step(",\"timeout_s\":0.01")), replace: false);
            Frame();
            step.Finish = true;   // the step's condition is met on the frame its time is up
            Frame();

            Assert.AreEqual(EndReasons.Timeout, EndReason);
        }

        // ---- start, stop

        [TestMethod]
        public void EveryPlan_StartsAndStopsTheGuardsOnce()
        {
            FakeGuard guard = AddGuard();
            step.Finish = true;
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();
            coordinator.Submit(Plan(null, Step()), replace: false);
            coordinator.Abort(EndReasons.PlannerAbort, null);
            coordinator.Abort(EndReasons.PlannerAbort, null);

            CollectionAssert.AreEqual(new[] { 1, 2 }, guard.Started);
            Assert.AreEqual(2, guard.Stopped);
        }

        [TestMethod]
        public void Replace_StopsForTheOldPlan_BeforeStartingForTheNew()
        {
            FakeGuard guard = AddGuard();
            List<string> calls = new List<string>();
            guard.OnStart = id => calls.Add("start" + id);
            guard.OnStop = () => calls.Add("stop");
            coordinator.Submit(Plan(null, Step()), replace: false);
            coordinator.Submit(Plan(null, Step()), replace: true);

            CollectionAssert.AreEqual(new[] { "start1", "stop", "start2" }, calls);
        }

        [TestMethod]
        public void Stop_RunsAfterTheEndActions_AndBeforeThePlanEndEvent()
        {
            FakeGuard guard = AddGuard();
            guard.OnStop = () => events.Add("guard_stop");
            step.OnBeginContext = c => c.Plan.AtEnd("cut", r => events.Add("end_action"));
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();
            coordinator.Abort(EndReasons.Reload, null);

            string[] types = EventTypes();
            CollectionAssert.AreEqual(new[] { "end_action", "guard_stop", "plan_aborted" }, types.Skip(types.Length - 3).ToArray());
        }

        [TestMethod]
        public void NoPlan_NoCheck()
        {
            FakeGuard guard = AddGuard();
            guard.Trip = new GuardTrip(EndReasons.PartLoss, "x");
            Frame();
            Assert.AreEqual(0, guard.Checks);
        }

        // ---- exceptions

        [TestMethod]
        public void CheckException_FailsTheStep_AfterAskingTheOthers()
        {
            FakeGuard bad = AddGuard(0);
            FakeGuard good = AddGuard(1);
            bad.OnCheck = on => throw new InvalidOperationException("boom");
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();

            Assert.AreEqual(EndReasons.StepFailed, EndReason);
            StringAssert.Contains(EndDetail, "guard error: InvalidOperationException: boom");
            Assert.AreEqual(1, good.Checks);
            Assert.AreEqual(0, step.Begun);
        }

        [TestMethod]
        public void CheckException_DoesNotHideATrip()
        {
            FakeGuard bad = AddGuard(0);
            FakeGuard good = AddGuard(1);
            bad.OnCheck = on => throw new InvalidOperationException("boom");
            good.Trip = new GuardTrip(EndReasons.NoThrust, "x");
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();

            Assert.AreEqual(EndReasons.NoThrust, EndReason);
        }

        [TestMethod]
        public void StartException_FailsOnlyThatPlan()
        {
            FakeGuard guard = AddGuard();
            guard.OnStart = id =>
            {
                if (id == 1)
                    throw new InvalidOperationException("boom");
            };
            coordinator.Submit(Plan(null, Step()), replace: false);
            Assert.IsTrue(coordinator.Running);
            Frame();
            Assert.AreEqual(EndReasons.StepFailed, EndReason);
            StringAssert.Contains(EndDetail, "guard error: InvalidOperationException: boom");

            step.Finish = true;
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();
            Assert.AreEqual(EndReasons.Completed, EndReason);
        }

        [TestMethod]
        public void StopException_IsRecorded_AndDoesNotStopTheEnd()
        {
            FakeGuard bad = AddGuard(0);
            FakeGuard good = AddGuard(1);
            bad.OnStop = () => throw new InvalidOperationException("boom");
            coordinator.Submit(Plan(null, Step()), replace: false);
            coordinator.Abort(EndReasons.PlannerAbort, null);

            Assert.IsFalse(coordinator.Running);
            Assert.AreEqual(1, good.Stopped);
            Dictionary<string, object> error = events.Since(0, 1000).Single(e => (string)e["type"] == "plan_guard_error");
            Assert.AreEqual("InvalidOperationException: boom", ((Dictionary<string, object>)error["data"])["message"]);
            Assert.AreEqual(1, error["plan_id"]);
        }

        // ---- step context

        [TestMethod]
        public void Steps_KnowTheirPlanAndPlace()
        {
            List<string> seen = new List<string>();
            step.Finish = true;
            step.OnBeginContext = c => seen.Add(c.PlanId + ":" + c.StepIndex);
            coordinator.Submit(Plan(null, Step(), Step()), replace: false);
            Frame();
            Frame();
            coordinator.Submit(Plan(null, Step()), replace: false);
            Frame();

            CollectionAssert.AreEqual(new[] { "1:0", "1:1", "2:0" }, seen);
        }

        private sealed class FakeGuard : IPlanGuard
        {
            public IReadOnlyList<GuardOption> Options { get; set; } = new GuardOption[0];
            public int Priority { get; set; }

            public GuardTrip Trip;
            public int Checks;
            public int Stopped;
            public readonly List<int> Started = new List<int>();
            public Action<int> OnStart;
            public Func<Func<string, bool>, GuardTrip> OnCheck;
            public Action OnStop;

            public void Start(int planId)
            {
                Started.Add(planId);
                OnStart?.Invoke(planId);
            }

            public GuardTrip Check(Func<string, bool> enabled)
            {
                Checks++;
                return OnCheck != null ? OnCheck(enabled) : Trip;
            }

            public void Stop()
            {
                Stopped++;
                OnStop?.Invoke();
            }
        }

        /// <summary>A step that runs until Finish; every run shares this type's fields.</summary>
        private sealed class StubStepType : IStepType
        {
            public bool Finish;
            public bool ThrowOnControl;
            public int Begun;
            public string EndReason;
            public Action OnBegin;
            public Action<StepContext> OnBeginContext;

            public string Name => "stub";

            public string Describe() => "test step";

            public IStepRun Create(Dictionary<string, object> parameters) => new StubRun(this);
        }

        private sealed class StubRun : IStepRun
        {
            private readonly StubStepType type;

            public StubRun(StubStepType type) => this.type = type;

            public void Begin(StepContext context)
            {
                type.Begun++;
                type.OnBegin?.Invoke();
                type.OnBeginContext?.Invoke(context);
            }

            public StepResult Judge(StepContext context) => type.Finish ? StepResult.Done : StepResult.Running;

            public void Control(FlightCtrlState state, StepContext context)
            {
                if (type.ThrowOnControl)
                    throw new InvalidOperationException("control failed");
            }

            public void End(string reason) => type.EndReason = reason;
        }
    }
}
