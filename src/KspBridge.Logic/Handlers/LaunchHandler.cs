using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;

namespace KspBridge.Handlers
{
    public static class LaunchHandler
    {
        private static void RequireGame()
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "scene_not_allowed", "no game is loaded");
            }
        }

        private static string SaveShips(string facility) =>
            Path.Combine(Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "saves"), HighLogic.SaveFolder), "Ships"), facility);

        private static string StockShips(string facility) =>
            Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "Ships"), facility);

        /// <summary>GET /api/craft?facility=VAB|SPH</summary>
        public static object ListCraft(NameValueCollection query)
        {
            RequireGame();
            string only = query["facility"]?.ToUpperInvariant();
            List<object> list = new List<object>();
            foreach (string facility in new[] { "VAB", "SPH" })
            {
                if (only != null && only != facility)
                {
                    continue;
                }
                AddCraft(list, SaveShips(facility), facility, false);
                AddCraft(list, StockShips(facility), facility, true);
            }
            return new Dictionary<string, object> { ["craft"] = list };
        }

        private static void AddCraft(List<object> list, string dir, string facility, bool stock)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.craft"))
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileNameWithoutExtension(file),
                    ["facility"] = facility,
                    ["stock"] = stock,
                    ["path"] = file,
                });
            }
        }

        /// <summary>GET /api/crew</summary>
        public static object ListCrew()
        {
            RequireGame();
            List<object> list = new List<object>();
            foreach (ProtoCrewMember c in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = c.name,
                    ["trait"] = c.trait,
                    ["level"] = c.experienceLevel,
                    ["status"] = c.rosterStatus.ToString(),
                    ["available"] = c.rosterStatus == ProtoCrewMember.RosterStatus.Available,
                });
            }
            return new Dictionary<string, object> { ["crew"] = list };
        }

        /// <summary>POST /api/launch {craft, facility, site, crew:[names]}</summary>
        public static object Launch(RequestBody body)
        {
            RequireGame();
            GameScenes scene = HighLogic.LoadedScene;
            if (scene != GameScenes.SPACECENTER && scene != GameScenes.EDITOR && scene != GameScenes.FLIGHT && scene != GameScenes.TRACKSTATION)
            {
                throw ApiException.SceneNotAllowed("launch");
            }

            string facility = body.Has("facility") ? body.GetString("facility").ToUpperInvariant() : "VAB";
            if (facility != "VAB" && facility != "SPH")
            {
                throw ApiException.BadRequest("facility must be VAB or SPH");
            }
            string site = body.Has("site") ? body.GetString("site") : (facility == "VAB" ? "LaunchPad" : "Runway");
            if (PSystemSetup.Instance != null
                && PSystemSetup.Instance.GetSpaceCenterFacility(site) == null && PSystemSetup.Instance.GetLaunchSite(site) == null)
            {
                throw ApiException.BadRequest("unknown launch site '" + site + "'");
            }
            if (scene == GameScenes.FLIGHT && FlightGlobals.ClearToSave() != ClearToSaveStatus.CLEAR)
            {
                // StartWithNewLaunch saves the game first; stock blocks leaving a flight in this state too
                throw new ApiException(409, "not_clear_to_save", "cannot leave the current flight now (" + FlightGlobals.ClearToSave() + ")");
            }
            string name = body.GetString("craft");

            string path = Path.Combine(SaveShips(facility), name + ".craft");
            if (!File.Exists(path))
            {
                path = Path.Combine(StockShips(facility), name + ".craft");
            }
            if (!File.Exists(path))
            {
                throw ApiException.NotFound("craft '" + name + "' not found in " + facility);
            }

            ConfigNode craft = ConfigNode.Load(path);
            if (craft == null)
            {
                throw ApiException.BadRequest("craft file could not be read");
            }

            // flightState is stale inside a flight: also check the loaded vessels
            bool occupied = ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, site).Count > 0
                || (scene == GameScenes.FLIGHT && FlightGlobals.Vessels.Any(v => v != null && (v.Landed || v.Splashed || v.situation == Vessel.Situations.PRELAUNCH)
                    && string.Equals(v.landedAt, site, StringComparison.OrdinalIgnoreCase)));
            if (occupied)
            {
                throw new ApiException(409, "site_occupied", "launch site '" + site + "' is occupied (recover the vessel there first)");
            }

            double cost = CraftCost(craft);
            if (Career.IsCareer && Funding.Instance != null && Funding.Instance.Funds < cost)
            {
                throw ApiException.BadRequest("not enough funds (" + Funding.Instance.Funds.ToString("F0") + " < " + cost.ToString("F0") + ")");
            }

            VesselCrewManifest manifest = VesselCrewManifest.FromConfigNode(craft);
            List<string> crewNames = CrewNames(body);
            if (crewNames.Count == 0)
            {
                manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craft, manifest, true, false);
            }
            else
            {
                AssignCrew(manifest, crewNames);
            }

            string flag = HighLogic.CurrentGame.flagURL;
            List<string> assigned = manifest.GetAllCrew(false).Where(c => c != null).Select(c => c.name).ToList();
            FlightDriver.StartWithNewLaunch(path, flag, site, manifest);
            return new Dictionary<string, object>
            {
                ["accepted"] = true,
                ["craft"] = name,
                ["site"] = site,
                ["crew"] = assigned.Cast<object>().ToList(),
                ["estimated_cost"] = cost,
            };
        }

        private static List<string> CrewNames(RequestBody body)
        {
            List<string> names = new List<string>();
            if (!body.Has("crew"))
            {
                return names;
            }
            foreach (object o in body.GetList("crew"))
            {
                if (!(o is string s) || s.Length == 0)
                {
                    throw ApiException.BadRequest("crew must be an array of names");
                }
                names.Add(s);
            }
            return names;
        }

        private static void AssignCrew(VesselCrewManifest manifest, List<string> names)
        {
            KerbalRoster roster = HighLogic.CurrentGame.CrewRoster;
            List<PartCrewManifest> seats = manifest.GetCrewableParts();
            int capacity = seats.Sum(p => p.partCrew.Length);
            if (names.Count > capacity)
            {
                throw ApiException.BadRequest("craft has only " + capacity + " seats");
            }
            foreach (PartCrewManifest p in seats)
            {
                for (int i = 0; i < p.partCrew.Length; i++)
                {
                    p.RemoveCrewFromSeat(i);
                }
            }

            int n = 0;
            foreach (PartCrewManifest p in seats)
            {
                for (int i = 0; i < p.partCrew.Length && n < names.Count; i++, n++)
                {
                    if (!roster.Exists(names[n]))
                    {
                        throw ApiException.BadRequest("unknown kerbal '" + names[n] + "'");
                    }
                    ProtoCrewMember c = roster[names[n]];
                    if (c.rosterStatus != ProtoCrewMember.RosterStatus.Available)
                    {
                        throw ApiException.BadRequest("kerbal '" + names[n] + "' is " + c.rosterStatus);
                    }
                    p.AddCrewToSeat(c, i);
                }
            }
        }

        private static double CraftCost(ConfigNode craft)
        {
            double cost = 0;
            foreach (ConfigNode part in craft.GetNodes("PART"))
            {
                string id = part.GetValue("part") ?? "";
                int us = id.LastIndexOf('_');
                AvailablePart ap = PartLoader.getPartInfoByName(us > 0 ? id.Substring(0, us) : id);
                if (ap != null)
                {
                    cost += ap.cost;
                }
            }
            return cost;
        }

        /// <summary>POST /api/recover</summary>
        public static object Recover()
        {
            Vessel v = Guard.ActiveVessel();
            if (!(v.Landed || v.Splashed) || !v.IsRecoverable)
            {
                throw new ApiException(409, "not_recoverable", "vessel must be landed or splashed down and recoverable (situation " + v.situation + ")");
            }
            string name = v.vesselName;
            GameEvents.OnVesselRecoveryRequested.Fire(v);
            return new Dictionary<string, object> { ["accepted"] = true, ["vessel"] = name };
        }

        /// <summary>POST /api/revert</summary>
        public static object Revert()
        {
            Guard.ActiveVessel();
            if (!FlightDriver.CanRevertToPostInit || FlightDriver.PostInitState == null
                || !HighLogic.CurrentGame.Parameters.Flight.CanRestart)
            {
                throw new ApiException(409, "cannot_revert", "revert to launch is not available for this flight");
            }
            FlightDriver.RevertToLaunch();
            return new Dictionary<string, object> { ["accepted"] = true };
        }
    }
}
