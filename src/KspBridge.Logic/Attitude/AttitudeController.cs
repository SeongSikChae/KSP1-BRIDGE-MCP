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
        private static string _frame = "surface";
        private static double _pitchOffset;
        private static double _yawOffset;
        private static double? _maxAoa;
        private static Vector3 _lastDirection;
        private static string _frameInvalid;
        private static bool _aoaLimited;
        public static bool IsActive => _vessel != null;

        public static readonly string[] Frames =
        {
            "surface", "prograde", "retrograde", "normal", "antinormal", "radial_in", "radial_out",
            "surface_prograde", "surface_retrograde", "node",
        };

        /// <summary>Current frame of the active hold (null when none).</summary>
        public static string Frame => _vessel == null ? null : _frame;

        /// <summary>Incremented by every Set: a caller that stored it can tell whether its hold was replaced (even with the same frame).</summary>
        public static int Generation { get; private set; }

        public static void Set(Vessel v, string mode, double pitch, double heading, double? roll)
            => Set(v, mode, "surface", pitch, heading, roll, 0, 0, null);

        public static void Set(Vessel v, string mode, string frame, double pitch, double heading, double? roll,
            double pitchOffset, double yawOffset, double? maxAoa)
        {
            if (mode != "own" && mode != "sas")
            {
                throw ApiException.BadRequest("mode must be 'own' or 'sas'");
            }
            if (System.Array.IndexOf(Frames, frame) < 0)
            {
                throw ApiException.BadRequest("frame must be one of: " + string.Join(", ", Frames));
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
            _frame = frame;
            _pitchOffset = pitchOffset;
            _yawOffset = yawOffset;
            _maxAoa = maxAoa;
            _lastDirection = v.GetTransform().up;
            _frameInvalid = null;
            Generation++;
            _aoaLimited = false;

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
                ["frame"] = mine ? _frame : null,
                ["pitch_offset"] = mine && _frame != "surface" ? (object)_pitchOffset : null,
                ["yaw_offset"] = mine && _frame != "surface" ? (object)_yawOffset : null,
                ["max_aoa_deg"] = mine ? (object)_maxAoa : null,
                ["aoa_limited"] = mine ? (object)_aoaLimited : null,
                ["frame_invalid"] = mine ? _frameInvalid : null,
                ["error_deg"] = ErrorDeg(v),
            };
        }

        /// <summary>Angle between the nose and the held target (degrees), or null when no hold is active on this vessel.</summary>
        public static double? ErrorDeg(Vessel v)
        {
            if (_vessel == null || _vessel != v)
            {
                return null;
            }
            return Vector3.Angle(v.GetTransform().up, TargetDirection(v));
        }

        public static bool Active(Vessel v) => _vessel != null && _vessel == v;

        /// <summary>World direction the nose should point at.</summary>
        private static Vector3 TargetDirection(Vessel v)
        {
            Vector3 dir;
            if (_frame == "surface")
            {
                dir = SurfaceFrame.SurfaceRotation(v) * (Quaternion.Euler(-(float)_pitch, (float)_heading, 0) * Vector3.forward);
            }
            else
            {
                Vector3? f = FrameDirection(v);
                _frameInvalid = f.HasValue ? null : "reference direction unavailable (speed < 0.5 m/s or no maneuver node)";
                dir = f.HasValue ? ApplyOffsets(v, f.Value) : _lastDirection;
            }
            dir = LimitAoa(v, dir);
            _lastDirection = dir;
            return dir;
        }

        private static Vector3 Up(Vessel v) => (Vector3)(v.CoMD - v.mainBody.position).normalized;

        /// <summary>Unit reference direction of a non-surface frame in world space, or null when undefined.</summary>
        private static Vector3? FrameDirection(Vessel v)
        {
            Vector3 obt = (Vector3)v.obt_velocity;
            Vector3 srf = (Vector3)v.srf_velocity;
            Vector3 up = Up(v);
            Vector3 normal = Vector3.Cross(obt, up).normalized; // navball "normal" (orbit angular momentum)
            bool obtOk = obt.magnitude >= 0.5f;
            bool srfOk = srf.magnitude >= 0.5f;
            switch (_frame)
            {
                case "prograde": return obtOk ? obt.normalized : (Vector3?)null;
                case "retrograde": return obtOk ? -obt.normalized : (Vector3?)null;
                case "normal": return obtOk ? normal : (Vector3?)null;
                case "antinormal": return obtOk ? -normal : (Vector3?)null;
                case "radial_out": return obtOk ? Vector3.Cross(normal, obt).normalized : (Vector3?)null;
                case "radial_in": return obtOk ? -Vector3.Cross(normal, obt).normalized : (Vector3?)null;
                case "surface_prograde": return srfOk ? srf.normalized : (Vector3?)null;
                case "surface_retrograde": return srfOk ? -srf.normalized : (Vector3?)null;
                case "node":
                    ManeuverNode n = v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes.Count > 0
                        ? v.patchedConicSolver.maneuverNodes[0] : null;
                    if (n == null)
                    {
                        return null;
                    }
                    Vector3 burn = (Vector3)n.GetBurnVector(v.orbit);
                    return burn.magnitude > 1e-3f ? burn.normalized : (Vector3?)null;
            }
            return null;
        }

        /// <summary>pitch_offset raises the nose toward local up (axis Cross(dir, up); orbit normal when dir is near up), yaw_offset turns about local up.</summary>
        private static Vector3 ApplyOffsets(Vessel v, Vector3 dir)
        {
            if (_pitchOffset == 0 && _yawOffset == 0)
            {
                return dir;
            }
            Vector3 up = Up(v);
            // Pitch: tilt dir toward the part of "up" perpendicular to it (+ = nose up). When dir is (anti)parallel to up,
            // tilt toward the orbital velocity instead (equivalent to pitching about the orbit normal).
            bool nearUp = Vector3.Angle(dir, up) < 5f || Vector3.Angle(dir, -up) < 5f;
            Vector3 toward = Vector3.ProjectOnPlane(nearUp ? (Vector3)v.obt_velocity : up, dir).normalized;
            float p = (float)_pitchOffset * Mathf.Deg2Rad;
            Vector3 d = toward.sqrMagnitude > 0.5f ? dir * Mathf.Cos(p) + toward * Mathf.Sin(p) : dir;
            // Yaw: about local up, + = clockwise seen from above (like a compass heading increase).
            d = Quaternion.AngleAxis((float)_yawOffset, up) * d;
            return d.normalized;
        }

        /// <summary>Keeps the target within max_aoa_deg of the surface velocity while dynamic pressure is at least 1 kPa.</summary>
        private static Vector3 LimitAoa(Vessel v, Vector3 dir)
        {
            _aoaLimited = false;
            if (!_maxAoa.HasValue || v.dynamicPressurekPa < 1.0 || v.srf_velocity.magnitude < 0.5)
            {
                return dir;
            }
            Vector3 vel = ((Vector3)v.srf_velocity).normalized;
            if (Vector3.Angle(vel, dir) <= _maxAoa.Value)
            {
                return dir;
            }
            _aoaLimited = true;
            return Vector3.RotateTowards(vel, dir, (float)_maxAoa.Value * Mathf.Deg2Rad, 0f);
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
