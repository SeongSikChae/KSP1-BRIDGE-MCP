using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>Parsed get_measurement arguments.</summary>
    internal sealed class MeasureQuery
    {
        public double? FromS;
        public double? ToS;
        public bool Samples;

        /// <summary>Items of the sample list, in the asked order; all items when not given.</summary>
        public List<SampleItem> Items;

        public int MaxSamples = MeasureReport.DefaultMaxSamples;
    }

    /// <summary>
    /// Builds the measure answers from a <see cref="Measurement"/> without the game (spec C7·C8, plan.md "Mod 요청"):
    /// header, range selection, per-item summary, frame interval and the thinned sample list.
    /// </summary>
    internal static class MeasureReport
    {
        public const int DefaultMaxSamples = 200;
        public const int MaxMaxSamples = 2000;

        /// <summary>Reads and checks the get arguments; bad_request for unknown items, a bad max_samples or from_s &gt; to_s.</summary>
        public static MeasureQuery ParseQuery(Dictionary<string, object> args)
        {
            MeasureQuery q = new MeasureQuery
            {
                FromS = args.Num("from_s"),
                ToS = args.Num("to_s"),
                Samples = args.Bool("samples"),
            };
            if (q.FromS.HasValue && q.ToS.HasValue && q.FromS.Value > q.ToS.Value)
                throw BridgeException.BadRequest("'from_s' (" + Text(q.FromS.Value) + ") must not be greater than 'to_s' (" + Text(q.ToS.Value) + ")");

            double? max = args.Num("max_samples");
            if (max.HasValue)
            {
                double v = max.Value;
                if (v != Math.Floor(v) || v < 1 || v > MaxMaxSamples)
                    throw BridgeException.BadRequest("'max_samples' must be an integer from 1 to " + Text(MaxMaxSamples) + " (got " + Text(v) + ")");
                q.MaxSamples = (int)v;
            }

            List<object> items = args.Arr("items");
            if (items == null)
            {
                q.Items = new List<SampleItem>(SampleItems.All);
            }
            else
            {
                q.Items = new List<SampleItem>();
                foreach (object raw in items)
                {
                    SampleItem item = raw is string name ? SampleItems.Find(name) : null;
                    if (item == null)
                        throw BridgeException.BadRequest("unknown item " + Describe(raw) + "; items are " + ItemNames()
                            + " (t_s and ut are always included)");
                    if (!q.Items.Contains(item))
                        q.Items.Add(item);
                }
            }
            return q;
        }

        public static Dictionary<string, object> StartAnswer(Measurement m) => new Dictionary<string, object>
        {
            ["id"] = m.Id,
            ["start_ut"] = m.StartUt,
            ["sample_limit"] = m.SampleLimit,
            ["vessel"] = m.Vessel,
        };

        public static Dictionary<string, object> StopAnswer(Measurement m) => new Dictionary<string, object>
        {
            ["id"] = m.Id,
            ["start_ut"] = m.StartUt,
            ["end_ut"] = m.EndUt,
            ["sample_count"] = m.Samples.Count,
            ["end_reason"] = m.EndReason,
        };

        /// <summary>{"measurement": null} when there is none, otherwise the measurement with its summary (and samples when asked).</summary>
        public static Dictionary<string, object> GetAnswer(Measurement m, MeasureQuery q)
        {
            if (m == null)
                return new Dictionary<string, object> { ["measurement"] = null };

            List<Sample> all = m.Samples;
            int first = 0;
            while (first < all.Count && q.FromS.HasValue && all[first].Ut - m.StartUt < q.FromS.Value)
                first++;
            int end = first;
            while (end < all.Count && (!q.ToS.HasValue || all[end].Ut - m.StartUt <= q.ToS.Value))
                end++;
            int count = end - first;

            Dictionary<string, object> answer = new Dictionary<string, object>
            {
                ["id"] = m.Id,
                ["state"] = m.Running ? "running" : "ended",
                ["vessel"] = m.Vessel,
                ["start_ut"] = m.StartUt,
                ["end_ut"] = m.EndUt,
                ["end_reason"] = m.EndReason,
                ["error"] = m.Error,
                ["sample_count"] = all.Count,
                ["sample_limit"] = m.SampleLimit,
                ["range"] = new Dictionary<string, object>
                {
                    ["from_s"] = q.FromS,
                    ["to_s"] = q.ToS,
                    ["sample_count"] = count,
                },
                ["frame_dt_s"] = FrameInterval(all, first, end),
                ["summary"] = count == 0 ? null : Summary(m, first, end),
            };
            if (q.Samples)
                answer["samples"] = SampleList(m, first, end, q);
            return new Dictionary<string, object> { ["measurement"] = answer };
        }

        /// <summary>Minimum and maximum UT difference of neighbouring samples in the range; null with fewer than two samples.</summary>
        private static Dictionary<string, object> FrameInterval(List<Sample> samples, int first, int end)
        {
            if (end - first < 2)
                return null;
            double min = double.MaxValue, max = double.MinValue;
            for (int i = first + 1; i < end; i++)
            {
                double dt = samples[i].Ut - samples[i - 1].Ut;
                min = Math.Min(min, dt);
                max = Math.Max(max, dt);
            }
            return new Dictionary<string, object> { ["min"] = min, ["max"] = max };
        }

        private static Dictionary<string, object> Summary(Measurement m, int first, int end)
        {
            Dictionary<string, object> summary = new Dictionary<string, object>();
            foreach (SampleItem item in SampleItems.All)
                summary[item.Name] = item.IsDiscrete ? DiscreteSummary(m, item, first, end) : NumberSummary(m, item, first, end);
            return summary;
        }

        /// <summary>First, last, min/max with the earliest elapsed time they occur, and arithmetic mean (angles are not unwrapped).</summary>
        private static Dictionary<string, object> NumberSummary(Measurement m, SampleItem item, int first, int end)
        {
            List<Sample> samples = m.Samples;
            double min = double.MaxValue, max = double.MinValue, sum = 0;
            double minT = 0, maxT = 0;
            for (int i = first; i < end; i++)
            {
                double v = item.Number(samples[i]);
                double t = samples[i].Ut - m.StartUt;
                if (v < min)
                {
                    min = v;
                    minT = t;
                }
                if (v > max)
                {
                    max = v;
                    maxT = t;
                }
                sum += v;
            }
            return new Dictionary<string, object>
            {
                ["first"] = item.Number(samples[first]),
                ["last"] = item.Number(samples[end - 1]),
                ["min"] = min,
                ["min_t_s"] = minT,
                ["max"] = max,
                ["max_t_s"] = maxT,
                ["mean"] = sum / (end - first),
            };
        }

        /// <summary>First, last and every change from the previous sample in the range ({t_s, value}).</summary>
        private static Dictionary<string, object> DiscreteSummary(Measurement m, SampleItem item, int first, int end)
        {
            List<Sample> samples = m.Samples;
            List<object> changes = new List<object>();
            object previous = item.Discrete(samples[first]);
            for (int i = first + 1; i < end; i++)
            {
                object v = item.Discrete(samples[i]);
                if (!Equals(v, previous))
                    changes.Add(new Dictionary<string, object> { ["t_s"] = samples[i].Ut - m.StartUt, ["value"] = v });
                previous = v;
            }
            return new Dictionary<string, object>
            {
                ["first"] = item.Discrete(samples[first]),
                ["last"] = item.Discrete(samples[end - 1]),
                ["changes"] = changes,
            };
        }

        /// <summary>Columns t_s, ut and the chosen items, and every stride-th sample of the range (stride = ceil(N / max_samples)).</summary>
        private static Dictionary<string, object> SampleList(Measurement m, int first, int end, MeasureQuery q)
        {
            int count = end - first;
            int stride = count <= q.MaxSamples ? 1 : (count + q.MaxSamples - 1) / q.MaxSamples;
            List<object> columns = new List<object> { "t_s", "ut" };
            foreach (SampleItem item in q.Items)
                columns.Add(item.Name);

            List<object> rows = new List<object>();
            for (int i = first; i < end; i += stride)
            {
                Sample s = m.Samples[i];
                List<object> row = new List<object>(columns.Count) { s.Ut - m.StartUt, s.Ut };
                foreach (SampleItem item in q.Items)
                    row.Add(item.Value(s));
                rows.Add(row);
            }
            return new Dictionary<string, object>
            {
                ["stride"] = stride,
                ["columns"] = columns,
                ["rows"] = rows,
            };
        }

        private static string ItemNames()
        {
            List<string> names = new List<string>();
            foreach (SampleItem item in SampleItems.All)
                names.Add(item.Name);
            return string.Join(", ", names);
        }

        private static string Describe(object item) => item is string s ? "'" + s + "'" : (item == null ? "null" : item.GetType().Name);

        private static string Text(double v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
