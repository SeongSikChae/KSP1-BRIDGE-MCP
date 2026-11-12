using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// circularize: waits (WarpToUt) until the burn start before apoapsis (half the burn time and half the spool-up early), then burns along
    /// the Δv vector from the current orbital velocity to the circular velocity at the current position, throttling down over the last
    /// 2 s of Δv. Below 10 m/s the burn direction is locked so the 5° attitude gate stays still. Done when the remaining Δv is within
    /// tolerance + engine tailoff, or after the lock when Δv starts to grow again or the eccentricity stops dropping for EccFlatTicks
    /// ticks; the last two are judged only on ticks that fired (last final command throttle &gt; 0 and thrust &gt; 0).
    /// No-thrust waiting and the no_thrust abort are the Coordinator's common judgement: the mode just asks for throttle 1.
    /// </summary>
    public sealed class CircularizeMode : IFlightMode
    {
        public static readonly string[] Args = { "tolerance" };

        /// <summary>Consecutive fired ticks without an eccentricity drop that complete the burn after the lock.</summary>
        private const int EccFlatTicks = 5;

        /// <summary>An eccentricity drop smaller than this counts as no drop.</summary>
        private const double EccTolerance = 0;

        /// <summary>Remaining Δv (m/s) below which the burn direction is locked.</summary>
        private const double LockDv = 10;

        /// <summary>Attitude error (deg) above which the mode does not burn.</summary>
        private const double AttitudeGateDeg = 5;

        private readonly double _tolerance;
        private bool _burning;
        private Vector3d? _lockedDir;   // Orbit frame (orbit.pos / orbit.vel)
        private double _prevDv = double.PositiveInfinity;
        private double _prevEcc = double.PositiveInfinity;
        private int _eccFlat;
        private bool _gated;   // log only: last attitude gate state

        private CircularizeMode(double tolerance)
        {
            _tolerance = tolerance;
        }

        /// <summary>Validates the arguments at submit time and returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double tolerance = Plan.OptionalNumber(o, "tolerance", path, 0, 100, 0.5);
            return () => new CircularizeMode(tolerance);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            double ut = Planetarium.GetUniversalTime();
            if (!_burning)
            {
                Orbit o = v.orbit;
                bool pastAp = o.timeToAp > o.period / 2;   // already past apoapsis when the step started
                double utAp = ut + o.timeToAp;
                double dvAp = Math.Sqrt(v.mainBody.gravParameter / o.ApR) - o.getOrbitalVelocityAtUT(utAp).magnitude;
                double burn = EngineModel.BurnTime(v, dvAp);
                ResetStall();
                if (double.IsInfinity(burn))
                {
                    // Wants thrust: waiting for staging or the no_thrust abort is the Coordinator's call.
                    return new ModeCommand(AttitudeTarget.Vector(Horizontal(v)), 1);
                }
                double start = utAp - burn / 2 - EngineModel.SpoolUpS(v) / 2;
                if (!pastAp && ut < start)
                {
                    return new ModeCommand(AttitudeTarget.Vector(Horizontal(v)), 0) { WarpToUt = start };
                }
                _burning = true;   // start already passed or apoapsis passed: burn now (no WarpToUt, the Coordinator releases warp)
                Log("burn start: past_ap=" + pastAp + " tta=" + F(o.timeToAp, 1) + " dv_ap=" + F(dvAp, 2) + " burn=" + F(burn, 2) +
                    " start_ut=" + F(start, 2) + " ut=" + F(ut, 2) + " stage_dv=" + F(t.Num["stage_dv"], 1) +
                    " xzy_angle=" + F(Vector3d.Angle(o.vel.xzy, v.obt_velocity), 4));   // Orbit frame .xzy vs world obt_velocity (expect ~0)
            }

            Vector3d r = v.orbit.pos, vel = v.orbit.vel;
            Vector3d h = Vector3d.Cross(r, vel).normalized;
            Vector3d vCirc = Vector3d.Cross(h, r).normalized * Math.Sqrt(v.mainBody.gravParameter / r.magnitude);
            Vector3d dv = vCirc - vel;   // Δv vector at the current position
            double dvMag = dv.magnitude, ecc = v.orbit.eccentricity;

            if (_lockedDir == null && dvMag < LockDv)
            {
                _lockedDir = dv.normalized;
                Log("direction locked: dv=" + F(dvMag, 3) + " ecc=" + F(ecc, 5));
            }
            string reason = dvMag <= EngineModel.TailoffDv(v) + _tolerance ? "dv" : null;
            // Only ticks whose last final command throttle (after overrides) was > 0 and that have thrust now judge growth/flatness.
            bool fired = t.PrevCommandThrottle > 0 && t.Num["thrust"] > 0;
            if (_lockedDir != null && fired)
            {
                _eccFlat = ecc < _prevEcc - EccTolerance ? 0 : _eccFlat + 1;
                if (reason == null && dvMag > _prevDv)
                {
                    reason = "dv_growing";
                }
                if (reason == null && _eccFlat >= EccFlatTicks)
                {
                    reason = "ecc_flat";
                }
                _prevDv = dvMag;
                _prevEcc = ecc;
            }
            else
            {
                ResetStall();
            }
            if (_lockedDir != null)
            {
                Log("locked tick: dv=" + F(dvMag, 4) + " ecc=" + F(ecc, 6) + " fired=" + fired + " ecc_flat=" + _eccFlat +
                    " prev_throttle=" + F(t.PrevCommandThrottle, 3) + " thrust=" + F(t.Num["thrust"], 2));
            }
            if (reason != null)
            {
                Done = true;
                Log("done: " + reason + " dv=" + F(dvMag, 3) + " ecc=" + F(ecc, 5) + " pe=" + F(v.orbit.PeA, 0) + " ap=" + F(v.orbit.ApA, 0));
                return new ModeCommand(null, 0);
            }

            AttitudeTarget att = AttitudeTarget.Vector((_lockedDir ?? dv.normalized).xzy);   // Orbit frame → world (obt_velocity = orbit.vel.xzy)
            double accel = EngineModel.MaxAccel(v);
            if (accel <= 0)
            {
                return new ModeCommand(att, 1);   // wants thrust, no attitude gate: the Coordinator decides wait or no_thrust
            }
            double throttle = Clamp01(dvMag / (accel * 2));   // fade over the last 2 s of Δv
            double err = t.Num["attitude_error"];
            bool gated = double.IsNaN(err) || err > AttitudeGateDeg;
            if (gated)
            {
                throttle = 0;   // no burn until the attitude is right (no value included); after the lock the target does not move
            }
            if (gated != _gated)
            {
                _gated = gated;
                Log("attitude gate " + (gated ? "closed" : "open") + ": err=" + F(err, 2) + " dv=" + F(dvMag, 2) + " locked=" + (_lockedDir != null));
            }
            return new ModeCommand(att, throttle);
        }

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] circularize " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Horizontal direction of the orbital velocity (world frame), the attitude held while waiting.</summary>
        private static Vector3d Horizontal(Vessel v)
        {
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            return Vector3d.Exclude(up, v.obt_velocity).normalized;
        }

        private void ResetStall()
        {
            _prevDv = double.PositiveInfinity;
            _prevEcc = double.PositiveInfinity;
            _eccFlat = 0;
        }

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        public Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["burning"] = _burning,
            ["locked_dir"] = _lockedDir is Vector3d d ? new List<object> { d.x, d.y, d.z } : null,
            ["prev_dv"] = _prevDv,
            ["prev_ecc"] = _prevEcc,
            ["ecc_flat"] = _eccFlat,
        };

        /// <summary>Restores Save's keys. locked_dir null = not locked yet; prev_dv / prev_ecc null = +∞ (Json.Serialize writes ∞ as null).</summary>
        public void Load(Vessel v, Dictionary<string, object> state)
        {
            _burning = state.TryGetValue("burning", out object b) && b is bool burning && burning;
            _lockedDir = state.TryGetValue("locked_dir", out object l) && l is List<object> xs && xs.Count == 3
                ? new Vector3d(Convert.ToDouble(xs[0]), Convert.ToDouble(xs[1]), Convert.ToDouble(xs[2]))
                : (Vector3d?)null;
            _prevDv = Handover.D(state, "prev_dv", double.PositiveInfinity);
            _prevEcc = Handover.D(state, "prev_ecc", double.PositiveInfinity);
            _eccFlat = (int)Handover.D(state, "ecc_flat", 0);
        }
    }
}
