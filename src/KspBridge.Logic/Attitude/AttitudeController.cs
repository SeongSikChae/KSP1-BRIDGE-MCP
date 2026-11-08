using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Attitude
{
    /// <summary>
    /// Holds a surface-relative attitude target on the active vessel. Main thread only.
    /// The target is a nose direction (pitch/heading); roll is controlled only when given.
    /// own: PD controller on Vessel.OnFlyByWire. sas: stock SAS stability assist with LockRotation every physics tick.
    /// </summary>
    public static class AttitudeController
    {
        private static Vessel _vessel;
        private static string _mode;
        private static double _pitch;
        private static double _heading;
        private static double? _roll;
        public static bool IsActive => _vessel != null;

        public static void Set(Vessel v, string mode, double pitch, double heading, double? roll)
        {
            if (mode != "own" && mode != "sas")
            {
                throw ApiException.BadRequest("mode must be 'own' or 'sas'");
            }
            if (pitch < -90 || pitch > 90)
            {
                throw ApiException.BadRequest("pitch must be -90..90");
            }
            if (mode == "sas" && !v.Autopilot.CanSetMode(VesselAutopilot.AutopilotMode.StabilityAssist))
            {
                throw ApiException.BadRequest("SAS is not available on this vessel (no pilot or SAS-capable probe core)");
            }

            Settings.Reload(); // allows tuning gains/signs in settings.cfg without restarting KSP
            Release();
            Flight.SasMonitor.Clear(); // an attitude hold replaces a held SAS mode
            _vessel = v;
            _mode = mode;
            _pitch = pitch;
            _heading = ((heading % 360) + 360) % 360;
            _roll = roll;

            if (mode == "own")
            {
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, false); // SAS would fight the PD controller
                v.OnFlyByWire += Steer;
            }
            else
            {
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                v.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }

        public static void Release()
        {
            if (_vessel != null && _mode == "own")
            {
                _vessel.OnFlyByWire -= Steer;
            }
            if (_vessel != null && _mode == "sas" && _vessel.Autopilot != null && _vessel.Autopilot.SAS != null)
            {
                // back to plain stability assist holding the current attitude
                _vessel.Autopilot.SAS.lockedMode = true;
                _vessel.Autopilot.SAS.LockRotation(_vessel.ReferenceTransform.rotation);
            }
            _vessel = null;
            _mode = null;
        }

        /// <summary>Called from the addon's FixedUpdate.</summary>
        public static void FixedUpdate()
        {
            if (_vessel == null)
            {
                return;
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || FlightGlobals.ActiveVessel != _vessel || _vessel.state == Vessel.State.DEAD)
            {
                Release();
                return;
            }
            if (_mode == "sas")
            {
                if (!_vessel.ActionGroups[KSPActionGroup.SAS])
                {
                    _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                }
                if (_vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist)
                {
                    _vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
                }
                if (_roll.HasValue)
                {
                    _vessel.Autopilot.SAS.lockedMode = true;
                    _vessel.Autopilot.SAS.LockRotation(TargetRotation(_vessel));
                }
                else
                {
                    // Locked (hold) mode re-locks to the current rotation whenever the vessel turns fast, so it never
                    // slews far; steer like the stock prograde/target modes instead (non-locked, target direction).
                    _vessel.Autopilot.SAS.lockedMode = false;
                    _vessel.Autopilot.SAS.SetTargetOrientation(TargetDirection(_vessel), false);
                }
            }
        }

        public static Dictionary<string, object> Describe(Vessel v)
        {
            bool mine = _vessel != null && _vessel == v;
            return new Dictionary<string, object>
            {
                ["active"] = mine,
                ["mode"] = mine ? _mode : null,
                ["pitch"] = mine ? (object)_pitch : null,
                ["heading"] = mine ? (object)_heading : null,
                ["roll"] = mine && _roll.HasValue ? (object)_roll.Value : null,
                ["error_deg"] = mine ? (object)(double)Vector3.Angle(v.GetTransform().up, TargetDirection(v)) : null,
            };
        }

        /// <summary>World direction the nose should point at.</summary>
        private static Vector3 TargetDirection(Vessel v)
        {
            return SurfaceFrame.SurfaceRotation(v) * (Quaternion.Euler(-(float)_pitch, (float)_heading, 0) * Vector3.forward);
        }

        /// <summary>Full target rotation: given roll, or the smallest rotation that brings the nose to the target (keeps current roll).</summary>
        private static Quaternion TargetRotation(Vessel v)
        {
            if (_roll.HasValue)
            {
                return SurfaceFrame.TargetRotation(v, _pitch, _heading, _roll.Value);
            }
            Quaternion r = v.GetTransform().rotation;
            return Quaternion.FromToRotation(r * Vector3.up, TargetDirection(v)) * r;
        }

        /// <summary>PD steering in the vessel frame (x = pitch axis, y = roll axis / nose, z = yaw axis).</summary>
        private static void Steer(FlightCtrlState st)
        {
            Vessel v = _vessel;
            if (v == null)
            {
                return;
            }
            Settings s = Settings.Current;
            Quaternion r = v.GetTransform().rotation;
            Vector3 nose = r * Vector3.up;
            Vector3 target = TargetDirection(v);

            Vector3 axis = Vector3.Cross(nose, target);
            float ang = Vector3.Angle(nose, target) * Mathf.Deg2Rad;
            Vector3 e = axis.sqrMagnitude > 1e-9f ? Quaternion.Inverse(r) * (axis.normalized * ang) : Vector3.zero;

            if (_roll.HasValue)
            {
                Quaternion full = SurfaceFrame.TargetRotation(v, _pitch, _heading, _roll.Value);
                Vector3 curRight = Vector3.ProjectOnPlane(r * Vector3.right, nose);
                Vector3 tgtRight = Vector3.ProjectOnPlane(full * Vector3.right, nose);
                e.y = Vector3.SignedAngle(curRight, tgtRight, nose) * Mathf.Deg2Rad;
            }

            Vector3 w = v.angularVelocity;
            st.pitch = Mathf.Clamp(s.PitchSign * (s.Kp * e.x - s.Kd * w.x), -1f, 1f);
            st.yaw = Mathf.Clamp(s.YawSign * (s.Kp * e.z - s.Kd * w.z), -1f, 1f);
            st.roll = _roll.HasValue
                ? Mathf.Clamp(s.RollSign * (s.Kp * e.y - s.Kd * w.y), -1f, 1f)
                : Mathf.Clamp(-s.RollSign * s.Kd * w.y, -1f, 1f); // damp roll rate only
        }
    }
}
