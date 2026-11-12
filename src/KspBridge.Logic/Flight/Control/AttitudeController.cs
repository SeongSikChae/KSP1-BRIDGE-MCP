using KspBridge.Flight.Modes;
using System;
using UnityEngine;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// Turns the Coordinator's command into FlightCtrlState through vessel.OnFlyByWire: throttle, and a torque-normalized PID
    /// on pitch/roll/yaw. Axes (KSP 1.12 decompile): vessel.angularVelocity and vessel.MOI are in ReferenceTransform local space,
    /// x = pitch, y = roll, z = yaw; a positive pitch/roll/yaw input makes a -x/-y/-z torque. Main thread only.
    /// </summary>
    public static class AttitudeController
    {
        private const float KStop = 0.8f;
        private const float OmegaMax = 0.5f;
        private const float Kp = 4f;
        private const float Ki = 0.4f;

        private static Vessel _v;
        private static bool _sasBefore;
        private static Vector3 _integral;

        // Torque provider cache. Key: the vessel object (a reloaded scene keeps persistentId but has new modules) and part count.
        private static ITorqueProvider[] _torque = new ITorqueProvider[0];
        private static Vessel _torqueVessel;
        private static int _torqueParts = -1;

        /// <summary>The vessel the controller is attached to (null when detached).</summary>
        public static Vessel Target => _v;

        /// <summary>Attitude control pauses only on rails warp or while packed; physics warp keeps control.</summary>
        public static bool Suspended(Vessel v) =>
            v.packed || (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex > 0);

        /// <summary>Angle between the nose (ReferenceTransform up) and the target direction, deg (roll not included).</summary>
        public static double ErrorDeg(Vessel v, AttitudeTarget target) =>
            Vector3d.Angle(v.ReferenceTransform.up, target.Direction(v));

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
            _integral = Vector3.zero;
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
            _integral = Vector3.zero;
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

                if (!c.Attitude.HasValue)
                {
                    return;   // attitude without attitude arguments: throttle only
                }
                if (Suspended(_v))
                {
                    _integral = Vector3.zero;   // attitude_error is not known either
                    return;
                }
                Steer(s, c.Attitude.Value);
            }
            catch (Exception e)
            {
                Coordinator.Abort("error", null, e.Message);
            }
        }

        private static void Steer(FlightCtrlState s, AttitudeTarget target)
        {
            Vector3 err = AxisErrors(_v, target);
            Vector3 torque = AvailableTorque(_v);
            Vector3 moi = _v.MOI;
            Vector3 omega = _v.angularVelocity;
            float dt = TimeWarp.fixedDeltaTime;
            float pitch = 0, roll = 0, yaw = 0;
            for (int i = 0; i < 3; i++)
            {
                float alpha = Mathf.Max(torque[i] / Mathf.Max(moi[i], 1e-3f), 1e-3f);
                float want = i == 1 && !target.HasRoll
                    ? 0f   // no roll target: damp the roll rate only
                    : Mathf.Sign(err[i]) * Mathf.Min(Mathf.Sqrt(2 * alpha * Mathf.Abs(err[i])) * KStop, OmegaMax);
                float e = want - omega[i];
                _integral[i] = Mathf.Clamp(_integral[i] + e * dt, -1, 1);
                float a = Kp * e + Ki * _integral[i];
                float input = Mathf.Clamp(-a / alpha, -1, 1);   // a positive input turns the rate negative
                if (i == 0) pitch = input; else if (i == 1) roll = input; else yaw = input;
            }
            s.pitch = pitch;
            s.roll = roll;
            s.yaw = yaw;
        }

        /// <summary>
        /// Per-axis error (rad) in ReferenceTransform local space, signed so that turning toward the target needs
        /// angularVelocity[i] in the sign of err[i]. A positive x rate turns the nose (+y) toward +z; a positive z rate toward -x;
        /// a positive y rate turns +z toward +x.
        /// </summary>
        private static Vector3 AxisErrors(Vessel v, AttitudeTarget target)
        {
            Quaternion toLocal = Quaternion.Inverse(v.ReferenceTransform.rotation);
            Vector3 dir = toLocal * (Vector3)target.Direction(v);
            float pitchErr = Mathf.Atan2(dir.z, dir.y);
            float yawErr = Mathf.Atan2(-dir.x, dir.y);
            float rollErr = 0;
            if (target.HasRoll)
            {
                Vector3 fwd = toLocal * (target.RollRotation(v) * Vector3.forward);
                rollErr = Mathf.Atan2(fwd.x, fwd.z);
            }
            return new Vector3(pitchErr, rollErr, yawErr);
        }

        /// <summary>Per-axis available torque (kN·m): Σ (|pos| + |neg|) / 2 over torque providers (neg's sign varies by module).</summary>
        private static Vector3 AvailableTorque(Vessel v)
        {
            if (!ReferenceEquals(v, _torqueVessel) || v.parts.Count != _torqueParts)
            {
                _torqueVessel = v;
                _torqueParts = v.parts.Count;
                _torque = v.FindPartModulesImplementing<ITorqueProvider>().ToArray();
            }
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _torque.Length; i++)
            {
                _torque[i].GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                sum += (Abs(pos) + Abs(neg)) * 0.5f;
            }
            return sum;
        }

        private static Vector3 Abs(Vector3 x) => new Vector3(Mathf.Abs(x.x), Mathf.Abs(x.y), Mathf.Abs(x.z));
    }
}
