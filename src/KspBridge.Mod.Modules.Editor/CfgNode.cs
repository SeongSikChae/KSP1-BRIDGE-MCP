using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// KSP-independent tree with the shape of a ConfigNode: a name, ordered key/value pairs (keys may repeat) and child nodes.
    /// The craft editing model works on this so it can be unit tested without the game; ShipEditor converts to and from
    /// ConfigNode. Values are kept as text exactly as read, so anything the editor does not understand is written back as is.
    /// </summary>
    public sealed class CfgNode
    {
        private readonly List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
        private readonly List<CfgNode> nodes = new List<CfgNode>();

        public CfgNode(string name) => Name = name ?? string.Empty;

        public string Name { get; set; }

        public IReadOnlyList<KeyValuePair<string, string>> Values => values;
        public IReadOnlyList<CfgNode> Nodes => nodes;

        /// <summary>First value of <paramref name="key"/>, or null.</summary>
        public string GetValue(string key)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (pair.Key == key)
                    return pair.Value;
            }
            return null;
        }

        /// <summary>Every value of <paramref name="key"/> in order.</summary>
        public List<string> GetValues(string key) => values.Where(p => p.Key == key).Select(p => p.Value).ToList();

        public bool HasValue(string key) => values.Any(p => p.Key == key);

        /// <summary>Replaces the first value of <paramref name="key"/> (keeping its position), or appends it.</summary>
        public void SetValue(string key, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Key == key)
                {
                    values[i] = new KeyValuePair<string, string>(key, value);
                    return;
                }
            }
            values.Add(new KeyValuePair<string, string>(key, value));
        }

        public void AddValue(string key, string value) => values.Add(new KeyValuePair<string, string>(key, value));

        /// <summary>
        /// Replaces every value of <paramref name="key"/> with <paramref name="newValues"/>, keeping the position of the first
        /// old one (appended when there was none).
        /// </summary>
        public void SetValues(string key, IEnumerable<string> newValues)
        {
            int at = values.FindIndex(p => p.Key == key);
            values.RemoveAll(p => p.Key == key);
            if (at < 0 || at > values.Count)
                at = values.Count;
            values.InsertRange(at, newValues.Select(v => new KeyValuePair<string, string>(key, v)));
        }

        public void RemoveValues(string key) => values.RemoveAll(p => p.Key == key);

        public List<CfgNode> GetNodes(string name) => nodes.Where(n => n.Name == name).ToList();

        public CfgNode GetNode(string name) => nodes.FirstOrDefault(n => n.Name == name);

        public void AddNode(CfgNode node) => nodes.Add(node ?? throw new ArgumentNullException(nameof(node)));

        public void InsertNode(int index, CfgNode node) => nodes.Insert(index, node ?? throw new ArgumentNullException(nameof(node)));

        public bool RemoveNode(CfgNode node) => nodes.Remove(node);

        public int IndexOfNode(CfgNode node) => nodes.IndexOf(node);

        /// <summary>Deep copy.</summary>
        public CfgNode Clone()
        {
            CfgNode copy = new CfgNode(Name);
            copy.values.AddRange(values);
            foreach (CfgNode child in nodes)
                copy.nodes.Add(child.Clone());
            return copy;
        }
    }
}
