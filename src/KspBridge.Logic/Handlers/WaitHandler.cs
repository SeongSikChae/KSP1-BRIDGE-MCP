using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using KspBridge.Flight;

namespace KspBridge.Handlers
{
    /// <summary>POST /api/wait — runs on the HTTP thread, probing the main thread every 100 ms.</summary>
    public static class WaitHandler
    {
        public const double MaxTimeout = 120;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        private sealed class Probe
        {
            public bool Met;
            public string Abort;
            public object Value;
            public Dictionary<string, object> Vessel;
            public Dictionary<string, object> Anomaly;
        }

        private static readonly string[] AnomalyTypes = { "part_lost", "control_lost", "flameout" };
        private static readonly string[] DefaultAbortOn = { "part_lost", "control_lost" };

        /// <summary>State at the start of the wait that anomalies are compared against.</summary>
        private sealed class Baseline
        {
            public Guid VesselId;
            public int PartsDied;
            public List<ModuleEngines> Burning;
        }

        public static object Wait(RequestBody body, MainThreadDispatcher dispatcher)
        {
            double timeout = body.GetDouble("timeout");
            if (timeout <= 0 || timeout > MaxTimeout)
            {
                throw ApiException.BadRequest("timeout must be in (0, " + MaxTimeout + "] seconds");
            }

            Condition condition;
            if (body.Has("condition"))
            {
                if (body.Has("field"))
                {
                    throw ApiException.BadRequest("give either condition or field/op/value, not both");
                }
                condition = Condition.Parse(body.GetObject("condition"));
            }
            else
            {
                condition = Condition.Parse(new Dictionary<string, object>
                {
                    ["field"] = body.GetString("field"),
                    ["op"] = body.GetRaw("op"),
                    ["value"] = body.GetRaw("value"),
                });
            }
            string field = condition.SingleField;

            HashSet<string> abortOn = new HashSet<string>(DefaultAbortOn);
            if (body.Has("abort_on"))
            {
                abortOn.Clear();
                foreach (object o in body.GetList("abort_on"))
                {
                    string s = o as string;
                    if (s == null || Array.IndexOf(AnomalyTypes, s) < 0)
                    {
                        throw ApiException.BadRequest("abort_on items must be: " + string.Join(", ", AnomalyTypes));
                    }
                    abortOn.Add(s);
                }
            }

            Baseline start = (Baseline)dispatcher.Invoke(() =>
            {
                Vessel av = Guard.ActiveVessel();
                return new Baseline { VesselId = av.id, PartsDied = AnomalyMonitor.PartsDied(av.id), Burning = BurningEngines(av) };
            }, ProbeTimeout);
            Guid vesselId = start.VesselId;

            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                Probe p;
                try
                {
                    p = (Probe)dispatcher.Invoke(() => Check(vesselId, condition, start, abortOn), ProbeTimeout);
                }
                catch (ApiException e) when (e.Status == 504)
                {
                    // main thread stalled for seconds: a scene load (revert, recovery, quickload) is in progress
                    p = new Probe { Abort = "game busy (scene change in progress)" };
                }
                bool timedOut = sw.Elapsed.TotalSeconds >= timeout;
                if (p.Met || p.Abort != null || p.Anomaly != null || timedOut)
                {
                    Dictionary<string, object> r = new Dictionary<string, object>
                    {
                        ["met"] = p.Met,
                        ["elapsed"] = Math.Round(sw.Elapsed.TotalSeconds, 2),
                        ["field"] = field,
                        ["value"] = p.Value,
                        ["vessel"] = p.Vessel,
                    };
                    if (p.Abort != null)
                    {
                        r["reason"] = p.Abort;
                    }
                    else if (p.Anomaly != null)
                    {
                        r["reason"] = "anomaly";
                        r["anomaly"] = p.Anomaly;
                    }
                    else if (!p.Met)
                    {
                        r["reason"] = "timeout";
                    }
                    return r;
                }
                Thread.Sleep(100);
            }
        }

        /// <summary>Engines ignited and not flamed out at the start of the wait; a later flameout of any of them is the anomaly.</summary>
        private static List<ModuleEngines> BurningEngines(Vessel v) =>
            v.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Where(e => e.getIgnitionState && !e.flameout).ToList();

        private static Dictionary<string, object> DetectAnomaly(Vessel v, Baseline start, HashSet<string> abortOn)
        {
            int died = AnomalyMonitor.PartsDied(v.id) - start.PartsDied;
            if (abortOn.Contains("part_lost") && died > 0)
            {
                return new Dictionary<string, object> { ["type"] = "part_lost", ["detail"] = died + " part(s) destroyed" };
            }
            if (abortOn.Contains("control_lost") && AnomalyMonitor.ControlLost)
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "control_lost",
                    ["detail"] = AnomalyMonitor.ControlLostDetail(),
                    ["attitude_error_deg"] = Attitude.AttitudeController.ErrorDeg(v),
                };
            }
            ModuleEngines flamedOut = abortOn.Contains("flameout") ? start.Burning.FirstOrDefault(e => e != null && e.flameout) : null;
            if (flamedOut != null)
            {
                return new Dictionary<string, object> { ["type"] = "flameout", ["detail"] = (flamedOut.part != null ? flamedOut.part.partInfo.title : "an engine") + " flamed out" };
            }
            return null;
        }

        private static object Check(Guid vesselId, Condition condition, Baseline start, HashSet<string> abortOn)
        {
            Probe p = new Probe();
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                p.Abort = "left the flight scene (" + HighLogic.LoadedScene + ")";
                return p;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.id != vesselId)
            {
                p.Abort = "active vessel changed or destroyed";
                return p;
            }

            Dictionary<string, object> values = new Dictionary<string, object>();
            p.Met = condition.Evaluate(v, values);
            p.Value = condition.Report(values);
            if (!p.Met)
            {
                p.Anomaly = DetectAnomaly(v, start, abortOn);
                if (p.Anomaly != null)
                {
                    FlightRecorder.AnomalyEvent(v, p.Anomaly);
                }
            }
            p.Vessel = new Dictionary<string, object>
            {
                ["situation"] = v.situation.ToString(),
                ["altitude_asl"] = v.altitude,
                ["apoapsis"] = v.orbit.ApA,
                ["periapsis"] = v.orbit.PeA,
                ["time_to_ap"] = v.orbit.timeToAp,
                ["speed_vertical"] = v.verticalSpeed,
                ["speed_surface"] = v.srfSpeed,
                ["stage"] = v.currentStage,
            };
            return p;
        }
    }
}
