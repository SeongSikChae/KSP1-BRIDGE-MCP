using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        internal const float KStop = 0.8f;      // internal: ControllerTrace evaluates the same curve
        internal const float OmegaMax = 0.5f;
        private const float Kp = 4f;
        private const float Ki = 0.4f;
        internal const float KLin = Kp / 3f;   // outer-loop gain near zero error: a third of the inner rate loop (design 0055 decision 1)
        private const float FfTau = 0.3f;     // target-rate feedforward low-pass time constant, s (decision 1-2)

        // Target-rate feedforward state (decision 1-2): last target direction (world) and the filtered rate.
        private static Vector3d _prevDir;
        private static bool _prevDirValid;
        private static Vector3 _ff;

        private static Vessel _v;
        private static bool _sasBefore;
        private static VesselAutopilot.AutopilotMode _sasModeBefore;
        private static bool _rcsBefore;
        private static bool _rcsTouched;   // the plan really changed the RCS group at least once
        private static Vector3 _integral;

        // Torque provider cache. Key: the vessel object (a reloaded scene keeps persistentId but has new modules) and part count.
        private static ITorqueProvider[] _torque = new ITorqueProvider[0];
        private static Vessel _torqueVessel;
        private static int _torqueParts = -1;
        private static int[] _torqueKind = new int[0];   // ControllerTrace kind of each _torque entry, rebuilt with it
        private static readonly Vector3[] _kindSum = new Vector3[ControllerTrace.Kinds];   // last AvailableTorque per kind
        private static bool _powered = true;   // electric-charge hysteresis state (HasCharge), reset when the vessel changes
        private static bool _hasWheel;         // the cached providers include a reaction wheel (rebuilt with the cache)
        private static bool _traceFailed;   // a trace failure is logged once; a reload creates fresh statics

        /// <summary>The vessel the controller is attached to (null when detached).</summary>
        public static Vessel Target => _v;

        /// <summary>Attitude control pauses only on rails warp or while packed; physics warp keeps control.</summary>
        public static bool Suspended(Vessel v) =>
            v.packed || (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex > 0);

        /// <summary>Angle between the nose (ReferenceTransform up) and the target direction, deg (roll not included).</summary>
        public static double ErrorDeg(Vessel v, AttitudeTarget target) =>
            Vector3d.Angle(v.ReferenceTransform.up, target.Direction(v));

        /// <param name="hp">Resume: SAS/RCS state handed over from the previous logic (SAS is already off by then); missing
        /// fields (older logic) are recorded from the vessel now.</param>
        public static void Attach(Vessel v, HandedPlan hp = null)
        {
            if (_v == v)
            {
                return;   // a plan replacement does not re-attach: the original SAS/RCS state stays the one to restore
            }
            Detach();
            Record(v, hp);
            _v = v;
            _integral = Vector3.zero;
            ResetFeedforward();
            v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            v.OnFlyByWire += Fly;
        }

        /// <summary>Docking adopted a merged vessel: re-binds OnFlyByWire to it and keeps the pre-plan SAS/RCS record
        /// (no Detach/Record, so the plan end restores the state from before the plan).</summary>
        public static void Retarget(Vessel v)
        {
            if (_v == v || _v == null)
            {
                return;   // not attached yet (reload Resume adopts before Attach): Attach records and binds v itself
            }
            if (_v != null)
            {
                _v.OnFlyByWire -= Fly;
            }
            _v = v;
            ResetFeedforward();   // the old vessel's target direction is no reference for the new one
            v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            v.OnFlyByWire += Fly;
        }

        private static void Record(Vessel v, HandedPlan hp)
        {
            _sasBefore = hp?.SasBefore ?? v.ActionGroups[KSPActionGroup.SAS];
            _sasModeBefore = ManualControl.ParseModeOrNull(hp?.SasModeBefore) ?? v.Autopilot.Mode;   // Disable keeps the mode
            _rcsBefore = hp?.RcsBefore ?? v.ActionGroups[KSPActionGroup.RCS];
            _rcsTouched = hp?.RcsTouched ?? false;
        }

        public static VesselAutopilot.AutopilotMode SasModeBefore => _sasModeBefore;
        public static bool RcsBefore => _rcsBefore;
        public static bool RcsTouched => _rcsTouched;

        /// <summary>
        /// Once at each step start (never every tick: override groups and the toggle cooldown could flip it back and forth).
        /// rcs given: set it. Omitted after the plan changed RCS: back to the state before the plan. Omitted otherwise: untouched.
        /// A change refused by an override group or a cooldown is skipped and not counted as touched.
        /// </summary>
        public static void ApplyStepRcs(bool? rcs)
        {
            if (_v == null)
            {
                return;
            }
            bool? want = rcs ?? (_rcsTouched ? _rcsBefore : (bool?)null);
            if (!want.HasValue || _v.ActionGroups[KSPActionGroup.RCS] == want.Value)
            {
                return;
            }
            if (ManualControl.TrySetGroup(_v, KSPActionGroup.RCS, want.Value, out _))
            {
                _rcsTouched = true;   // only when it really changed
            }
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

        /// <summary>SAS state to restore when the plan ends (reload handover).</summary>
        public static bool SasBefore => _sasBefore;

        /// <summary>PID integral per axis (reload handover).</summary>
        public static List<object> SaveIntegral() => new List<object> { (double)_integral.x, (double)_integral.y, (double)_integral.z };

        /// <summary>Resume, after Attach: continues the handed-over integral. Missing or malformed values leave it at zero.</summary>
        public static void LoadIntegral(List<object> l)
        {
            if (l == null || l.Count != 3)
            {
                return;
            }
            try
            {
                _integral = new Vector3(
                    (float)Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                    (float)Convert.ToDouble(l[1], CultureInfo.InvariantCulture),
                    (float)Convert.ToDouble(l[2], CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                _integral = Vector3.zero;   // malformed: start from zero rather than fail the takeover
            }
        }

        /// <summary>A failed resume before Attach: makes v the release target with the handed-over SAS state, so Release
        /// (RestoreSas, Detach) acts on it. No OnFlyByWire and no SAS change here. Already attached: unchanged.</summary>
        public static void Adopt(Vessel v, HandedPlan hp)
        {
            if (_v != null || v == null)
            {
                return;
            }
            _v = v;
            Record(v, hp);
            _integral = Vector3.zero;
            ResetFeedforward();
        }

        /// <summary>Applies the SAS state recorded at Attach, with its mode; call before Detach. SetGroup alone comes back as
        /// stability on the next VesselAutopilot.Update, so Enable(mode) is called, but only on the live active vessel: Release
        /// also runs for vessel_changed / vessel_lost / scene, and CanSetMode uses the active vessel's display velocity.</summary>
        public static void RestoreSas()
        {
            if (_v == null)
            {
                return;
            }
            _v.ActionGroups.SetGroup(KSPActionGroup.SAS, _sasBefore);
            if (_sasBefore && ReferenceEquals(_v, FlightGlobals.ActiveVessel) && _v.loaded && !_v.packed && _v.state != Vessel.State.DEAD)
            {
                VesselAutopilot ap = _v.Autopilot;
                ap.Enable(ap.CanSetMode(_sasModeBefore) ? _sasModeBefore : VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }

        /// <summary>Puts RCS back to its state before the plan, only when the plan changed it; call before Detach.</summary>
        public static void RestoreRcs()
        {
            if (_rcsTouched && _v != null)
            {
                ManualControl.TrySetGroup(_v, KSPActionGroup.RCS, _rcsBefore, out _);   // an override-held group is left alone
            }
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
                    ResetFeedforward();
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
                if (c.Translate is Vector3 tr)
                {
                    // Same tool frame and signs as the rcs_translate pulse (ManualControl).
                    s.X = ManualControl.SignX * ManualControl.LiftDeadZone(tr.x);
                    s.Y = ManualControl.SignY * ManualControl.LiftDeadZone(tr.y);
                    s.Z = ManualControl.SignZ * ManualControl.LiftDeadZone(tr.z);
                }

                if (!c.Attitude.HasValue)
                {
                    ResetFeedforward();
                    return;   // attitude without attitude arguments: throttle only
                }
                if (Suspended(_v))
                {
                    _integral = Vector3.zero;   // attitude_error is not known either
                    ResetFeedforward();
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
            Vector3 ff = Feedforward(target, dt);
            float pitch = 0, roll = 0, yaw = 0;
            Vector3 want3 = Vector3.zero, alpha3 = Vector3.zero;   // trace only
            for (int i = 0; i < 3; i++)
            {
                float alpha = Mathf.Max(torque[i] / Mathf.Max(moi[i], 1e-3f), 1e-3f);
                const float g = 1f;   // design 0055 decision 2 (unit 3): the estimated gain of this axis, outer loop only
                float absErr = Mathf.Abs(err[i]);
                float want = i == 1 && !target.HasRoll
                    ? 0f   // no roll target: damp the roll rate only
                    : Mathf.Clamp(ff[i] + Mathf.Sign(err[i]) * Mathf.Min(Mathf.Min(g * KLin * absErr,
                        Mathf.Sqrt(2 * g * alpha * absErr) * KStop), OmegaMax), -OmegaMax, OmegaMax);
                want3[i] = want;
                alpha3[i] = alpha;
                float e = want - omega[i];
                // conditional integration (anti-windup, #22 U4): no integration while the output is saturated and e would
                // push it further. With a small alpha (one wheel in vacuum) the wound-up Ki term held full input past the
                // stopping curve and the axis overshot by 25..35° each way.
                float next = Mathf.Clamp(_integral[i] + e * dt, -1, 1);
                float aNext = Kp * e + Ki * next;
                if (Mathf.Abs(aNext / alpha) <= 1 || Mathf.Sign(e) != Mathf.Sign(aNext))
                {
                    _integral[i] = next;
                }
                float a = Kp * e + Ki * _integral[i];
                float input = Mathf.Clamp(-a / alpha, -1, 1);   // a positive input turns the rate negative
                if (i == 0) pitch = input; else if (i == 1) roll = input; else yaw = input;
            }
            s.pitch = pitch;
            s.roll = roll;
            s.yaw = yaw;
            try
            {
                ControllerTrace.Record(_v.persistentId, new ControllerTrace.Sample
                {
                    Ut = Planetarium.GetUniversalTime(),
                    Throttle = Coordinator.Command.Throttle,
                    Q = _v.dynamicPressurekPa,
                    Err = err,
                    Omega = omega,
                    Want = want3,
                    Input = new Vector3(pitch, roll, yaw),
                    Alpha = alpha3,
                    Moi = moi,
                    Torque = (Vector3[])_kindSum.Clone(),
                    Ff = ff,
                });
            }
            catch (Exception e)
            {
                if (!_traceFailed)
                {
                    _traceFailed = true;
                    Debug.LogError("[KspBridge] controller trace: " + e);   // observation only: never aborts the plan
                }
            }
        }

        /// <summary>Forgets the last target direction and the filtered rate: plan start, step start or replacement, vessel
        /// change, and ticks that do not steer.</summary>
        internal static void ResetFeedforward()
        {
            _prevDirValid = false;
            _ff = Vector3.zero;
        }

        /// <summary>
        /// Target-rate feedforward for pitch (x) and yaw (z), rad/s in the err sign convention (design 0055 decision 1-2): the
        /// target direction's world rotation rate d0 × d1 / dt in vessel axes, low-passed (FfTau). A change faster than the
        /// controller can follow (1.5·OmegaMax per tick, e.g. a reference switch) is a step and adds nothing. No roll.
        /// </summary>
        private static Vector3 Feedforward(AttitudeTarget target, float dt)
        {
            Vector3d dirNow = target.Direction(_v).normalized;
            Vector3 raw = Vector3.zero;
            if (_prevDirValid && dt > 0 && Vector3d.Angle(_prevDir, dirNow) * Mathf.Deg2Rad <= 1.5 * OmegaMax * dt)
            {
                // +x turns the nose (+y) toward +z and +z turns it toward -x: the same signs as the pitch / yaw errors
                Vector3 wWorld = (Vector3)(Vector3d.Cross(_prevDir, dirNow) / dt);
                Vector3 wLocal = Quaternion.Inverse(_v.ReferenceTransform.rotation) * wWorld;
                raw = new Vector3(wLocal.x, 0, wLocal.z);
            }
            _ff += (raw - _ff) * (1 - Mathf.Exp(-dt / FfTau));
            _prevDir = dirNow;
            _prevDirValid = true;
            return Vector3.ClampMagnitude(_ff, OmegaMax);
        }

        /// <summary>ControllerTrace kind of a torque provider: 0 wheel, 1 gimbal, 2 control surface (incl. ModuleAeroSurface),
        /// 3 RCS (incl. ModuleRCSFX), 4 other.</summary>
        private static int KindOf(ITorqueProvider p) =>
            p is ModuleReactionWheel ? 0 : p is ModuleGimbal ? 1 : p is ModuleControlSurface ? 2 : p is ModuleRCS ? 3 : 4;

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
                if (!ReferenceEquals(v, _torqueVessel))
                {
                    _powered = true;   // power hysteresis follows the vessel, not its part count (staging keeps it)
                }
                _torqueVessel = v;
                _torqueParts = v.parts.Count;
                _torque = v.FindPartModulesImplementing<ITorqueProvider>().ToArray();
                _torqueKind = new int[_torque.Length];   // same block: lengths always match
                _hasWheel = false;
                for (int i = 0; i < _torque.Length; i++)
                {
                    _torqueKind[i] = KindOf(_torque[i]);
                    _hasWheel |= _torqueKind[i] == 0;
                }
            }
            Array.Clear(_kindSum, 0, _kindSum.Length);
            bool power = !_hasWheel || HasCharge(v);   // one lookup per tick, only when a wheel can use it
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _torque.Length; i++)
            {
                // GetPotentialTorque ignores power and wheel state: a wheel that is not running gives no torque (design 0055 decision 3)
                if (_torque[i] is ModuleReactionWheel w && (!power || w.wheelState != ModuleReactionWheel.WheelState.Active))
                {
                    continue;
                }
                _torque[i].GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                Vector3 t = (Abs(pos) + Abs(neg)) * 0.5f;
                sum += t;
                _kindSum[_torqueKind[i]] += t;   // trace only
            }
            return sum;
        }

        /// <summary>Electric charge with hysteresis: off once empty, on again at 1% of capacity, so a charge hovering
        /// near zero does not switch the wheels' torque in and out every tick.</summary>
        private static bool HasCharge(Vessel v)
        {
            double amount, max;
            try
            {
                PartResourceDefinition ec = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
                if (ec == null)
                {
                    return true;   // no such resource in this game: keep the old behaviour
                }
                v.GetConnectedResourceTotals(ec.id, out amount, out max);
            }
            catch (Exception)
            {
                return _powered;   // a failed lookup keeps the last state: the torque model must not abort the plan
            }
            if (_powered && amount <= 1e-6)
            {
                _powered = false;
            }
            else if (!_powered && max > 0 && amount >= 0.01 * max)
            {
                _powered = true;
            }
            return _powered;
        }

        private static Vector3 Abs(Vector3 x) => new Vector3(Mathf.Abs(x.x), Mathf.Abs(x.y), Mathf.Abs(x.z));
    }
}
