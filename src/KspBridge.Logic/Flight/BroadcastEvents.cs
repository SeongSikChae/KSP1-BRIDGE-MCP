using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Narration events for a broadcast recording (start_broadcast): liftoff, stage separations, altitude marks,
    /// orbit/landing/splashdown and SOI changes of the active vessel. Collected only while armed; the App polls them.
    /// Times are Unity real time so the App can place them on the recording timeline (ageMs) regardless of time warp.
    /// </summary>
    public static class BroadcastEvents
    {
        private const int MaxEvents = 500;

        private static readonly List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();
        private static readonly List<Action> Unhooks = new List<Action>();
        private static readonly HashSet<int> Crossed = new HashSet<int>();
        private static bool _armed;
        private static bool _liftedOff;
        private static long _seq;
        private static int _separations;
        private static double _lastSeparation = double.MinValue;
        private static int _atmosphereIndex = -1;
        private static double[] _altitudes = new double[0];

        public static bool IsArmed => _armed;

        public static void Hook()
        {
            if (Unhooks.Count > 0)
            {
                return;
            }
            // KSP EventData reads the delegate Target: lambdas only, no static method groups.
            On(GameEvents.onVesselSituationChange, a => OnSituation(a.host, a.from, a.to));
            On(GameEvents.onVesselSOIChanged, a => OnSoi(a.host, a.from, a.to));
            On(GameEvents.onStageSeparation, r => OnSeparation(r));
        }

        public static void Unhook()
        {
            foreach (Action a in Unhooks)
            {
                a();
            }
            Unhooks.Clear();
            _armed = false;
        }

        private static void On<T>(EventData<T> ev, EventData<T>.OnEvent h)
        {
            ev.Add(h);
            Unhooks.Add(() => ev.Remove(h));
        }

        /// <summary>Clears the buffer and starts collecting. altitudes ascending; atmosphereDepth is one of them or 0.</summary>
        public static void Arm(double[] altitudes, double atmosphereDepth)
        {
            lock (Events)
            {
                Events.Clear();
            }
            _separations = 0;
            _lastSeparation = double.MinValue;
            Crossed.Clear();
            _liftedOff = false;
            _altitudes = altitudes ?? new double[0];
            _atmosphereIndex = atmosphereDepth > 0 ? Array.IndexOf(_altitudes, atmosphereDepth) : -1;
            // Armed in flight: marks already below the vessel are not announced.
            Vessel v = FlightGlobals.ActiveVessel;
            for (int i = 0; v != null && i < _altitudes.Length; i++)
            {
                if (v.altitude >= _altitudes[i])
                {
                    Crossed.Add(i);
                }
            }
            _armed = true;
        }

        /// <summary>Stops collecting; returns how many events were buffered.</summary>
        public static int Disarm()
        {
            _armed = false;
            lock (Events)
            {
                return Events.Count;
            }
        }

        /// <summary>Events with seq &gt; since, each with ageMs = milliseconds since it happened (real time).</summary>
        public static Dictionary<string, object> Since(long since)
        {
            double now = Time.realtimeSinceStartup;
            List<object> list = new List<object>();
            lock (Events)
            {
                foreach (Dictionary<string, object> e in Events)
                {
                    if ((long)e["seq"] > since)
                    {
                        Dictionary<string, object> copy = new Dictionary<string, object>(e);
                        copy["ageMs"] = Math.Max(0, Math.Round((now - (double)e["rt"]) * 1000));
                        list.Add(copy);
                    }
                }
            }
            return new Dictionary<string, object> { ["armed"] = _armed, ["events"] = list, ["last"] = _seq };
        }

        /// <summary>Altitude marks; called every physics tick.</summary>
        public static void FixedUpdate()
        {
            if (!_armed)
            {
                return;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                return;
            }
            for (int i = 0; i < _altitudes.Length; i++)
            {
                if (!Crossed.Contains(i) && v.altitude >= _altitudes[i])
                {
                    Crossed.Add(i);
                    Add("altitude", "meters", _altitudes[i], "atmosphere", i == _atmosphereIndex);
                }
            }
        }

        private static void OnSituation(Vessel v, Vessel.Situations from, Vessel.Situations to)
        {
            if (!_armed || v == null || v != FlightGlobals.ActiveVessel)
            {
                return;
            }
            // Airless bodies go LANDED/PRELAUNCH → SUB_ORBITAL without FLYING.
            if ((to == Vessel.Situations.FLYING || to == Vessel.Situations.SUB_ORBITAL)
                && (from == Vessel.Situations.PRELAUNCH || from == Vessel.Situations.LANDED) && !_liftedOff)
            {
                _liftedOff = true;
                Add("liftoff", "vessel", v.vesselName);
                return;
            }
            if (to == Vessel.Situations.ORBITING || to == Vessel.Situations.LANDED || to == Vessel.Situations.SPLASHED)
            {
                Add("situation", "situation", to.ToString(), "body", v.mainBody != null ? v.mainBody.bodyName : "");
            }
        }

        private static void OnSoi(Vessel v, CelestialBody from, CelestialBody to)
        {
            if (!_armed || v == null || v != FlightGlobals.ActiveVessel || to == null)
            {
                return;
            }
            Add("soi", "from", from != null ? from.bodyName : "", "to", to.bodyName);
        }

        private static void OnSeparation(EventReport r)
        {
            if (!_armed)
            {
                return;
            }
            // One event per decoupler, and the decoupler may end up on the debris: count one separation per
            // staging moment (decouplers firing within 0.5 s, e.g. radial boosters, are the same separation).
            double now = Time.realtimeSinceStartup;
            Vessel active = FlightGlobals.ActiveVessel;
            if (active == null || now - _lastSeparation < 0.5)
            {
                return;
            }
            // Only separations of the active vessel: the decoupler is on it or on the piece that just left it (within 200 m).
            Vessel from = r != null && r.origin != null ? r.origin.vessel : null;
            if (from != null && from != active && Vector3d.Distance(from.GetWorldPos3D(), active.GetWorldPos3D()) > 200)
            {
                return;
            }
            _lastSeparation = now;
            _separations++;
            Add("separation", "index", _separations);
        }

        private static void Add(string type, params object[] kv)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            Dictionary<string, object> e = new Dictionary<string, object>
            {
                ["seq"] = ++_seq,
                ["type"] = type,
                ["rt"] = (double)Time.realtimeSinceStartup,
                ["ut"] = Planetarium.GetUniversalTime(),
                ["met"] = v != null ? v.missionTime : 0.0,
            };
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                e[(string)kv[i]] = kv[i + 1];
            }
            lock (Events)
            {
                Events.Add(e);
                if (Events.Count > MaxEvents)
                {
                    Events.RemoveAt(0);
                }
            }
        }
    }
}
