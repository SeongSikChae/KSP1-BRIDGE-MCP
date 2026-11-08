using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KspBridge.Handlers;

namespace KspBridge.Flight
{
    /// <summary>
    /// Keeps the SAS mode requested through the API (set_sas_mode) and watches it every physics tick: when the game
    /// drops it (vessel modified by staging, crew unconscious, mode unavailable) or SAS stops steering, the event is
    /// recorded and the mode is re-applied as soon as it can be set again. Main thread only.
    /// </summary>
    public static class SasMonitor
    {
        private const int MaxEvents = 10;
        private const double RetryInterval = 0.5;
        private const double ModifiedWindow = 0.5;
        private const int GraceTicks = 3;
        private static int _badTicks;

        private static readonly FieldInfo FbwConnectedField =
            typeof(VesselAutopilot.VesselSAS).GetField("FBWconnected", BindingFlags.Instance | BindingFlags.NonPublic);

        private static Vessel _vessel;
        private static VesselAutopilot.AutopilotMode? _requested;
        private static bool _lost;
        private static double _lastRetryUt = double.NegativeInfinity;
        private static double _lastModifiedUt = double.NegativeInfinity;
        private static Guid _eventsVessel;
        private static readonly List<Dictionary<string, object>> _events = new List<Dictionary<string, object>>();
        private static EventData<Vessel>.OnEvent _onModified;

        public static bool IsActive => _requested.HasValue;

        public static void Hook()
        {
            if (_onModified == null)
            {
                _onModified = v => OnVesselModified(v); // KSP EventData reads the delegate Target: no static method groups
                GameEvents.onVesselWasModified.Add(_onModified);
            }
        }

        public static void Unhook()
        {
            if (_onModified != null)
            {
                GameEvents.onVesselWasModified.Remove(_onModified);
                _onModified = null;
            }
        }

        /// <summary>set_sas_mode succeeded: hold this mode.</summary>
        public static void Request(Vessel v, VesselAutopilot.AutopilotMode mode)
        {
            ResetEventsFor(v);
            _vessel = v;
            _requested = mode;
            _lost = false;
            _badTicks = 0;
        }

        /// <summary>
        /// Sets the mode so that it sticks. With the autopilot not yet enabled (SAS just switched on), SetMode only stores
        /// the mode and the next VesselAutopilot.Update calls Enable() = Enable(StabilityAssist), overwriting it; so enable
        /// directly with the wanted mode in that case. Returns true when the autopilot is enabled in that mode.
        /// </summary>
        public static bool Apply(Vessel v, VesselAutopilot.AutopilotMode mode)
        {
            if (!v.ActionGroups[KSPActionGroup.SAS])
            {
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }
            if (!v.Autopilot.Enabled)
            {
                v.Autopilot.Enable(mode);
            }
            else
            {
                v.Autopilot.SetMode(mode);
            }
            return v.Autopilot.Enabled && v.Autopilot.Mode == mode;
        }

        /// <summary>set_sas(false), set_attitude, vessel/scene change, reload.</summary>
        public static void Clear()
        {
            _vessel = null;
            _requested = null;
            _lost = false;
            _badTicks = 0;
        }

        public static void FixedUpdate()
        {
            if (_requested == null)
            {
                return;
            }
            Vessel v = _vessel;
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || v == null || FlightGlobals.ActiveVessel != v || v.state == Vessel.State.DEAD)
            {
                Clear();
                return;
            }
            VesselAutopilot.AutopilotMode want = _requested.Value;
            if ((v.Landed || v.Splashed)
                && (want == VesselAutopilot.AutopilotMode.Prograde || want == VesselAutopilot.AutopilotMode.Retrograde))
            {
                // On the ground the velocity vector follows the vessel tipping over: re-applying would steer it down.
                Record(v, "released", new Dictionary<string, object>
                {
                    ["mode"] = SasModes.ToName(want),
                    ["reason"] = v.Splashed ? "splashed" : "landed",
                });
                Clear();
                return;
            }
            bool sasOn = v.ActionGroups[KSPActionGroup.SAS];
            bool? steering = Steering(v);
            bool ok = sasOn && v.Autopilot.Mode == want && steering != false;
            double now = Planetarium.GetUniversalTime();

