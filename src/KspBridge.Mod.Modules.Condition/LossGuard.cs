using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>
    /// Ends the plan when its vessel is destroyed (always) or loses a part (abort_on.part_loss, on by default): a part explodes, or
    /// a joint breaks under force. Staging and undocking raise neither event. Game events are only collected here; Check, on the
    /// Coordinator's next judgement, records parts_lost and reports the trip.
    /// </summary>
    internal sealed class LossGuard : IPlanGuard
    {
        private static readonly GuardOption[] Keys = { new GuardOption(EndReasons.PartLoss, true) };

        private readonly EventLog events;
        private readonly LossLog log = new LossLog();
        private Vessel vessel;
        private int planId;
        private string failure;

        public LossGuard(Subscriptions subscriptions, EventLog events)
        {
            this.events = events;
            subscriptions.Add(GameEvents.onPartWillDie, OnPartWillDie);
            subscriptions.Add(GameEvents.onJointBreak, OnJointBreak);
            subscriptions.Add(GameEvents.onVesselWillDestroy, OnVesselWillDestroy);
            subscriptions.Add(GameEvents.onCrash, OnCrash);
            subscriptions.Add(GameEvents.onCollision, OnCollision);
            subscriptions.Add(GameEvents.onCrashSplashdown, OnCrashSplashdown);
            subscriptions.Add(GameEvents.onOverheat, OnOverheat);
            subscriptions.Add(GameEvents.onOverG, OnOverG);
            subscriptions.Add(GameEvents.onOverPressure, OnOverPressure);
        }

        public IReadOnlyList<GuardOption> Options => Keys;

        public int Priority => 0;

        public void Start(int plan)
        {
            planId = plan;
            failure = null;
            vessel = FlightGlobals.ActiveVessel;
            log.Start();
        }

        public GuardTrip Check(Func<string, bool> enabled)
        {
            LossReport report = TakeAndRecord();
            if (report.VesselDestroyed)
                return new GuardTrip(EndReasons.VesselDestroyed, report.Describe());
            if (report.Losses.Count > 0 && enabled(EndReasons.PartLoss))
                return new GuardTrip(EndReasons.PartLoss, report.Describe());
            if (failure != null)
            {
                string message = failure;
                failure = null;
                throw new InvalidOperationException(message);
            }
            return null;
        }

        public void Stop()
        {
            try
            {
                TakeAndRecord();
            }
            finally
            {
                log.Stop();
                vessel = null;
                failure = null;
            }
        }

        private LossReport TakeAndRecord()
        {
            LossReport report = log.Take();
            if (report.Losses.Count > 0)
                events.Add("parts_lost", planId, report.ToEvent());
            return report;
        }

        private void OnPartWillDie(Part part) => Collect(() => AddLoss(part, Loss.Exploded));

        private void OnJointBreak(EventReport report) => Collect(() => AddLoss(report.origin, Loss.BrokeOff));

        private void OnVesselWillDestroy(Vessel destroyed) => Collect(() =>
        {
            if (ReferenceEquals(destroyed, vessel))
                log.AddVesselDestroyed();
        });

        private void OnCrash(EventReport report) => AddCause(report, "crash");

        private void OnCollision(EventReport report) => AddCause(report, "collision");

        private void OnCrashSplashdown(EventReport report) => AddCause(report, "crash_splashdown");

        private void OnOverheat(EventReport report) => AddCause(report, "overheat");

        private void OnOverG(EventReport report) => AddCause(report, "over_g");

        private void OnOverPressure(EventReport report) => AddCause(report, "over_pressure");

        /// <summary>
        /// Counts the part when it belongs to the plan's vessel now; one that came off earlier belongs to another vessel. Compared by
        /// reference, because Unity calls a destroyed object equal to null.
        /// </summary>
        private void AddLoss(Part part, string kind)
        {
            if (ReferenceEquals(part, null) || !ReferenceEquals(part.vessel, vessel))
                return;
            log.Add(part.GetInstanceID(), part.partInfo != null ? part.partInfo.name : null, kind);
        }

        private void AddCause(EventReport report, string cause) => Collect(() =>
        {
            if (!ReferenceEquals(report.origin, null))
                log.AddCause(report.origin.GetInstanceID(), cause);
        });

        /// <summary>Never throws into the game's event; a failure is reported by the next Check.</summary>
        private void Collect(Action action)
        {
            if (!log.Running || ReferenceEquals(vessel, null))
                return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = failure ?? ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
