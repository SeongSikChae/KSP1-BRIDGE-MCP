using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Contracts;

namespace KspBridge.Handlers
{
    public static class CareerHandler
    {
        internal static readonly SpaceCenterFacility[] Facilities =
        {
            SpaceCenterFacility.LaunchPad, SpaceCenterFacility.Runway, SpaceCenterFacility.VehicleAssemblyBuilding,
            SpaceCenterFacility.SpaceplaneHangar, SpaceCenterFacility.TrackingStation, SpaceCenterFacility.MissionControl,
            SpaceCenterFacility.ResearchAndDevelopment, SpaceCenterFacility.AstronautComplex, SpaceCenterFacility.Administration,
        };

        /// <summary>GET /api/career</summary>
        public static object GetCareer()
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "scene_not_allowed", "no game is loaded");
            }
            Career.Require();

            Dictionary<string, object> facilities = new Dictionary<string, object>();
            foreach (SpaceCenterFacility f in Facilities)
            {
                // GetFacilityLevelCount returns the highest level index (stock: 2) and can read low outside the KSC scene.
                int max = ScenarioUpgradeableFacilities.GetFacilityLevelCount(f);
                if (max < 2)
                {
                    max = 2;
                }
                float norm = ScenarioUpgradeableFacilities.GetFacilityLevel(f);
                facilities[Snake(f.ToString())] = new Dictionary<string, object>
                {
                    ["level"] = (int)Math.Round(norm * max),
                    ["max_level"] = max,
                };
            }

            return new Dictionary<string, object>
            {
                ["mode"] = HighLogic.CurrentGame.Mode.ToString(),
                ["save"] = HighLogic.SaveFolder,
                ["ut"] = Planetarium.GetUniversalTime(),
                ["funds"] = Funding.Instance != null ? Funding.Instance.Funds : 0,
                ["science"] = ResearchAndDevelopment.Instance != null ? (double)ResearchAndDevelopment.Instance.Science : 0,
                ["reputation"] = Reputation.Instance != null ? (double)Reputation.Instance.reputation : 0,
                ["facilities"] = facilities,
            };
        }

        /// <summary>GET /api/contracts?state=offered|active|completed</summary>
        public static object ListContracts(NameValueCollection query)
        {
            ContractSystem cs = RequireContracts();
            string state = query["state"]?.ToLowerInvariant();
            IEnumerable<Contract> source;
            switch (state)
            {
                case null:
                case "":
                    source = cs.Contracts.Where(c => c.ContractState == Contract.State.Offered || c.ContractState == Contract.State.Active);
                    break;
                case "offered":
                    source = cs.Contracts.Where(c => c.ContractState == Contract.State.Offered);
                    break;
                case "active":
                    source = cs.Contracts.Where(c => c.ContractState == Contract.State.Active);
                    break;
                case "completed":
                    source = cs.ContractsFinished.Where(c => c.ContractState == Contract.State.Completed);
                    break;
                default:
                    throw ApiException.BadRequest("state must be offered, active or completed");
            }
            return new Dictionary<string, object> { ["contracts"] = source.Select(c => (object)Describe(c)).ToList() };
        }

        /// <summary>POST /api/contracts/{id}/accept|decline</summary>
        public static object Act(string idText, string action)
        {
            ContractSystem cs = RequireContracts();
            if (!long.TryParse(idText, out long id))
            {
                throw ApiException.BadRequest("contract id must be an integer");
            }
            Contract c = cs.Contracts.FirstOrDefault(x => x.ContractID == id)
                ?? throw ApiException.NotFound("no current contract with id " + id);
            if (c.ContractState != Contract.State.Offered)
            {
                throw ApiException.BadRequest("contract is " + c.ContractState + ", not Offered");
            }
            bool ok = action == "accept" ? c.Accept() : c.Decline();
            if (!ok)
            {
                throw ApiException.BadRequest("the game refused to " + action + " the contract (active contract limit?)");
            }
            return Describe(c);
        }

        private static ContractSystem RequireContracts()
        {
            Career.Require();
            return ContractSystem.Instance ?? throw new ApiException(409, "scene_not_allowed", "contract system is not loaded in this scene");
        }

        private static Dictionary<string, object> Describe(Contract c)
        {
            return new Dictionary<string, object>
            {
                ["id"] = c.ContractID,
                ["title"] = c.Title,
                ["state"] = c.ContractState.ToString(),
                ["prestige"] = c.Prestige.ToString(),
                ["deadline_ut"] = c.DateDeadline > 0 ? (object)c.DateDeadline : null,
                ["rewards"] = new Dictionary<string, object>
                {
                    ["funds_advance"] = c.FundsAdvance,
                    ["funds_completion"] = c.FundsCompletion,
                    ["science"] = (double)c.ScienceCompletion,
                    ["reputation"] = (double)c.ReputationCompletion,
                },
                ["parameters"] = Params(Enumerable.Range(0, c.ParameterCount).Select(c.GetParameter)), // AllParameters is recursive: top level = direct children only
            };
        }

        private static List<object> Params(IEnumerable<ContractParameter> ps)
        {
            List<object> list = new List<object>();
            foreach (ContractParameter p in ps)
            {
                List<ContractParameter> children = new List<ContractParameter>();
                for (int i = 0; i < p.ParameterCount; i++)
                {
                    children.Add(p.GetParameter(i));
                }
                list.Add(new Dictionary<string, object>
                {
                    ["title"] = p.Title,
                    ["state"] = p.State.ToString(),
                    ["optional"] = p.Optional,
                    ["parameters"] = Params(children),
                });
            }
            return list;
        }

        internal static string Snake(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && i > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(s[i]));
            }
            return sb.ToString();
        }
    }
}
