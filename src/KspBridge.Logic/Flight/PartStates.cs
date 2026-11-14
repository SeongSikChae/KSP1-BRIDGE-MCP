using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Flight
{
    /// <summary>
    /// get_vessel_parts and invoke_part_event (design 0041): what the flight part action window (UIPartActionWindow) shows
    /// for parts of the active vessel. Main thread only.
    /// </summary>
    public static class PartStates
    {
        /// <summary>Module name of the part-level fields and events (Part.Fields, Part.Events).</summary>
        internal const string PartLevel = "Part";

        public static object List(Vessel v, string partIdText, string module)
        {
            IEnumerable<Part> parts = v.parts;
            if (partIdText != null)
            {
                uint id = ParsePartId(partIdText);
                parts = new[] { v.parts.FirstOrDefault(p => p.persistentId == id) ?? throw PartNotFound(id) };
            }
            List<object> rows = new List<object>();
            foreach (Part p in parts)
            {
                List<object> modules = new List<object>();
                if (module == null || module == PartLevel)
                {
                    AddIfShown(modules, DescribePartLevel(p));
                }
                for (int i = 0; i < p.Modules.Count; i++)
                {
                    PartModule m = p.Modules[i];
                    if (m.isEnabled && (module == null || m.moduleName == module))
                    {
                        AddIfShown(modules, DescribeModule(p, m, i));
                    }
                }
                if (module != null && modules.Count == 0)
                {
                    continue;
                }
                rows.Add(new Dictionary<string, object>
                {
                    ["id"] = (double)p.persistentId,
                    ["name"] = p.partInfo.name,
                    ["title"] = p.partInfo.title,
                    ["modules"] = modules,
                    ["resources"] = Resources(p),
                });
            }
            return new Dictionary<string, object> { ["vessel"] = v.vesselName, ["id"] = (double)v.persistentId, ["parts"] = rows };
        }

        /// <summary>CanActivateEvent steps 1·2: the event is in the window's list at all.</summary>
        internal static bool Listed(BaseEvent e) =>
            !(e.advancedTweakable && !GameSettings.ADVANCED_TWEAKABLES) && e.guiActive && e.active && !e.EventIsDisabledByVariant;

        /// <summary>CanActivateEvent steps 3·4 for a listed event: null = the button can be pressed now.</summary>
        internal static string Unavailable(BaseEvent e, Part p)
        {
            if (p.vessel == null || p.vessel != FlightGlobals.ActiveVessel)
            {
                return "not_active_vessel";
            }
            bool ships = InputLockManager.IsUnlocked(ControlTypes.ACTIONS_SHIP);
            bool full = !e.requireFullControl || InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY);
            if ((ships && p.vessel.IsControllable && full) || e.guiActiveUncommand)
            {
                return null;
            }
            return !ships || !full ? "input_locked" : "no_control";
        }

        internal static Dictionary<string, object> DescribeModule(Part p, PartModule m, int index)
        {
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["index"] = index,
                ["module"] = m.moduleName,
                ["fields"] = Fields(m.Fields, m),
                ["events"] = Events(m.Events, p),
            };
            Dictionary<string, object> state = State(m);
            if (state != null)
            {
                d["state"] = state;
            }
            return d;
        }

        internal static Dictionary<string, object> DescribePartLevel(Part p) => new Dictionary<string, object>
        {
            ["index"] = null,
            ["module"] = PartLevel,
            ["fields"] = Fields(p.Fields, p),
            ["events"] = Events(p.Events, p),
        };

        /// <summary>DebugHandler.PartId's rule and text, for a query string value.</summary>
        internal static uint ParsePartId(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || !(v >= 0 && v <= uint.MaxValue) || v != Math.Floor(v))
            {
                throw ApiException.BadRequest("part_id must be an integer part id (persistentId) 0.." + uint.MaxValue);
            }
            return (uint)v;
        }

        internal static ApiException PartNotFound(uint id) =>
            new ApiException(404, "part_not_found", "part " + id + " is not in the active vessel (use get_vessel_parts for part ids)");

        private static void AddIfShown(List<object> modules, Dictionary<string, object> d)
        {
            if (((List<object>)d["fields"]).Count > 0 || ((List<object>)d["events"]).Count > 0 || d.ContainsKey("state"))
            {
                modules.Add(d);
            }
        }

        private static List<object> Fields(BaseFieldList list, object host)
        {
            List<object> rows = new List<object>();
            foreach (BaseField f in list)
            {
                if (!f.guiActive || (f.advancedTweakable && !GameSettings.ADVANCED_TWEAKABLES))
                {
                    continue;
                }
                Dictionary<string, object> d = new Dictionary<string, object> { ["field"] = f.name, ["title"] = f.guiName, ["units"] = f.guiUnits };
                try
                {
                    d["value"] = FieldValue(f.GetValue(host));
                }
                catch (Exception ex)   // design 0041: one bad getter must not fail the whole list
                {
                    d["value"] = null;
                    d["error"] = ex.GetType().Name + ": " + ex.Message;
                }
                rows.Add(d);
            }
            return rows;
        }

        private static List<object> Events(BaseEventList list, Part p)
        {
            List<object> rows = new List<object>();
            foreach (BaseEvent e in list)
            {
                if (!Listed(e))
                {
                    continue;
                }
                string reason = Unavailable(e, p);
                rows.Add(new Dictionary<string, object>
                {
                    ["event"] = e.name,
                    ["title"] = e.guiName,
                    ["available"] = reason == null,
                    ["reason"] = reason,
                });
            }
            return rows;
        }

        /// <summary>Main states that are not window fields (design 0041 decision 2).</summary>
        private static Dictionary<string, object> State(PartModule m)
        {
            switch (m)
            {
                case ModuleDeployablePart d:
                    return new Dictionary<string, object> { ["deploy_state"] = d.deployState.ToString() };
                case ModuleDockingNode n:
                    return new Dictionary<string, object>
                    {
                        ["state"] = n.state,
                        ["docked_part_id"] = n.otherNode != null && n.otherNode.part != null ? (object)(double)n.otherNode.part.persistentId : null,
                    };
                default:
                    return null;
            }
        }

        private static List<object> Resources(Part p)
        {
            List<object> rows = new List<object>();
            foreach (PartResource r in p.Resources)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["resource"] = r.resourceName,
                    ["amount"] = r.amount,
                    ["max"] = r.maxAmount,
                    ["flow_enabled"] = r.flowState,
                });
            }
            return rows;
        }

        /// <summary>Numbers, bools and strings as they are (Json writes float and int); enums by name; anything else by ToString.</summary>
        private static object FieldValue(object value) =>
            value == null || value is bool || value is string || value is double || value is float || value is int ? value : value.ToString();
    }
}
