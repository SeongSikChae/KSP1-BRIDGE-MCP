using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        }

        public static object Wait(RequestBody body, MainThreadDispatcher dispatcher)
        {
            string field = body.GetString("field");
            string op = body.GetString("op");
            double timeout = body.GetDouble("timeout");
            if (timeout <= 0 || timeout > MaxTimeout)
            {
                throw ApiException.BadRequest("timeout must be in (0, " + MaxTimeout + "] seconds");
            }

            Func<object, bool> test = FlightFields.BuildTest(field, op, body);
            Guid vesselId = (Guid)dispatcher.Invoke(() => Guard.ActiveVessel().id, ProbeTimeout);

            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                Probe p;
                try
                {
                    p = (Probe)dispatcher.Invoke(() => Check(vesselId, field, test), ProbeTimeout);
                }
                catch (ApiException e) when (e.Status == 504)
                {
                    // main thread stalled for seconds: a scene load (revert, recovery, quickload) is in progress
                    p = new Probe { Abort = "game busy (scene change in progress)" };
                }
                bool timedOut = sw.Elapsed.TotalSeconds >= timeout;
                if (p.Met || p.Abort != null || timedOut)
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
                    else if (!p.Met)
                    {
                        r["reason"] = "timeout";
                    }
                    return r;
                }
                Thread.Sleep(100);
            }
        }

        private static object Check(Guid vesselId, string field, Func<object, bool> test)
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

            p.Value = FlightFields.Read(v, field);
            p.Met = test(p.Value);
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
