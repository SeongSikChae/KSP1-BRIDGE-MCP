using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Editor
{
    /// <summary>set_part_field and set_resource: what the editor part action window does when a value is changed. Main thread only.</summary>
    public static class PartSettings
    {
        /// <summary>
        /// UIPartActionFieldItem.SetFieldValue: symmetry counterparts get the value first (module at the same index when it has the
        /// same type, else the first module of that class) with onSymmetryFieldChanged, then the part itself with onFieldChanged
        /// (old value), then the editor refresh and Undo step unless the control suppresses ship-modified events.
        /// Nothing happens when every part already has the value. With several modules of the same name, the first enabled one
        /// that shows the field is used.
        /// </summary>
        public static object SetField(uint id, string module, string fieldName, object raw)
        {
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            List<PartModule> named = p.Modules.Cast<PartModule>().Where(x => x.moduleName == module).ToList();
            if (named.Count == 0)
            {
                throw new ApiException(404, "module_not_found", module + " is not on " + p.partInfo.name
                    + " (modules: " + string.Join(", ", p.Modules.Cast<PartModule>().Select(x => x.moduleName).Distinct().ToArray()) + ")");
            }
            List<PartModule> showing = named.Where(x => x.Fields[fieldName] != null && x.Fields[fieldName].guiActiveEditor).ToList();
            if (showing.Count == 0)
            {
                throw new ApiException(404, "field_not_found", fieldName + " is not an editor field of " + module + " (use get_part for fields)");
            }
            PartModule m = showing.FirstOrDefault(x => x.isEnabled)
                ?? throw new ApiException(409, "module_disabled", module + " on " + p.partInfo.name + " is disabled, so the editor does not show " + fieldName);
            BaseField field = m.Fields[fieldName];
            UI_Control control = field.uiControlEditor;
            if (control == null || control is UI_Label || control is UI_ProgressBar || !control.controlEnabled)
            {
                throw ApiException.BadRequest(module + "." + fieldName + " is shown in the editor but cannot be changed");
            }
            object value = Convert(field, raw);
            CheckRange(field, control, value);

            object old = field.GetValue(field.host);
            bool changed = !Equals(old, value);
            int index = p.Modules.IndexOf(m);
            if ((control.affectSymCounterparts & UI_Scene.Editor) != UI_Scene.None)
            {
                foreach (Part s in Counterparts(p))
                {
                    PartModule sm = CounterpartModule(s, index, m);
                    if (sm == null)
                    {
                        continue;
                    }
                    changed |= !Equals(sm.Fields.GetValue(field.name), value);
                    sm.Fields.SetValue(field.name, value);
                    // UIPartActionFieldItem.FireSymmetryEvents: the counterpart's editor and flight callbacks, with the source field.
                    BaseField sf = sm.Fields[field.name];
                    sf?.uiControlEditor?.onSymmetryFieldChanged?.Invoke(field, value);
                    sf?.uiControlFlight?.onSymmetryFieldChanged?.Invoke(field, value);
                }
            }
            if (changed)
            {
                field.SetValue(value, field.host);
                control.onFieldChanged?.Invoke(field, old);
                if (!control.suppressEditorShipModified)
                {
                    EditorScene.Commit(ship);
                }
            }
            List<Part> group = PartPlacer.SymmetryGroup(p);
            RefreshWindows(group);
            return WithValues(EditResult.Changed(ship, group), group.Select(s =>
            {
                PartModule sm = s == p ? m : CounterpartModule(s, index, m);
                return (object)new Dictionary<string, object> { ["id"] = s.persistentId, ["value"] = sm?.Fields.GetValue(field.name) };
            }));
        }

        private static IEnumerable<Part> Counterparts(Part p) => (p.symmetryCounterparts ?? new List<Part>()).Where(s => s != null);

        /// <summary>UIPartActionFieldItem.SetSymCounterpartValue: the module at the same index when it has the same type, else the first of that class.</summary>
        private static PartModule CounterpartModule(Part s, int index, PartModule m) =>
            index >= 0 && index < s.Modules.Count && s.Modules[index] != null && s.Modules[index].GetType() == m.GetType()
                ? s.Modules[index]
                : s.Modules[m.ClassName];

        /// <summary>
        /// Redraws the open part action windows of the parts. The editor changes values from inside the window itself; a tool
        /// changes them from outside, and window items such as toggles only read their value when the window is built.
        /// </summary>
        private static void RefreshWindows(IEnumerable<Part> parts)
        {
            foreach (Part s in parts)
            {
                MonoUtilities.RefreshPartContextWindow(s);
            }
        }

        /// <summary>An edit result plus the values read back from each part after the change.</summary>
        private static object WithValues(object result, IEnumerable<object> values)
        {
            Dictionary<string, object> d = (Dictionary<string, object>)result;
            d["values"] = values.ToList();
            return d;
        }

        /// <summary>
        /// UIPartActionResourceEditor: the part's resource set to fraction of its maximum, and symmetry counterparts to the same amount.
        /// </summary>
        public static object SetResource(uint id, string resource, double fraction)
        {
            if (double.IsNaN(fraction) || fraction < 0 || fraction > 1)
            {
                throw ApiException.BadRequest("fraction must be 0..1");
            }
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            PartResource r = p.Resources[resource]
                ?? throw new ApiException(404, "resource_not_found", resource + " is not in " + p.partInfo.name
                    + " (resources: " + string.Join(", ", p.Resources.Cast<PartResource>().Select(x => x.resourceName).ToArray()) + ")");
            if (!r.isTweakable)
            {
                throw ApiException.BadRequest(resource + " in " + p.partInfo.name + " cannot be adjusted in the editor");
            }
            List<Part> counterparts = Counterparts(p).ToList();
            double amount = fraction * r.maxAmount;
            // Check before changing anything, so a counterpart without the resource leaves every part unchanged.
            foreach (Part s in counterparts)
            {
                if (s.Resources[resource] == null)
                {
                    throw new ApiException(404, "resource_not_found", resource + " is not in symmetry counterpart " + s.persistentId);
                }
            }
            r.amount = amount;
            foreach (Part s in counterparts)
            {
                s.Resources[resource].amount = amount;
            }
            EditorScene.Commit(ship);
            List<Part> group = PartPlacer.SymmetryGroup(p);
            RefreshWindows(group);
            return WithValues(EditResult.Changed(ship, group), group.Select(s => (object)new Dictionary<string, object>
            {
                ["id"] = s.persistentId,
                ["amount"] = Math.Round(s.Resources[resource].amount, 3),
                ["max"] = Math.Round(s.Resources[resource].maxAmount, 3),
            }));
        }

        /// <summary>A JSON value (number, bool or string) as the field's own type, or 400.</summary>
        private static object Convert(BaseField field, object raw)
        {
            Type t = field.FieldInfo.FieldType;
            string name = field.name;
            if (t == typeof(bool))
            {
                if (raw is bool b)
                {
                    return b;
                }
            }
            else if (t == typeof(string))
            {
                if (raw is string s)
                {
                    return s;
                }
            }
            else if (t.IsEnum)
            {
                if (raw is string s && Enum.GetNames(t).Any(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase)))
                {
                    return Enum.Parse(t, s, true);
                }
                if (raw is double d)
                {
                    // Compare numerically so enums of any underlying type work and out-of-range numbers are rejected.
                    object match = Enum.GetValues(t).Cast<object>()
                        .FirstOrDefault(e => System.Convert.ToDouble(e, CultureInfo.InvariantCulture) == d);
                    if (match != null)
                    {
                        return match;
                    }
                }
                throw ApiException.BadRequest(name + " must be one of " + string.Join(", ", Enum.GetNames(t)));
            }
            else if (t == typeof(float) || t == typeof(double) || t == typeof(int))
            {
                if (raw is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                {
                    if (t == typeof(int))
                    {
                        if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
                        {
                            throw ApiException.BadRequest(name + " must be a whole number");
                        }
                        return (int)d;
                    }
                    if (t == typeof(float))
                    {
                        if (Math.Abs(d) > float.MaxValue)
                        {
                            throw ApiException.BadRequest(name + " is out of the float range");
                        }
                        return (float)d;
                    }
                    return d;
                }
            }
            else
            {
                throw ApiException.BadRequest(name + " has type " + t.Name + ", which set_part_field does not support");
            }
            throw ApiException.BadRequest(name + " needs " + (t == typeof(bool) ? "true or false" : t == typeof(string) ? "a text value" : "a number"));
        }

        /// <summary>
        /// The limits the placed part's editor control offers: slider or number box range, scale intervals, or the option /
        /// state list (by name, or by index for an int field).
        /// </summary>
        private static void CheckRange(BaseField field, UI_Control control, object value)
        {
            switch (control)
            {
                case UI_FloatRange range:
                    Between(field, value, range.minValue, range.maxValue);
                    break;
                case UI_FloatEdit edit:
                    Between(field, value, edit.minValue, edit.maxValue);
                    break;
                case UI_ScaleEdit scale when scale.intervals != null && scale.intervals.Length > 0:
                    Between(field, value, scale.intervals[0], scale.intervals[scale.intervals.Length - 1]);
                    break;
                case UI_ChooseOption choose when choose.options != null:
                    InList(field, value, choose.options);
                    break;
                case UI_Cycle cycle when cycle.stateNames != null:
                    InList(field, value, cycle.stateNames);
                    break;
            }
        }

        private static void Between(BaseField field, object value, float min, float max)
        {
            if (!(value is float || value is double || value is int))
            {
                return;
            }
            double v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (v < min || v > max)
            {
                throw ApiException.BadRequest(field.name + " must be " + min + ".." + max);
            }
        }

        private static void InList(BaseField field, object value, string[] names)
        {
            if (value is string s && !names.Contains(s))
            {
                throw ApiException.BadRequest(field.name + " must be one of " + string.Join(", ", names));
            }
            if (value is int i && (i < 0 || i >= names.Length))
            {
                throw ApiException.BadRequest(field.name + " must be an index 0.." + (names.Length - 1) + " (" + string.Join(", ", names) + ")");
            }
        }
    }
}
