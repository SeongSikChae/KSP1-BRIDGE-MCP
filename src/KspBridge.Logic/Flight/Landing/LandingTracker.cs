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
        private bool _dropped;
        private double _lat = double.NaN;
        private double _lon = double.NaN;
        private double _alt = double.NaN;
        private DragModel _drag;
        private Impact _pred;
        private double _predUt = double.NegativeInfinity;
        private double _predMs = double.NaN;
        private double _frameCheck = double.NaN;

        public LandingTracker(LandingTarget arg)
        {
            _arg = arg;
        }

        /// <summary>A target is fixed and kept (not dropped).</summary>
        public bool Active => _fixed && !_dropped;

        public Impact Prediction => _pred;

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
            if (ut - _predUt >= PredictIntervalS)
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
                ["prediction"] = "ballistic",
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
                    _dropped = true;
                    string why = tv == null ? "missing" : tv.mainBody != v.mainBody ? "other_body" : "situation " + tv.situation;
                    Log("target vessel " + F(_arg.VesselId, 0) + " not usable: " + why);
                    FlightEvents.Add("landing_target_unreachable", Coordinator.StepIndex, new Dictionary<string, object>
                    {
                        ["reason"] = "target_vessel",
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
