using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// SAS and RCS outside a flight plan (set_sas, set_rcs, rcs_translate) and their state for get_flight/get_status.
    /// Main thread only. Refused while a flight plan runs: the plan owns SAS (off) and RCS (step key rcs).
    /// </summary>
    public static class ManualControl
    {
        private static readonly (string Name, VesselAutopilot.AutopilotMode Mode)[] Modes =
        {
            ("stability", VesselAutopilot.AutopilotMode.StabilityAssist),
            ("prograde", VesselAutopilot.AutopilotMode.Prograde),
            ("retrograde", VesselAutopilot.AutopilotMode.Retrograde),
            ("normal", VesselAutopilot.AutopilotMode.Normal),
            ("antinormal", VesselAutopilot.AutopilotMode.Antinormal),
            ("radial_in", VesselAutopilot.AutopilotMode.RadialIn),
            ("radial_out", VesselAutopilot.AutopilotMode.RadialOut),
            ("target", VesselAutopilot.AutopilotMode.Target),
            ("anti_target", VesselAutopilot.AutopilotMode.AntiTarget),
            ("maneuver", VesselAutopilot.AutopilotMode.Maneuver),
        };

        /// <summary>Tool name of a mode; null for a value this table does not know (never throws: the handover uses it).</summary>
        public static string ModeName(VesselAutopilot.AutopilotMode mode)
        {
            foreach ((string name, VesselAutopilot.AutopilotMode m) in Modes)
            {
                if (m == mode)
                {
                    return name;
                }
            }
            return null;
        }

        /// <summary>Mode of a tool name; null for null or an unknown name.</summary>
        public static VesselAutopilot.AutopilotMode? ParseModeOrNull(string name)
        {
            foreach ((string n, VesselAutopilot.AutopilotMode m) in Modes)
            {
                if (n == name)
                {
                    return m;
                }
            }
            return null;
        }

        private static VesselAutopilot.AutopilotMode ParseMode(string name) => ParseModeOrNull(name)
            ?? throw ApiException.BadRequest("mode must be one of " + string.Join(", ", Modes.Select(x => x.Name)));

        private static string SpeedModeName(FlightGlobals.SpeedDisplayModes m) =>
            m == FlightGlobals.SpeedDisplayModes.Orbit ? "orbit" : m == FlightGlobals.SpeedDisplayModes.Surface ? "surface" : "target";

        private static FlightGlobals.SpeedDisplayModes ParseReference(Vessel v, string reference)
        {
            FlightGlobals.SpeedDisplayModes m = reference == "orbit" ? FlightGlobals.SpeedDisplayModes.Orbit
                : reference == "surface" ? FlightGlobals.SpeedDisplayModes.Surface
                : reference == "target" ? FlightGlobals.SpeedDisplayModes.Target
                : throw ApiException.BadRequest("reference must be orbit, surface or target");
            if (m == FlightGlobals.SpeedDisplayModes.Target && v.targetObject == null)   // a destroyed target is not caught (stock TargetLockInvalid alike)
            {
                throw new ApiException(409, "sas_mode_unavailable", "reference target needs a target");
            }
            return m;
        }

        /// <summary>Modes CanSetMode allows now (it depends on the navball speed display mode).</summary>
        private static List<string> Available(Vessel v)
        {
            VesselAutopilot ap = v.Autopilot;
            return Modes.Where(x => ap.CanSetMode(x.Mode)).Select(x => x.Name).ToList();
        }

        /// <summary>The checks every manual control tool shares.</summary>
        private static void RequireManual(Vessel v)
        {
            FlightReset.RequireNoPendingRecovery();
            FlightReset.RequireSceneReady();
            if (Coordinator.Running)
            {
                throw new ApiException(409, "plan_running", "a flight plan is running (abort_flight first)");
            }
            if (v.isEVA)
            {
                throw new ApiException(409, "eva_not_supported", "EVA kerbals are not supported");
            }
            if (!v.IsControllable)
            {
                throw new ApiException(409, "not_controllable", "the vessel cannot be controlled (no pilot, probe core or signal)");
            }
        }

        /// <summary>
        /// set_sas. Atomic: the display mode, the SAS group and the autopilot state are recorded first and put back on any failure.
        /// Turning on calls Autopilot.Enable(mode) directly: SetMode on a disabled autopilot only records the mode, and the next
        /// VesselAutopilot.Update turns it on with Enable() = stability (VesselAutopilot.cs 2295~2323, 2499~2534).
        /// </summary>
        public static object SetSas(Vessel v, RequestBody b)
        {
            RequireManual(v);
            bool on = b.GetBool("on");
            string name = b.Has("mode") ? b.GetString("mode") : null;
            string reference = b.Has("reference") ? b.GetString("reference") : null;
            if (!on && (name != null || reference != null))
            {
                throw ApiException.BadRequest("mode and reference need on: true");
            }
            VesselAutopilot.AutopilotMode? m = name == null ? (VesselAutopilot.AutopilotMode?)null : ParseMode(name);
            FlightGlobals.SpeedDisplayModes? speed = reference == null ? (FlightGlobals.SpeedDisplayModes?)null : ParseReference(v, reference);

            VesselAutopilot ap = v.Autopilot;
            SasSnapshot before = SasSnapshot.Take(v);
            try
            {
                if (!on)
                {
                    SetGroupChecked(v, KSPActionGroup.SAS, false);
                    return DescribeSas(v);
                }
                bool running = before.Enabled && before.Group;
                VesselAutopilot.AutopilotMode want = m ?? (running ? before.Mode : VesselAutopilot.AutopilotMode.StabilityAssist);
                if (speed.HasValue && speed.Value != FlightGlobals.speedDisplayMode)
                {
                    FlightGlobals.SetSpeedMode(speed.Value);
                }
                if (!ap.CanSetMode(want))
                {
                    throw Unavailable(v, want);   // judged once, right after the display mode change
                }
                if (!running)
                {
                    SetGroupChecked(v, KSPActionGroup.SAS, true);
                    if (!ap.Enable(want))
                    {
                        throw Unavailable(v, want);
                    }
                }
                else if (want != ap.Mode && !ap.SetMode(want))
                {
                    throw Unavailable(v, want);
                }
                return DescribeSas(v);
            }
            catch (Exception e)
            {
                if (!before.Restore(v))
                {
                    // the group could not be put back (cooldown): say so instead of promising "unchanged"
                    string left = "; SAS group left " + (v.ActionGroups[KSPActionGroup.SAS] ? "on" : "off") + " (could not restore: cooldown or override)";
                    throw e is ApiException a ? new ApiException(a.Status, a.Code, a.Message + left) : new ApiException(500, "error", e.Message + left);
                }
                throw;
            }
        }

        /// <summary>Error code by the mode actually judged: stability means SAS itself is unavailable.</summary>
        private static ApiException Unavailable(Vessel v, VesselAutopilot.AutopilotMode judged) =>
            judged == VesselAutopilot.AutopilotMode.StabilityAssist
                ? new ApiException(409, "sas_unavailable", "SAS is not available on this vessel")
                : new ApiException(409, "sas_mode_unavailable", ModeName(judged) + " is not available now (speed display "
                    + SpeedModeName(FlightGlobals.speedDisplayMode) + "; available: " + string.Join(", ", Available(v)) + ")");

        /// <summary>
        /// SetGroup goes through ToggleGroup. An override group (ADDITIONAL_ACTION_GROUPS) toggles only its own list and leaves the
        /// group itself unchanged (ActionGroupList.cs 106~148), which cannot be undone afterwards: refused before touching it.
        /// A cooldown changes nothing, so it is found by reading the group back.
        /// </summary>
        internal static void SetGroupChecked(Vessel v, KSPActionGroup g, bool on)
        {
            if (!TrySetGroup(v, g, on, out string blocked))
            {
                throw new ApiException(409, "group_locked", g + " " + blocked);
            }
        }

        /// <summary>
        /// The single path for every action group change (tools, plan step rcs, restores): override check → SetGroup → read back.
        /// True when the group ends up as wanted. Never touches a group held by an override (that would only flip the user's
        /// override list). blocked says why it failed.
        /// </summary>
        internal static bool TrySetGroup(Vessel v, KSPActionGroup g, bool on, out string blocked)
        {
            blocked = null;
            if (v.ActionGroups[g] == on)
            {
                return true;
            }
            if (Overridden(v, g))
            {
                blocked = "is held by action group override " + v.GroupOverride;
                return false;
            }
            v.ActionGroups.SetGroup(g, on);
            if (v.ActionGroups[g] != on)
            {
                blocked = "did not change (cooldown)";
                return false;
            }
            return true;
        }

        internal static bool Overridden(Vessel v, KSPActionGroup g)
        {
            int n = v.GroupOverride;
            return GameSettings.ADDITIONAL_ACTION_GROUPS && n > 0 && v.OverrideDefault != null && n <= v.OverrideDefault.Length
                && (v.OverrideDefault[n - 1] || (v.OverrideActionControl[n - 1] & g) != 0);
        }

        /// <summary>The state set_sas puts back on failure. Restore only touches what differs: Enable on a running autopilot
        /// re-initializes it and re-locks stability to the current attitude.</summary>
        private sealed class SasSnapshot
        {
            public FlightGlobals.SpeedDisplayModes Speed;
            public bool Group;
            public bool Enabled;
            public VesselAutopilot.AutopilotMode Mode;

            public static SasSnapshot Take(Vessel v) => new SasSnapshot
            {
                Speed = FlightGlobals.speedDisplayMode,
                Group = v.ActionGroups[KSPActionGroup.SAS],
                Enabled = v.Autopilot.Enabled,
                Mode = v.Autopilot.Mode,
            };

            /// <summary>Never throws. False when the SAS group could not be put back.</summary>
            public bool Restore(Vessel v)
            {
                Safe(() =>
                {
                    if (FlightGlobals.speedDisplayMode != Speed)
                    {
                        FlightGlobals.SetSpeedMode(Speed);
                    }
                });
                bool groupOk = false;
                Safe(() => groupOk = TrySetGroup(v, KSPActionGroup.SAS, Group, out _));
                Safe(() =>
                {
                    VesselAutopilot ap = v.Autopilot;
                    if (Enabled && (!ap.Enabled || ap.Mode != Mode))
                    {
                        ap.Enable(Mode);
                    }
                });
                return groupOk;
            }
        }

        public static Dictionary<string, object> DescribeSas(Vessel v)
        {
            VesselAutopilot ap = v.Autopilot;
            return new Dictionary<string, object>
            {
                ["on"] = ap.Enabled && v.ActionGroups[KSPActionGroup.SAS],
                ["mode"] = ModeName(ap.Mode),
                ["speed_mode"] = SpeedModeName(FlightGlobals.speedDisplayMode),
                ["available_modes"] = Available(v),
            };
        }

        /// <summary>set_rcs: the RCS action group. A refused change leaves the group as it was.</summary>
        public static object SetRcs(Vessel v, RequestBody b)
        {
            RequireManual(v);
            SetGroupChecked(v, KSPActionGroup.RCS, b.GetBool("on"));
            return DescribeRcs(v);
        }

        public static Dictionary<string, object> DescribeRcs(Vessel v)
        {
            int parts = 0;
            foreach (Part p in v.parts)
            {
                if (p.FindModuleImplementing<ModuleRCS>() != null)
                {
                    parts++;
                }
            }
            object mono = null;
            PartResourceDefinition mp = PartResourceLibrary.Instance?.GetDefinition("MonoPropellant");   // a mod may remove it
            if (mp != null)
            {
                v.GetConnectedResourceTotals(mp.id, out double amount, out double max);
                mono = max > 0 ? new Dictionary<string, object> { ["amount"] = amount, ["max"] = max } : null;
            }
            return new Dictionary<string, object>
            {
                ["on"] = v.ActionGroups[KSPActionGroup.RCS],
                ["parts"] = parts,
                ["monopropellant"] = mono,
            };
        }

        // ---- rcs_translate pulse --------------------------------------------------------------------------------------------
        // Tool frame: x = ReferenceTransform.right, y = -ReferenceTransform.forward (dorsal), z = ReferenceTransform.up (nose).
        // ModuleRCS reads inputLin = ReferenceTransform.rotation * (X, Z, Y) (ModuleRCS.cs 229~437); the signs below are fixed by
        // the dv_local measurement (0015 unit 3). Start values from the decompile: a thruster fires when its direction agrees with
        // inputLin and pushes the vessel the other way (AddForceAtPosition(-vector ...)), and FlightInputHandler negates X and Z for
        // the player's keys (2465~2466): X and Z negative; Y maps to local forward = -y of the tool frame, so its sign is positive.
        private const float SignX = -1f;
        private const float SignY = 1f;
        private const float SignZ = -1f;
        private const float DeadZone = 0.05f;   // ModuleRCS EPSILON: smaller inputs are dropped

        private static bool _active;            // never judged by Unity's == null: a destroyed vessel compares equal to null
        private static Vessel _pv;
        private static uint _pvId;
        private static Vector3 _axes;
        private static double _endUt;
        private static Vector3d _dv;
        private static Vector3d _lastVel;
        private static Vector3d _lastGrav;
        private static double _lastDt;
        private static CelestialBody _lastBody;
        private static bool _lastRotating;
        private static bool _haveLast;
        private static readonly HashSet<string> _notes = new HashSet<string>();
        private static string _ended;
        private static bool _any;

        /// <summary>True when v is the vessel of the running pulse.</summary>
        public static bool IsPulseVessel(Vessel v) => _active && v.persistentId == _pvId;

        public static object Translate(Vessel v, RequestBody b)
        {
            RequireManual(v);
            double s = b.GetDouble("seconds");
            if (!(s > 0 && s <= 30))
            {
                throw ApiException.BadRequest("seconds must be in (0, 30]");
            }
            Vector3 a = new Vector3(Axis(b, "x"), Axis(b, "y"), Axis(b, "z"));
            if (a == Vector3.zero)
            {
                throw ApiException.BadRequest("x, y or z must be non-zero");
            }
            if (!v.ActionGroups[KSPActionGroup.RCS])
            {
                throw new ApiException(409, "rcs_off", "RCS is off (set_rcs on: true first)");
            }
            if (AttitudeController.Suspended(v))
            {
                throw new ApiException(409, "on_rails", "rails warp or packed vessel");
            }
            StopPulse("replaced");
            _pv = v;
            _pvId = v.persistentId;
            _axes = a;
            _endUt = Planetarium.GetUniversalTime() + s;
            _dv = Vector3d.zero;
            _haveLast = false;
            _notes.Clear();
            _ended = null;
            _any = true;
            _active = true;
            v.OnFlyByWire += Fly;
            return DescribeTranslate();
        }

        private static float Axis(RequestBody b, string name)
        {
            double x = b.GetDouble(name, 0);
            if (!(x >= -1 && x <= 1))
            {
                throw ApiException.BadRequest(name + " must be in [-1, 1]");
            }
            if (x != 0 && Math.Abs(x) < DeadZone)
            {
                throw ApiException.BadRequest(name + " must be 0 or at least " + DeadZone + " in magnitude (RCS dead zone)");
            }
            return (float)x;
        }

        private static bool Feeding(Vessel v) =>
            ReferenceEquals(v, FlightGlobals.ActiveVessel) && !AttitudeController.Suspended(v) && v.IsControllable
            && v.ActionGroups[KSPActionGroup.RCS] && Planetarium.GetUniversalTime() < _endUt;

        /// <summary>OnFlyByWire. FlightInputHandler feeds every loaded vessel and its order against FixedUpdate is not fixed, so
        /// every condition is judged here. An exception here would break the multicast and the handler's vessel loop.</summary>
        private static void Fly(FlightCtrlState st)
        {
            try
            {
                if (!_active || !Feeding(_pv))
                {
                    return;
                }
                st.X = SignX * _axes.x;
                st.Z = SignZ * _axes.z;
                st.Y = SignY * _axes.y;
            }
            catch (Exception)
            {
                StopPulse("error");
            }
        }

        /// <summary>FixedUpdate, after Coordinator.Tick: end conditions, a safety net for missed hooks, and dv_local.</summary>
        public static void Tick()
        {
            if (!_active)
            {
                return;
            }
            try
            {
                Vessel v = _pv;
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    StopPulse("scene");
                }
                else if ((object)v == null || !v || v.state == Vessel.State.DEAD)
                {
                    StopPulse("vessel_lost");
                }
                else if (!ReferenceEquals(FlightGlobals.ActiveVessel, v))
                {
                    StopPulse("vessel_changed");
                }
                else if (AttitudeController.Suspended(v))
                {
                    StopPulse("on_rails");
                }
                else if (!v.ActionGroups[KSPActionGroup.RCS])
                {
                    StopPulse("rcs_off");
                }
                else if (!v.IsControllable)
                {
                    StopPulse("not_controllable");
                }
                else
                {
                    Accumulate(v);   // this tick's thrust counts before done
                    if (Planetarium.GetUniversalTime() >= _endUt)
                    {
                        StopPulse("done");
                    }
                }
            }
            catch (Exception)
            {
                StopPulse("error");
            }
        }

        /// <summary>dv_local: (Δobt_velocity − previous g·dt) on this tick's control axes. Gravity is removed (it turns obt_velocity
        /// by several m/s per second in orbit). g and dt are the previous tick's: VesselPrecalculate writes the next step's
        /// graviticAcceleration after measuring, the same pairing as the stock perturbation_immediate. Body, rotating frame or dt
        /// changes are skipped (acceleration_immediate is not used: it is stale on such ticks, Vessel.cs 7543).</summary>
        private static void Accumulate(Vessel v)
        {
            Vector3d vel = v.obt_velocity;
            CelestialBody body = v.mainBody;
            bool rotating = FlightGlobals.RefFrameIsRotating;
            double dt = TimeWarp.fixedDeltaTime;
            if (_haveLast && ReferenceEquals(body, _lastBody) && rotating == _lastRotating && dt == _lastDt)
            {
                Vector3d d = vel - _lastVel - _lastGrav * _lastDt;
                Transform rt = v.ReferenceTransform;
                _dv += new Vector3d(Vector3d.Dot(d, rt.right), -Vector3d.Dot(d, rt.forward), Vector3d.Dot(d, rt.up));
            }
            else if (_haveLast)
            {
                _notes.Add("frame_changed");
            }
            if (v.LandedOrSplashed)
            {
                _notes.Add("landed");
            }
            if (v.atmDensity > 0)
            {
                _notes.Add("atmosphere");
            }
            _lastVel = vel;
            _lastBody = body;
            _lastRotating = rotating;
            _lastGrav = v.graviticAcceleration;
            _lastDt = dt;
            _haveLast = true;
        }

        /// <summary>Ends the running pulse. Never throws. The reason is recorded first, without touching Unity objects.</summary>
        public static void StopPulse(string reason)
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            _ended = reason;
            Vessel v = _pv;
            _pv = null;
            if ((object)v == null)
            {
                return;
            }
            Safe(() => v.OnFlyByWire -= Fly);
            Safe(() =>
            {
                if (v)
                {
                    v.ctrlState.X = 0;   // an axis override group would keep it: SetControlState skips blocked axes
                    v.ctrlState.Y = 0;
                    v.ctrlState.Z = 0;
                }
            });
        }

        /// <summary>Logic stop: the pulse is not handed over.</summary>
        public static IList<string> Stop()
        {
            bool had = _active;
            StopPulse("logic_stop");
            return had ? new List<string> { "rcs pulse stopped" } : new List<string>();
        }

        private static Dictionary<string, object> DescribeTranslate()
        {
            if (!_any)
            {
                return null;
            }
            return new Dictionary<string, object>
            {
                ["active"] = _active,
                ["vessel_id"] = _pvId,
                ["x"] = (double)_axes.x,
                ["y"] = (double)_axes.y,
                ["z"] = (double)_axes.z,
                ["remaining_s"] = _active ? (object)Math.Max(0, _endUt - Planetarium.GetUniversalTime()) : null,
                ["dv_local"] = new Dictionary<string, object> { ["x"] = _dv.x, ["y"] = _dv.y, ["z"] = _dv.z },
                ["dv_note"] = _notes.Count == 0 ? null : _notes.OrderBy(n => n).Cast<object>().ToList(),
                ["ended"] = _ended,
            };
        }

        /// <summary>get_status translate: the last pulse in any scene (leave/recover end in the space center).</summary>
        public static Dictionary<string, object> Summary() => !_any ? null : new Dictionary<string, object>
        {
            ["active"] = _active,
            ["vessel_id"] = _pvId,
            ["ended"] = _ended,
        };

        /// <summary>get_flight controls group.</summary>
        public static Dictionary<string, object> Describe(Vessel v) => new Dictionary<string, object>
        {
            ["sas"] = DescribeSas(v),
            ["rcs"] = DescribeRcs(v),
            ["translate"] = DescribeTranslate(),
        };

        /// <summary>Fields added to get_status.flight.</summary>
        public static void AddSummary(Vessel v, Dictionary<string, object> d)
        {
            d["sas"] = v.Autopilot.Enabled && v.ActionGroups[KSPActionGroup.SAS];
            d["rcs"] = v.ActionGroups[KSPActionGroup.RCS];
            d["translating"] = _active;
        }

        private static void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] manual control: " + e);
            }
        }
    }
}
