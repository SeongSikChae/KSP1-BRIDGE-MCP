using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>
    /// Verification helpers so tests run through MCP alone (no clicking in the game): launch a craft,
    /// revert to launch, go to the space center, put the active vessel on a given orbit, list and switch loaded vessels, save and
    /// load named saves. Scene loads and vessel switches end a running plan through the core session tracking.
    /// </summary>
    public sealed class DebugModule : IBridgeModule
    {
        private ModuleContext context;
        private PendingOrbit pendingOrbit;
        private int nextOrbitId = 1;

        public string Name => "debug";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.AddHandler("launch", Launch);
            context.AddHandler("revert", _ => Revert());
            context.AddHandler("space_center", _ => SpaceCenter());
            context.AddHandler("set_orbit.start", SetOrbitStart);
            context.AddHandler("set_orbit.result", SetOrbitResult);
            context.AddHandler("list_vessels", _ => ListVessels());
            context.AddHandler("switch_vessel", SwitchVessel);
            context.AddHandler("save", Save);
            context.AddHandler("load", Load);
            context.Subscriptions.Add(GameEvents.onGameSceneLoadRequested, scene => pendingOrbit?.Cancel("the scene changed to " + scene));
            context.Subscriptions.Add(() => pendingOrbit?.Cancel("the debug module was reloaded"));
        }

        private static double Now => UnityEngine.Time.realtimeSinceStartup;

        /// <summary>
        /// Starts moving the active vessel to the given orbit with the game's set-orbit cheat (SetShipOrbit). The game puts the
        /// vessel on rails and updates its situation a few frames later, so set_orbit.result is asked until it shows the orbit.
        /// </summary>
        private object SetOrbitStart(Dictionary<string, object> args)
        {
            RequireReadyGame();
            Vessel vessel = context.Session.InFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            if (vessel == null)
                throw BridgeException.NotReady("set_orbit needs the flight scene with an active vessel (now " + context.Session.Scene + ")");

            OrbitRequest request = OrbitRequest.Parse(args, Bodies(), vessel.mainBody.bodyName);
            if (context.Coordinator.Running)
                throw BridgeException.BadRequest("a plan is running; end it with abort_plan before set_orbit (nothing was changed)");
            if (pendingOrbit != null && !pendingOrbit.CanReplace(Now))
                throw BridgeException.BadRequest("set_orbit request " + pendingOrbit.Id + " is still in progress");

            context.Log("debug set_orbit " + request.Body.Name + " Ap " + request.Apoapsis + " Pe " + request.Periapsis + " inc " + request.Inclination);
            FlightGlobals.fetch.SetShipOrbit(request.Body.Index, request.Eccentricity, request.SemiMajorAxis, request.Inclination, 0, 0, 0, 0);
            pendingOrbit = new PendingOrbit(nextOrbitId++, Now, request);
            return new Dictionary<string, object> { ["request"] = pendingOrbit.Id, ["pending"] = true };
        }

        private object SetOrbitResult(Dictionary<string, object> args)
        {
            double value = args.Num("request", required: true).Value;
            PendingOrbit pending = pendingOrbit;
            if (pending == null || value != pending.Id)
                throw BridgeException.BadRequest("'request' must be the number set_orbit.start returned");

            if (pending.State == OrbitState.Waiting)
            {
                Vessel vessel = context.Session.InFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
                if (vessel != null && vessel.orbit != null && pending.Request.IsApplied(vessel.mainBody.bodyName, vessel.situation.ToString(),
                        vessel.orbit.ApA, vessel.orbit.PeA))
                {
                    pending.Apply(new Dictionary<string, object>
                    {
                        ["applied"] = true,
                        ["body"] = vessel.mainBody.bodyName,
                        ["situation"] = vessel.situation.ToString(),
                        ["apoapsis_m"] = vessel.orbit.ApA,
                        ["periapsis_m"] = vessel.orbit.PeA,
                        ["inclination_deg"] = vessel.orbit.inclination,
                    });
                }
                else
                {
                    pending.Expire(Now);
                }
            }

            switch (pending.State)
            {
                case OrbitState.Applied:
                    return pending.Result;
                case OrbitState.Waiting:
                    return new Dictionary<string, object> { ["request"] = pending.Id, ["pending"] = true };
                default:
                    throw BridgeException.NotReady(pending.Reason);
            }
        }

        private static List<OrbitBody> Bodies()
        {
            List<OrbitBody> bodies = new List<OrbitBody>();
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody b = FlightGlobals.Bodies[i];
                if (b == null)
                    continue;
                double? terrainMax = null;
                if (b.pqsController != null)
                {
                    double height = b.pqsController.radiusMax - b.pqsController.radius;
                    if (height > 0)
                        terrainMax = height;
                }
                bodies.Add(new OrbitBody
                {
                    Name = b.bodyName,
                    Index = i,
                    Radius = b.Radius,
                    AtmosphereTop = b.atmosphere ? b.atmosphereDepth : (double?)null,
                    TerrainMax = terrainMax,
                    SphereOfInfluence = b.sphereOfInfluence,
                });
            }
            return bodies;
        }

        private void RequireReadyGame()
        {
            if (HighLogic.CurrentGame == null)
                throw BridgeException.NotReady("no game is loaded (load a save first)");
            string block = context.Session.BlockReason();
            if (block != null)
                throw BridgeException.NotReady(block);
        }

        private object Launch(Dictionary<string, object> args)
        {
            RequireReadyGame();
            GameScenes scene = HighLogic.LoadedScene;
            if (scene != GameScenes.SPACECENTER && scene != GameScenes.FLIGHT && scene != GameScenes.EDITOR && scene != GameScenes.TRACKSTATION)
                throw BridgeException.NotReady("launch needs the space center, flight, editor or tracking station (now " + scene + ")");

            string craft = args.Str("craft", "Kerbal X");
            string facility = args.Str("facility", "VAB");
            if (facility != "VAB" && facility != "SPH")
                throw BridgeException.BadRequest("facility must be VAB or SPH");
            string site = args.Str("site", facility == "VAB" ? "LaunchPad" : "Runway");

            string path = FindCraft(craft, facility)
                ?? throw BridgeException.BadRequest("craft '" + craft + "' not found in Ships/" + facility + " (stock or save)");

            ConfigNode node = ConfigNode.Load(path)
                ?? throw BridgeException.BadRequest("craft file could not be read: " + path);
            VesselCrewManifest manifest = VesselCrewManifest.FromConfigNode(node);
            manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(node, manifest);

            context.Log("debug launch " + craft + " from " + site);
            FlightDriver.StartWithNewLaunch(path, HighLogic.CurrentGame.flagURL, site, manifest);
            return new Dictionary<string, object> { ["accepted"] = true, ["craft"] = craft, ["site"] = site, ["path"] = path };
        }

        private object Revert()
        {
            RequireReadyGame();
            if (!context.Session.InFlight)
                throw BridgeException.NotReady("revert needs the flight scene (now " + context.Session.Scene + ")");
            if (!FlightDriver.CanRevertToPostInit)
                throw new BridgeException("revert_unavailable", "this flight cannot revert to launch");
            FlightDriver.RevertToLaunch();
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        private object SpaceCenter()
        {
            RequireReadyGame();
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                return new Dictionary<string, object> { ["accepted"] = true, ["already"] = true };
            if (context.Session.InFlight)
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        /// <summary>A ready flight scene with an active vessel, or not_ready naming the scene.</summary>
        private Vessel RequireFlight(string what)
        {
            if (HighLogic.CurrentGame == null)
                throw BridgeException.NotReady(what + " needs the flight scene with an active vessel (now " + context.Session.Scene +
                                               "; no game is loaded)");
            RequireReadyGame();
            Vessel active = context.Session.InFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            if (active == null)
                throw BridgeException.NotReady(what + " needs the flight scene with an active vessel (now " + context.Session.Scene + ")");
            return active;
        }

        private static bool IsOwned(Vessel v) => v.DiscoveryInfo != null && v.DiscoveryInfo.Level == DiscoveryLevels.Owned;

        private object ListVessels()
        {
            Vessel active = RequireFlight("list_vessels");
            Vector3d activePos = active.GetWorldPos3D();
            List<object> vessels = new List<object>();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null)
                    continue;
                vessels.Add(new Dictionary<string, object>
                {
                    ["id"] = v.persistentId,
                    ["name"] = v.GetDisplayName(),
                    ["type"] = v.vesselType.ToString(),
                    ["situation"] = v.situation.ToString(),
                    ["body"] = v.mainBody != null ? v.mainBody.bodyName : null,
                    ["loaded"] = v.loaded,
                    ["owned"] = IsOwned(v),
                    ["active"] = v == active,
                    ["distance_m"] = (v.GetWorldPos3D() - activePos).magnitude,
                });
            }
            return new Dictionary<string, object> { ["vessels"] = vessels };
        }

        /// <summary>
        /// Makes a loaded vessel active without reloading the scene (SetActiveVessel, or ForceSetActiveVessel with force, which
        /// skips only the save check here because ownership is checked first). The game fires onVesselChange inside the call,
        /// so the core ends a running plan with vessel_change before this answers.
        /// </summary>
        private object SwitchVessel(Dictionary<string, object> args)
        {
            RequireFlight("switch_vessel");
            double raw = args.Num("id", required: true).Value;
            if (double.IsNaN(raw) || raw < 0 || raw > uint.MaxValue || raw != Math.Floor(raw))
                throw BridgeException.BadRequest("'id' must be a vessel id from debug_list_vessels (an integer 0..4294967295)");
            uint id = (uint)raw;
            bool force = args.Bool("force");

            Vessel target = null;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v != null && v.persistentId == id)
                {
                    target = v;
                    break;
                }
            }

            ClearToSaveStatus status = force ? ClearToSaveStatus.CLEAR : FlightGlobals.ClearToSave(logMsg: false);
            SwitchDecision decision = VesselSwitchCheck.Decide(new VesselFacts
            {
                Found = target != null,
                Active = target != null && target == FlightGlobals.ActiveVessel,
                Loaded = target != null && target.loaded,
                Owned = target != null && IsOwned(target),
                Name = target != null ? target.GetDisplayName() : null,
            }, force, status.ToString());

            switch (decision.Outcome)
            {
                case SwitchOutcome.Already:
                    return new Dictionary<string, object> { ["id"] = id, ["name"] = target.GetDisplayName(), ["already"] = true };
                case SwitchOutcome.Reject:
                    string reason = decision.Reason;
                    if (!force && status != ClearToSaveStatus.CLEAR && target != null && target.loaded && IsOwned(target))
                        reason += " - " + FlightGlobals.GetNotClearToSaveStatusReason(status, "Cannot switch vessels");
                    throw BridgeException.BadRequest(reason);
            }

            context.Log("debug switch_vessel " + target.GetDisplayName() + (force ? " (force)" : ""));
            bool switched = force ? FlightGlobals.ForceSetActiveVessel(target) : FlightGlobals.SetActiveVessel(target);
            if (!switched || FlightGlobals.ActiveVessel != target)
                throw BridgeException.BadRequest("the game refused to switch to '" + target.GetDisplayName() + "'");
            return new Dictionary<string, object> { ["id"] = id, ["name"] = target.GetDisplayName(), ["already"] = false };
        }

        /// <summary>
        /// Saves like the game's F5 (QuickSaveLoad.doSave) into &lt;name&gt;.sfs only: no persistent write, OVERWRITE instead of
        /// BACKUP (BACKUP may delete the game's backup folder), and the difficulty setting CanQuickSave is not followed.
        /// </summary>
        private object Save(Dictionary<string, object> args)
        {
            Vessel active = RequireFlight("save");
            string name = SaveName.Parse(args.Str("name"), forSave: true);
            string refused = QuickSaveCheck.Decide(active.state == Vessel.State.DEAD, FlightGlobals.ClearToSave(logMsg: false).ToString());
            if (refused != null)
                throw BridgeException.BadRequest(refused);

            context.Log("debug save " + name);
            GameEvents.onGameAboutToQuicksave.Fire();
            Game game = HighLogic.CurrentGame.Updated();
            game.startScene = GameScenes.FLIGHT;
            try
            {
                GamePersistence.SaveGame(game, name, HighLogic.SaveFolder, SaveMode.OVERWRITE);
            }
            catch (Exception e)
            {
                throw BridgeException.BadRequest("the game could not save '" + name + "': " + e.Message);
            }
            finally
            {
                game.startScene = GameScenes.SPACECENTER;
            }
            return new Dictionary<string, object> { ["name"] = name };
        }

        /// <summary>
        /// Loads a save made in flight and opens the flight on its active vessel, following the success path of the game's
        /// quickload. Everything is checked before the scene changes: the file values first (SaveFileCheck, no side effects),
        /// then the game built by LoadGameCfg (LoadedGameCheck). LoadGameCfg clears the vessel id dictionaries, so a failure
        /// after it says how to recover.
        /// </summary>
        private object Load(Dictionary<string, object> args)
        {
            RequireReadyGame();
            GameScenes scene = HighLogic.LoadedScene;
            if (scene != GameScenes.SPACECENTER && scene != GameScenes.FLIGHT && scene != GameScenes.EDITOR && scene != GameScenes.TRACKSTATION)
                throw BridgeException.NotReady("load needs the space center, flight, editor or tracking station (now " + scene + ")");
            string name = SaveName.Parse(args.Str("name"), forSave: false);

            string path = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + HighLogic.SaveFolder + "/" + name + ".sfs");
            if (!File.Exists(path))
                throw BridgeException.BadRequest("no save named '" + name + "' in this save folder");
            ConfigNode root;
            try
            {
                root = ConfigNode.Load(path);
            }
            catch (Exception e)
            {
                throw BridgeException.BadRequest("the save file is damaged: it could not be read (" + e.Message + ")");
            }
            if (root == null)
                throw BridgeException.BadRequest("the save file is damaged: it could not be read");

            SaveFileFacts facts = ReadFacts(root);
            string refused = SaveFileCheck.Check(facts, Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            if (refused != null)
                throw BridgeException.BadRequest(refused);
            SaveFileCheck.TryGuid(facts.ActivePid, out Guid activePid);

            context.Log("debug load " + name);
            if (ROCManager.Instance != null)
                ROCManager.Instance.ClearROCsCache();
            Game game;
            try
            {
                game = GamePersistence.LoadGameCfg(root, name, nullIfIncompatible: true, suppressIncompatibleMessage: true);
            }
            catch (Exception e)
            {
                throw AfterIdsCleared("the game failed to read the save (" + e.Message + ")");
            }
            LoadedGameFacts loaded = new LoadedGameFacts
            {
                GameNull = game == null,
                GameCompatible = game != null && game.compatible,
                FlightStateNull = game == null || game.flightState == null,
                FlightStateCompatible = game != null && game.flightState != null && game.flightState.compatible,
            };
            if (game != null && game.flightState != null && game.flightState.protoVessels != null)
            {
                foreach (ProtoVessel pv in game.flightState.protoVessels)
                    loaded.ProtoVesselIds.Add(pv != null ? pv.vesselID : Guid.Empty);
            }
            string notLoadable = LoadedGameCheck.Check(loaded, activePid, out int index);
            if (notLoadable != null)
                throw AfterIdsCleared(notLoadable);

            GamePersistence.UpdateScenarioModules(game);
            GameEvents.onGameStatePostLoad.Fire(root);
            FlightDriver.StartAndFocusVessel(game, index);
            return new Dictionary<string, object> { ["accepted"] = true, ["name"] = name };
        }

        private static BridgeException AfterIdsCleared(string reason)
            => BridgeException.BadRequest(reason + ". The game already cleared the current flight's vessel id records while reading " +
                                          "it; restart the flight with debug_revert or by loading another save");

        private static SaveFileFacts ReadFacts(ConfigNode root)
        {
            SaveFileFacts facts = new SaveFileFacts();
            ConfigNode game = root.GetNode("GAME");
            if (game == null)
                return facts;
            facts.HasGame = true;
            facts.Version = game.GetValue("version");
            facts.Scene = game.GetValue("scene");
            ConfigNode flight = game.GetNode("FLIGHTSTATE");
            if (flight == null)
                return facts;
            facts.HasFlightState = true;
            facts.ActiveVessel = flight.GetValue("activeVessel");
            ConfigNode[] vessels = flight.GetNodes("VESSEL");
            facts.VesselCount = vessels.Length;
            if (int.TryParse(facts.ActiveVessel, NumberStyles.Integer, CultureInfo.InvariantCulture, out int active)
                && active >= 0 && active < vessels.Length)
                facts.ActivePid = vessels[active].GetValue("pid");
            return facts;
        }

        private static string FindCraft(string craft, string facility)
        {
            string root = KSPUtil.ApplicationRootPath;
            string[] candidates =
            {
                Path.Combine(root, "saves/" + HighLogic.SaveFolder + "/Ships/" + facility + "/" + craft + ".craft"),
                Path.Combine(root, "Ships/" + facility + "/" + craft + ".craft"),
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }
    }
}
