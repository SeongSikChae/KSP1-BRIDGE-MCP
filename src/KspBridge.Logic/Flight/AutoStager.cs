using KspBridge.Flight.Modes;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// Staging. This unit handles only on_start: ["stage"] requests; the auto_stage sequence
    /// (burnout → tailoff → separate → settle → ignite) is unit 8. Main thread only.
    /// </summary>
    public static class AutoStager
    {
        private static StageRequest _onStart;
        private static bool _onStartQueued;

        /// <summary>Condition variable "staging".</summary>
        public static string StateName => "none";

        /// <summary>Telemetry.StagingPending: an on_start stage request awaits confirmation (unit 8 adds the sequence).</summary>
        public static bool Pending => _onStart != null || _onStartQueued;

        public static void Update(Vessel v, Telemetry t, Plan plan, double ut, bool send)
        {
            if (_onStart == null)
            {
                return;
            }
            switch (_onStart.Tick(v, ut, send))
            {
                case StageRequest.Result.Done:
                    Staged(v, "on_start");
                    _onStart = TakeQueued();   // a queued request starts ticking from the next call (no second send this tick)
                    break;
                case StageRequest.Result.Failed:
                    FlightEvents.Add("stage_failed", Coordinator.StepIndex, new Dictionary<string, object> { ["stage"] = v.currentStage });
                    _onStart = TakeQueued();   // the step goes on
                    break;
            }
        }

        /// <summary>BeginStep's on_start: ["stage"]. A request already sent and not yet confirmed is kept until its result;
        /// the new one queues behind it. An unsent request is simply replaced.</summary>
        public static void RequestOnStart()
        {
            if (_onStart != null && _onStart.Sent)
            {
                _onStartQueued = true;
            }
            else
            {
                _onStart = new StageRequest();
            }
        }

        private static StageRequest TakeQueued()
        {
            if (!_onStartQueued)
            {
                return null;
            }
            _onStartQueued = false;
            return new StageRequest();
        }

        private static void Staged(Vessel v, string reason) =>
            FlightEvents.Add("staged", Coordinator.StepIndex, new Dictionary<string, object> { ["stage"] = v.currentStage, ["reason"] = reason });

        /// <summary>A request already sent whose stage drop happened before its confirm tick still leaves its staged event.
        /// v = plan vessel (null when the active vessel is not the plan vessel → no event).</summary>
        private static void RecordSent(Vessel v)
        {
            if (v != null && _onStart != null && _onStart.Sent && v.currentStage < _onStart.StageBefore)
            {
                Staged(v, "on_start");
            }
        }

        /// <summary>Release (complete, abort, failure after attach).</summary>
        public static void Reset(Vessel v)
        {
            RecordSent(v);
            _onStart = null;
            _onStartQueued = false;
        }

        /// <summary>Coordinator.Stop.</summary>
        public static void Clear(Vessel v) => Reset(v);

        /// <summary>Tick order (e): unit 8 holds throttle 0 while separating and settling.</summary>
        public static ModeCommand Override(ModeCommand cmd) => cmd;

        public static Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["on_start"] = _onStart?.Save(),
            ["on_start_queued"] = _onStartQueued,
        };

        public static void Load(Dictionary<string, object> d)
        {
            _onStart = StageRequest.Load(d["on_start"] as Dictionary<string, object>);
            _onStartQueued = (bool)d["on_start_queued"];
        }
    }
}
