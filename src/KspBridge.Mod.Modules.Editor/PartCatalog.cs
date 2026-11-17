using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>Parts the current game can build with, with the numbers a Planner needs to pick them.</summary>
    internal static class PartCatalog
    {
        public static object List(Dictionary<string, object> args, Action requireGame)
        {
            requireGame();
            string category = args.Str("category");
            string search = args.Str("search");

            List<AvailablePart> parts = Available()
                .Where(p => EditorQuery.MatchesPart(p.name, p.title, p.category.ToString(), category, search))
                .OrderBy(p => p.category.ToString(), StringComparer.Ordinal)
                .ThenBy(p => p.name, StringComparer.Ordinal)
                .ToList();
            Page<AvailablePart> page = EditorQuery.Paginate(parts, args.Num("limit"), args.Num("offset"));

            return new Dictionary<string, object>
            {
                ["total"] = page.Total,
                ["offset"] = page.Offset,
                ["parts"] = page.Items.Select(p => (object)Describe(p)).ToList(),
            };
        }

        /// <summary>
        /// Loaded parts minus hidden ones (category none: EVA kerbals, flags), tech-hidden ones, and in career/science
        /// games the ones whose tech node is not researched. Purchase state is not checked (out of scope).
        /// </summary>
        private static IEnumerable<AvailablePart> Available()
        {
            Game.Modes mode = HighLogic.CurrentGame.Mode;
            bool research = (mode == Game.Modes.CAREER || mode == Game.Modes.SCIENCE_SANDBOX) && ResearchAndDevelopment.Instance != null;
            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                if (part == null || part.partPrefab == null || part.category == PartCategories.none || part.TechHidden)
                    continue;
                if (research && !ResearchAndDevelopment.PartTechAvailable(part))
                    continue;
                yield return part;
            }
        }

        private static Dictionary<string, object> Describe(AvailablePart info)
        {
            Part prefab = info.partPrefab;
            return new Dictionary<string, object>
            {
                ["name"] = info.name,
                ["title"] = info.title,
                ["category"] = info.category.ToString(),
                ["sizes"] = (info.bulkheadProfiles ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => (object)s.Trim()).ToList(),
                ["dry_mass_t"] = Round(prefab.mass),
                ["cost"] = Round(info.cost),
                ["resources"] = prefab.Resources.Cast<PartResource>()
                    .Select(r => (object)new Dictionary<string, object> { ["name"] = r.resourceName, ["max"] = Round(r.maxAmount) })
                    .ToList(),
                ["engine"] = Engine(prefab.FindModuleImplementing<ModuleEngines>()),
                ["modules"] = prefab.Modules.Cast<PartModule>().Select(m => (object)m.moduleName).ToList(),
            };
        }

        /// <summary>Thrust and Isp in vacuum and at sea level (Kerbin, 1 atm). Sea-level thrust scales with Isp at fixed fuel flow.</summary>
        private static Dictionary<string, object> Engine(ModuleEngines engine)
        {
            if (engine == null)
                return null;
            double ispVac = engine.atmosphereCurve.Evaluate(0f);
            double ispAsl = engine.atmosphereCurve.Evaluate(1f);
            double thrustVac = engine.maxThrust;
            double thrustAsl = ispVac > 0 ? thrustVac * ispAsl / ispVac : 0;
            return new Dictionary<string, object>
            {
                ["thrust_vac_kn"] = Round(thrustVac),
                ["thrust_asl_kn"] = Round(thrustAsl),
                ["isp_vac_s"] = Round(ispVac),
                ["isp_asl_s"] = Round(ispAsl),
                ["propellants"] = engine.propellants
                    .Select(p => (object)new Dictionary<string, object> { ["name"] = p.name, ["ratio"] = Round(p.ratio) })
                    .ToList(),
            };
        }

        internal static double Round(double value) => Math.Round(value, 3);
    }
}
