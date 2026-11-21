using KspBridge.Mod.Core;
using System.Collections.Generic;
using System.IO;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>
    /// Verification helpers so tests run through MCP alone (no clicking in the game): launch a craft,
    /// revert to launch, go to the space center, put the active vessel on a given orbit. Scene loads end a running plan through
    /// the core session tracking.
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
