using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// Settings a player can change on a part in the editor: tweakable resource amounts and module fields that have an
    /// editor control (slider, toggle, option list, cycle, number box). Other fields are left alone, so the ship stays one
    /// the editor itself could build. Changes go through the control's own change callback like a click would, then the
    /// ship-modified event fires; symmetry partners get the same value unless symmetry=false.
    /// </summary>
    internal static class PartSettings
    {
        private sealed class Setting
        {
            public PartModule Module;
            public int ModuleIndex;
            public BaseField Field;
            public UI_Control Control;
        }

        private static IEnumerable<Setting> Settings(Part part)
        {
            for (int i = 0; i < part.Modules.Count; i++)
            {
                PartModule module = part.Modules[i];
                foreach (BaseField field in module.Fields)
                {
                    if (field == null || !field.guiActiveEditor || field.uiControlEditor == null || field.FieldInfo == null)
                        continue;
                    UI_Control control = field.uiControlEditor;
                    if (!(control is UI_FloatRange || control is UI_Toggle || control is UI_ChooseOption || control is UI_Cycle || control is UI_FloatEdit))
                        continue;
                    if (!control.controlEnabled)
                        continue;
                    yield return new Setting { Module = module, ModuleIndex = i, Field = field, Control = control };
                }
            }
        }

        public static object Describe(Part part)
        {
            return new Dictionary<string, object>
            {
                ["id"] = (double)part.craftID,
                ["name"] = part.partInfo?.name,
                ["resources"] = part.Resources.Cast<PartResource>().Select(r => (object)new Dictionary<string, object>
                {
                    ["name"] = r.resourceName,
                    ["amount"] = PartCatalog.Round(r.amount),
                    ["max"] = PartCatalog.Round(r.maxAmount),
                    ["tweakable"] = r.isTweakable,
                }).ToList(),
                ["fields"] = Settings(part).Select(s => (object)DescribeField(s)).ToList(),
            };
        }

        private static Dictionary<string, object> DescribeField(Setting s)
        {
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["module"] = s.Module.moduleName,
                ["module_index"] = (double)s.ModuleIndex,
                ["field"] = s.Field.name,
                ["title"] = s.Field.guiName,
            };
            object value = s.Field.GetValue(s.Module);
            switch (s.Control)
            {
                case UI_FloatRange range:
                    d["type"] = "float";
                    d["value"] = ToDouble(value);
                    d["min"] = (double)range.minValue;
                    d["max"] = (double)range.maxValue;
                    d["step"] = (double)range.stepIncrement;
                    break;
                case UI_FloatEdit edit:
                    d["type"] = "float";
                    d["value"] = ToDouble(value);
                    if (!float.IsInfinity(edit.minValue))
                        d["min"] = (double)edit.minValue;
                    if (!float.IsInfinity(edit.maxValue))
                        d["max"] = (double)edit.maxValue;
                    break;
                case UI_Toggle _:
                    d["type"] = "bool";
                    d["value"] = value is bool b && b;
                    break;
                case UI_ChooseOption choose:
                    d["type"] = "option";
                    d["value"] = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                    d["options"] = (choose.options ?? new string[0]).Select(o => (object)o).ToList();
                    break;
                case UI_Cycle cycle:
                    d["type"] = "cycle";
                    d["value"] = ToDouble(value);
                    d["options"] = (cycle.stateNames ?? new string[0]).Select(o => (object)o).ToList();
                    break;
            }
            return d;
        }

        private static double ToDouble(object value) => value == null ? 0 : PartCatalog.Round(System.Convert.ToDouble(value, CultureInfo.InvariantCulture));

        /// <summary>
        /// Checks every requested change on <paramref name="part"/> first (bad_request names the first problem), then applies
        /// them to the part and, unless <paramref name="symmetry"/> is false, its partners. Returns the parts changed.
        /// </summary>
        public static List<Part> Apply(ShipConstruct ship, Part part, List<object> resources, List<object> fields, bool symmetry)
        {
            if ((resources == null || resources.Count == 0) && (fields == null || fields.Count == 0))
                throw BridgeException.BadRequest("pass resources and/or fields");
            List<Part> targets = new List<Part> { part };
            if (symmetry)
                targets.AddRange(part.symmetryCounterparts);

            List<Action<Part>> changes = new List<Action<Part>>();
            foreach (Dictionary<string, object> r in Items(resources, "resources"))
                changes.Add(ResourceChange(targets, r));
            foreach (Dictionary<string, object> f in Items(fields, "fields"))
                changes.Add(FieldChange(targets, part, f));

            foreach (Part target in targets)
            {
                foreach (Action<Part> change in changes)
                    change(target);
            }
            GameEvents.onEditorShipModified.Fire(ship);
            return targets;
        }

        private static IEnumerable<Dictionary<string, object>> Items(List<object> list, string name)
        {
            if (list == null)
                yield break;
            foreach (object item in list)
                yield return item as Dictionary<string, object> ?? throw BridgeException.BadRequest(name + " entries must be objects");
        }

        private static Action<Part> ResourceChange(List<Part> targets, Dictionary<string, object> r)
        {
            string name = r.Str("name", required: true);
            double amount = r.Num("amount", required: true).Value;
            foreach (Part t in targets)
            {
                PartResource res = t.Resources[name];
                if (res == null)
                    throw BridgeException.BadRequest("part " + t.craftID + " has no resource '" + name + "'");
                if (!res.isTweakable)
                    throw BridgeException.BadRequest("resource '" + name + "' of part " + t.craftID + " cannot be changed in the editor");
                if (double.IsNaN(amount) || amount < 0 || amount > res.maxAmount)
                    throw BridgeException.BadRequest("resource '" + name + "' amount must be 0.." + PartCatalog.Round(res.maxAmount));
            }
            return t => t.Resources[name].amount = amount;
        }

        private static Action<Part> FieldChange(List<Part> targets, Part part, Dictionary<string, object> f)
        {
            string fieldName = f.Str("field", required: true);
            double? index = f.Num("module_index");
            string moduleName = f.Str("module");
            if (!index.HasValue && moduleName == null)
                throw BridgeException.BadRequest("each field needs module or module_index");
            if (!f.TryGetValue("value", out object value) || value == null)
                throw BridgeException.BadRequest("field '" + fieldName + "' needs a value");

            Setting Find(Part p)
            {
                List<Setting> all = Settings(p).Where(s => s.Field.name == fieldName).ToList();
                List<Setting> match = index.HasValue
                    ? all.Where(s => s.ModuleIndex == (int)index.Value).ToList()
                    : all.Where(s => s.Module.moduleName == moduleName).ToList();
                if (match.Count == 0)
                    throw BridgeException.BadRequest("part " + p.craftID + " has no editor setting '" + fieldName + "' in "
                        + (index.HasValue ? "module " + index.Value : moduleName) + "; see editor_get_part_settings");
                if (match.Count > 1)
                    throw BridgeException.BadRequest("several " + moduleName + " modules have '" + fieldName + "'; pass module_index");
                return match[0];
            }

            object converted = null;
            foreach (Part t in targets)
                converted = Coerce(Find(t), value);
            object final = converted;
            return t =>
            {
                Setting s = Find(t);
                object old = s.Field.GetValue(s.Module);
                s.Field.SetValue(final, s.Module);
                s.Control.onFieldChanged?.Invoke(s.Field, old);
            };
        }

        /// <summary>The value in the field's own type after checking the control's range or options.</summary>
        private static object Coerce(Setting s, object value)
        {
            Type type = s.Field.FieldInfo.FieldType;
            string what = "'" + s.Field.name + "'";
            switch (s.Control)
            {
                case UI_Toggle _:
                    if (!(value is bool b))
                        throw BridgeException.BadRequest(what + " needs true or false");
                    return b;
                case UI_ChooseOption choose:
                    string text = value as string ?? throw BridgeException.BadRequest(what + " needs one of its options (a string)");
                    if (choose.options == null || !choose.options.Contains(text))
                        throw BridgeException.BadRequest(what + " must be one of: " + string.Join(", ", choose.options ?? new string[0]));
                    return text;
                case UI_Cycle cycle:
                    if (!(value is double c) || c != Math.Floor(c) || c < 0 || c >= (cycle.stateNames?.Length ?? 0))
                        throw BridgeException.BadRequest(what + " needs an index 0.." + ((cycle.stateNames?.Length ?? 1) - 1));
                    return ToFieldType((int)c, type);
                case UI_FloatRange range:
                    return Number(value, range.minValue, range.maxValue, type, what);
                case UI_FloatEdit edit:
                    return Number(value, edit.minValue, edit.maxValue, type, what);
            }
            throw BridgeException.BadRequest(what + " cannot be changed");
        }

        private static object Number(object value, float min, float max, Type type, string what)
        {
            if (!(value is double d) || double.IsNaN(d))
                throw BridgeException.BadRequest(what + " needs a number");
            if (d < min - 1e-6 || d > max + 1e-6)
                throw BridgeException.BadRequest(what + " must be " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture));
            return ToFieldType(d, type);
        }

        private static object ToFieldType(double d, Type type)
        {
            if (type == typeof(float))
                return (float)d;
            if (type == typeof(int))
                return (int)Math.Round(d);
            if (type == typeof(double))
                return d;
            return System.Convert.ChangeType(d, type, CultureInfo.InvariantCulture);
        }
    }
}
