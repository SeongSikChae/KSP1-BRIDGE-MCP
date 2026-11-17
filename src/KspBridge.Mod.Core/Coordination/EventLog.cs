using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Mod.Core
{
    /// <summary>Ring buffer of flight events (plan/step lifecycle) the Planner reads with get_flight_events.</summary>
    public sealed class EventLog
    {
        public const int Capacity = 500;
        private readonly LinkedList<Dictionary<string, object>> items = new LinkedList<Dictionary<string, object>>();
        private readonly Func<double> clock;

        public EventLog(Func<double> clock) => this.clock = clock;

        public long LastSeq { get; private set; }

        public void Add(string type, int? planId = null, Dictionary<string, object> data = null)
        {
            Dictionary<string, object> evt = new Dictionary<string, object>
            {
                ["seq"] = ++LastSeq,
                ["type"] = type,
                ["ut"] = SafeClock(),
                ["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            if (planId.HasValue)
                evt["plan_id"] = planId.Value;
            if (data != null)
                evt["data"] = data;
            items.AddLast(evt);
            while (items.Count > Capacity)
                items.RemoveFirst();
        }

        /// <summary>Events with seq greater than <paramref name="since"/>, oldest first, at most <paramref name="limit"/>.</summary>
        public List<Dictionary<string, object>> Since(long since, int limit)
            => items.Where(e => Convert.ToInt64(e["seq"], CultureInfo.InvariantCulture) > since).Take(Math.Max(1, limit)).ToList();

        public Dictionary<string, object> Snapshot()
            => new Dictionary<string, object> { ["last_seq"] = LastSeq, ["items"] = items.ToList<object>() };

        public void Restore(Dictionary<string, object> snapshot)
        {
            if (snapshot == null)
                return;
            items.Clear();
            if (snapshot.TryGetValue("items", out object list) && list is List<object> restored)
            {
                foreach (Dictionary<string, object> evt in restored.OfType<Dictionary<string, object>>())
                    items.AddLast(evt);
            }
            if (snapshot.TryGetValue("last_seq", out object seq) && seq is double d)
                LastSeq = (long)d;
        }

        private double SafeClock()
        {
            try
            {
                return clock();
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
