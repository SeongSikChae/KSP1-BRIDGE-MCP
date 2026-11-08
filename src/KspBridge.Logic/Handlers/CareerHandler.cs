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
            Dictionary<string, object> d = new Dictionary<string, object>
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
            List<object> waypoints = Waypoints(c);
            if (waypoints.Count > 0)
            {
                d["waypoints"] = waypoints;
            }
            return d;
        }

        /// <summary>Waypoints this contract placed (survey/observation spots): body, latitude, longitude, altitude; distance/bearing from the active vessel on the same body.</summary>
        private static List<object> Waypoints(Contract c)
        {
            List<object> list = new List<object>();
            // The waypoint manager is not loaded in every scene: take the waypoints from the contract parameters
            // (survey/visit parameters hold a FinePrint.Waypoint field) and add any others the manager knows.
            List<KeyValuePair<FinePrint.Waypoint, string>> found = new List<KeyValuePair<FinePrint.Waypoint, string>>();
            foreach (ContractParameter p in c.AllParameters)
            {
                foreach (System.Reflection.FieldInfo f in p.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(FinePrint.Waypoint) && f.GetValue(p) is FinePrint.Waypoint w && found.All(k => k.Key != w))
                    {
                        found.Add(new KeyValuePair<FinePrint.Waypoint, string>(w, p.Title));
                    }
                }
            }
            FinePrint.WaypointManager m = FinePrint.WaypointManager.Instance();
            if (m != null)
            {
                foreach (FinePrint.Waypoint w in m.Waypoints.Where(w => w.contractReference == c && found.All(k => k.Key != w)))
                {
                    found.Add(new KeyValuePair<FinePrint.Waypoint, string>(w, null));
                }
            }
            Vessel v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            foreach (KeyValuePair<FinePrint.Waypoint, string> k in found)
            {
                FinePrint.Waypoint w = k.Key;
                Dictionary<string, object> e = new Dictionary<string, object>
                {
                    ["name"] = w.name,
                    ["body"] = w.celestialName,
                    ["latitude"] = w.latitude,
                    ["longitude"] = w.longitude,
                    ["altitude"] = w.altitude,
                    ["on_surface"] = w.isOnSurface,
                };
                if (k.Value != null)
                {
                    e["parameter"] = k.Value;
                }
                if (v != null && v.mainBody != null && v.mainBody.bodyName == w.celestialName)
                {
                    e["distance"] = GreatCircle(v.mainBody.Radius, v.latitude, v.longitude, w.latitude, w.longitude, out double bearing);
                    e["bearing"] = bearing;
                }
                list.Add(e);
            }
            return list;
        }

        /// <summary>Haversine distance (m) and initial bearing (deg, 0..360) from point 1 to point 2.</summary>
        internal static double GreatCircle(double radius, double lat1, double lon1, double lat2, double lon2, out double bearing)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin((p2 - p1) / 2) * Math.Sin((p2 - p1) / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            bearing = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
            return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
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
