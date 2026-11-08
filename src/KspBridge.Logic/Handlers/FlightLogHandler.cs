using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KspBridge.Flight;

namespace KspBridge.Handlers
{
    /// <summary>GET /api/flights and /api/flights/{id}/log — flight recorder files (docs 0025).</summary>
    public static class FlightLogHandler
    {
        private const int MaxSamples = 400;
        private static readonly Regex IdPattern = new Regex("^[0-9]+-[0-9]+$");

        public static object List(NameValueCollection q)
        {
            FlightRecorder.Flush();
            string dir = FlightRecorder.Directory;
            string filter = q["vessel"];
            int limit = ParseInt(q["limit"], 20);
            List<object> flights = new List<object>();
            if (Directory.Exists(dir))
            {
                foreach (string path in Directory.GetFiles(dir, "*.jsonl")
                    .Select(p => new { p, ut = FlightFiles.StartUt(Path.GetFileNameWithoutExtension(p)) })
                    .Where(x => x.ut.HasValue)
                    .OrderByDescending(x => x.ut.Value)
                    .Select(x => x.p))
                {
                    string id = Path.GetFileNameWithoutExtension(path);
                    Dictionary<string, object> start = FlightFiles.ReadFirst(path);
                    string vessel = start != null && start.TryGetValue("vessel", out object n) ? n as string : null;
                    if (!string.IsNullOrEmpty(filter) && (vessel == null || vessel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }
                    Dictionary<string, object> end = FlightFiles.ReadLastEnd(path);
                    bool recording = id == FlightRecorder.CurrentId;
                    flights.Add(new Dictionary<string, object>
                    {
                        ["id"] = id,
                        ["vessel"] = vessel,
                        ["start_ut"] = Get(start, "ut"),
                        ["start_reason"] = Get(start, "reason"),
                        ["crew"] = Get(start, "crew"),
                        ["recording"] = recording,
                        ["reason"] = recording ? "recording" : end != null ? Get(end, "reason") : "incomplete",
                        ["end_met"] = Get(end, "met"),
                        ["max_alt"] = Get(end, "max_alt"),
                        ["max_g"] = Get(end, "max_g"),
                        ["parts_lost"] = Get(end, "parts_lost"),
                        ["crew_lost"] = Get(end, "crew_lost"),
                        ["touchdown"] = Get(end, "touchdown"),
                        ["checks"] = Get(end, "checks"),
                        ["size_kb"] = Math.Round(new FileInfo(path).Length / 1024.0, 1),
                    });
                    if (flights.Count >= limit)
                    {
                        break;
                    }
                }
            }
            return new Dictionary<string, object> { ["dir"] = dir, ["flights"] = flights };
        }

        public static object Log(string id, NameValueCollection q)
        {
            if (!IdPattern.IsMatch(id))
            {
                throw ApiException.BadRequest("bad flight id '" + id + "' (from list_flights)");
            }
            FlightRecorder.Flush();
            string path = Path.Combine(FlightRecorder.Directory, id + ".jsonl");
            if (!File.Exists(path))
            {
                throw ApiException.NotFound("flight '" + id + "' not found");
            }
            double? fromMet = ParseDouble(q["from_met"]);
            double? toMet = ParseDouble(q["to_met"]);
            string around = q["around"];
            double window = ParseDouble(q["window"]) ?? 10;
            double? step = ParseDouble(q["step"]);
            HashSet<string> types = new HashSet<string>((q["types"] ?? "s,e").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));

            List<Dictionary<string, object>> lines = new List<Dictionary<string, object>>();
            int skipped = 0;
            using (StreamReader sr = new StreamReader(FlightFiles.OpenShared(path), Encoding.UTF8))
            {
                string l;
                while ((l = sr.ReadLine()) != null)
                {
                    Dictionary<string, object> d = FlightFiles.TryParse(l);
                    if (d != null)
                    {
                        lines.Add(d);
                    }
                    else if (l.Length > 0)
                    {
                        skipped++;
                    }
                }
            }

            List<double[]> windows = null;
            if (!string.IsNullOrEmpty(around))
            {
                windows = lines.Where(d => Type(d) == "e" && (Get(d, "type") as string) == around && Get(d, "met") is double)
                    .Select(d => new[] { (double)d["met"] - window, (double)d["met"] + window }).ToList();
            }
            bool InRange(Dictionary<string, object> d)
            {
                if (!(Get(d, "met") is double met))
                {
                    return true;
                }
                if (fromMet.HasValue && met < fromMet.Value || toMet.HasValue && met > toMet.Value)
                {
                    return false;
                }
                return windows == null || windows.Any(w => met >= w[0] && met <= w[1]);
            }

            List<Dictionary<string, object>> Select(double? sampleStep)
            {
                List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
                double lastKept = double.NegativeInfinity;
                foreach (Dictionary<string, object> d in lines)
                {
                    string t = Type(d);
                    if (t == "start" || t == "end")
                    {
                        result.Add(d);
                        continue;
                    }
                    if (!types.Contains(t) || !InRange(d))
                    {
                        continue;
                    }
                    if (t == "s" && sampleStep.HasValue && Get(d, "met") is double met)
                    {
                        // A gap larger than the step (new window) keeps the first sample of the next stretch too.
                        if (met - lastKept < sampleStep.Value - 1e-6 && met >= lastKept)
                        {
                            continue;
                        }
                        lastKept = met;
                    }
                    result.Add(d);
                }
                return result;
            }

            List<Dictionary<string, object>> selected = Select(step);
            double? auto = null;
            double s2 = step.HasValue ? Math.Max(step.Value, 0.25) * 2 : 0.5;
            while (selected.Count(d => Type(d) == "s") > MaxSamples)
            {
                auto = s2;
                selected = Select(s2);
                s2 *= 2;
            }
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["samples"] = selected.Count(d => Type(d) == "s"),
                ["events"] = selected.Count(d => Type(d) == "e"),
                ["windows"] = windows?.Select(w => (object)new List<object> { w[0], w[1] }).ToList(),
                ["downsampled_step"] = auto,
                ["skipped_lines"] = skipped,
                ["lines"] = selected.Cast<object>().ToList(),
            };
        }

        private static string Type(Dictionary<string, object> d) => Get(d, "t") as string;

        private static object Get(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out object o) ? o : null;

        private static int ParseInt(string s, int def) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i > 0 ? i : def;

        private static double? ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
    }
}
