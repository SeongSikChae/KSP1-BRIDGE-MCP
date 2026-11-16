using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using KspBridge.Flight.Modes;

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

        // #22 decision 10: brake start point inputs (from LandMode) and the window state shown in get_flight.landing
        private double _brakeDecel = double.NaN;
        private double _brakeFinalAlt;
        private double _brakeIn = double.NaN;
        private bool _brakeWindow;

        /// <summary>Net deceleration (margin × max accel − g) and final altitude for the brake start search of the next predictions.</summary>
        public void SetBrake(double decel, double finalAlt)
        {
            _brakeDecel = decel;
            _brakeFinalAlt = finalAlt;
        }

        /// <summary>Seconds to the predicted brake start (NaN: none) and whether the burn window is open.</summary>
        public void SetBrakeState(double brakeIn, bool window)
        {
            _brakeIn = brakeIn;
            _brakeWindow = window;
        }

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
                state["drag_scale"] = _drag.Scale;   // U5b: the mach table is rebuilt from the parts
                state["drag_source"] = _drag.Source;
                state["braking"] = _braking;
            }
        }

        public void Load(Vessel v, Dictionary<string, object> state)
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
                _drag = DragModel.Restore(v, Handover.D(state, "drag_scale", 1), source);   // an older drag_k is ignored
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
                // DIAG (#22 decision 12-2, U6 removes): the plain RK4 prediction, logged only (impact=) next to the applied one
                Impact plain = ImpactPredictor.Predict(body, v.CoMD - body.position, v.obt_velocity, ut, v.totalMass * 1000, _drag,
                    brakeDecel: _brakeDecel, finalAlt: _brakeFinalAlt);
                double plainMs = sw.Elapsed.TotalMilliseconds;
                _frameCheck = ImpactPredictor.FrameCheck(v);
                // decision 12-2 2nd stage: the prediction used for guidance follows KSP's integration (impact_mod= in the log)
                PhysStep phys = Policy();
                Stopwatch swMod = Stopwatch.StartNew();
                Impact mod = ImpactPredictor.Predict(body, v.CoMD - body.position, v.obt_velocity, ut, v.totalMass * 1000, _drag,
                    brakeDecel: _brakeDecel, finalAlt: _brakeFinalAlt, step: phys);
                double modMs = swMod.Elapsed.TotalMilliseconds;
                _pred = mod;
                Log("predict: ms=" + F(plainMs, 2) + " frame_check=" + F(_frameCheck, 3) + " k=" + F(_drag.KNow(v), 4) + " scale=" + F(_drag.Scale, 3) +
                    " k_sample=" + F(_drag.LastSample, 4) + " src=" + _drag.Source +
                    (plain == null ? " impact=null" : " impact=" + F(plain.Lat, 5) + "," + F(plain.Lon, 5) + " t=" + F(plain.Ut - ut, 1) +
                        " err=" + F(ImpactPredictor.Distance(body, plain.Lat, plain.Lon, _lat, _lon), 1)) +
                    (mod == null ? " impact_mod=null" : " impact_mod=" + F(mod.Lat, 5) + "," + F(mod.Lon, 5) +
                        " err_mod=" + F(ImpactPredictor.Distance(body, mod.Lat, mod.Lon, _lat, _lon), 1) +
                        (plain == null ? "" : " d_impact_m=" + F(ImpactPredictor.Distance(body, plain.Lat, plain.Lon, mod.Lat, mod.Lon), 1) +
                            " d_brake_s=" + F(mod.BrakeUt - plain.BrakeUt, 2))) +
                    " ms_mod=" + F(modMs, 2) + " warp_end_in=" + F(phys.WarpEndUt - ut, 1));
                _lastBrakeUt = _pred != null ? _pred.BrakeUt : double.NaN;
                Diagnose(v, ut);
                // get_flight predict_ms: the whole prediction tick, so the 1st-stage diagnostics' cost (the second prediction and
                // the doubled 30 s propagation, review R1-2 #1) shows in the response; the plain prediction alone is the log's ms
                _predMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        // DIAG (#22 U5 reentry investigation): actual non-gravity acceleration over the last prediction interval against
        // the predictor's drag model, and the model air density against the vessel's.
        private Vector3d _diagV;
        private double _diagUt = double.NaN;

        private void Diagnose(Vessel v, double ut)
        {
            CelestialBody body = v.mainBody;
            Vector3d r = v.CoMD - body.position;
            Vector3d vel = v.obt_velocity;
            if (!double.IsNaN(_diagUt) && ut - _diagUt > 0.5 && v.ctrlState.mainThrottle == 0)
            {
                double dt = ut - _diagUt;
                double rm = r.magnitude;
                Vector3d grav = -body.gravParameter / (rm * rm * rm) * r;
                Vector3d aAct = (vel - _diagV) / dt - grav;
                Vector3d air = v.srf_velocity;
                Vector3d u = air.normalized;
                double alt = rm - body.Radius;
                double rhoModel = body.atmosphere && alt < body.atmosphereDepth
                    ? body.GetDensity(body.GetPressure(alt), ImpactPredictor.AtmoTemperature(body, r, alt)) : 0;   // as the predictor
                double mass = v.totalMass * 1000;
                double aModel = mass > 0 ? _drag.KNow(v) * rhoModel * air.sqrMagnitude / mass : 0;
                double dragSum = 0, liftSum = 0;
                foreach (Part p in v.parts)
                {
                    if (p != null)
                    {
                        dragSum += p.dragScalar;
                        liftSum += p.bodyLiftScalar;
                    }
                }
                double along = -Vector3d.Dot(aAct, u);                        // deceleration along the air velocity
                double perp = Vector3d.Exclude(u, aAct).magnitude;
                Log("diag: alt=" + F(alt, 0) + " srf=" + F(air.magnitude, 1) + " mach=" + F(v.mach, 2) + " q=" + F(v.dynamicPressurekPa, 3) +
                    " rho_model=" + F(rhoModel, 6) + " rho_vessel=" + F(v.atmDensity, 6) +
                    " a_act_along=" + F(along, 3) + " a_act_perp=" + F(perp, 3) + " a_model=" + F(aModel, 3) +
                    " a_dragsum=" + F(mass > 0 ? dragSum * 1000 / mass : 0, 3) + " a_bodylift=" + F(mass > 0 ? liftSum * 1000 / mass : 0, 3) +
                    " k=" + F(_drag.KNow(v), 4) + " dt=" + F(dt, 2) + " krak=" + F(Krakensbane.GetFrameVelocity().magnitude, 1) +
                    " inv_rot=" + body.inverseRotation);
            }
            _diagV = vel;
            _diagUt = ut;

            // 30 s forward check, one at a time: the predictor's state against the vessel's at the tick it fires, the prediction
            // interpolated to that tick's time (U5c: the late tick under physics warp no longer counts as an error)
            if (!double.IsNaN(_chkUt) && ut >= _chkUt)
            {
                double late = ut - _chkUt;
                string band = v.altitude >= 70000 ? "above" : v.altitude >= 60000 ? "70-60" : v.altitude >= 50000 ? "60-50"
                    : v.altitude >= 40000 ? "50-40" : "below";
                if (late <= (ChkN - 1) * ChkStep)
                {
                    int i = Math.Min((int)(late / ChkStep), ChkN - 2);
                    double f = (late - i * ChkStep) / ChkStep;
                    double pLat = _chkLat[i] + (_chkLat[i + 1] - _chkLat[i]) * f;
                    double pLon = _chkLon[i] + LandingTarget.Wrap180(_chkLon[i + 1] - _chkLon[i]) * f;
                    double pAlt = _chkAlt[i] + (_chkAlt[i + 1] - _chkAlt[i]) * f;
                    double dLat = (v.latitude - pLat) * Math.PI / 180 * body.Radius;
                    double dLon = LandingTarget.Wrap180(v.longitude - pLon) * Math.PI / 180 * body.Radius * Math.Cos(v.latitude * Math.PI / 180);
                    // DIAG (decision 12-2 1st stage): the KSP-integration propagation from the same start
                    double mLat = _chkLatM[i] + (_chkLatM[i + 1] - _chkLatM[i]) * f;
                    double mLon = _chkLonM[i] + LandingTarget.Wrap180(_chkLonM[i + 1] - _chkLonM[i]) * f;
                    double mAlt = _chkAltM[i] + (_chkAltM[i + 1] - _chkAltM[i]) * f;
                    double dLatM = (v.latitude - mLat) * Math.PI / 180 * body.Radius;
                    double dLonM = LandingTarget.Wrap180(v.longitude - mLon) * Math.PI / 180 * body.Radius * Math.Cos(v.latitude * Math.PI / 180);
                    Log("diag check: late=" + F(late, 2) + " d_north=" + F(dLat, 1) + " d_east=" + F(dLon, 1) +
                        " d_alt=" + F(v.altitude - pAlt, 1) + " d_north_mod=" + F(dLatM, 1) + " d_east_mod=" + F(dLonM, 1) +
                        " d_alt_mod=" + F(v.altitude - mAlt, 1) + " lon=" + F(v.longitude, 4) + " pred_lon=" + F(pLon, 4) +
                        " throttle=" + F(v.ctrlState.mainThrottle, 2) + " h_chk=" + F(_chkH, 3) + " warp_start=" + F(_chkWarp, 1) +
                        " warp=" + F(TimeWarp.CurrentRate, 1) + " band=" + band);
                }
                else
                {
                    Log("diag check: late=" + F(late, 2) + " (over " + F((ChkN - 1) * ChkStep, 1) + " s: no comparison) warp=" +
                        F(TimeWarp.CurrentRate, 1) + " band=" + band);
                }
                _chkUt = double.NaN;
            }
            if (double.IsNaN(_chkUt) && v.ctrlState.mainThrottle == 0)
            {
                ImpactPredictor.Propagate(body, r, vel, ut, v.totalMass * 1000, _drag, ChkS, ChkStep, default, _chkLat, _chkLon, _chkAlt);
                // the step the vessel has now (0 on rails); the policy's h would be wrong where an attitude check or a player
                // cancel holds ×1 (review R1-1 #1), and windows whose warp changes are not judged anyway
                _chkH = v.packed ? 0 : TimeWarp.fixedDeltaTime;
                ImpactPredictor.Propagate(body, r, vel, ut, v.totalMass * 1000, _drag, ChkS, ChkStep,
                    new PhysStep { On = true, Fixed = true, FixedH = _chkH }, _chkLatM, _chkLonM, _chkAltM);
                _chkWarp = TimeWarp.CurrentRate;
                _chkUt = ut + ChkS;
            }
        }

        /// <summary>#22 decision 12-2: the physics step policy for this target land's predictions. T from the previous
        /// prediction's brake start (not saved over a reload: the first prediction after it uses +Infinity).</summary>
        private PhysStep Policy() => new PhysStep
        {
            On = true,
            WarpEndUt = double.IsNaN(_lastBrakeUt) ? double.PositiveInfinity : _lastBrakeUt - LandMode.WarpLeadS,
            DiagBand = true,
        };

        private double _lastBrakeUt = double.NaN;

        private const double ChkS = 30, ChkStep = 0.5;
        private const int ChkN = 5;   // samples at 30, 30.5, ..., 32 s
        private double _chkUt = double.NaN;
        private double _chkWarp, _chkH;
        private readonly double[] _chkLat = new double[ChkN], _chkLon = new double[ChkN], _chkAlt = new double[ChkN];
        private readonly double[] _chkLatM = new double[ChkN], _chkLonM = new double[ChkN], _chkAltM = new double[ChkN];

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
                ["drag_k"] = N(_drag.KNow(v)),
                ["drag_scale"] = N(_drag.Scale),
                ["brake_ut_in"] = N(_brakeIn),
                ["brake_window"] = _brakeWindow,
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
