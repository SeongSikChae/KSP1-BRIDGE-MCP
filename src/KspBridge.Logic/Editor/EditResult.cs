using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Editor
{
    /// <summary>Response of an edit tool: the parts it added, changed or removed, and structure warnings about them (U10).</summary>
    public static class EditResult
    {
        public static object Added(ShipConstruct ship, IEnumerable<Part> parts) => Make(ship, parts, null, null);

        private static object Make(ShipConstruct ship, IEnumerable<Part> added, IEnumerable<Part> changed, IEnumerable<uint> removed) =>
            new Dictionary<string, object>
            {
                ["added"] = (added ?? Enumerable.Empty<Part>()).Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["changed"] = (changed ?? Enumerable.Empty<Part>()).Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["removed"] = (removed ?? Enumerable.Empty<uint>()).Cast<object>().ToList(),
                ["warnings"] = new List<object>(),
            };
    }
}