            if (ok)
            {
                _lost = false;
                _badTicks = 0;
                return;
            }
            // Grace: right after staging a non-locked mode is disconnected until the next render Update reconnects it.
            if (++_badTicks < GraceTicks)
            {
                return;
            }
            if (!_lost)
            {
                _lost = true;
                string evt = !sasOn || v.Autopilot.Mode != want ? "mode_lost" : "steering_lost";
                Record(v, evt, new Dictionary<string, object>
                {
                    ["from"] = SasModes.ToName(want),
                    ["to"] = sasOn ? SasModes.ToName(v.Autopilot.Mode) : "off",
                    ["reason"] = Reason(v, want, now),
                });
            }
            // Stock skips the autopilot while the vessel is uncontrollable (crew blacked out) or packed; wait like it does.
            if (now - _lastRetryUt < RetryInterval || !v.IsControllable || v.packed || !v.Autopilot.CanSetMode(want))
            {
                return;
            }
            _lastRetryUt = now;
            if (Apply(v, want))
            {
                Record(v, "reapplied", new Dictionary<string, object> { ["mode"] = SasModes.ToName(want) });
            }
        }

        /// <summary>"sas" block of GET /api/vessel.</summary>
        public static Dictionary<string, object> Describe(Vessel v)
        {
            ResetEventsFor(v);
            bool mine = _requested.HasValue && _vessel == v;
            List<object> available = new List<object>();
            foreach (VesselAutopilot.AutopilotMode m in Enum.GetValues(typeof(VesselAutopilot.AutopilotMode)))
            {
                if (v.Autopilot.CanSetMode(m))
                {
                    available.Add(SasModes.ToName(m));
                }
            }
            return new Dictionary<string, object>
            {
                ["on"] = v.ActionGroups[KSPActionGroup.SAS],
                ["requested"] = mine ? SasModes.ToName(_requested.Value) : null,
                ["actual"] = SasModes.ToName(v.Autopilot.Mode),
                ["hold"] = mine,
                ["autopilot_enabled"] = v.Autopilot.Enabled,
                ["steering"] = Steering(v),
                ["available_modes"] = available,
                ["events"] = _events.Cast<object>().ToList(),
            };
        }

        private static void OnVesselModified(Vessel v)
        {
            if (v != null && v == FlightGlobals.ActiveVessel)
            {
                _lastModifiedUt = Planetarium.GetUniversalTime();
            }
        }

        /// <summary>SAS feeding control input: autopilot enabled and fly-by-wire connected (private; null if unreadable).</summary>
        internal static bool? Steering(Vessel v)
        {
            if (!v.ActionGroups[KSPActionGroup.SAS] || !v.Autopilot.Enabled)
            {
                return false;
            }
            if (FbwConnectedField == null || v.Autopilot.SAS == null)
            {
                return null;
            }
            return (bool)FbwConnectedField.GetValue(v.Autopilot.SAS);
        }

        private static string Reason(Vessel v, VesselAutopilot.AutopilotMode want, double now)
        {
            if (now - _lastModifiedUt <= ModifiedWindow)
            {
                return "vessel_modified";
            }
            List<ProtoCrewMember> crew = v.GetVesselCrew();
            if (crew.Count > 0 && crew.All(c => c.inactive))
            {
                return "crew_unconscious";
            }
            if (!v.Autopilot.CanSetMode(want))
            {
                return "mode_unavailable";
            }
            return "unknown";
        }

        private static void Record(Vessel v, string evt, Dictionary<string, object> extra)
        {
            ResetEventsFor(v);
            Dictionary<string, object> e = new Dictionary<string, object>
            {
                ["ut"] = Planetarium.GetUniversalTime(),
                ["met"] = v.missionTime,
                ["event"] = evt,
            };
            foreach (KeyValuePair<string, object> kv in extra)
            {
                e[kv.Key] = kv.Value;
            }
            _events.Add(e);
            FlightRecorder.SasEvent(v, e);
            if (_events.Count > MaxEvents)
            {
                _events.RemoveAt(0);
            }
        }

        private static void ResetEventsFor(Vessel v)
        {
            if (v != null && v.id != _eventsVessel)
            {
                _events.Clear();
                _eventsVessel = v.id;
            }
        }
    }
}
