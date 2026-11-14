using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// Ring buffer of the attitude controller's ticks (design 0053 decision 2): what Steer actually computed and output, recorded
    /// only on ticks it steers. Observation only. Holds one vessel's samples: cleared when a plan starts, on a scene load request,
    /// and when another vessel records. Main thread only.
    /// </summary>
    internal static class ControllerTrace
    {
        internal const int Capacity = 1000;   // ~20 s at 50 Hz
        internal const int Kinds = 5;         // wheel, gimbal, surface, rcs, other
        internal static readonly string[] KindNames = { "wheel", "gimbal", "surface", "rcs", "other" };

        /// <summary>One steered tick. Vectors are per axis (x pitch, y roll, z yaw) in Steer's frame and signs.</summary>
        internal struct Sample
        {
            public double Ut, Throttle, Q;
            public Vector3 Err, Omega, Want, Input, Alpha, Moi;   // rad, rad/s, rad/s, ±1, rad/s², t·m²
            public Vector3[] Torque;   // available torque per kind (kN·m), Kinds long, a new array per sample
        }

        private static readonly Sample[] _buf = new Sample[Capacity];
        private static int _start, _count;
        private static uint _vessel;   // persistentId of the recorded vessel

        internal static int Count => _count;

        internal static void Record(uint vessel, in Sample s)
        {
            if (_count > 0 && vessel != _vessel)
            {
                Clear();   // never mix vessels
            }
            _vessel = vessel;
            _buf[(_start + _count) % Capacity] = s;
            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % Capacity;
            }
        }

        internal static void Clear()
        {
            _start = 0;
            _count = 0;
        }

        // ---- Summary (design 0053 decisions 3 and 4), computed on request ----

        private const double Deg = 180 / Math.PI;
        private const int MaxLag = 10;
        private static readonly string[] AxisNames = { "pitch", "roll", "yaw" };

        /// <summary>{samples: 0} without samples of the active vessel; else the summary of the newest seconds, plus series every
        /// step samples when step is given.</summary>
        internal static Dictionary<string, object> Describe(Vessel active, double seconds, int? step)
        {
            List<Sample> w = _count > 0 && active.persistentId == _vessel ? Window(seconds) : new List<Sample>();
            if (w.Count == 0)
            {
                return new Dictionary<string, object> { ["samples"] = 0.0 };
            }
            double tick = MedianDt(w);
            var axes = new Dictionary<string, object>();
            for (int a = 0; a < 3; a++)
            {
                axes[AxisNames[a]] = Axis(w, a, tick);
            }
            var d = new Dictionary<string, object>
            {
                ["window_s"] = R(w[w.Count - 1].Ut - w[0].Ut, 2),
                ["samples"] = (double)w.Count,
                ["tick_s"] = R(tick, 4),
                ["throttle_mean"] = R(w.Average(s => s.Throttle), 3),
                ["q_mean"] = R(w.Average(s => s.Q), 3),
                ["axes"] = axes,
            };
            if (step is int k)
            {
                d["series"] = Series(w, k);
            }
            return d;
        }

        /// <summary>The samples of the newest seconds, oldest first.</summary>
        private static List<Sample> Window(double seconds)
        {
            var w = new List<Sample>(_count);
            double from = _buf[(_start + _count - 1) % Capacity].Ut - seconds;
            for (int i = 0; i < _count; i++)
            {
                Sample s = _buf[(_start + i) % Capacity];
                if (s.Ut >= from)
                {
                    w.Add(s);
                }
            }
            return w;
        }

        private static double MedianDt(List<Sample> w)
        {
            var dts = new List<double>();
            for (int i = 1; i < w.Count; i++)
            {
                double dt = w[i].Ut - w[i - 1].Ut;
                if (dt > 0)
                {
                    dts.Add(dt);
                }
            }
            return dts.Count == 0 ? double.NaN : Median(dts);
        }

        private static Dictionary<string, object> Axis(List<Sample> w, int a, double tick)
        {
            int n = w.Count;
            // contiguous[i]: the pair (i-1, i) is one physics step apart (gaps over 1.5 ticks are left out of differences)
            var contiguous = new bool[n];
            for (int i = 1; i < n; i++)
            {
                double dt = w[i].Ut - w[i - 1].Ut;
                contiguous[i] = dt > 0 && !(dt > 1.5 * tick);
            }

            double sumSq = 0, errMax = 0, sat = 0, alphaSum = 0;
            foreach (Sample s in w)
            {
                double e = s.Err[a] * Deg;
                sumSq += e * e;
                errMax = Math.Max(errMax, Math.Abs(e));
                if (Math.Abs(s.Input[a]) >= 0.99)
                {
                    sat++;
                }
                alphaSum += s.Alpha[a];
            }
            double alphaModel = alphaSum / n;

            var d = new Dictionary<string, object>
            {
                ["err_rms_deg"] = R(Math.Sqrt(sumSq / n), 3),
                ["err_max_deg"] = R(errMax, 3),
                ["input_sat_ratio"] = R(sat / n, 3),
                ["alpha_model"] = R(alphaModel, 4),
            };
            Oscillation(w, a, contiguous, d, out double offsetDeg, out double ampDeg);
            Fit(w, a, contiguous, d);

            // want_slope_at_amp: slope of the target-rate curve at the oscillation's operating point
            object slope = null;
            if (!double.IsNaN(ampDeg))
            {
                double e = Math.Max(Math.Abs(offsetDeg), ampDeg) / Deg;
                if (e > 0 && alphaModel > 0)
                {
                    slope = Math.Sqrt(2 * alphaModel * e) * AttitudeController.KStop >= AttitudeController.OmegaMax
                        ? 0.0
                        : R(AttitudeController.KStop * Math.Sqrt(alphaModel / (2 * e)), 3);
                }
            }
            d["want_slope_at_amp"] = slope;

            var torque = new Dictionary<string, object>();
            for (int k = 0; k < Kinds; k++)
            {
                double sum = 0;
                foreach (Sample s in w)
                {
                    sum += s.Torque[k][a];
                }
                torque[KindNames[k]] = R(sum / n, 3);
            }
            d["torque"] = torque;
            return d;
        }

        /// <summary>osc_hz, osc_amp_deg, osc_peak_ratio, err_offset_deg: spectrum of the detrended error over the longest
        /// contiguous run without rate-limited slews (0.2..5 Hz, Hann window). Null when that run is under 5 s.</summary>
        private static void Oscillation(List<Sample> w, int a, bool[] contiguous, Dictionary<string, object> d,
            out double offsetDeg, out double ampDeg)
        {
            offsetDeg = double.NaN;
            ampDeg = double.NaN;
            int bestFrom = -1, bestTo = -1, from = -1;
            double bestSpan = 0;
            for (int i = 0; i <= w.Count; i++)
            {
                bool usable = i < w.Count && Math.Abs(w[i].Want[a]) < AttitudeController.OmegaMax;
                bool joins = usable && from >= 0 && contiguous[i];
                if (usable && !joins)
                {
                    Close(from, i - 1);
                    from = i;
                }
                else if (!usable)
                {
                    Close(from, i - 1);
                    from = -1;
                }
            }
            void Close(int f, int t)
            {
                if (f >= 0 && t > f && w[t].Ut - w[f].Ut > bestSpan)
                {
                    bestSpan = w[t].Ut - w[f].Ut;
                    bestFrom = f;
                    bestTo = t;
                }
            }

            d["osc_hz"] = null;
            d["osc_amp_deg"] = null;
            d["osc_peak_ratio"] = null;
            d["err_offset_deg"] = null;
            if (bestFrom < 0 || bestSpan < 5)
            {
                return;
            }

            int m = bestTo - bestFrom + 1;
            double t0 = w[bestFrom].Ut;
            var t = new double[m];
            var e = new double[m];
            double st = 0, se = 0;
            for (int i = 0; i < m; i++)
            {
                t[i] = w[bestFrom + i].Ut - t0;
                e[i] = w[bestFrom + i].Err[a] * Deg;
                st += t[i];
                se += e[i];
            }
            double tm = st / m, em = se / m, stt = 0, ste = 0;
            for (int i = 0; i < m; i++)
            {
                stt += (t[i] - tm) * (t[i] - tm);
                ste += (t[i] - tm) * (e[i] - em);
            }
            double slope = stt > 0 ? ste / stt : 0;
            var r = new double[m];
            var wt = new double[m];
            double wsum = 0;
            for (int i = 0; i < m; i++)
            {
                r[i] = e[i] - (em + slope * (t[i] - tm));
                wt[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t[i] / bestSpan);   // 0 at both ends of the run
                wsum += wt[i];
            }

            var mags = new List<double>();
            double peak = -1, peakF = double.NaN;
            for (int fi = 0; fi <= 96; fi++)
            {
                double f = 0.2 + 0.05 * fi;
                double re = 0, im = 0;
                for (int i = 0; i < m; i++)
                {
                    double ph = 2 * Math.PI * f * t[i];
                    re += wt[i] * r[i] * Math.Cos(ph);
                    im -= wt[i] * r[i] * Math.Sin(ph);
                }
                double mag = Math.Sqrt(re * re + im * im);
                mags.Add(mag);
                if (mag > peak)
                {
                    peak = mag;
                    peakF = f;
                }
            }
            double median = Median(mags);
            offsetDeg = em;
            d["err_offset_deg"] = R(offsetDeg, 3);
            if (!(peak > 0))
            {
                ampDeg = 0;   // a flat error (e.g. roll without a roll target): no frequency to report
                d["osc_amp_deg"] = 0.0;
                return;
            }
            ampDeg = wsum > 0 ? 2 * peak / wsum : double.NaN;
            d["osc_hz"] = R(peakF, 2);
            d["osc_amp_deg"] = R(ampDeg, 3);
            d["osc_peak_ratio"] = median > 0 ? R(peak / median, 2) : null;
        }

        /// <summary>alpha_gain, accel_bias, alpha_r2, lag_ticks, lag_r2: angular acceleration regressed with an intercept on the
        /// predicted acceleration -input·alpha of k ticks earlier, k = 1..10 on one pair set; the best r² picks k.</summary>
        private static void Fit(List<Sample> w, int a, bool[] contiguous, Dictionary<string, object> d)
        {
            d["alpha_gain"] = null;
            d["accel_bias"] = null;
            d["alpha_r2"] = null;
            d["lag_ticks"] = null;
            d["lag_r2"] = null;

            // one pair set for every k: i with samples i-MaxLag..i all contiguous
            var idx = new List<int>();
            int run = 0;
            for (int i = 1; i < w.Count; i++)
            {
                run = contiguous[i] ? run + 1 : 0;
                if (run >= MaxLag)
                {
                    idx.Add(i);
                }
            }
            if (idx.Count < 10)
            {
                return;
            }
            // one-sided saturation: x varies only with alpha, mixed with a q-driven disturbance
            int sat = 0;
            bool pos = false, neg = false;
            foreach (int i in idx)
            {
                float u = w[i - 1].Input[a];
                if (Math.Abs(u) >= 0.99)
                {
                    sat++;
                }
                pos |= u > 0;
                neg |= u < 0;
            }
            if (sat >= 0.9 * idx.Count && !(pos && neg))
            {
                return;
            }

            var y = new double[idx.Count];
            for (int j = 0; j < idx.Count; j++)
            {
                int i = idx[j];
                y[j] = (w[i].Omega[a] - w[i - 1].Omega[a]) / (w[i].Ut - w[i - 1].Ut);
            }
            double ym = y.Average(), syy = 0;
            foreach (double v in y)
            {
                syy += (v - ym) * (v - ym);
            }
            if (!(syy > 0))
            {
                return;
            }

            var r2s = new List<object>();
            double bestR2 = double.NegativeInfinity, bestG = 0, bestC = 0;
            int bestK = 0;
            for (int k = 1; k <= MaxLag; k++)
            {
                var x = new double[idx.Count];
                for (int j = 0; j < idx.Count; j++)
                {
                    int i = idx[j] - k;
                    x[j] = -w[i].Input[a] * w[i].Alpha[a];
                }
                double xm = x.Average(), sxx = 0, sxy = 0, absMean = 0;
                for (int j = 0; j < x.Length; j++)
                {
                    sxx += (x[j] - xm) * (x[j] - xm);
                    sxy += (x[j] - xm) * (y[j] - ym);
                    absMean += Math.Abs(x[j]);
                }
                absMean /= x.Length;
                if (!(Math.Sqrt(sxx / x.Length) >= 0.01 * absMean) || !(sxx > 0))
                {
                    return;   // the predicted acceleration hardly varies: no fit for any k
                }
                double g = sxy / sxx;
                double r2 = sxy * sxy / (sxx * syy);   // = 1 - SSres / SStot for least squares with an intercept
                r2s.Add(R(r2, 3));
                if (r2 > bestR2)
                {
                    bestR2 = r2;
                    bestG = g;
                    bestC = ym - g * xm;
                    bestK = k;
                }
            }
            d["alpha_gain"] = R(bestG, 3);
            d["accel_bias"] = R(bestC, 5);
            d["alpha_r2"] = R(bestR2, 3);
            d["lag_ticks"] = (double)(bestK - 1);   // Δomega[i] comes from input[i-1]: 0 = no delay
            d["lag_r2"] = r2s;
        }

        private static List<object> Series(List<Sample> w, int step)
        {
            var list = new List<object>();
            double t0 = w[0].Ut;
            for (int i = 0; i < w.Count; i += step)
            {
                Sample s = w[i];
                var e = new Dictionary<string, object>
                {
                    ["t"] = R(s.Ut - t0, 3),
                    ["throttle"] = R(s.Throttle, 3),
                    ["q"] = R(s.Q, 3),
                };
                for (int a = 0; a < 3; a++)
                {
                    e[AxisNames[a]] = new Dictionary<string, object>
                    {
                        ["err_deg"] = R(s.Err[a] * Deg, 3),
                        ["omega"] = R(s.Omega[a], 4),
                        ["want"] = R(s.Want[a], 4),
                        ["input"] = R(s.Input[a], 3),
                    };
                }
                list.Add(e);
            }
            return list;
        }

        private static double Median(List<double> v)
        {
            v.Sort();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }

        private static object R(double x, int digits) =>
            double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, digits);
    }
}
