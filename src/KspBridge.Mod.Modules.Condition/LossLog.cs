using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>A part the vessel lost.</summary>
    internal sealed class Loss
    {
        public const string Exploded = "exploded";
        public const string BrokeOff = "broke_off";

        public string Name;
        public string Kind;

        /// <summary>What the game said destroyed it; null when it said nothing.</summary>
        public string Cause;
    }

    /// <summary>What was lost between two checks.</summary>
    internal sealed class LossReport
    {
        public const int MaxParts = 10;

        public bool VesselDestroyed;
        public List<Loss> Losses = new List<Loss>();

        /// <summary>Data of the parts_lost event: every loss is counted, the first ones are listed.</summary>
        public Dictionary<string, object> ToEvent() => new Dictionary<string, object>
        {
            ["count"] = Losses.Count,
            ["parts"] = Losses.Take(MaxParts).Select(l => (object)new Dictionary<string, object>
            {
                ["name"] = l.Name,
                ["kind"] = l.Kind,
                ["cause"] = l.Cause,
            }).ToList(),
        };

        /// <summary>Plan end detail.</summary>
        public string Describe()
        {
            string parts = Losses.Count == 0
                ? null
                : Losses.Count + " part(s) lost: " + string.Join(", ", Losses.Take(MaxParts).Select(Text))
                  + (Losses.Count > MaxParts ? ", ..." : "");
            if (!VesselDestroyed)
                return parts;
            return "the active vessel was destroyed" + (parts == null ? "" : "; " + parts);
        }

        private static string Text(Loss loss)
            => (loss.Name ?? "unknown part") + " (" + loss.Kind + (loss.Cause == null ? "" : ", " + loss.Cause) + ")";
    }

    /// <summary>
    /// Collects losses from game events between two checks (pure). The game tells the cause of a loss before or after the loss
    /// itself, within the same frame, so causes are kept by part and joined when the losses are taken. Nothing is collected while
    /// it is not running.
    /// </summary>
    internal sealed class LossLog
    {
        private readonly List<KeyValuePair<int, Loss>> losses = new List<KeyValuePair<int, Loss>>();
        private readonly Dictionary<int, string> causes = new Dictionary<int, string>();
        private bool destroyed;

        public bool Running { get; private set; }

        public void Start()
        {
            Clear();
            Running = true;
        }

        public void Stop()
        {
            Clear();
            Running = false;
        }

        public void Add(int part, string name, string kind)
        {
            if (Running)
                losses.Add(new KeyValuePair<int, Loss>(part, new Loss { Name = name, Kind = kind }));
        }

        /// <summary>The first cause the game gives for a part stands.</summary>
        public void AddCause(int part, string cause)
        {
            if (Running && !causes.ContainsKey(part))
                causes[part] = cause;
        }

        public void AddVesselDestroyed()
        {
            if (Running)
                destroyed = true;
        }

        /// <summary>What was collected since the last call; the log is empty afterwards.</summary>
        public LossReport Take()
        {
            LossReport report = new LossReport { VesselDestroyed = destroyed };
            foreach (KeyValuePair<int, Loss> pair in losses)
            {
                causes.TryGetValue(pair.Key, out string cause);
                pair.Value.Cause = cause;
                report.Losses.Add(pair.Value);
            }
            Clear();
            return report;
        }

        private void Clear()
        {
            losses.Clear();
            causes.Clear();
            destroyed = false;
        }
    }
}
