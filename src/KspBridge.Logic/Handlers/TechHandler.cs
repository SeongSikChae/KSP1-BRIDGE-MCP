using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FinePrint;

namespace KspBridge.Handlers
{
    /// <summary>Tech tree query and research outside the R&amp;D screen (same checks as RDTech.ResearchTech).</summary>
    public static class TechHandler
    {
        /// <summary>GET /api/tech?all=true</summary>
        public static object List(NameValueCollection query)
        {
            Career.Require();
            bool all = string.Equals(query["all"], "true", StringComparison.OrdinalIgnoreCase);

            List<object> nodes = new List<object>();
            foreach (ProtoRDNode n in Nodes())
            {
                string id = n.tech.techID;
                string state = Researched(id) ? "researched" : ParentsMet(n) ? "researchable" : "locked";
                if (!all && state == "locked")
                {
                    continue;
                }
                Dictionary<string, object> item = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["title"] = ResearchAndDevelopment.GetTechnologyTitle(id),
                    ["cost"] = n.tech.scienceCost,
                    ["state"] = state,
                    ["parents"] = n.parents.Select(p => (object)p.tech.techID).ToList(),
                    ["any_parent"] = n.AnyParentToUnlock,
                };
                List<AvailablePart> parts = PartsOf(id);
                if (all)
                {
                    item["part_count"] = parts.Count;
                }
                else
                {
                    item["parts"] = parts.Select(Describe).ToList();
                }
                nodes.Add(item);
            }

            return new Dictionary<string, object>
            {
                ["science"] = (double)ResearchAndDevelopment.Instance.Science,
                ["cost_limit"] = (double)CostLimit(),
                ["nodes"] = nodes,
            };
        }

        /// <summary>POST /api/tech/{id}/research</summary>
        public static object Research(string id)
        {
            Career.Require();
            ProtoRDNode n = Nodes().FirstOrDefault(x => x.tech.techID == id);
            if (n == null)
            {
                throw ApiException.NotFound("tech node '" + id + "' not found");
            }
            if (Researched(id))
            {
                throw new ApiException(409, "already_researched", "'" + id + "' is already researched");
            }
            if (!ParentsMet(n))
            {
                string missing = string.Join(", ", n.parents.Where(p => !Researched(p.tech.techID)).Select(p => p.tech.techID));
                throw new ApiException(409, "prerequisites_missing",
                    (n.AnyParentToUnlock ? "research any of: " : "research first: ") + missing);
            }
            int cost = n.tech.scienceCost;
            float limit = CostLimit();
            if (cost > limit)
            {
                throw new ApiException(409, "cost_limit", "cost " + cost + " exceeds the R&D facility limit " + limit + " (upgrade R&D)");
            }
            ResearchAndDevelopment rnd = ResearchAndDevelopment.Instance;
            // Same affordability check as RDTech.ResearchTech (includes strategy modifiers).
            if (!CurrencyModifierQuery.RunQuery(TransactionReasons.RnDTechResearch, 0f, -cost, 0f).CanAfford())
            {
                throw new ApiException(409, "not_enough_science", "needs " + cost + " science, have " + rnd.Science);
            }

            rnd.AddScience(-cost, TransactionReasons.RnDTechResearch);
            rnd.UnlockProtoTechNode(n.tech);
            ResearchAndDevelopment.RefreshTechTreeUI();
            RefreshContractDefs();

            return new Dictionary<string, object>
            {
                ["researched"] = id,
                ["title"] = ResearchAndDevelopment.GetTechnologyTitle(id),
                ["cost"] = cost,
                ["science"] = (double)rnd.Science,
                ["parts_unlocked"] = PartsOf(id).Where(ResearchAndDevelopment.PartModelPurchased).Select(Describe).ToList(),
            };
        }

        private static ProtoRDNode[] Nodes()
        {
            ProtoRDNode[] nodes = AssetBase.RnDTechTree != null ? AssetBase.RnDTechTree.GetTreeNodes() : null;
            if (nodes == null || ResearchAndDevelopment.Instance == null)
            {
                throw new ApiException(503, "not_ready", "tech tree is not loaded yet");
            }
            return nodes;
        }

        /// <summary>Same as stock CheatTechnology: ContractDefs.TechResearched is internal, so call it by reflection.</summary>
        private static void RefreshContractDefs()
        {
            if (ContractDefs.Instance == null)
            {
                return;
            }
            System.Reflection.MethodInfo m = typeof(ContractDefs).GetMethod("TechResearched",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (m != null)
            {
                m.Invoke(ContractDefs.Instance, new object[] { default(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult>) });
            }
        }

        private static float CostLimit()
            => GameVariables.Instance.GetScienceCostLimit(
                ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment));

        private static bool Researched(string id)
            => ResearchAndDevelopment.GetTechnologyState(id) == RDTech.State.Available;

        private static bool ParentsMet(ProtoRDNode n)
        {
            if (n.parents.Count == 0)
            {
                return true;
            }
            return n.AnyParentToUnlock
                ? n.parents.Any(p => Researched(p.tech.techID))
                : n.parents.All(p => Researched(p.tech.techID));
        }

        private static List<AvailablePart> PartsOf(string id)
            => PartLoader.LoadedPartsList.Where(ap => ap.TechRequired == id && ap.category != PartCategories.none).ToList();

        private static object Describe(AvailablePart ap)
            => new Dictionary<string, object> { ["name"] = ap.name, ["title"] = ap.title };
    }
}
