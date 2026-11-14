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

        /// <summary>
        /// Presses one part action window button (UIPartActionButton.OnClick in flight): a module event is first sent to the
        /// symmetry counterparts' part-level event lists, then invoked; a part-level event is only invoked.
        /// </summary>
        public static object Invoke(Vessel v, RequestBody b)
        {
            uint id = PartIdFromBody(b);
            string module = b.GetString("module");
            string eventName = b.GetString("event");
            int? moduleIndex = b.Has("module_index") ? ModuleIndex(b) : (int?)null;
            Part p = v.parts.FirstOrDefault(x => x.persistentId == id) ?? throw PartNotFound(id);
            PartModule target = FindModule(p, module, moduleIndex, "event_unavailable", "part-level events", out int? index);
            BaseEventList events = target == null ? p.Events : target.Events;
            BaseEvent e = events[eventName] ?? throw new ApiException(404, "event_not_found", eventName + " is not an event of " + module
                + " on " + p.partInfo.name + " (events: " + string.Join(", ", events.Select(x => x.name).ToArray()) + ")");
            string reason = !Listed(e) ? "inactive" : Unavailable(e, p);
            if (reason != null)
            {
                throw new ApiException(409, "event_unavailable", module + "." + eventName + " cannot be pressed in the part action window now: " + reason);
            }

            uint before = v.persistentId;
            List<object> counterparts = new List<object>();
            try
            {
                if (target != null)
                {
                    foreach (Part s in p.symmetryCounterparts)
                    {
                        counterparts.Add(new Dictionary<string, object> { ["part_id"] = (double)s.persistentId, ["sent"] = s.Events.Send(e.id) });
                    }
                }
                e.Invoke();
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                Record(id, p, module, index, eventName, counterparts, false, false, error);
                throw new ApiException(500, "event_failed", error);
            }

            bool changed = FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.persistentId != before;
            bool separated = p == null || p.vessel != v;
            Record(id, p, module, index, eventName, counterparts, changed, separated, null);
            Dictionary<string, object> r = new Dictionary<string, object>
            {
                ["part_id"] = (double)id,
                ["part"] = p != null ? p.partInfo.name : null,
                ["module"] = module,
                ["index"] = index,
                ["event"] = eventName,
                ["title"] = e.guiName,
                ["counterparts"] = counterparts,
                ["vessel_changed"] = changed,
                ["separated"] = separated,
                ["part_vessel_id"] = p != null && p.vessel != null ? (object)(double)p.vessel.persistentId : null,
                ["state_error"] = null,
            };
            try
            {
                r["module_state"] = target == null ? DescribePartLevel(p) : DescribeModule(p, target, index.Value);
            }
            catch (Exception ex)   // the event already ran: answer 200 so a retry does not press a toggle twice
            {
                r["module_state"] = null;
                r["state_error"] = ex.GetType().Name + ": " + ex.Message;
            }
            return r;
        }

        /// <summary>
        /// A flight part action window value change (UIPartActionFieldItem.SetFieldValue, scene Flight; design 0043): counterparts
        /// first when the control affects them in flight, then the part itself with onFieldChanged (old value), only when something
        /// changes. A game callback that throws part way answers 500 field_failed naming what already changed.
        /// </summary>
        public static object SetField(Vessel v, RequestBody b)
        {
            uint id = PartIdFromBody(b);
            string module = b.GetString("module");
            string name = b.GetString("field");
            object raw = b.GetRaw("value") ?? throw ApiException.BadRequest("'value' is required");
            int? moduleIndex = b.Has("module_index") ? ModuleIndex(b) : (int?)null;
            Part p = v.parts.FirstOrDefault(x => x.persistentId == id) ?? throw PartNotFound(id);
            PartModule target = FindModule(p, module, moduleIndex, "field_not_editable", "part-level fields", out int? index);
            BaseFieldList fields = target == null ? p.Fields : target.Fields;
            BaseField f = fields[name];
            if (f == null || !Shown(f))
            {
                throw new ApiException(404, "field_not_found", name + " is not a field the part action window shows for " + module + " on "
                    + p.partInfo.name + " (fields: " + string.Join(", ", fields.Cast<BaseField>().Where(Shown).Select(x => x.name).ToArray()) + ")");
            }
            UI_Control c = EditableControl(f)
                ?? throw new ApiException(409, "field_not_editable", module + "." + name + " is shown but cannot be changed in flight");
            if (!Unlocked(FieldLock(c)))
            {
                throw new ApiException(409, "field_locked", module + "." + name + " cannot be changed now: input_locked ("
                    + (c.requireFullControl ? "TWEAKABLES_FULLONLY" : "TWEAKABLES_ANYCONTROL") + ")");   // the enum's ToString names composite flags
            }
            object value = Editor.PartSettings.Convert(f, raw);
            Editor.PartSettings.CheckRange(f, c, value);

            object old = f.GetValue(f.host);
            bool changed = !Equals(old, value);
            List<Part> group = new List<Part> { p };
            List<object> written = new List<object>();   // counterpart ids whose value was set
            bool own = false;
            try
            {
                if ((c.affectSymCounterparts & UI_Scene.Flight) != UI_Scene.None)
                {
                    foreach (Part s in p.symmetryCounterparts.Where(s => s != null))
                    {
                        if (target == null)
                        {
                            changed |= !Equals(s.Fields.GetValue(name), value);
                            s.Fields.SetValue(name, value);   // part-level: value only, no events (SetSymCounterpartValue)
                            written.Add((double)s.persistentId);
                        }
                        else
                        {
                            PartModule sm = Editor.PartSettings.CounterpartModule(s, index.Value, target);
                            if (sm == null)
                            {
                                continue;
                            }
                            changed |= !Equals(sm.Fields.GetValue(name), value);
                            sm.Fields.SetValue(name, value);
                            written.Add((double)s.persistentId);
                            BaseField sf = sm.Fields[name];
                            sf?.uiControlEditor?.onSymmetryFieldChanged?.Invoke(f, value);
                            sf?.uiControlFlight?.onSymmetryFieldChanged?.Invoke(f, value);
                        }
                        group.Add(s);
                    }
                }
                if (changed)
                {
                    f.SetValue(value, f.host);
                    own = true;
                    c.onFieldChanged?.Invoke(f, old);
                    LogField(id, p, module, index, name, old, value, written, true, null);
                }
            }
            catch (Exception ex)   // a game callback failed part way (design 0043 step 8): say what already changed
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                LogField(id, p, module, index, name, old, value, written, own, error);
                throw new ApiException(500, "field_failed", error + " (value already set on counterparts [" + string.Join(", ", written.Select(x => x.ToString()).ToArray())
                    + "]" + (own ? " and on the part itself" : "") + "; read get_vessel_parts before retrying)");
            }
            finally
            {
                foreach (Part s in group)
                {
                    MonoUtilities.RefreshPartContextWindow(s);
                }
            }
            return new Dictionary<string, object>
            {
                ["part_id"] = (double)id,
                ["part"] = p.partInfo.name,
                ["module"] = module,
                ["index"] = index,
                ["field"] = name,
                ["old"] = FieldValue(old),
                ["value"] = FieldValue(f.GetValue(f.host)),
                ["changed"] = changed,
                ["values"] = group.Select(s => (object)new Dictionary<string, object>
                {
                    ["part_id"] = (double)s.persistentId,
                    ["value"] = FieldValue(target == null ? s.Fields.GetValue(name)
                        : (s == p ? target : Editor.PartSettings.CounterpartModule(s, index.Value, target))?.Fields.GetValue(name)),
                }).ToList(),
            };
        }

        private static void LogField(uint id, Part p, string module, int? index, string name, object old, object value, List<object> written, bool own, string error) =>
            Log("part_field", new Dictionary<string, object>
            {
                ["part_id"] = (double)id,
                ["part"] = p.partInfo.name,
                ["module"] = module,
                ["index"] = index,
                ["field"] = name,
                ["old"] = FieldValue(old),
                ["value"] = FieldValue(value),
                ["counterparts"] = written,
                ["own"] = own,
                ["error"] = error,
            }, "part_field " + p.partInfo.name + " (" + id + ") " + module + "." + name + " " + FieldValue(old) + " -> " + FieldValue(value)
                + (error != null ? " failed: " + error : ""));

        /// <summary>A flight event (step -1 without a plan) and the same line in KSP.log (proposal decision 10).</summary>
        internal static void Log(string type, Dictionary<string, object> data, string line)
        {
            FlightEvents.Add(type, Coordinator.Running ? Coordinator.StepIndex : -1, data);
            UnityEngine.Debug.Log("[KspBridge] " + line);
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
            bool ships = Unlocked(ControlTypes.ACTIONS_SHIP);
            bool full = !e.requireFullControl || Unlocked(ControlTypes.TWEAKABLES_FULLONLY);
            if ((ships && p.vessel.IsControllable && full) || e.guiActiveUncommand)
            {
                return null;
            }
            return !ships || !full ? "input_locked" : "no_control";
        }

        /// <summary>
        /// InputLockManager.IsUnlocked without flightDriver_ApplicationFocus: FlightDriver.OnApplicationFocus sets that lock only
        /// to block the player's keys while the game window is in the background, which is how an agent usually calls (same as
        /// launch_craft leaving out EditorDriver_AppFocus).
        /// </summary>
        private static bool Unlocked(ControlTypes control) =>
            !InputLockManager.lockStack.Any(l => l.Key != FocusLock && (l.Value & (ulong)control) != 0);

        private const string FocusLock = "flightDriver_ApplicationFocus";

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

        /// <summary>DebugHandler.PartId's rule and text, for a body value.</summary>
        private static uint PartIdFromBody(RequestBody b)
        {
            double v = b.GetDouble("part_id");
            if (!(v >= 0 && v <= uint.MaxValue) || v != Math.Floor(v))
            {
                throw ApiException.BadRequest("part_id must be an integer part id (persistentId) 0.." + uint.MaxValue);
            }
            return (uint)v;
        }

        private static int ModuleIndex(RequestBody b)
        {
            double v = b.GetDouble("module_index");
            if (!(v >= 0 && v <= int.MaxValue) || v != Math.Floor(v))
            {
                throw ApiException.BadRequest("module_index must be an integer >= 0 (index from get_vessel_parts)");
            }
            return (int)v;
        }

        /// <summary>
        /// The module to act on (design 0041 invoke_part_event step 4); null = part-level. disabled: the 409 code when the module is
        /// disabled. partLevel: the words in the module_index 400 ("part-level events" keeps invoke_part_event's text).
        /// </summary>
        private static PartModule FindModule(Part p, string module, int? moduleIndex, string disabled, string partLevel, out int? index)
        {
            if (module == PartLevel)
            {
                if (moduleIndex != null)
                {
                    throw ApiException.BadRequest("module_index is not used with module \"" + PartLevel + "\" (" + partLevel + ")");
                }
                index = null;
                return null;
            }
            if (moduleIndex != null)
            {
                int i = moduleIndex.Value;
                if (i >= p.Modules.Count || p.Modules[i].moduleName != module)
                {
                    throw new ApiException(404, "module_not_found", "module " + i + " of " + p.partInfo.name + " is not " + module
                        + " (" + (i < p.Modules.Count ? p.Modules[i].moduleName : "only " + p.Modules.Count + " modules") + ")");
                }
                index = i;
            }
            else
            {
                List<int> named = Enumerable.Range(0, p.Modules.Count).Where(i => p.Modules[i].moduleName == module).ToList();
                if (named.Count == 0)
                {
                    throw new ApiException(404, "module_not_found", module + " is not on " + p.partInfo.name
                        + " (modules: " + string.Join(", ", p.Modules.Cast<PartModule>().Select(x => x.moduleName).Distinct().ToArray()) + ")");
                }
                List<int> enabled = named.Where(i => p.Modules[i].isEnabled).ToList();
                if (enabled.Count > 1)
                {
                    throw new ApiException(409, "ambiguous_module", p.partInfo.name + " has several enabled " + module
                        + " modules; give module_index (" + string.Join(", ", enabled.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()) + ")");
                }
                index = enabled.Count == 1 ? enabled[0] : named[0];
            }
            PartModule target = p.Modules[index.Value];
            if (!target.isEnabled)
            {
                throw new ApiException(409, disabled, module + " on " + p.partInfo.name + " is disabled (module disabled)");
            }
            return target;
        }

        /// <summary>part_event in get_flight_events and a [KspBridge] KSP.log line (proposal decision 10).</summary>
        private static void Record(uint id, Part p, string module, int? index, string ev, List<object> counterparts, bool changed, bool separated, string error)
        {
            string name = p != null ? p.partInfo.name : null;
            FlightEvents.Add("part_event", Coordinator.Running ? Coordinator.StepIndex : -1, new Dictionary<string, object>
            {
                ["part_id"] = (double)id,
                ["part"] = name,
                ["module"] = module,
                ["index"] = index,
                ["event"] = ev,
                ["counterparts"] = counterparts,
                ["vessel_changed"] = changed,
                ["separated"] = separated,
                ["error"] = error,
            });
            UnityEngine.Debug.Log("[KspBridge] part_event " + name + " (" + id + ") " + module + "." + ev
                + (error != null ? " failed: " + error : separated ? " separated" : ""));
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
                if (!Shown(f))
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
                UI_Control c = EditableControl(f);
                bool editable = c != null && Unlocked(FieldLock(c));
                d["editable"] = editable;
                if (editable)
                {
                    AddRange(d, f, c);
                }
                rows.Add(d);
            }
            return rows;
        }

        /// <summary>CanActivateField for an active-vessel part (design 0041 window rules).</summary>
        private static bool Shown(BaseField f) => f.guiActive && !(f.advancedTweakable && !GameSettings.ADVANCED_TWEAKABLES);

        /// <summary>
        /// The flight control a player can change the value with (design 0043 set_vessel_part_field step 5), or null; the field type
        /// must also be one PartSettings.Convert takes, so editable never promises a value set_vessel_part_field refuses.
        /// </summary>
        private static UI_Control EditableControl(BaseField f) =>
            f.uiControlFlight is UI_Control c && !(c is UI_Label) && !(c is UI_ProgressBar) && c.controlEnabled && Convertible(f.FieldInfo.FieldType) ? c : null;

        /// <summary>The types PartSettings.Convert takes.</summary>
        private static bool Convertible(Type t) =>
            t == typeof(bool) || t == typeof(string) || t.IsEnum || t == typeof(float) || t == typeof(double) || t == typeof(int);

        /// <summary>UIPartAction* value change: requireFullControl → TWEAKABLES_FULLONLY, else TWEAKABLES_ANYCONTROL.</summary>
        private static ControlTypes FieldLock(UI_Control c) => c.requireFullControl ? ControlTypes.TWEAKABLES_FULLONLY : ControlTypes.TWEAKABLES_ANYCONTROL;

        /// <summary>UIPartActionResource.Setup: the flow button exists.</summary>
        private static bool FlowToggleable(PartResource r) => r.info.resourceFlowMode != ResourceFlowMode.NO_FLOW && !r.hideFlow;

        /// <summary>The range or choices the control offers (design 0043 get_vessel_parts additions).</summary>
        private static void AddRange(Dictionary<string, object> d, BaseField f, UI_Control c)
        {
            switch (c)
            {
                case UI_FloatRange r:
                    d["min"] = r.minValue;
                    d["max"] = r.maxValue;
                    d["step"] = r.stepIncrement;
                    break;
                case UI_FloatEdit e:
                    d["min"] = e.minValue;
                    d["max"] = e.maxValue;
                    break;
                case UI_ScaleEdit s when s.intervals != null && s.intervals.Length > 0:
                    d["min"] = s.intervals[0];
                    d["max"] = s.intervals[s.intervals.Length - 1];
                    break;
                case UI_Cycle y when y.stateNames != null:
                    Options(d, f, y.stateNames, y.stateNames);
                    break;
                case UI_ChooseOption o when o.options != null:
                    Options(d, f, o.options, o.display ?? o.options);
                    break;
            }
        }

        /// <summary>options: titles the window shows; a text field also gets option_values, the values set_vessel_part_field takes.</summary>
        private static void Options(Dictionary<string, object> d, BaseField f, string[] values, string[] titles)
        {
            d["options"] = titles.Select(t => (object)KSP.Localization.Localizer.Format(t)).ToList();
            if (f.FieldInfo.FieldType == typeof(string))
            {
                d["option_values"] = values.Cast<object>().ToList();
            }
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

        /// <summary>Main states that are not window fields (design 0041 decision 2, design 0043 decision 6).</summary>
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
                case ModuleParachute c:
                    return new Dictionary<string, object> { ["deployment_state"] = c.deploymentState.ToString(), ["safe_state"] = c.deploymentSafeState.ToString() };
                case ModuleLight l:
                    return new Dictionary<string, object> { ["on"] = l.isOn };
                case ModuleWheels.ModuleWheelDeployment w:
                    return new Dictionary<string, object> { ["wheel_state"] = w.stateString, ["position"] = w.position };   // FSM state name, not a DeployState
                case ModuleEngines e:
                    return new Dictionary<string, object> { ["ignited"] = e.EngineIgnited, ["flameout"] = e.flameout, ["staged"] = e.staged, ["thrust_limit"] = e.thrustPercentage };
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
                    ["flow_toggleable"] = FlowToggleable(r),
                });
            }
            return rows;
        }

        /// <summary>Numbers, bools and strings as they are (Json writes float and int); enums by name; anything else by ToString.</summary>
        private static object FieldValue(object value) =>
            value == null || value is bool || value is string || value is double || value is float || value is int ? value : value.ToString();
    }
}
