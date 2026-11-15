using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// The land step's target and impact prediction (#22, docs 0061 decisions 3, 4, 8, 9; 0062 U2): fixes the target on the
    /// first real tick (a vessel target must be landed on the same body, else landing_target_unreachable and a vertical
    /// landing), keeps the drag model, re-predicts the unpowered impact once a second and describes it for get_flight.landing.
    /// </summary>
    public sealed class LandingTracker
    {
        private const double PredictIntervalS = 1;

        private readonly LandingTarget _arg;
        private bool _fixed;
        private bool _unresolved;   // a vessel target that could not be resolved at fix time: no target at all
        private bool _ended;        // guidance ended (dv / no_margin): the target is kept for reporting only
        private string _guidance = "free_fall";
        private readonly HashSet<string> _reported = new HashSet<string>();
        private double _lat = double.NaN;
        private double _lon = double.NaN;
        private double _alt = double.NaN;
        private DragModel _drag;
        private Impact _pred;
        private double _predUt = double.NegativeInfinity;
        private double _predMs = double.NaN;
        private double _frameCheck = double.NaN;
        private bool _braking;

        public LandingTracker(LandingTarget arg)
        {
            _arg = arg;
        }

        /// <summary>A target is fixed (resolved): get_flight.landing and landed.target_error_m are reported.</summary>
        public bool Active => _fixed && !_unresolved;

        /// <summary>Active and the guidance has not ended.</summary>
        public bool Guiding => Active && !_ended;

        public bool Ended => _ended;

        /// <summary>Ends the horizontal guidance (#22 decision 6); the target stays for reporting.</summary>
        public void EndGuidance() => _ended = true;

        /// <summary>free_fall / pre_loop / guided / vertical / off / dropped (get_flight.landing.guidance).</summary>
        public void SetGuidance(string state) => _guidance = state;

        /// <summary>Every landing_target_unreachable goes through here: once per (reason, phase) per step, not in the trial.</summary>
        public void Unreachable(string reason, string phase, Dictionary<string, object> extra)
        {
            string key = reason + "/" + phase;
            if (FlightEvents.InTrial || _reported.Contains(key))
            {
                return;
            }
            _reported.Add(key);
            Dictionary<string, object> data = extra ?? new Dictionary<string, object>();
            data["reason"] = reason;
            if (phase != null)
            {
                data["phase"] = phase;
            }
            FlightEvents.Add("landing_target_unreachable", Coordinator.StepIndex, data);
        }

        public Impact Prediction => _pred;

        /// <summary>UT of the last unpowered prediction (a new one arrives about once a second).</summary>
        public double PredictionUt => _predUt;

        /// <summary>Great-circle distance (m) from the current prediction to the target; NaN without one.</summary>
        public double PredictionError(CelestialBody body) =>
            _pred == null || !Active ? double.NaN : ImpactPredictor.Distance(body, _pred.Lat, _pred.Lon, _lat, _lon);

        /// <summary>Correction solve (#22 U4) on the current state with the kept target and drag model (call only while Active).</summary>
        public bool SolveCorrection(Vessel v, double ut, out Vector3d dvWorld)
        {
            CelestialBody body = v.mainBody;
            return ImpactCorrection.Solve(body, v.CoMD - body.position, v.obt_velocity, ut, v.totalMass * 1000, _drag,
                                          _lat, _lon, out dvWorld);
        }

        /// <summary>World position of the fixed target on the terrain (call only while Active).</summary>
        public Vector3d TargetWorld(Vessel v) => v.mainBody.GetWorldSurfacePosition(_lat, _lon, _alt);

        /// <summary>Burning (#22 decision 8): the prediction becomes the even-braking stop point (world position), every tick;
        /// the 1 s unpowered integration stops.</summary>
        public void Braking(Vessel v, Vector3d stopWorld)
        {
            if (FlightEvents.InTrial || !Active)
            {
                return;
            }
            _braking = true;
            CelestialBody body = v.mainBody;
            _pred = new Impact
            {
                Lat = body.GetLatitude(stopWorld, false),
                Lon = LandingTarget.Wrap180(body.GetLongitude(stopWorld, false)),
                Ut = Planetarium.GetUniversalTime(),
                Alt = _alt,
            };
        }

        /// <summary>Great-circle distance (m) from the vessel to the kept target; NaN without one.</summary>
        public double ErrorFrom(Vessel v) =>
            Active ? ImpactPredictor.Distance(v.mainBody, v.latitude, LandingTarget.Wrap180(v.longitude), _lat, _lon) : double.NaN;

        /// <summary>reload handover (#22 U3): the fixed target and the drag model; the target argument itself comes back from
        /// the plan re-parse.</summary>
        public void Save(Dictionary<string, object> state)
        {
            state["target_fixed"] = _fixed;
            state["target_unresolved"] = _unresolved;
            state["reported"] = new List<object>(_reported);
            if (Active)
            {
                state["guidance_ended"] = _ended;
                state["guidance"] = _guidance;
                state["t_lat"] = _lat;
                state["t_lon"] = _lon;
                state["t_alt"] = _alt;
                state["drag_k"] = _drag.K;
                state["drag_source"] = _drag.Source;
                state["braking"] = _braking;
            }
        }

        public void Load(Dictionary<string, object> state)
        {
            _fixed = Handover.B(state, "target_fixed");
            _unresolved = Handover.B(state, "target_unresolved");
            _reported.Clear();
            if (state.TryGetValue("reported", out object list) && list is List<object> keys)
            {
                foreach (object k in keys)
                {
                    if (k is string s1)
                    {
                        _reported.Add(s1);
                    }
                }
            }
            if (Active)
            {
                _ended = Handover.B(state, "guidance_ended");
                _guidance = state.TryGetValue("guidance", out object gs) && gs is string g1 ? g1 : _guidance;
                _lat = Handover.D(state, "t_lat", double.NaN);
                _lon = Handover.D(state, "t_lon", double.NaN);
                _alt = Handover.D(state, "t_alt", 0);
                _braking = Handover.B(state, "braking");
                string source = state.TryGetValue("drag_source", out object s) ? s as string : null;
                _drag = DragModel.Restore(Handover.D(state, "drag_k", 0), source);
                if (double.IsNaN(_lat) || double.IsNaN(_lon))
                {
                    _fixed = false;   // incomplete handover: fix the target again on the next tick
                }
            }
        }

        /// <summary>Every land tick. Nothing is decided in the submit trial (events would carry the previous step index).</summary>
        public void Tick(Vessel v, double ut)
        {
            if (FlightEvents.InTrial)
            {
                return;
            }
            if (!_fixed)
            {
                Fix(v);
            }
            if (!Active)
            {
                return;
            }
            _drag.Measure(v, TimeWarp.fixedDeltaTime);
            if (!_braking && ut - _predUt >= PredictIntervalS)
            {
                _predUt = ut;
                Stopwatch sw = Stopwatch.StartNew();
                CelestialBody body = v.mainBody;
                _pred = ImpactPredictor.Predict(body, v.CoMD - body.position, v.obt_velocity, ut, v.totalMass * 1000, _drag);
                _predMs = sw.Elapsed.TotalMilliseconds;
                _frameCheck = ImpactPredictor.FrameCheck(v);
                Log("predict: ms=" + F(_predMs, 2) + " frame_check=" + F(_frameCheck, 3) + " k=" + F(_drag.K, 4) +
                    " k_sample=" + F(_drag.LastSample, 4) + " src=" + _drag.Source +
                    (_pred == null ? " impact=null" : " impact=" + F(_pred.Lat, 5) + "," + F(_pred.Lon, 5) + " t=" + F(_pred.Ut - ut, 1) +
                        " err=" + F(ImpactPredictor.Distance(body, _pred.Lat, _pred.Lon, _lat, _lon), 1)));
            }
        }

        /// <summary>get_flight.landing; null without a kept target.</summary>
        public Dictionary<string, object> Describe(Vessel v)
        {
            if (!Active)
            {
                return null;
            }
            double err = _pred == null ? double.NaN : ImpactPredictor.Distance(v.mainBody, _pred.Lat, _pred.Lon, _lat, _lon);
            return new Dictionary<string, object>
            {
                ["target_latitude"] = N(_lat),
                ["target_longitude"] = N(_lon),
                ["target_terrain_altitude"] = N(_alt),
                ["predicted_latitude"] = _pred == null ? null : N(_pred.Lat),
                ["predicted_longitude"] = _pred == null ? null : N(_pred.Lon),
                ["error_m"] = N(err),
                ["prediction"] = _braking ? "braking" : "ballistic",
                ["guidance"] = _guidance,
                ["drag_k"] = N(_drag.K),
                ["drag_source"] = _drag.Source,
                ["frame_check_mps"] = N(_frameCheck),
                ["predict_ms"] = N(_predMs),
            };
        }

        private void Fix(Vessel v)
        {
            _fixed = true;
            if (_arg.IsVessel)
            {
                Vessel tv = Targeting.FindVessel(_arg.VesselId);
                bool ok = tv != null && tv.mainBody == v.mainBody
                    && (tv.situation == Vessel.Situations.LANDED || tv.situation == Vessel.Situations.SPLASHED);
                if (!ok)
                {
                    _unresolved = true;
                    string why = tv == null ? "missing" : tv.mainBody != v.mainBody ? "other_body" : "situation " + tv.situation;
                    Log("target vessel " + F(_arg.VesselId, 0) + " not usable: " + why);
                    Unreachable("target_vessel", null, new Dictionary<string, object>
                    {
                        ["vessel"] = _arg.VesselId,
                        ["detail"] = why,
                    });
                    return;
                }
                _lat = tv.latitude;
                _lon = LandingTarget.Wrap180(tv.longitude);
            }
            else
            {
                _lat = _arg.Latitude;
                _lon = _arg.Longitude;
            }
            double ground = v.mainBody.TerrainAltitude(_lat, _lon, true);
            _alt = v.mainBody.ocean ? Math.Max(0, ground) : ground;
            _drag = DragModel.Initial(v);
            Log("target fixed: lat=" + F(_lat, 5) + " lon=" + F(_lon, 5) + " terrain=" + F(_alt, 1));
        }

        private static object N(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, 6);

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] land " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, CultureInfo.InvariantCulture);
    }
}
