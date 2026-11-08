using System;
using System.Collections.Generic;
using System.Linq;
using KspBridge.Attitude;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Executes the first maneuver node in-game: align (node frame) → optional warp → ignite at node UT minus the
    /// half-delta-v time → throttle down near the end → cut when the remaining delta-v is below tolerance.
    /// Alignment and a stopped rotation come before any warp: the node burn vector is inertial and rails warp keeps the
    /// inertial attitude, so the vessel is still on the node direction when the warp ends. Main thread only.
    /// </summary>
    public static class NodeExecutor
    {
        private const double G0 = 9.80665;
        private const double AlignDeg = 2;
        private const float StillRadPerSec = 0.01f;
        private const double WarpLeadSeconds = 30;
        private const float MinThrottle = 0.05f;
        private const double IgniteLateSeconds = 1.0;

        public static string State { get; private set; } = "idle"; // idle | aligning | waiting | burning | done | aborted
        public static string Reason { get; private set; }

        private static Vessel _v;
        private static ManeuverNode _node;
        private static Vector3d _dir0;
        private static double _igniteUt, _nodeUt, _nodeDv, _tolerance;
        private static bool _warp, _remove, _warped;
        private static float _throttle;

        public static bool Running => State == "aligning" || State == "waiting" || State == "burning";

        public static double? Remaining(Vessel v)
        {
            if (Running && _v == v && _node != null)
            {
                return _node.GetBurnVector(v.orbit).magnitude;
            }
            ManeuverNode n = v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes.Count > 0 ? v.patchedConicSolver.maneuverNodes[0] : null;
            return n?.GetBurnVector(v.orbit).magnitude;
        }

        public static Dictionary<string, object> Start(Vessel v, double tolerance, bool warp, bool remove)
        {
            if (v.patchedConicSolver == null || v.patchedConicSolver.maneuverNodes.Count == 0)
            {
                throw ApiException.BadRequest("no maneuver node");
            }
            if (tolerance <= 0)
            {
                throw ApiException.BadRequest("tolerance must be > 0");
            }
            // The controller only steers off rails at 1x: starting under warp could leave the vessel unaligned at ignition.
            if (v.packed || TimeWarp.CurrentRate > 1.01f)
            {
                throw ApiException.BadRequest("stop time warp first (the vessel must be off rails at 1x to align)");
            }
            ManeuverNode n = v.patchedConicSolver.maneuverNodes[0];
            double dv = n.GetBurnVector(v.orbit).magnitude;
            Burn(v, dv, out double burnTime);
            Burn(v, dv / 2, out double halfTime);
            double igniteUt = n.UT - halfTime;
            if (igniteUt <= Planetarium.GetUniversalTime())
            {
                throw ApiException.BadRequest("too late: ignition (node UT - " + halfTime.ToString("F1") + " s) is already past");
            }

            Cancel("restarted");
            AttitudeController.Set(v, "own", "node", 0, 0, null, 0, 0, null);
            // Own the throttle from the start: engines off while aligning and waiting (a held throttle would burn early).
            _v = v;
            SetThrottle(0f);
            _node = n;
            _nodeUt = n.UT;
            _nodeDv = dv;
            _igniteUt = igniteUt;
            _tolerance = tolerance;
            _warp = warp;
            _remove = remove;
            _warped = false;
            Reason = null;
            SetState("aligning");

            double stageDv = StageDv(v);
            List<object> warnings = new List<object>();
            if (!double.IsNaN(stageDv) && stageDv < dv)
            {
                warnings.Add("current stage delta-v " + stageDv.ToString("F0") + " < node " + dv.ToString("F0") + " m/s (no auto staging)");
            }
            return new Dictionary<string, object>
            {
                ["state"] = State,
                ["node_dv"] = dv,
                ["burn_time"] = burnTime,
                ["half_dv_time"] = halfTime,
                ["ignite_ut"] = igniteUt,
                ["ignite_in"] = igniteUt - Planetarium.GetUniversalTime(),
                ["stage_dv"] = double.IsNaN(stageDv) ? null : (object)stageDv,
                ["warnings"] = warnings,
            };
        }

        /// <summary>Rocket-equation burn time at full throttle of the ignited engines.</summary>
        private static void Burn(Vessel v, double dv, out double seconds)
        {
            float pressure = (float)(v.staticPressurekPa * PhysicsGlobals.KpaToAtmospheres);
            List<ModuleEngines> engines = v.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                .Where(e => e.EngineIgnited && e.isOperational).ToList();
            double thrust = engines.Sum(e => (double)e.maxThrust * e.thrustPercentage / 100.0);
            double flow = engines.Sum(e => (double)e.maxThrust * e.thrustPercentage / 100.0 / Math.Max(1e-3, e.atmosphereCurve.Evaluate(pressure)));
            if (thrust <= 0)
            {
                throw ApiException.BadRequest("no ignited engine (stage first)");
            }
            double ve = thrust / flow * G0;
            double m0 = v.GetTotalMass();
            double m1 = m0 * Math.Exp(-dv / ve);
            seconds = (m0 - m1) / (thrust / ve);
        }

        private static double StageDv(Vessel v)
        {
            VesselDeltaV d = v.VesselDeltaV;
            if (d == null || !d.IsReady)
            {
                return double.NaN;
            }
            DeltaVStageInfo s = d.GetStage(v.currentStage);
            return s == null ? double.NaN : s.deltaVinVac;
        }

        public static object Describe() => new Dictionary<string, object>
        {
            ["state"] = State,
            ["reason"] = Reason,
            ["ignite_ut"] = Running ? (object)_igniteUt : null,
            ["dv_remaining"] = _v != null ? Remaining(_v) : null,
        };

        public static void Cancel(string reason)
        {
            if (Running)
            {
                Abort(reason);
            }
        }

        public static void FixedUpdate()
        {
            if (!Running)
            {
                return;
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || FlightGlobals.ActiveVessel != _v || _v.state == Vessel.State.DEAD)
            {
                Abort("vessel_changed");
                return;
            }
            if (_v.patchedConicSolver == null || !_v.patchedConicSolver.maneuverNodes.Contains(_node))
            {
                Abort("node_removed");
                return;
            }
            if (Math.Abs(_node.UT - _nodeUt) > 0.5 || (State != "burning" && Math.Abs(_node.GetBurnVector(_v.orbit).magnitude - _nodeDv) > 0.5))
            {
                Abort("node_changed");
                return;
            }
            if (AttitudeController.Frame != "node" || !AttitudeController.Active(_v) || !OwnsThrottle())
            {
                Abort("overridden");
                return;
            }

            Vector3d burn = _node.GetBurnVector(_v.orbit);
            double ut = Planetarium.GetUniversalTime();
            // Never burn late: past the ignition time without having ignited (still aligning, or warp not finished)
            // the burn would be off-center around the node, so stop instead of reporting a wrong "done".
            if (State != "burning" && ut > _igniteUt + IgniteLateSeconds)
            {
                Abort(State == "aligning" ? "not_aligned_in_time" : "missed_ignition");
                return;
            }
            switch (State)
            {
                case "aligning":
                    double? err = AttitudeController.ErrorDeg(_v);
                    if (err.HasValue && err.Value < AlignDeg && _v.angularVelocity.magnitude < StillRadPerSec)
                    {
                        SetState("waiting");
                    }
                    break;

                case "waiting":
                    if (_warp && !_warped && _igniteUt - ut > 2 * WarpLeadSeconds && TimeWarp.CurrentRate <= 1.01f)
                    {
                        TimeWarp.fetch.WarpTo(_igniteUt - WarpLeadSeconds);
                        _warped = true;
                    }
                    if (ut >= _igniteUt && TimeWarp.CurrentRate <= 1.01f && !_v.packed
                        && AttitudeController.ErrorDeg(_v) < AlignDeg * 2)
                    {
                        _dir0 = burn;
                        SetThrottle(1f);
                        SetState("burning");
                    }
                    break;

                case "burning":
                    if (burn.magnitude < _tolerance || Vector3d.Dot(burn, _dir0) < 0)
                    {
                        Finish();
                        break;
                    }
                    if (FlightFields.StageFlameout(_v))
                    {
                        Abort("flameout");
                        break;
                    }
                    double accel = Prediction.Get(_v).MaxAccel;
                    float throttle = accel > 1e-3 ? (float)Math.Min(1.0, burn.magnitude / accel) : 1f;
                    SetThrottle(Math.Max(MinThrottle, throttle));
                    break;
            }
        }

        private static void Finish()
        {
            StopEngines();
            if (_remove)
            {
                _node.RemoveSelf();
            }
            SetState("done");
        }

        private static void Abort(string reason)
        {
            Reason = reason;
            StopEngines();
            SetState("aborted");
        }

        private static void SetThrottle(float value)
        {
            _throttle = value;
            ThrottleLock.Set(_v, value);
        }

        /// <summary>The throttle lock is still the one this run set (another set_throttle or a release took it over otherwise).</summary>
        private static bool OwnsThrottle() =>
            _v != null && ThrottleLock.Active(_v) && Math.Abs(ThrottleLock.Value - _throttle) < 1e-4f;

        /// <summary>Throttle to 0 and release only what this run still owns, so a user's own throttle hold or attitude is left alone.</summary>
        private static void StopEngines()
        {
            if (OwnsThrottle())
            {
                ThrottleLock.Set(_v, 0f);
                FlightInputHandler.state.mainThrottle = 0f;
                ThrottleLock.Clear();
            }
            if (_v != null && AttitudeController.Active(_v) && AttitudeController.Frame == "node")
            {
                AttitudeController.Release();
            }
        }

        private static void SetState(string state)
        {
            State = state;
            if (_v != null)
            {
                FlightRecorder.NodeExecEvent(_v, new Dictionary<string, object>
                {
                    ["state"] = state,
                    ["reason"] = Reason,
                    ["dv_remaining"] = _node != null && _v.orbit != null ? (object)Math.Round(_node.GetBurnVector(_v.orbit).magnitude, 2) : null,
                    ["attitude_error_deg"] = AttitudeController.ErrorDeg(_v),
                });
            }
        }
    }
}
