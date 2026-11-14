using KspBridge.Flight.Control;
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
        public static readonly string[] Args = { "margin", "final_altitude", "final_speed", "gear_altitude" };

        private readonly double _margin;
        private readonly double _finalAlt;
        private readonly double _finalSpeed;
        private readonly double _gearAlt;   // NaN = gear off

        private LandMode(double margin, double finalAlt, double finalSpeed, double gearAlt)
        {
            _margin = margin;
            _finalAlt = finalAlt;
            _finalSpeed = finalSpeed;
            _gearAlt = gearAlt;
        }

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
            return () => new LandMode(margin, finalAlt, finalSpeed, gearAlt);
        }

        /// <summary>Re-measure interval (s) while the legs extend after the Gear group turns on.</summary>
        private const double RemeasureS = 0.5;

        /// <summary>How long (s) after turning on the Gear group the bottom keeps being re-measured.</summary>
        private const double GearSettleS = 5;

        /// <summary>Max tilt from vertical (deg) per m/s of horizontal speed (decision 10; no dead band, so a slow drift is
        /// still cancelled before touchdown).</summary>
        private const double TiltPerMs = 5;

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
                    FlightEvents.Add("landed", Coordinator.StepIndex, new Dictionary<string, object>
                    {
                        ["tilt_deg"] = R(tilt),
                        ["tipped"] = tilt > TippedDeg,
                        ["time_since_touchdown_s"] = R(ut - _touchUt),
                    });
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

            // attitude: up, tilted toward surface retrograde by at most max_tilt(vh)
            double maxTilt = Clamp(vh * TiltPerMs, 0, 90);
            AttitudeTarget att = AttitudeTarget.RotateTowards(up, -v.srf_velocity, maxTilt);

            double maxAccel = EngineModel.MaxAccel(v);
            if (maxAccel <= 0)
            {
                return new ModeCommand(att, 1);   // no engine thrust (not tilt): the Coordinator waits or aborts no_thrust
            }
            double aMaxV = maxAccel * cos;
            double aReq = vv > 0 && h > _finalAlt
                ? (vv * vv - _finalSpeed * _finalSpeed) / (2 * (h - _finalAlt)) + g
                : double.NaN;

            if (!_burning)
            {
                Judge(v, t, h, vv, aReq, maxAccel, "best", g);   // before the burn too, against the best attitude
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
            if (Judge(v, t, h, vv, aReq, aCap, misaligned ? "best" : "actual", g) && !misaligned && cos > 0)
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
                    " a_req=" + F(aReq, 2) + " a_max_v=" + F(aMaxV, 2) + " thrust=" + F(t.Num["thrust"], 1) + " sit=" + v.situation);
            }
            return new ModeCommand(att, throttle);
        }

        /// <summary>Infeasibility on this tick (no latch). aCap: MaxAccel (best attitude) before the burn and while _aligning
        /// after a forced start, otherwise the actual vertical capacity aMaxV; basis says which. Reports once per step and
        /// reason.</summary>
        private bool Judge(Vessel v, Telemetry t, double h, double vv, double aReq, double aCap, string basis, double g)
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
            if (!double.IsNaN(dvLeft) && aCap > g)
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

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] land " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, CultureInfo.InvariantCulture);

        public Dictionary<string, object> Save() => new Dictionary<string, object>
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
        }
    }
}
