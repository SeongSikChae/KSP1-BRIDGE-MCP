using KspBridge.Flight.Control;
using KspBridge.Flight.Landing;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// land: powered landing from a descent (#17, docs 0028~0030). Burns against the stopping distance to the ground
    /// (radar altitude minus the vessel bottom), holds final_speed below final_altitude, tilts to vertical as the
    /// horizontal speed drops, turns on the Gear group at gear_altitude and completes once landed and still.
    /// </summary>
    public sealed class LandMode : IFlightMode
    {
        public static readonly string[] Args =
            { "margin", "final_altitude", "final_speed", "gear_altitude", "latitude", "longitude", "vessel" };

        private readonly double _margin;
        private readonly double _finalAlt;
        private readonly double _finalSpeed;
        private readonly double _gearAlt;   // NaN = gear off
        private readonly LandingTracker _track;   // null = vertical landing in place (#17); #22 target and impact prediction

        // #22 velocity loop state (Save/Load)
        private bool _inLoop;
        private Vector3d _aCmdPrev = Vector3d.zero;
        private double _aCmdUt;
        private double _dvShortSince = double.NaN;
        private double _attLostSince = double.NaN;

        // #22 U4 correction burn state (Save/Load)
        private bool _corrDecided;
        private bool _correcting;
        private Vector3d _corrDv = Vector3d.zero;   // inertial, world axes
        private double _corrUsed;
        private double _corrStartUt;
        private double _corrPredUt = double.NaN;    // last prediction UT counted for divergence
        private double _corrPrevErr = double.NaN;
        private int _corrGrowing;
        private double _corrUsedAtPred;             // _corrUsed at the last counted prediction

        private LandMode(double margin, double finalAlt, double finalSpeed, double gearAlt, LandingTarget target)
        {
            _margin = margin;
            _finalAlt = finalAlt;
            _finalSpeed = finalSpeed;
            _gearAlt = gearAlt;
            _track = target == null ? null : new LandingTracker(target);
        }

        /// <summary>get_flight.landing: null without a target (or after the target was dropped).</summary>
        public Dictionary<string, object> Landing(Vessel v) => _track?.Describe(v);

        /// <summary>Validates the arguments at submit time and returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double margin = Plan.OptionalNumber(o, "margin", path, 0.1, 1, 0.8);
            double finalAlt = Plan.OptionalNumber(o, "final_altitude", path, 0, 1000, 20);
            double finalSpeed = Plan.OptionalNumber(o, "final_speed", path, 0.1, 20, 2);
            double gearAlt;
            if (o.TryGetValue("gear_altitude", out object g) && g is bool b)
            {
                if (b)
                {
                    throw Plan.Invalid(path + ".gear_altitude", "must be a number 0..10000 or false");
                }
                gearAlt = double.NaN;
            }
            else
            {
                gearAlt = Plan.OptionalNumber(o, "gear_altitude", path, 0, 10000, 500);
            }
            // form only (#22 decision 1, 9): the vessel's existence and state are checked when the step starts
            LandingTarget target = LandingTarget.Parse(o.ContainsKey, k => o[k], path, (p, r) => Plan.Invalid(p, r));
            return () => new LandMode(margin, finalAlt, finalSpeed, gearAlt, target);
        }

        /// <summary>Re-measure interval (s) while the legs extend after the Gear group turns on.</summary>
        private const double RemeasureS = 0.5;

        /// <summary>How long (s) after turning on the Gear group the bottom keeps being re-measured.</summary>
        private const double GearSettleS = 5;

        /// <summary>Max tilt from vertical (deg) per m/s of horizontal speed (decision 10; no dead band, so a slow drift is
        /// still cancelled before touchdown).</summary>
        private const double TiltPerMs = 5;

        // #22 decision 6: horizontal velocity loop toward a target
        private const double Deg = Math.PI / 180;

        /// <summary>Velocity loop time constant (s): U3 1st run hunted with a 3..5 s period.</summary>
        private const double Tau = 4;

        /// <summary>Tilt cap (deg) above / below the vertical-first height.</summary>
        private const double GuidedTiltCap = 30;
        private const double VerticalTiltCap = 15;

        /// <summary>Vertical deceleration margin (deg) under acos(a_req / a_max): above the +10° tracking error allowed.</summary>
        private const double VerticalMarginDeg = 12;

        /// <summary>Below this cap (deg) there is no room for horizontal guidance: end it (no_margin).</summary>
        private const double MinCapDeg = 1;

        /// <summary>Commanded tilt / direction change limit (deg/s): measured average turn rate below 600 m.</summary>
        private const double TiltRateDeg = 10;

        /// <summary>Vertical-first height floor (m); the height is max(this, final_altitude × 5).</summary>
        private const double VerticalFirstM = 100;

        /// <summary>Fuel reserve (m/s) over the vertical landing need, and how long (s) a shortfall must last.</summary>
        private const double DvReserve = 50;
        private const double DvShortS = 2;

        /// <summary>In the air the wanted horizontal speed keeps the velocity within this angle (deg) of vertical: tail-first
        /// flight stays aerodynamically stable (U3 2nd run tumbled at vh 44 / vv 11 m/s).</summary>
        private const double AoaLimitDeg = 15;

        /// <summary>Axis-to-command angle (deg) and duration (s) that count as a lost attitude in the loop.</summary>
        private const double AttitudeLostDeg = 30;
        private const double AttitudeLostS = 1;

        /// <summary>#22 decision 5: correct above the atmosphere when the predicted error is over this (m).</summary>
        private const double CorrectStartM = 100;

        /// <summary>Correction converged below this predicted error (m).</summary>
        private const double CorrectDoneM = 20;

        /// <summary>Correction time limit from its start, alignment included (s).</summary>
        private const double CorrectMaxS = 60;

        /// <summary>Throttle 0 until the axis is within this of the correction direction (deg).</summary>
        private const double CorrectAlignDeg = 5;

        /// <summary>Throttle = remaining Δv / (max accel × this): tapers over the last seconds.</summary>
        private const double CorrectTaperS = 2;

        /// <summary>Lowest throttle once aligned, so the remaining Δv runs out instead of tapering forever.</summary>
        private const double CorrectMinThrottle = 0.05;

        /// <summary>Consecutive predicted error increases that end the correction as diverged.</summary>
        private const int CorrectGrowMax = 3;

        /// <summary>Δv (m/s) applied between two predictions for an error increase to count toward divergence.</summary>
        private const double CorrectGrowMinDv = 0.05;

        /// <summary>Angular rate (rad/s) below which the axis counts as settled for the correction burn.</summary>
        private const double CorrectAlignRate = 0.02;

        /// <summary>Final-segment speed gain (1/s).</summary>
        private const double FinalGain = 1;

        /// <summary>Axis-to-target angle (deg) for a normal start and for ending the post-forced-start alignment wait.</summary>
        private const double AlignDeg = 15;

        /// <summary>Descent speed (m/s) a burn needs to start: a vessel resting on the ground never starts.</summary>
        private const double MinStartVv = 0.5;

        /// <summary>Interval (s) of the burn and post-touchdown telemetry log lines (Player.log, landing diagnosis).</summary>
        private const double DiagS = 1;

        /// <summary>Surface speed (m/s) below which a landed vessel counts as still.</summary>
        private const double StillSpeed = 0.5;

        /// <summary>Seconds the vessel must stay landed and still to complete.</summary>
        private const double StillS = 2;

        /// <summary>Angular speed (rad/s) below which a landed vessel counts as still (a slow tip-over is not still).</summary>
        private const double StillAngVel = 0.05;

        /// <summary>Tilt (deg) from vertical above which the landed event reports tipped.</summary>
        private const double TippedDeg = 45;

        private bool _touched;
        private double _touchUt;
        private double _stillSince = double.NaN;   // NaN = not still
        private readonly List<string> _infeasible = new List<string>();   // landing_infeasible reasons already reported
        private double _diagUt;
        private bool _burning;
        private bool _aligning;   // forced start with the axis still turning: until it first comes within AlignDeg
        private bool _gearDone;
        private double _gearSettleUntil;   // 0 = not settling

        // bottom_offset cache: re-measured when the part count or the Gear group changes, and every RemeasureS while settling
        private double _bottom;
        private int _partCount = -1;
        private bool _gearState;
        private double _measuredUt;

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            double h = Height(v);
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            double r = (v.CoMD - v.mainBody.position).magnitude;
            double g = v.mainBody.gravParameter / (r * r);
            double vv = -v.verticalSpeed;   // down positive
            double vh = Vector3d.Exclude(up, v.srf_velocity).magnitude;
            double cos = Vector3d.Dot(v.ReferenceTransform.up, up);

            // after the first touchdown: throttle 0, hold vertical, complete once landed and still for StillS
            bool landed = v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED;
            double ut = Planetarium.GetUniversalTime();
            if (!_touched)
            {
                _track?.Tick(v, ut);   // #22 U2: fixes the target and predicts only; attitude and throttle unchanged
            }
            // Not in the submit trial: its events are committed after plan_started with the previous plan's step index
            // (the Coordinator sets the step after the trial); the first real Tick records the touchdown.
            if (landed && !_touched && !FlightEvents.InTrial)
            {
                _touched = true;
                _touchUt = ut;
                Log("touchdown: vv=" + F(v.verticalSpeed, 2) + " vh=" + F(vh, 2) + " sit=" + v.situation + " h=" + F(h, 2));
                FlightEvents.Add("touchdown", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["vertical_speed"] = R(v.verticalSpeed),
                    ["horizontal_speed"] = R(vh),
                    ["situation"] = v.situation.ToString(),
                });
            }
            if (_touched)
            {
                bool still = landed && v.srfSpeed < StillSpeed && v.angularVelocity.magnitude < StillAngVel;
                _stillSince = still ?(double.IsNaN(_stillSince) ? ut : _stillSince) : double.NaN;
                if (!double.IsNaN(_stillSince) && ut - _stillSince >= StillS && !Done)
                {
                    double tilt = Vector3d.Angle(v.ReferenceTransform.up, up);
                    Log("landed: tilt=" + F(tilt, 1) + " since_touchdown=" + F(ut - _touchUt, 2));
                    Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        ["tilt_deg"] = R(tilt),
                        ["tipped"] = tilt > TippedDeg,
                        ["time_since_touchdown_s"] = R(ut - _touchUt),
                        ["latitude"] = R(v.latitude),                                // #22
                        ["longitude"] = R(LandingTarget.Wrap180(v.longitude)),
                    };
                    double err = _track != null ? _track.ErrorFrom(v) : double.NaN;
                    if (_track != null && _track.Active)
                    {
                        data["target_error_m"] = R(err);
                        data["guidance_ended"] = _track.Ended;
                    }
                    Log("landed: target_error=" + F(err, 1) + " lat=" + F(v.latitude, 6) + " lon=" + F(v.longitude, 6));
                    FlightEvents.Add("landed", Coordinator.StepIndex, data);
                    Done = true;
                }
                if (ut - _touchUt <= 10 && ut - _diagUt >= DiagS)   // post-touchdown telemetry (tip-over diagnosis)
                {
                    _diagUt = ut;
                    Log("ground: t=" + F(ut - _touchUt, 2) + " tilt=" + F(Vector3d.Angle(v.ReferenceTransform.up, up), 1) +
                        " ang_vel=" + F(v.angularVelocity.magnitude, 3) +
                        " vv=" + F(v.verticalSpeed, 2) + " vh=" + F(vh, 2) + " srf=" + F(v.srfSpeed, 2) + " sit=" + v.situation +
                        " h=" + F(h, 2));
                }
                return new ModeCommand(AttitudeTarget.Vector(up), 0);
            }

            // Never in the submit trial: a failed submit must leave the vessel unchanged; the first real Tick turns it on.
            if (!_gearDone && !double.IsNaN(_gearAlt) && h <= _gearAlt && !FlightEvents.InTrial)
            {
                _gearDone = true;
                if (!ManualControl.TrySetGroup(v, KSPActionGroup.Gear, true, out string blocked))
                {
                    Log("gear blocked: " + blocked);
                }
                _gearSettleUntil = Planetarium.GetUniversalTime() + GearSettleS;
                Log("gear on: h=" + F(h, 1));
            }

            // maxAccel and aReq before the attitude (values and uses unchanged; #22 needs them for the guidance)
            double maxAccel = EngineModel.MaxAccel(v);
            double aReq = vv > 0 && h > _finalAlt
                ? (vv * vv - _finalSpeed * _finalSpeed) / (2 * (h - _finalAlt)) + g
                : double.NaN;

            // #22 decision 5: one correction burn above the atmosphere, decided on the first real tick
            if (_track != null && !_burning && !FlightEvents.InTrial)
            {
                ModeCommand? corr = Correction(v, t, ut, h, vv, g, maxAccel);
                if (corr != null)
                {
                    return corr.Value;
                }
            }

            // #22 decision 6: while burning toward a kept target, a horizontal velocity loop (τ) once the relative horizontal
            // speed fits under the tilt cap; otherwise the #17 formula (surface retrograde, stopping at −srf_velocity).
            Vector3d vhVec = Vector3d.Exclude(up, v.srf_velocity);
            bool guide = _burning && _track != null && _track.Guiding && maxAccel > 0;
            double aV = Math.Max(double.IsNaN(aReq) ? g : aReq, g);              // vertical accel the throttle will make
            double hV = Math.Max(VerticalFirstM, _finalAlt * 5);
            bool vertical = h <= hV;
            Vector3d want = guide && !vertical ? Want(v, up, h, hV, vv, g) : Vector3d.zero;
            double capDeg = maxAccel > 0
                ? Math.Min(vertical ? VerticalTiltCap : GuidedTiltCap,
                    Math.Max(0, Math.Acos(Clamp(aV / maxAccel, 0, 1)) / Deg - VerticalMarginDeg))   // vertical decel first
                : 0;
            if (guide && capDeg < MinCapDeg && !FlightEvents.InTrial)
            {
                _track.EndGuidance();
                _track.Unreachable("no_margin", "braking", new Dictionary<string, object>
                {
                    ["cap_deg"] = R(capDeg),
                    ["a_req"] = R(aV),
                    ["a_max"] = R(maxAccel),
                });
                Log("guidance ended: no_margin cap=" + F(capDeg, 2) + " a_v=" + F(aV, 2) + " a_max=" + F(maxAccel, 2));
                guide = false;
            }
            double entryLim = Math.Max(vv, 0) * Math.Tan(capDeg * Deg);
            if (guide && !_inLoop && (want - vhVec).magnitude <= entryLim && vh <= entryLim && !FlightEvents.InTrial)
            {
                _inLoop = true;   // stays in the loop afterwards
                // seed with the tilt the #17 formula actually commands this tick (it stops at −srf_velocity): no jump
                double t17 = Math.Min(Vector3d.Angle(up, -v.srf_velocity), Clamp(vh * TiltPerMs, 0, 90));
                _aCmdPrev = vh > 1e-3 ? -vhVec.normalized * (aV * Math.Tan(Math.Min(t17, capDeg) * Deg)) : Vector3d.zero;
                Log("loop entry: h=" + F(h, 1) + " vv=" + F(vv, 2) + " vh=" + F(vh, 2) + " want=" + F(want.magnitude, 2) +
                    " cap=" + F(capDeg, 1) + " t17=" + F(t17, 2));
            }
            AttitudeTarget att;
            double maxTilt;
            Vector3d aCmd = Vector3d.zero;
            if (!guide || !_inLoop)
            {
                maxTilt = Clamp(vh * TiltPerMs, 0, 90);                          // #17 formula, unchanged
                att = AttitudeTarget.RotateTowards(up, -v.srf_velocity, maxTilt);
            }
            else
            {
                double capA = aV * Math.Tan(capDeg * Deg);
                aCmd = ClampMag((want - vhVec) / Tau, capA);
                aCmd = RateLimitTilt(_aCmdPrev, aCmd, aV, TiltRateDeg * Math.Max(0, ut - _aCmdUt));   // ≤ 10°/s
                aCmd = ClampMag(aCmd, capA);                                     // the cap stays hard after the rate limit
                _aCmdPrev = aCmd;
                att = AttitudeTarget.Vector(up * aV + aCmd);                     // tilt = atan(|aCmd| / aV) toward aCmd
                maxTilt = Math.Atan2(aCmd.magnitude, aV) / Deg;
            }
            _aCmdUt = ut;   // every tick: no large step after a thrust gap

            // #22 decision 6: attitude lost in the loop (axis more than 30° off the command for 1 s) ends the guidance
            if (guide && _inLoop && !FlightEvents.InTrial)
            {
                double axisErr = Vector3d.Angle(v.ReferenceTransform.up, att.Direction(v));
                _attLostSince = axisErr > AttitudeLostDeg ? (double.IsNaN(_attLostSince) ? ut : _attLostSince) : double.NaN;
                if (!double.IsNaN(_attLostSince) && ut - _attLostSince >= AttitudeLostS)
                {
                    _track.EndGuidance();
                    _track.Unreachable("attitude", "braking", new Dictionary<string, object> { ["axis_err_deg"] = R(axisErr) });
                    Log("guidance ended: attitude axis_err=" + F(axisErr, 1) + " h=" + F(h, 1));
                    _attLostSince = double.NaN;
                    guide = false;
                    maxTilt = Clamp(vh * TiltPerMs, 0, 90);                      // #17 formula from this tick on
                    att = AttitudeTarget.RotateTowards(up, -v.srf_velocity, maxTilt);
                }
            }
            else
            {
                _attLostSince = double.NaN;
            }
            if (_track != null && _track.Active && !FlightEvents.InTrial)
            {
                _track.SetGuidance(!_burning ? "free_fall" : _track.Ended ? "dropped" : !guide ? "off"
                    : !_inLoop ? "pre_loop" : vertical ? "vertical" : "guided");
                if (_burning)
                {
                    _track.Braking(v, v.CoMD + vhVec * Tau);   // where the loop would bring the horizontal speed to 0
                }
            }

            // #22 decision 6: fuel reserve over the remaining descent's need, 2 s in a row ends the guidance
            double dvLeft = t.Num["total_dv"];
            double dvNeed = double.NaN;
            if (!guide || double.IsNaN(dvLeft) || FlightEvents.InTrial)
            {
                _dvShortSince = double.NaN;   // not guiding this tick (thrust gap, no dv): restart the 2 s timer
            }
            else
            {
                // gravity loss over the remaining descent of the throttle formula's profile: constant decel to
                // final_altitude, then final_speed (U3: the full-thrust estimate missed the slow final descent)
                double tLeft = h > _finalAlt
                    ? 2 * (h - _finalAlt) / Math.Max(vv + _finalSpeed, 1e-3) + _finalAlt / _finalSpeed
                    : Math.Max(h, 0) / _finalSpeed;
                dvNeed = v.srfSpeed + g * tLeft;
                _dvShortSince = dvLeft < dvNeed + DvReserve ? (double.IsNaN(_dvShortSince) ? ut : _dvShortSince) : double.NaN;
                if (!double.IsNaN(_dvShortSince) && ut - _dvShortSince >= DvShortS)
                {
                    _track.EndGuidance();
                    _track.Unreachable("dv", "braking", new Dictionary<string, object>
                    {
                        ["dv_need"] = R(dvNeed),
                        ["dv_left"] = R(dvLeft),
                    });
                    Log("guidance ended: dv need=" + F(dvNeed, 1) + " left=" + F(dvLeft, 1));
                    _dvShortSince = double.NaN;
                }
            }

            if (maxAccel <= 0)
            {
                return new ModeCommand(att, 1);   // no engine thrust (not tilt): the Coordinator waits or aborts no_thrust
            }
            double aMaxV = maxAccel * cos;

            if (!_burning)
            {
                Judge(v, t, h, vv, aReq, maxAccel, "best", g, false);   // before the burn: accel only, against the best attitude
                // normal start: axis near the actual target direction (not the tilt limit), judged with the target's cos so
                // an axis tilted past the target does not start high; forced starts: near the ground, or no margin even
                // with a vertical axis
                Vector3d dir = att.Direction(v);
                double alignErr = Vector3d.Angle(v.ReferenceTransform.up, dir);
                double aMaxVTarget = maxAccel * Vector3d.Dot(dir, up);
                string why = vv <= MinStartVv ? null
                    : h <= _finalAlt ? "final_altitude"
                    : aReq >= _margin * maxAccel ? "no_margin"
                    : alignErr <= AlignDeg && aReq >= _margin * aMaxVTarget ? "aligned"
                    : null;
                if (why == null)
                {
                    return new ModeCommand(att, 0);
                }
                _burning = true;
                _aligning = why != "aligned" && alignErr > AlignDeg;
                Log("burn start: why=" + why + " h=" + F(h, 1) + " vv=" + F(vv, 2) + " vh=" + F(vh, 2) + " align_err=" + F(alignErr, 1) +
                    " cos=" + F(cos, 3) + " a_req=" + F(aReq, 2) + " a_max=" + F(maxAccel, 2) + " a_max_v_target=" + F(aMaxVTarget, 2));
            }

            double throttle = h > _finalAlt
                ? aReq / aMaxV
                : (g + FinalGain * (vv - _finalSpeed)) / aMaxV;
            throttle = cos <= 0 || double.IsNaN(throttle) ? 0 : Clamp01(throttle);

            // only after a forced start, until the axis first comes within AlignDeg of the target (latched, never re-armed):
            // scale down thrust pointed the wrong way. A normal burn whose moving target the axis lags behind is untouched.
            double burnAlignErr = Vector3d.Angle(v.ReferenceTransform.up, att.Direction(v));
            if (_aligning && burnAlignErr <= AlignDeg)
            {
                _aligning = false;
                Log("aligned after forced start: align_err=" + F(burnAlignErr, 1) + " h=" + F(h, 1));
            }
            bool misaligned = _aligning;
            if (misaligned)
            {
                throttle *= Math.Max(0, Math.Cos(burnAlignErr * Math.PI / 180));
            }

            // misaligned (after a forced start, before the first alignment): judge against the best attitude and do not
            // force full throttle, so a still-turning axis neither reports a false accel nor burns hard the wrong way
            double aCap = misaligned ? maxAccel : aMaxV;
            if (Judge(v, t, h, vv, aReq, aCap, misaligned ? "best" : "actual", g, true) && !misaligned && cos > 0)
            {
                throttle = 1;   // this tick only; back to the computed throttle once it clears
            }
            double now = Planetarium.GetUniversalTime();
            if (now - _diagUt >= DiagS)   // burn telemetry (landing diagnosis)
            {
                _diagUt = now;
                Log("burn: h=" + F(h, 1) + " radar=" + F(v.radarAltitude, 1) + " hft=" + F(v.heightFromTerrain, 1) +
                    " terrain=" + F(v.terrainAltitude, 1) + " vv=" + F(vv, 2) + " vh=" + F(vh, 2) + " thr=" + F(throttle, 3) +
                    " cos=" + F(cos, 3) + " align_err=" + F(burnAlignErr, 1) + " max_tilt=" + F(maxTilt, 1) +
                    " a_req=" + F(aReq, 2) + " a_max_v=" + F(aMaxV, 2) + " thrust=" + F(t.Num["thrust"], 1) + " sit=" + v.situation +
                    (_track != null && _track.Active
                        ? " guidance=" + (_track.Ended ? "dropped" : !guide ? "off" : !_inLoop ? "pre_loop" : vertical ? "vertical" : "guided") +
                          " want=" + F(want.magnitude, 2) + " dv_h=" + F((want - vhVec).magnitude, 2) +
                          " a_cmd=" + F(aCmd.magnitude, 3) + " a_cmd_to_target=" + F(Vector3d.Dot(aCmd, TargetDir(v, up)), 3) +
                          " a_v=" + F(aV, 2) + " cap=" + F(capDeg, 1) + " tilt=" + F(Vector3d.Angle(v.ReferenceTransform.up, up), 2) +
                          " dv_need=" + F(dvNeed, 1) + " dv_left=" + F(dvLeft, 1)
                        : ""));
            }
            return new ModeCommand(att, throttle) { Aligning = misaligned };
        }

        /// <summary>Infeasibility on this tick (no latch). aCap: MaxAccel (best attitude) before the burn and while _aligning
        /// after a forced start, otherwise the actual vertical capacity aMaxV; basis says which. Reports once per step and
        /// reason. checkDv: the dv reason is judged only while burning (before it the descent still loses speed to drag).</summary>
        private bool Judge(Vessel v, Telemetry t, double h, double vv, double aReq, double aCap, string basis, double g, bool checkDv)
        {
            if (!(vv > _finalSpeed))
            {
                return false;
            }
            bool infeasible = false;
            if (h > _finalAlt && aReq > aCap)
            {
                infeasible = true;
                Report("accel", aReq, aCap, basis, double.NaN, double.NaN, h);
            }
            double dvLeft = t.Num["total_dv"];
            if (checkDv && !double.IsNaN(dvLeft) && aCap > g)   // dv only while burning: before it, drag is not counted and would report a false shortfall
            {
                double dvNeed = v.srfSpeed + g * vv / (aCap - g);   // speed + gravity loss over the vertical stop
                if (dvLeft < dvNeed)
                {
                    infeasible = true;
                    Report("dv", aReq, aCap, basis, dvNeed, dvLeft, h);
                }
            }
            return infeasible;
        }

        private void Report(string reason, double aReq, double aMax, string basis, double need, double left, double h)
        {
            if (_infeasible.Contains(reason) || FlightEvents.InTrial)   // trial: reported on the first real Tick instead
            {
                return;   // once per step and reason
            }
            _infeasible.Add(reason);
            Log("landing_infeasible: reason=" + reason + " a_req=" + F(aReq, 2) + " a_max=" + F(aMax, 2) + " basis=" + basis +
                " dv_need=" + F(need, 1) + " dv_left=" + F(left, 1) + " h=" + F(h, 1));
            FlightEvents.Add("landing_infeasible", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["a_req"] = R(aReq),
                ["a_max"] = R(aMax),
                ["a_max_basis"] = basis,
                ["dv_need"] = R(need),
                ["dv_left"] = R(left),
                ["altitude"] = R(h),
            });
        }

        /// <summary>Rounded to 3 decimals; NaN and infinity become null (JSON).</summary>
        private static object R(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, 3);

        private static double Clamp(double x, double min, double max) => x < min ? min : x > max ? max : x;

        private static double Clamp01(double x) => Clamp(x, 0, 1);

        /// <summary>Ground clearance of the vessel bottom: radarAltitude (CoM) minus the CoM-to-bottom distance.</summary>
        private double Height(Vessel v)
        {
            double ut = Planetarium.GetUniversalTime();
            bool gear = v.ActionGroups[KSPActionGroup.Gear];
            int count = v.parts.Count;
            if (count != _partCount || gear != _gearState || (ut < _gearSettleUntil && ut - _measuredUt >= RemeasureS))
            {
                _bottom = MeasureBottom(v, out int fallback);
                _partCount = count;
                _gearState = gear;
                _measuredUt = ut;
                Log("bottom=" + F(_bottom, 3) + " parts=" + count + " gear=" + gear + " fallback=" + fallback +
                    " radar=" + F(v.radarAltitude, 2));
            }
            return v.radarAltitude - _bottom;
        }

        /// <summary>Distance from the CoM to the lowest collider point along the vessel's −up axis (ReferenceTransform).
        /// fallback = colliders measured by their world AABB (shapes ClosestPoint does not support).</summary>
        private static double MeasureBottom(Vessel v, out int fallback)
        {
            fallback = 0;
            Vector3 com = v.CoMD;
            Vector3 down = -v.ReferenceTransform.up;
            Vector3 far = com + down * 1000f;
            double max = 0;
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Collider[] cs = parts[i].GetPartColliders();
                if (cs == null)
                {
                    continue;
                }
                for (int j = 0; j < cs.Length; j++)
                {
                    Collider c = cs[j];
                    if (c == null || !c.enabled || c.isTrigger)
                    {
                        continue;
                    }
                    max = Math.Max(max, Lowest(c, com, down, far, ref fallback));
                }
            }
            return max;
        }

        /// <summary>ClosestPoint only for the shapes Unity supports (it returns the query point unchanged for others);
        /// otherwise the lowest of the 8 world AABB corners (below the real shape: conservative).</summary>
        private static double Lowest(Collider c, Vector3 com, Vector3 down, Vector3 far, ref int fallback)
        {
            MeshCollider mc = c as MeshCollider;
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (mc != null && mc.convex))
            {
                return Vector3.Dot(c.ClosestPoint(far) - com, down);
            }
            fallback++;
            Bounds b = c.bounds;
            double max = double.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                max = Math.Max(max, Vector3.Dot(p - com, down));
            }
            return max;
        }

        /// <summary>#22 decision 6: |want| = min(|d| / tGoV, √(2·a_hLim·|d|), |d| / (4τ)) toward the target; tGoV to the
        /// vertical-first height, a_hLim = 0.5·g·tan 30°; |d|/(4τ) with the τ loop is critically damped.</summary>
        /// <summary>#22 decision 5: decides once on the first real tick (the tracker has just fixed the target and predicted),
        /// then runs the correction burn until a stop condition. null: no correction command this tick, the normal flow goes on
        /// from the same tick (no burn start judgement while correcting).</summary>
        private ModeCommand? Correction(Vessel v, Telemetry t, double ut, double h, double vv, double g, double maxAccel)
        {
            CelestialBody body = v.mainBody;
            bool above = !body.atmosphere || v.altitude >= body.atmosphereDepth;
            if (!_corrDecided)
            {
                _corrDecided = true;
                if (!_track.Active)
                {
                    return null;   // no target (unresolved vessel): nothing to correct
                }
                double err0 = _track.PredictionError(body);
                if (_track.Prediction == null)
                {
                    _track.Unreachable("no_impact", "correction", null);
                    Log("correction: no impact");
                    return null;
                }
                if (!above || err0 <= CorrectStartM)
                {
                    Log("correction: skipped above=" + above + " err=" + F(err0, 1));
                    return null;
                }
                if (!_track.SolveCorrection(v, ut, out _corrDv))
                {
                    _track.Unreachable("no_solution", "correction", new Dictionary<string, object> { ["error_m"] = R(err0) });
                    Log("correction: no solution err=" + F(err0, 1));
                    return null;
                }
                double dvLeft = t.Num["total_dv"];
                if (_corrDv.magnitude > dvLeft)
                {
                    _track.Unreachable("dv", "correction", new Dictionary<string, object>
                    {
                        ["dv_need"] = R(_corrDv.magnitude),
                        ["dv_left"] = R(dvLeft),
                    });
                    Log("correction: dv need=" + F(_corrDv.magnitude, 1) + " left=" + F(dvLeft, 1));
                    return null;   // keep landing with the burn-time guidance
                }
                _correcting = true;
                _corrStartUt = ut;
                _corrUsed = 0;
                _corrPredUt = _track.PredictionUt;
                _corrPrevErr = err0;
                _corrGrowing = 0;
                _corrUsedAtPred = 0;
                FlightEvents.Add("landing_correction_start", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["error_m"] = R(err0),
                    ["dv"] = R(_corrDv.magnitude),
                });
                Log("correction start: err=" + F(err0, 1) + " dv=" + F(_corrDv.magnitude, 2) + " h=" + F(h, 0));
            }
            if (!_correcting)
            {
                return null;
            }

            double err = _track.PredictionError(body);
            // a new prediction: count consecutive increases, only across predictions with Δv applied in between
            // (U4 1st run: sub-metre changes of the unpowered prediction while coasting counted as divergence)
            if (_track.PredictionUt != _corrPredUt)
            {
                _corrPredUt = _track.PredictionUt;
                if (!double.IsNaN(err) && !double.IsNaN(_corrPrevErr) && _corrUsed - _corrUsedAtPred >= CorrectGrowMinDv)
                {
                    _corrGrowing = err > _corrPrevErr ? _corrGrowing + 1 : 0;
                }
                _corrPrevErr = err;
                _corrUsedAtPred = _corrUsed;
            }
            // distance to stop the descent at full thrust; ×2 ≥ ground height leaves no margin for the braking burn
            double stopDist = maxAccel > g ? Math.Max(vv, 0) * Math.Max(vv, 0) / (2 * (maxAccel - g)) : double.PositiveInfinity;
            string stop = err < CorrectDoneM ? "converged"
                : _corrUsed >= _corrDv.magnitude ? "dv_applied"
                : !above ? "atmosphere"
                : _corrGrowing >= CorrectGrowMax ? "diverged"
                : stopDist * 2 >= h ? "no_margin"
                : ut - _corrStartUt >= CorrectMaxS ? "timeout"
                : null;
            if (stop != null)
            {
                _correcting = false;
                FlightEvents.Add("landing_correction_done", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["error_m"] = R(err),
                    ["dv_used"] = R(_corrUsed),
                    ["dv"] = R(_corrDv.magnitude),
                    ["reason"] = stop,
                    ["elapsed_s"] = R(ut - _corrStartUt),
                });
                if (stop == "diverged")
                {
                    _track.Unreachable("correction_diverged", "correction", new Dictionary<string, object> { ["error_m"] = R(err) });
                }
                Log("correction done: " + stop + " err=" + F(err, 1) + " used=" + F(_corrUsed, 2) + "/" + F(_corrDv.magnitude, 2) +
                    " elapsed=" + F(ut - _corrStartUt, 1));
                return null;
            }

            Vector3d dir = _corrDv.normalized;
            double axisErr = Vector3d.Angle(v.ReferenceTransform.up, dir);
            double rem = _corrDv.magnitude - _corrUsed;
            // aligned = within 5° and settled (U4 1st run: the 129° slew swung through 5° and kept turning away)
            bool aligned = axisErr <= CorrectAlignDeg && v.angularVelocity.magnitude <= CorrectAlignRate;
            double throttle = !aligned ? 0 : Clamp(rem / (maxAccel * CorrectTaperS), CorrectMinThrottle, 1);
            // Δv applied along the correction direction since the last tick (thrust kN / mass t = m/s²)
            double thrust = t.Num["thrust"];
            if (v.totalMass > 0 && !double.IsNaN(thrust))
            {
                _corrUsed += thrust / v.totalMass * Math.Max(0, Math.Cos(axisErr * Deg)) * TimeWarp.fixedDeltaTime;
            }
            if (ut - _diagUt >= DiagS)
            {
                _diagUt = ut;
                Log("correction: err=" + F(err, 1) + " growing=" + _corrGrowing + " used=" + F(_corrUsed, 2) + "/" + F(_corrDv.magnitude, 2) +
                    " axis_err=" + F(axisErr, 1) + " ang_vel=" + F(v.angularVelocity.magnitude, 3) + " thr=" + F(throttle, 3) + " stop_dist=" + F(stopDist, 0) + " h=" + F(h, 0) +
                    " elapsed=" + F(ut - _corrStartUt, 1));
            }
            return new ModeCommand(AttitudeTarget.Vector(dir), throttle);
        }

        private Vector3d Want(Vessel v, Vector3d up, double h, double hV, double vv, double g)
        {
            Vector3d d = Vector3d.Exclude(up, _track.TargetWorld(v) - v.CoMD);
            double dist = d.magnitude;
            if (dist < 1)
            {
                return Vector3d.zero;
            }
            double tGoV = Math.Max(h - hV, 1) / Math.Max(vv, _finalSpeed);
            double aHLim = 0.5 * g * Math.Tan(GuidedTiltCap * Deg);
            double speed = Math.Min(Math.Min(dist / tGoV, Math.Sqrt(2 * aHLim * dist)), dist / (4 * Tau));
            if (v.mainBody.atmosphere && v.altitude < v.mainBody.atmosphereDepth)
            {
                speed = Math.Min(speed, Math.Max(vv, 0) * Math.Tan(AoaLimitDeg * Deg));   // keep the velocity within 15° of vertical
            }
            return d / dist * speed;
        }

        /// <summary>Horizontal unit vector toward the target (zero when right above it).</summary>
        private Vector3d TargetDir(Vessel v, Vector3d up)
        {
            Vector3d d = Vector3d.Exclude(up, _track.TargetWorld(v) - v.CoMD);
            return d.magnitude < 1e-3 ? Vector3d.zero : d.normalized;
        }

        private static Vector3d ClampMag(Vector3d a, double max) => a.magnitude > max ? a.normalized * max : a;

        /// <summary>Tilt-space vector of a horizontal accel: direction × atan(|a| / aV) in degrees.</summary>
        private static Vector3d TiltVec(Vector3d a, double aV) =>
            a.magnitude < 1e-9 ? Vector3d.zero : a.normalized * (Math.Atan2(a.magnitude, aV) / Deg);

        /// <summary>Moves the commanded horizontal accel so the commanded tilt and its direction change by at most maxDeg
        /// (measured: 10°/s average turn rate below 600 m); through zero when the direction reverses.</summary>
        private static Vector3d RateLimitTilt(Vector3d prev, Vector3d next, double aV, double maxDeg)
        {
            Vector3d p = TiltVec(prev, aV);
            Vector3d n = TiltVec(next, aV);
            Vector3d step = n - p;
            Vector3d tv = step.magnitude <= maxDeg ? n : p + step.normalized * maxDeg;
            double deg = Math.Min(tv.magnitude, 89);
            return deg < 1e-6 ? Vector3d.zero : tv.normalized * (aV * Math.Tan(deg * Deg));
        }

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] land " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, CultureInfo.InvariantCulture);

        public Dictionary<string, object> Save()
        {
            Dictionary<string, object> state = new Dictionary<string, object>
            {
                ["burning"] = _burning,
                ["aligning"] = _aligning,
                ["gear_done"] = _gearDone,
                ["infeasible"] = new List<object>(_infeasible),
                ["touched"] = _touched,
                ["touch_ut"] = _touchUt,
                ["still_since"] = double.IsNaN(_stillSince) ? null : (object)_stillSince,
                ["gear_settle_until"] = _gearSettleUntil > 0 ? (object)_gearSettleUntil : null,
            };
            _track?.Save(state);   // #22: fixed target and drag model
            if (_track != null)
            {
                state["in_loop"] = _inLoop;
                state["a_cmd_x"] = _aCmdPrev.x;
                state["a_cmd_y"] = _aCmdPrev.y;
                state["a_cmd_z"] = _aCmdPrev.z;
                state["a_cmd_ut"] = _aCmdUt;
                state["dv_short_since"] = double.IsNaN(_dvShortSince) ? null : (object)_dvShortSince;
                state["att_lost_since"] = double.IsNaN(_attLostSince) ? null : (object)_attLostSince;
                state["correction_decided"] = _corrDecided;
                state["correcting"] = _correcting;
                state["corr_dv_x"] = _corrDv.x;
                state["corr_dv_y"] = _corrDv.y;
                state["corr_dv_z"] = _corrDv.z;
                state["corr_used"] = _corrUsed;
                state["corr_start_ut"] = _corrStartUt;
                state["corr_prev_err"] = double.IsNaN(_corrPrevErr) ? null : (object)_corrPrevErr;
                state["corr_growing"] = (double)_corrGrowing;
                state["corr_used_at_pred"] = _corrUsedAtPred;
            }
            return state;
        }

        /// <summary>Restores Save's keys. The bottom offset is re-measured on the first Height (_partCount = -1), and
        /// _measuredUt = 0 lets the 0.5 s re-measure continue while gear_settle_until has not passed.</summary>
        public void Load(Vessel v, Dictionary<string, object> state)
        {
            _burning = Handover.B(state, "burning");
            _aligning = Handover.B(state, "aligning");
            _gearDone = Handover.B(state, "gear_done");
            _touched = Handover.B(state, "touched");
            _touchUt = Handover.D(state, "touch_ut", 0);
            _stillSince = Handover.D(state, "still_since", double.NaN);
            _gearSettleUntil = Handover.D(state, "gear_settle_until", 0);
            _infeasible.Clear();
            if (state.TryGetValue("infeasible", out object list) && list is List<object> reasons)
            {
                foreach (object reason in reasons)
                {
                    if (reason is string s)
                    {
                        _infeasible.Add(s);
                    }
                }
            }
            _partCount = -1;
            _measuredUt = 0;
            _track?.Load(state);
            if (_track != null)
            {
                _inLoop = Handover.B(state, "in_loop");
                _aCmdPrev = new Vector3d(Handover.D(state, "a_cmd_x", 0), Handover.D(state, "a_cmd_y", 0), Handover.D(state, "a_cmd_z", 0));
                _aCmdUt = Handover.D(state, "a_cmd_ut", 0);
                _dvShortSince = Handover.D(state, "dv_short_since", double.NaN);
                _attLostSince = Handover.D(state, "att_lost_since", double.NaN);
                _corrDecided = Handover.B(state, "correction_decided");
                _correcting = Handover.B(state, "correcting");
                _corrDv = new Vector3d(Handover.D(state, "corr_dv_x", 0), Handover.D(state, "corr_dv_y", 0), Handover.D(state, "corr_dv_z", 0));
                _corrUsed = Handover.D(state, "corr_used", 0);
                _corrStartUt = Handover.D(state, "corr_start_ut", 0);   // the 60 s limit keeps counting across the reload
                _corrPrevErr = Handover.D(state, "corr_prev_err", double.NaN);
                _corrGrowing = (int)Handover.D(state, "corr_growing", 0);
                _corrUsedAtPred = Handover.D(state, "corr_used_at_pred", 0);
                _corrPredUt = double.NaN;   // the tracker predicts again on the first tick after the load
            }
        }

        public void End(Vessel v)
        {
        }
    }
}
