using KspBridge.Check;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Editor
{
    /// <summary>Response of an edit tool: the parts it added, changed or removed, and structure warnings about them (U10).</summary>
    public static class EditResult
    {
        public static object Added(ShipConstruct ship, IEnumerable<Part> parts) => Make(ship, parts, null, null);

        public static object Changed(ShipConstruct ship, IEnumerable<Part> parts) => Make(ship, null, parts, null);

        public static object Removed(ShipConstruct ship, IEnumerable<uint> ids) => Make(ship, null, null, ids);

        private static object Make(ShipConstruct ship, IEnumerable<Part> added, IEnumerable<Part> changed, IEnumerable<uint> removed)
        {
            List<Part> edited = (added ?? Enumerable.Empty<Part>()).Concat(changed ?? Enumerable.Empty<Part>()).ToList();
            // Parts attached below an edited part move with it, so their issues belong to this edit too.
            HashSet<Part> focus = new HashSet<Part>(edited.Concat(edited.SelectMany(EditorLogicBase.FindPartsInChildren)));
            return new Dictionary<string, object>
            {
                ["added"] = (added ?? Enumerable.Empty<Part>()).Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["changed"] = (changed ?? Enumerable.Empty<Part>()).Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["removed"] = (removed ?? Enumerable.Empty<uint>()).Cast<object>().ToList(),
                ["warnings"] = Warnings(ship, focus),
            };
        }

        /// <summary>The edit is already committed here, so a failing check is logged and reported, not thrown.</summary>
        private static List<object> Warnings(ShipConstruct ship, ICollection<Part> focus)
        {
            try
            {
                return StructureCheck.Related(ship, focus);
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] structure check failed: " + e);
                return new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["severity"] = "warning",
                        ["code"] = "check_failed",
                        ["parts"] = new List<object>(),
                        ["message"] = "the edit was applied but the structure check failed: " + e.GetType().Name + ": " + e.Message,
                    },
                };
            }
        }
    }
}
