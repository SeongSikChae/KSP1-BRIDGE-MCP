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
    /// Flight events with an ever-increasing seq in two rings (500 each): plan events and part diagnostics, so a vessel
    /// breaking apart never pushes plan events out. Reads merge both by seq. A trial (Submit's first command calculation)
    /// collects its plan events aside; the caller commits them on success or drops them on failure. Main thread only.
    /// </summary>
    public static class FlightEvents
    {
        public const int Capacity = 500;

        /// <summary>One ring of events; overwriting the oldest raises DroppedSeq.</summary>
        private sealed class Ring
        {
            private readonly FlightEvent[] _buf = new FlightEvent[Capacity];
            private int _start;
            public int Count;

            /// <summary>i-th oldest event.</summary>
            public FlightEvent At(int i) => _buf[(_start + i) % Capacity];

            public void Put(FlightEvent e)
            {
                if (Count < Capacity)
                {
                    _buf[(_start + Count) % Capacity] = e;
                    Count++;
                    return;
                }
                DroppedSeq = Math.Max(DroppedSeq, _buf[_start].Seq);
                _buf[_start] = e;
                _start = (_start + 1) % Capacity;
            }

            public void Clear()
            {
                Array.Clear(_buf, 0, Capacity);
                _start = 0;
                Count = 0;
            }
        }

        private static readonly Ring _plan = new Ring();    // every event except the part diagnostics
        private static readonly Ring _parts = new Ring();   // part diagnostics (PartTypes)
        private static List<FlightEvent> _trial;

        /// <summary>Types that go to the part ring (also when restored from a handover).</summary>
        private static readonly HashSet<string> PartTypes = new HashSet<string> { "part_lost", "part_collision", "part_limit", "part_joint_break" };

        public static long LastSeq { get; private set; }

        /// <summary>Largest seq lost by overwriting either ring, or before the restored window; since &lt; DroppedSeq means truncated.</summary>
        public static long DroppedSeq { get; private set; }

        /// <summary>Event session id: new when the buffer starts fresh (a logic start without handover).</summary>
        public static string Epoch { get; private set; } = Guid.NewGuid().ToString("N");

        public static void Add(string type, int step, Dictionary<string, object> data)
        {
            FlightEvent e = Create(type, step, data);
            if (_trial != null)
            {
                _trial.Add(e);
            }
            else
            {
                Push(e);
            }
        }

        /// <summary>Part diagnostics: own ring, never part of a trial (unlike plan events they do not recur on the next Tick).</summary>
        public static void AddPart(string type, int step, Dictionary<string, object> data)
        {
            FlightEvent e = Create(type, step, data);
            e.Seq = ++LastSeq;
            _parts.Put(e);
        }

        private static FlightEvent Create(string type, int step, Dictionary<string, object> data)
        {
            Vessel v = HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;   // also called outside flight (missed scene hook)
            return new FlightEvent
            {
                Ut = Planetarium.GetUniversalTime(),
                Met = v != null ? v.missionTime : double.NaN,
                Type = type,
                Step = step,
                Data = data,
            };
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
            _plan.Put(e);
        }

        /// <summary>Both rings oldest first, merged by seq (two cursors; each ring is in seq order).</summary>
        private static IEnumerable<FlightEvent> Merged()
        {
            int i = 0, j = 0;
            while (i < _plan.Count || j < _parts.Count)
            {
                if (j >= _parts.Count || (i < _plan.Count && _plan.At(i).Seq < _parts.At(j).Seq))
                {
                    yield return _plan.At(i++);
                }
                else
                {
                    yield return _parts.At(j++);
                }
            }
        }

        /// <summary>Handover: both rings merged oldest first ({seq, ut, met, type, step, data}; met NaN becomes null).</summary>
        public static List<object> Save()
        {
            List<object> l = new List<object>(_plan.Count + _parts.Count);
            foreach (FlightEvent e in Merged())
            {
                l.Add(new Dictionary<string, object>
                {
                    ["seq"] = (double)e.Seq,
                    ["ut"] = e.Ut,
                    ["met"] = e.Met,
                    ["type"] = e.Type,
                    ["step"] = (double)e.Step,
                    ["data"] = e.Data,
                });
            }
            return l;
        }

        /// <summary>Logic stop: empties both rings, seq, dropped seq and trial (the handover carries them; a restarted logic restores them).</summary>
        public static void Clear()
        {
            _plan.Clear();
            _parts.Clear();
            _trial = null;
            LastSeq = 0;
            DroppedSeq = 0;
        }

        /// <summary>Logic start: rings, seq and epoch from the handover. No epoch (no handover, unreadable) = a new session id.
        /// An unreadable event is skipped; seq still continues from the handed-over value. droppedSeq null (older logic):
        /// the smallest restored seq − 1, the single-ring rule.</summary>
        public static void Restore(List<object> events, long seq, string epoch, long? droppedSeq)
        {
            Clear();
            Epoch = string.IsNullOrEmpty(epoch) ? Guid.NewGuid().ToString("N") : epoch;
            long first = 0, last = 0;
            if (events != null)
            {
                foreach (object o in events)
                {
                    if (!(o is Dictionary<string, object> d) || !(d.TryGetValue("type", out object t) && t is string type))
                    {
                        continue;
                    }
                    FlightEvent e;
                    try
                    {
                        e = new FlightEvent
                        {
                            Seq = (long)Handover.D(d, "seq", 0),
                            Ut = Handover.D(d, "ut"),
                            Met = Handover.D(d, "met"),
                            Type = type,
                            Step = (int)Handover.D(d, "step", 0),
                            Data = d.TryGetValue("data", out object data) ? data as Dictionary<string, object> : null,
                        };
                    }
                    catch (Exception)
                    {
                        continue;   // a malformed event is skipped; seq still continues from the handed-over value
                    }
                    (PartTypes.Contains(type) ? _parts : _plan).Put(e);
                    first = first == 0 ? e.Seq : Math.Min(first, e.Seq);
                    last = Math.Max(last, e.Seq);
                }
            }
            DroppedSeq = Math.Max(DroppedSeq, droppedSeq ?? (first > 0 ? first - 1 : 0));
            LastSeq = Math.Max(seq, last);
        }

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
            foreach (FlightEvent e in Merged())
            {
                if (events.Count >= limit)
                {
                    break;
                }
                if (e.Seq <= since)
                {
                    continue;
                }
                events.Add(e.ToDictionary());
                next = e.Seq;
            }
            bool truncated = since < DroppedSeq;   // events after the cursor were dropped
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
