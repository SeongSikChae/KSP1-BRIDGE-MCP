using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace KspBridge.Handlers
{
    public static class PartsHandler
    {
        /// <summary>GET /api/parts?category=Engine&amp;unlocked=true</summary>
        public static object List(NameValueCollection query)
        {
            if (PartLoader.Instance == null || PartLoader.LoadedPartsList == null)
            {
                throw new ApiException(409, "scene_not_allowed", "parts are not loaded yet");
            }

            string category = query["category"];
            bool unlockedOnly = !string.Equals(query["unlocked"], "false", StringComparison.OrdinalIgnoreCase);
            bool detail = string.Equals(query["detail"], "true", StringComparison.OrdinalIgnoreCase);
            string nameFilter = query["name"];

            List<object> parts = new List<object>();
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap.partPrefab == null || ap.category == PartCategories.none)
                {
                    continue;
                }
                if (category != null && !string.Equals(ap.category.ToString(), category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool unlocked = IsUnlocked(ap);
                if (unlockedOnly && !unlocked)
                {
                    continue;
                }
                if (nameFilter != null && ap.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0
                    && ap.title.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                parts.Add(detail ? Describe(ap, unlocked) : Summary(ap, unlocked));
            }
            return new Dictionary<string, object> { ["count"] = parts.Count, ["parts"] = parts };
        }

        public static bool IsUnlocked(AvailablePart ap)
        {
            return !Career.IsCareer || ResearchAndDevelopment.PartModelPurchased(ap);
        }

        private static Dictionary<string, object> Summary(AvailablePart ap, bool unlocked)
        {
            return new Dictionary<string, object>
            {
                ["name"] = ap.name,
                ["title"] = ap.title,
                ["category"] = ap.category.ToString(),
                ["cost"] = (double)ap.cost,
                ["mass"] = (double)ap.partPrefab.mass,
                ["size"] = ap.bulkheadProfiles,
                ["unlocked"] = unlocked,
            };
        }

        private static Dictionary<string, object> Describe(AvailablePart ap, bool unlocked)
        {
            Part p = ap.partPrefab;

            List<object> nodes = new List<object>();
            foreach (AttachNode n in p.attachNodes)
            {
                nodes.Add(n.id);
            }

            List<object> resources = new List<object>();
            for (int i = 0; i < p.Resources.Count; i++)
            {
                PartResource r = p.Resources[i];
                resources.Add(new Dictionary<string, object> { ["name"] = r.resourceName, ["max"] = r.maxAmount });
            }

            ModuleEngines engine = p.FindModuleImplementing<ModuleEngines>();
            bool decoupler = p.FindModuleImplementing<ModuleDecouple>() != null
                || p.FindModuleImplementing<ModuleAnchoredDecoupler>() != null;

            return new Dictionary<string, object>
            {
                ["name"] = ap.name,
                ["title"] = ap.title,
                ["category"] = ap.category.ToString(),
                ["cost"] = (double)ap.cost,
                ["mass"] = (double)p.mass,
                ["size"] = ap.bulkheadProfiles,
                ["tech"] = ap.TechRequired,
                ["unlocked"] = unlocked,
                ["crew"] = p.CrewCapacity,
                ["nodes"] = nodes,
                ["surface_attach"] = p.srfAttachNode != null && p.attachRules != null && p.attachRules.srfAttach,
                ["resources"] = resources,
                ["engine"] = engine == null ? null : new Dictionary<string, object>
                {
                    ["thrust_vac"] = (double)engine.maxThrust,
                    ["isp_vac"] = (double)engine.atmosphereCurve.Evaluate(0f),
                    ["isp_asl"] = (double)engine.atmosphereCurve.Evaluate(1f),
                    ["propellants"] = Propellants(engine),
                },
                ["decoupler"] = decoupler,
                ["parachute"] = p.FindModuleImplementing<ModuleParachute>() != null,
            };
        }

        private static List<object> Propellants(ModuleEngines engine)
        {
            List<object> list = new List<object>();
            foreach (Propellant pr in engine.propellants)
            {
                list.Add(pr.name);
            }
            return list;
        }
    }
}
