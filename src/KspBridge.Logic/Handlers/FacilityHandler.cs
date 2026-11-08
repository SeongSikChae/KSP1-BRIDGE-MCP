using System;
using System.Collections.Generic;
using System.Linq;
using Upgradeables;
using UnityEngine;

namespace KspBridge.Handlers
{
    /// <summary>Facility levels, upgrade costs/effects, and upgrades (same checks as SpaceCenterBuilding.UpgradeFacility).</summary>
    public static class FacilityHandler
    {
        /// <summary>GET /api/facilities</summary>
        public static object List()
        {
            Career.Require();
            bool inKsc = HighLogic.LoadedScene == GameScenes.SPACECENTER;
            List<object> list = new List<object>();
            foreach (SpaceCenterFacility f in CareerHandler.Facilities)
            {
                int max = MaxLevel(f);
                int level = Level(f, max);
                UpgradeableFacility obj = inKsc ? Instances(f).FirstOrDefault() : null;
                double? cost = obj != null && level < max ? (double?)obj.GetUpgradeCost() : null;
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = CareerHandler.Snake(f.ToString()),
                    ["level"] = level,
                    ["max_level"] = max,
                    ["upgrade_cost"] = cost,
                    ["can_afford"] = cost.HasValue ? (object)CanAfford(cost.Value) : null,
                    ["effects"] = new Dictionary<string, object>
                    {
                        ["current"] = Effects(f, (float)level / max),
                        ["next"] = level < max ? Effects(f, (float)(level + 1) / max) : null,
                    },
                });
            }
            return new Dictionary<string, object>
            {
                ["funds"] = Funding.Instance != null ? Funding.Instance.Funds : 0,
                ["upgrade_costs_available"] = inKsc,
                ["facilities"] = list,
            };
        }

        /// <summary>POST /api/facilities/{id}/upgrade</summary>
        public static object Upgrade(string id)
        {
            Career.Require();
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                throw ApiException.SceneNotAllowed("facility upgrade (KSC scene only)");
            }
            SpaceCenterFacility f = CareerHandler.Facilities.FirstOrDefault(x => CareerHandler.Snake(x.ToString()) == id);
            if (CareerHandler.Snake(f.ToString()) != id)
            {
                throw ApiException.NotFound("unknown facility '" + id + "'. Valid: " + string.Join(", ", CareerHandler.Facilities.Select(x => CareerHandler.Snake(x.ToString()))));
            }
            List<UpgradeableFacility> objs = Instances(f);
            if (objs.Count == 0)
            {
                throw new ApiException(503, "not_ready", "facility '" + id + "' is not loaded yet");
            }
            int max = MaxLevel(f);
            int before = Level(f, max);
            if (before >= max)
            {
                throw new ApiException(409, "max_level", "'" + id + "' is already at the highest level");
            }
            if (f == SpaceCenterFacility.LaunchPad || f == SpaceCenterFacility.Runway)
            {
                string site = f == SpaceCenterFacility.LaunchPad ? "LaunchPad" : "Runway";
                if (ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, site).Count > 0)
                {
                    throw new ApiException(409, "site_occupied", "a vessel is on the " + site + " (recover it first)");
                }
            }
            // Stock WarnOfObstructingVessels(includeGrounds: true): vessels landed on the buildings or their grounds.
            List<SpaceCenterBuilding> buildings = UnityEngine.Object.FindObjectsOfType<SpaceCenterBuilding>()
                .Where(b => b.Facility != null && objs.Contains(b.Facility))
                .ToList();
            FlightState st = HighLogic.CurrentGame.flightState;
            List<string> obstructing = buildings
                .SelectMany(b => b.FindVesselsAtFacility(st, b.destructibles.Where(d => d != null))
                    .Concat(b.FindVesselsAtGrounds(st, b.transform.parent)))
                .Select(p => p.vesselName)
                .Distinct()
                .ToList();
            if (obstructing.Count > 0)
            {
                throw new ApiException(409, "site_occupied", "vessel(s) on the " + id + " site: " + string.Join(", ", obstructing) + " (recover them first)");
            }
            float cost = objs[0].GetUpgradeCost();
            if (!CanAfford(cost))
            {
                throw new ApiException(409, "not_enough_funds", "upgrade costs " + cost.ToString("F0") + ", funds " + Funding.Instance.Funds.ToString("F0"));
            }

            Funding.Instance.AddFunds(-Math.Abs(cost), TransactionReasons.StructureConstruction);
            foreach (SpaceCenterBuilding b in buildings) // stock ResetStructures: repair damaged structures first
            {
                foreach (DestructibleBuilding d in b.destructibles.Where(d => d != null))
                {
                    d.Reset();
                }
            }
            foreach (UpgradeableFacility o in objs)
            {
                o.SetLevel(before + 1);
            }
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["level_before"] = before,
                ["level_after"] = before + 1,
                ["cost"] = (double)cost,
                ["funds"] = Funding.Instance.Funds,
            };
        }

        private static int MaxLevel(SpaceCenterFacility f)
        {
            int max = ScenarioUpgradeableFacilities.GetFacilityLevelCount(f);
            return max < 2 ? 2 : max; // reads low outside the KSC scene (see CareerHandler)
        }

        private static int Level(SpaceCenterFacility f, int max)
            => (int)Math.Round(ScenarioUpgradeableFacilities.GetFacilityLevel(f) * max);

        private static List<UpgradeableFacility> Instances(SpaceCenterFacility f)
        {
            string suffix = "/" + f;
            return UnityEngine.Object.FindObjectsOfType<UpgradeableFacility>()
                .Where(o => o.id != null && o.id.EndsWith(suffix, StringComparison.Ordinal))
                .ToList();
        }

        private static bool CanAfford(double cost)
            => CurrencyModifierQuery.RunQuery(TransactionReasons.StructureConstruction, -(float)Math.Abs(cost), 0f, 0f).CanAfford();

        private static Dictionary<string, object> Effects(SpaceCenterFacility f, float norm)
        {
            GameVariables gv = GameVariables.Instance;
            switch (f)
            {
                case SpaceCenterFacility.LaunchPad:
                case SpaceCenterFacility.Runway:
                    bool pad = f == SpaceCenterFacility.LaunchPad;
                    Vector3 size = gv.GetCraftSizeLimit(norm, pad);
                    return new Dictionary<string, object>
                    {
                        ["max_mass_t"] = Finite(gv.GetCraftMassLimit(norm, pad)),
                        ["max_size_m"] = new List<object> { Finite(size.x), Finite(size.y), Finite(size.z) },
                    };
                case SpaceCenterFacility.VehicleAssemblyBuilding:
                case SpaceCenterFacility.SpaceplaneHangar:
                    bool vab = f == SpaceCenterFacility.VehicleAssemblyBuilding;
                    int parts = gv.GetPartCountLimit(norm, vab);
                    return new Dictionary<string, object>
                    {
                        ["max_parts"] = parts == int.MaxValue ? null : (object)parts,
                        ["action_groups"] = gv.UnlockedActionGroupsStock(norm, vab),
                        ["custom_action_groups"] = gv.UnlockedActionGroupsCustom(norm, vab),
                    };
                case SpaceCenterFacility.ResearchAndDevelopment:
                    return new Dictionary<string, object> { ["science_cost_limit"] = Finite(gv.GetScienceCostLimit(norm)) };
                case SpaceCenterFacility.MissionControl:
                    return new Dictionary<string, object>
                    {
                        ["active_contracts"] = Limit(gv.GetActiveContractsLimit(norm)),
                        ["flight_planning"] = gv.UnlockedFlightPlanning(norm),
                    };
                case SpaceCenterFacility.TrackingStation:
                    return new Dictionary<string, object>
                    {
                        ["patches_ahead"] = Limit(gv.GetPatchesAheadLimit(norm)),
                        ["tracked_objects"] = Limit(gv.GetTrackedObjectLimit(norm)),
                    };
                case SpaceCenterFacility.AstronautComplex:
                    return new Dictionary<string, object>
                    {
                        ["active_crew"] = Limit(gv.GetActiveCrewLimit(norm)),
                        ["eva"] = gv.UnlockedEVA(norm),
                    };
                case SpaceCenterFacility.Administration:
                    return new Dictionary<string, object> { ["active_strategies"] = Limit(gv.GetActiveStrategyLimit(norm)) };
                default:
                    return new Dictionary<string, object>();
            }
        }

        /// <summary>null = unlimited.</summary>
        private static object Limit(int n) => n >= int.MaxValue / 2 ? null : (object)n;

        private static object Finite(float v) => float.IsInfinity(v) || v >= float.MaxValue / 2 ? null : (object)(double)v;
    }
}
