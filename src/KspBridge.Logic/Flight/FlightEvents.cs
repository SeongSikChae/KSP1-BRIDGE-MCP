using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    public sealed class FlightEvent
    {
        public long Seq;
        public double Ut;
        public double Met;
        public string Type;
        public int Step;
        public Dictionary<string, object> Data;

        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["seq"] = Seq,
            ["ut"] = Math.Round(Ut, 3),
            ["met"] = double.IsNaN(Met) ? null : (object)Math.Round(Met, 3),
            ["type"] = Type,
            ["step"] = Step,
            ["data"] = Data,
        };
    }

    /// <summary>
    /// Flight event ring buffer (500) with an ever-increasing seq. A trial (Submit's first command calculation) collects its
    /// events aside; the caller commits them on success or drops them on failure. Main thread only.
    /// </summary>
    public static class FlightEvents
    {
        public const int Capacity = 500;

        private static readonly FlightEvent[] _buf = new FlightEvent[Capacity];
        private static int _start;
        private static int _count;
        private static List<FlightEvent> _trial;

        public static long LastSeq { get; private set; }

        /// <summary>Event session id: new when the buffer starts fresh (a logic start without handover).</summary>
        public static string Epoch { get; private set; } = Guid.NewGuid().ToString("N");

        public static void Add(string type, int step, Dictionary<string, object> data)
        {
            Vessel v = HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;   // also called outside flight (missed scene hook)
            FlightEvent e = new FlightEvent
            {
                Ut = Planetarium.GetUniversalTime(),
                Met = v != null ? v.missionTime : double.NaN,
                Type = type,
                Step = step,
                Data = data,
            };
            if (_trial != null)
            {
                _trial.Add(e);
            }
            else
            {
                Push(e);
            }
        }

        /// <summary>Following Adds go to a trial list without seq until EndTrial.</summary>
        public static void BeginTrial() => _trial = new List<FlightEvent>();

        /// <summary>Ends the trial and returns its events (the caller commits or drops them).</summary>
        public static List<FlightEvent> EndTrial()
        {
            List<FlightEvent> l = _trial;
            _trial = null;
            return l;
        }

        /// <summary>Numbers the events in order and stores them; clears the list so a second call adds nothing.</summary>
        public static void Commit(ref List<FlightEvent> events)
        {
            if (events == null)
            {
                return;
            }
            foreach (FlightEvent e in events)
            {
                Push(e);
            }
            events = null;
        }

        private static void Push(FlightEvent e)
        {
            e.Seq = ++LastSeq;
            if (_count < Capacity)
            {
                _buf[(_start + _count) % Capacity] = e;
                _count++;
            }
            else
            {
                _buf[_start] = e;
                _start = (_start + 1) % Capacity;
            }
        }

        /// <summary>i-th oldest event.</summary>
        private static FlightEvent At(int i) => _buf[(_start + i) % Capacity];

        /// <summary>Events with seq &gt; since, oldest first: {events, next, truncated, epoch}.</summary>
        public static Dictionary<string, object> Since(long since, int limit)
        {
            if (since > LastSeq)
            {
                // A cursor ahead of this session (e.g. KSP restarted): no events, read again from 0.
                return Result(new List<object>(), 0, true);
            }
            List<object> events = new List<object>();
            long next = since;
            for (int i = 0; i < _count && events.Count < limit; i++)
            {
                FlightEvent e = At(i);
                if (e.Seq <= since)
                {
                    continue;
                }
                events.Add(e.ToDictionary());
                next = e.Seq;
            }
            bool truncated = _count > 0 && since < At(0).Seq - 1;   // events older than the buffer were dropped
            return Result(events, next, truncated);
        }

        private static Dictionary<string, object> Result(List<object> events, long next, bool truncated) => new Dictionary<string, object>
        {
            ["events"] = events,
            ["next"] = next,
            ["truncated"] = truncated,
            ["epoch"] = Epoch,
        };
    }
}
