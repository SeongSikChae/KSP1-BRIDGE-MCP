using KspBridge.Flight.Modes;
using System;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// Turns the Coordinator's command into FlightCtrlState through vessel.OnFlyByWire. This unit: attach/detach, SAS handling
    /// and throttle; the attitude PID is unit 4. Main thread only.
    /// </summary>
    public static class AttitudeController
    {
        private static Vessel _v;
        private static bool _sasBefore;

        /// <summary>The vessel the controller is attached to (null when detached).</summary>
        public static Vessel Target => _v;

        /// <param name="sasBefore">Resume: SAS state handed over from the previous logic (SAS is already off by then).</param>
        public static void Attach(Vessel v, bool? sasBefore = null)
        {
            if (_v == v)
            {
                return;   // a plan replacement does not re-attach: the original SAS state stays the one to restore
            }
            Detach();
            _sasBefore = sasBefore ?? v.ActionGroups[KSPActionGroup.SAS];
            _v = v;
            v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            v.OnFlyByWire += Fly;
        }

        public static void Detach()
        {
            if (_v == null)
            {
                return;
            }
            _v.OnFlyByWire -= Fly;
            _v = null;
        }

        /// <summary>Applies the SAS state recorded at Attach; call before Detach.</summary>
        public static void RestoreSas()
        {
            _v?.ActionGroups.SetGroup(KSPActionGroup.SAS, _sasBefore);
        }

        public static void ZeroThrottle()
        {
            FlightInputHandler.state.mainThrottle = 0;
            if (_v != null)
            {
                _v.ctrlState.mainThrottle = 0;   // the previous target keeps no throttle when the active vessel changed
            }
        }

        private static void Fly(FlightCtrlState s)
        {
            try
            {
                if (!Coordinator.Running || _v == null)
                {
                    return;   // no input unless a plan runs (e.g. a replacement that failed half way)
                }
                if (_v.ActionGroups[KSPActionGroup.SAS])
                {
                    _v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);   // the user turning SAS on is undone every tick
                }
                ModeCommand c = Coordinator.Command;
                float throttle = (float)c.Throttle;
                s.mainThrottle = throttle;
                FlightInputHandler.state.mainThrottle = throttle;   // kept when the next tick copies the input state into ctrlState
                // attitude: unit 4
            }
            catch (Exception e)
            {
                Coordinator.Abort("error", null, e.Message);
            }
        }
    }
}
