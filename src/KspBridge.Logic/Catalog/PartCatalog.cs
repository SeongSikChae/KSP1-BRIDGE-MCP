using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Catalog
{
    /// <summary>Part catalog (list_parts, get_part). Works in any scene with a loaded game. Main thread only.</summary>
    public static class PartCatalog
    {
        public static object List(string category, string q, bool unlockedOnly)
        {
            RequireGame();
            IEnumerable<AvailablePart> parts = PartLoader.LoadedPartsList.Where(p => p.category != PartCategories.none && p.partPrefab != null);
            if (unlockedOnly)
            {
                parts = parts.Where(IsUsable);
            }
            if (category != null)
            {
                parts = parts.Where(p => string.Equals(p.category.ToString(), category, StringComparison.OrdinalIgnoreCase));
            }
            if (q != null)
            {
                parts = parts.Where(p => p.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (p.title != null && p.title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            return parts.OrderBy(p => p.category.ToString()).ThenBy(p => p.name).Select(p => (object)Summary(p)).ToList();
        }

        public static object Get(string name)
        {
            RequireGame();
            AvailablePart ap = Find(name);
            Part prefab = ap.partPrefab;
            Dictionary<string, object> d = Summary(ap);
            d["nodes"] = prefab.attachNodes.Select(n => (object)new Dictionary<string, object>
            {
                ["id"] = n.id,
                ["size"] = n.size,
                ["position"] = Format.Vec(n.position),
                ["orientation"] = Format.Vec(n.orientation),
            }).ToList();
            d["allow_stack"] = prefab.attachRules.allowStack;
            d["surface_attach"] = prefab.attachRules.srfAttach;
            d["allow_surface_children"] = prefab.attachRules.allowSrfAttach;
            d["resources"] = prefab.Resources.Cast<PartResource>().Select(r => (object)new Dictionary<string, object>
            {
                ["name"] = r.resourceName,
                ["amount"] = r.amount,
                ["max"] = r.maxAmount,
            }).ToList();
            d["fields"] = EditorFields(prefab);
            return d;
        }

        /// <summary>The part by exact name, or 404 part_not_found.</summary>
        public static AvailablePart Find(string name) =>
            PartLoader.getPartInfoByName(name)
            ?? throw new ApiException(404, "part_not_found", "no part named '" + name + "' (use list_parts for names)");

        /// <summary>The part by exact name if this save may use it; 404 part_not_found or 409 part_locked.</summary>
        public static AvailablePart RequireUsable(string name)
        {
            AvailablePart ap = Find(name);
            if (!IsUsable(ap))
            {
                throw new ApiException(409, "part_locked", ap.name + " is not unlocked or purchased in this save");
            }
            return ap;
        }

        /// <summary>Unlocked and purchased in career/science; always true in sandbox.</summary>
        public static bool IsUsable(AvailablePart ap)
        {
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
            {
                return true;
            }
            return ResearchAndDevelopment.PartTechAvailable(ap) && ResearchAndDevelopment.PartModelPurchased(ap);
        }

        private static Dictionary<string, object> Summary(AvailablePart ap) => new Dictionary<string, object>
        {
            ["name"] = ap.name,
            ["title"] = ap.title,
            ["category"] = ap.category.ToString(),
            ["cost"] = ap.cost,
            ["mass"] = Format.Round(ap.partPrefab.mass),
            ["size"] = ap.bulkheadProfiles,
            ["unlocked"] = IsUsable(ap),
        };

        /// <summary>Fields shown in the editor part action window: the ones set_part_field may change.</summary>
        private static List<object> EditorFields(Part prefab)
        {
            List<object> fields = new List<object>();
            foreach (PartModule m in prefab.Modules)
            {
                foreach (BaseField f in m.Fields)
                {
                    if (!f.guiActiveEditor)
                    {
                        continue;
                    }
                    Dictionary<string, object> d = new Dictionary<string, object>
                    {
                        ["module"] = m.moduleName,
                        ["field"] = f.name,
                        ["title"] = f.guiName,
                        ["value"] = f.GetValue(m),
                        ["units"] = f.guiUnits,
                    };
                    if (f.uiControlEditor is UI_FloatRange range)
                    {
                        d["min"] = range.minValue;
                        d["max"] = range.maxValue;
                        d["step"] = range.stepIncrement;
                    }
                    fields.Add(d);
                }
            }
            return fields;
        }

        private static void RequireGame()
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "no_game", "load a save first");
            }
        }
    }
}
