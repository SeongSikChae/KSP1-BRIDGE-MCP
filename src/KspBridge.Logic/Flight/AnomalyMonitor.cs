using System;
using System.Collections.Generic;
using KspBridge.Attitude;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Watches for flight anomalies that wait_until returns early on: destroyed parts and lost control authority.
    /// Control is judged by response, not by attitude error: on the pitch or yaw axis the final control input
    /// (vessel.ctrlState, after our controller or SAS) stays near its limit while the vessel does not accelerate
    /// in that direction. A big slew or swinging back after an overshoot accelerates as commanded and passes;
    /// no steering, no electric charge, or aerodynamic torque overpowering the controls is caught.
    /// </summary>
    public static class AnomalyMonitor
    {
        public const float SaturatedInput = 0.9f;
        public const double ControlLostSeconds = 2.5;
        public const double MinResponse = 0.01; // rad/s² in the commanded direction

        private static readonly Dictionary<Guid, int> _partsDied = new Dictionary<Guid, int>();
        private static readonly Axis _pitch = new Axis();
        private static readonly Axis _yaw = new Axis();
        private static Vector3 _prevOmega;
        private static Vessel _prevVessel;
        private static bool _hasPrev;

        // KSP's EventData.Add reads the delegate's Target: a static method group has none (NullReferenceException),
        // so register through a lambda (compiled to an instance method of the closure class).
        private static readonly EventData<Part>.OnEvent _onPartDie = p => OnPartDie(p);

        /// <summary>Saturation run on one axis: since when the input has been at its limit with the same sign, and the response so far.</summary>
        private sealed class Axis
        {
            public float Sign;
            public double Since = -1;
            public double ResponseSum;
            public int Ticks;
            public bool Lost;

            public void Reset()
            {
                Sign = 0;
                Since = -1;
                ResponseSum = 0;
                Ticks = 0;
                Lost = false;
            }

            /// <param name="command">Control input on this axis, -1..1.</param>
            /// <param name="accel">Angular acceleration on this axis, sign-corrected so + is the direction a + command turns.</param>
            public void Update(float command, double accel, double now)
            {
                float sign = Math.Abs(command) >= SaturatedInput ? Math.Sign(command) : 0;
                if (sign == 0 || sign != Sign)
                {
                    Reset();
                    Sign = sign;
                    if (sign == 0)
                    {
                        return;
                    }
                    Since = now;
                }
                ResponseSum += accel * sign;
                Ticks++;
                Lost = now - Since >= ControlLostSeconds && ResponseSum / Ticks < MinResponse;
            }
        }

        public static void Hook() => GameEvents.onPartDie.Add(_onPartDie);

        public static void Unhook()
        {
            GameEvents.onPartDie.Remove(_onPartDie);
            _partsDied.Clear();
            ResetControl();
        }

        private static void OnPartDie(Part p)
        {
            if (p == null || p.vessel == null)
            {
                return;
            }
            _partsDied.TryGetValue(p.vessel.id, out int n);
            _partsDied[p.vessel.id] = n + 1;
        }

        public static int PartsDied(Guid vesselId) => _partsDied.TryGetValue(vesselId, out int n) ? n : 0;

        public static bool ControlLost => _pitch.Lost || _yaw.Lost;

        /// <summary>Which axis lost control and for how long, for the anomaly detail.</summary>
        public static string ControlLostDetail()
        {
            Axis a = _pitch.Lost ? _pitch : _yaw;
            string name = _pitch.Lost ? "pitch" : "yaw";
            return name + " input at limit (" + (a.Sign > 0 ? "+" : "-") + ") for " + (Time.timeSinceLevelLoad - a.Since).ToString("F1")
                + " s, mean response " + (a.Ticks > 0 ? (a.ResponseSum / a.Ticks).ToString("F3") : "?") + " rad/s^2";
        }

        private static void ResetControl()
        {
            _pitch.Reset();
            _yaw.Reset();
            _hasPrev = false;
            _prevVessel = null;
        }

        public static void FixedUpdate()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            // Our attitude hold is always watched (a clamped or unsteerable vessel is a real anomaly). Stock SAS alone is
            // watched only in flight: on the ground, contact forces hold the vessel while SAS saturates (rover, runway).
            bool grounded = v != null && (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED
                || v.situation == Vessel.Situations.PRELAUNCH);
            bool watched = v != null && !v.packed
                && (AttitudeController.Active(v) || (v.ActionGroups[KSPActionGroup.SAS] && !grounded));
            if (!watched || v != _prevVessel)
            {
                ResetControl();
                if (!watched)
                {
                    return;
                }
            }

            // Vessel frame: x = pitch axis, y = roll axis (nose), z = yaw axis (same as the attitude controller).
            Vector3 omega = v.angularVelocity;
            double dt = TimeWarp.fixedDeltaTime;
            if (!_hasPrev || dt <= 0)
            {
                _prevOmega = omega;
                _prevVessel = v;
                _hasPrev = true;
                return;
            }
            Vector3 alpha = (omega - _prevOmega) / (float)dt;
            _prevOmega = omega;

            // A + input turns the vessel in the direction sign * axis (the controller's PitchSign/YawSign convention).
            Settings s = Settings.Current;
            FlightCtrlState st = v.ctrlState;
            double now = Time.timeSinceLevelLoad;
            _pitch.Update(st.pitch, alpha.x * s.PitchSign, now);
            _yaw.Update(st.yaw, alpha.z * s.YawSign, now);
        }
    }
}
