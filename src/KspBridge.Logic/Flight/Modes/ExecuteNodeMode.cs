using KspBridge.Flight.Maneuver;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// execute_node: burns the earliest KSP maneuver node. Waits (WarpToUt) until node UT − burn time/2 − spool-up/2 (burn time
    /// across stages, EngineModel.BurnTimeStaged), then burns along the node's remaining Δv (GetBurnVector), throttling down over the
    /// last 2 s of Δv and only while attitude_error ≤ CircularizeMode.AttitudeGateDeg. Below CircularizeMode.LockDv the direction is
    /// locked. Done when the remaining Δv is within tolerance + engine tailoff, or after the lock when it grows again on a fired tick;
    /// the node is then removed. Constants are shared with CircularizeMode, the burn logic is not (design 0032 decision 19).
    /// Mode reasons: no_node (none at Begin, or removed/changed while running), node_missed (burn start passed by more than
    /// late_limit_s). Events: node_burn_start, insufficient_dv (once, the stages cannot supply the node's Δv), node_burn_done.
    /// </summary>
    public sealed class ExecuteNodeMode : IFlightMode
    {
        public static readonly string[] Args = { "tolerance", "late_limit_s" };

        /// <summary>A node whose UT moved by more than this (s) is treated as another node.</summary>
        private const double NodeUtTolerance = 1;

        /// <summary>Attitude gate (deg) once the direction is locked. A burn off the locked axis leaves a sideways Δv the locked
        /// burn cannot remove (10 m/s at 5° ≈ 0.87 m/s; 0.373 m/s left at the 0.2 tolerance in the unit 3 measurement); the locked
        /// target does not move, so the error settles below this quickly (design 0032 decision 19).</summary>
        private const double LockedGateDeg = 1;

        private readonly double _tolerance;
        private readonly double _lateLimit;

        private double _nodeUt = double.NaN;
        private double _dvPlanned = double.NaN;
        private bool _burning;
        private double _startUt = double.NaN;
        private double _startDv = double.NaN;
        private double _startBurn = double.NaN;
        private double _startLate = double.NaN;
        private bool _startShort;
        private bool _startReported;
        private bool _shortReported;
        private Vector3d? _lockedDir;   // world
        private double _prevDv = double.PositiveInfinity;
        private bool _gated;   // log only

        private ExecuteNodeMode(double tolerance, double lateLimit)
        {
            _tolerance = tolerance;
            _lateLimit = lateLimit;
        }

        /// <summary>Validates the arguments at submit time and returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double tolerance = Plan.OptionalNumber(o, "tolerance", path, 0.01, 100, 0.2);
            double late = Plan.OptionalNumber(o, "late_limit_s", path, 0, 3600, 10);
            return () => new ExecuteNodeMode(tolerance, late);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
            ManeuverNode n = FirstNode(v) ?? throw new ModeAbortException("no_node", "no maneuver node to execute");
            _nodeUt = n.UT;
            _dvPlanned = n.DeltaV.magnitude;
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            if (Done)
            {
                return new ModeCommand(null, 0);   // the node is gone by now: never judge no_node after completion
            }
            ManeuverNode n = FirstNode(v);
            if (n == null || Math.Abs(n.UT - _nodeUt) > NodeUtTolerance)
            {
                throw new ModeAbortException("no_node", n == null ? "maneuver node removed" : "maneuver node changed");
            }
            double ut = Planetarium.GetUniversalTime();
            if (n.nextPatch == null)
            {
                return new ModeCommand(null, 0);   // KSP has not computed the node's orbit yet: wait a tick
            }
            Vector3d burn = n.GetBurnVector(v.orbit);   // world, remaining Δv
            double dvMag = burn.magnitude;
            AttitudeTarget aim = AttitudeTarget.Vector((_lockedDir ?? burn.normalized));

            if (!_burning)
            {
                double bt = EngineModel.BurnTimeStaged(v, dvMag, out bool shortDv);
                if (double.IsInfinity(bt))
                {
                    return new ModeCommand(aim, 1);   // no stage can fire: the Coordinator's no-thrust wait / no_thrust
                }
                double start = n.UT - bt / 2 - EngineModel.SpoolUpS(v) / 2;
                if (ut < start)
                {
                    return new ModeCommand(aim, 0) { WarpToUt = start };
                }
                double late = ut - start;
                if (late > _lateLimit)
                {
                    throw new ModeAbortException("node_missed", "burn start passed " + F(late, 1) + " s ago (late_limit_s " + F(_lateLimit, 1) + ")");
                }
                _burning = true;
                _startUt = ut;
                _startDv = dvMag;
                _startBurn = bt;
                _startLate = late;
                _startShort = shortDv;
                Log("burn start: node_ut=" + F(n.UT, 2) + " dv=" + F(dvMag, 2) + " burn=" + F(bt, 2) + " late=" + F(late, 2) + " short=" + shortDv);
            }
            if (!_startReported && !FlightEvents.InTrial)   // the trial instance becomes the running mode: report on its first real tick
            {
                _startReported = true;
                FlightEvents.Add("node_burn_start", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["dv_planned"] = R(_dvPlanned),
                    ["dv_remaining"] = R(_startDv),
                    ["burn_time"] = R(_startBurn),
                    ["late_s"] = R(_startLate),
                    ["node_ut"] = R(_nodeUt),
                });
                if (_startShort && !_shortReported)
                {
                    _shortReported = true;
                    FlightEvents.Add("insufficient_dv", Coordinator.StepIndex, new Dictionary<string, object>
                    {
                        ["dv_node"] = R(_startDv),
                        ["dv_available"] = R(t.Num["total_dv"]),
                    });
                }
            }

            if (_lockedDir == null && dvMag < CircularizeMode.LockDv)
            {
                _lockedDir = burn.normalized;
                Log("direction locked: dv=" + F(dvMag, 3));
            }
            string reason = dvMag <= EngineModel.TailoffDv(v) + _tolerance ? "dv" : null;
            bool fired = t.PrevCommandThrottle > 0 && t.Num["thrust"] > 0;
            if (_lockedDir != null && fired)
            {
                if (reason == null && dvMag > _prevDv)
                {
                    reason = "dv_growing";
                }
                _prevDv = dvMag;
            }
            if (reason != null)
            {
                if (!FlightEvents.InTrial)   // the trial changes nothing: the first real tick finishes and removes the node
                {
                    Finish(v, n, dvMag, ut, reason);
                }
                return new ModeCommand(null, 0);
            }

            AttitudeTarget att = AttitudeTarget.Vector(_lockedDir ?? burn.normalized);
            double accel = EngineModel.MaxAccel(v);
            if (accel <= 0)
            {
                return new ModeCommand(att, 1);   // wants thrust (staging gap): the Coordinator decides wait or no_thrust
            }
            double throttle = Clamp01(dvMag / (accel * 2));   // fade over the last 2 s of Δv
            double err = t.Num["attitude_error"];
            double gate = _lockedDir != null ? LockedGateDeg : CircularizeMode.AttitudeGateDeg;
            bool gated = double.IsNaN(err) || err > gate;
            if (gated)
            {
                throttle = 0;
            }
            if (gated != _gated)
            {
                _gated = gated;
                Log("attitude gate " + (gated ? "closed" : "open") + ": err=" + F(err, 2) + " dv=" + F(dvMag, 2));
            }
            return new ModeCommand(att, throttle);
        }

        private void Finish(Vessel v, ManeuverNode n, double dvRemaining, double ut, string reason)
        {
            Done = true;
            Log("done: " + reason + " dv_remaining=" + F(dvRemaining, 3) + " pe=" + F(v.orbit.PeA, 0) + " ap=" + F(v.orbit.ApA, 0));
            FlightEvents.Add("node_burn_done", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["dv_planned"] = R(_dvPlanned),
                ["dv_executed"] = R(_startDv - dvRemaining),
                ["dv_remaining"] = R(dvRemaining),
                ["burn_s"] = R(ut - _startUt),
                ["orbit_after"] = OrbitMath.OrbitSummary(v.orbit),
            });
            n.RemoveSelf();
        }

        private static ManeuverNode FirstNode(Vessel v)
        {
            PatchedConicSolver s = v.patchedConicSolver;
            return s != null && s.maneuverNodes.Count > 0 ? s.maneuverNodes[0] : null;
        }

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] execute_node " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        private static object R(double x) => OrbitMath.Num(x);

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        public Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["node_ut"] = _nodeUt,
            ["dv_planned"] = _dvPlanned,
            ["burning"] = _burning,
            ["start_ut"] = _startUt,
            ["start_dv"] = _startDv,
            ["start_burn"] = _startBurn,
            ["start_late"] = _startLate,
            ["start_short"] = _startShort,
            ["start_reported"] = _startReported,
            ["short_reported"] = _shortReported,
            ["locked_dir"] = _lockedDir is Vector3d d ? new List<object> { d.x, d.y, d.z } : null,
            ["prev_dv"] = _prevDv,
        };

        /// <summary>Restores Save's keys (the node itself stays in the game's save). locked_dir is a world direction: Stop and
        /// Start run in the same frame. prev_dv null = +∞.</summary>
        public void Load(Vessel v, Dictionary<string, object> state)
        {
            _nodeUt = Handover.D(state, "node_ut", double.NaN);
            _dvPlanned = Handover.D(state, "dv_planned", double.NaN);
            _burning = B(state, "burning");
            _startUt = Handover.D(state, "start_ut", double.NaN);
            _startDv = Handover.D(state, "start_dv", double.NaN);
            _startBurn = Handover.D(state, "start_burn", double.NaN);
            _startLate = Handover.D(state, "start_late", double.NaN);
            _startShort = B(state, "start_short");
            _startReported = B(state, "start_reported");
            _shortReported = B(state, "short_reported");
            _lockedDir = state.TryGetValue("locked_dir", out object l) && l is List<object> xs && xs.Count == 3
                ? new Vector3d(Convert.ToDouble(xs[0]), Convert.ToDouble(xs[1]), Convert.ToDouble(xs[2]))
                : (Vector3d?)null;
            _prevDv = Handover.D(state, "prev_dv", double.PositiveInfinity);
        }

        private static bool B(Dictionary<string, object> state, string key) => state.TryGetValue(key, out object b) && b is bool x && x;

        public void End(Vessel v)
        {
        }
    }
}
