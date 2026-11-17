using KspBridge.Mod.Core;
using System.Collections.Generic;
using System.IO;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>
    /// Verification helpers so tests run through MCP alone (no clicking in the game): launch a craft,
    /// revert to launch, go to the space center. Scene loads end a running plan through the core session tracking.
    /// </summary>
    public sealed class DebugModule : IBridgeModule
    {
        private ModuleContext context;

        public string Name => "debug";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.AddHandler("launch", Launch);
            context.AddHandler("revert", _ => Revert());
            context.AddHandler("space_center", _ => SpaceCenter());
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
