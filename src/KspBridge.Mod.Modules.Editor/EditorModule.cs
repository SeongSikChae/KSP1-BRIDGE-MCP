using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// VAB/SPH editor basics for the Planner: enter/leave/switch, part catalog, the ship being built, craft save/load/new.
    /// Only public, dialog-free game APIs are used, so every request is answered through MCP alone (no clicking).
    /// Leaving, switching, loading and starting a new ship discard unsaved changes; the tool descriptions say to save first.
    /// </summary>
    public sealed class EditorModule : IBridgeModule
    {
        private ModuleContext context;

        /// <summary>Facility asked for by the last enter/switch; editor requests wait until the editor reports it. Cleared once reached.</summary>
        private EditorFacility? requestedFacility;

        public string Name => "editor";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.AddHandler("enter", Enter);
            context.AddHandler("leave", _ => Leave());
            context.AddHandler("new", _ => NewShip());
            context.AddHandler("parts", args => PartCatalog.List(args, RequireGame));
            context.AddHandler("ship", _ => ShipView.Describe(RequireEditor()));
            context.AddHandler("save", args => CraftFiles.Save(RequireEditor(), args));
            context.AddHandler("load", args =>
            {
                RequireEditor();
                return CraftFiles.Load(args, context.Log);
            });
            context.AddHandler("crafts", Crafts);
        }

        /// <summary>A game is loaded and no scene load is in progress (core judgement).</summary>
        private void RequireGame()
        {
            if (HighLogic.CurrentGame == null)
                throw BridgeException.NotReady("no game is loaded (now " + context.Session.Scene + "); load a save first");
            string block = context.Session.BlockReason();
            if (block != null)
                throw BridgeException.NotReady(block);
        }

        /// <summary>
        /// The editor scene is loaded and its logic can take requests. Input locks are deliberately not checked:
        /// they follow window focus and dialogs, not whether the editor works.
        /// </summary>
        private ShipConstruct RequireEditor()
        {
            RequireGame();
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
                throw BridgeException.NotReady("editor tools need the editor scene (now " + context.Session.Scene + "); call editor_enter");
            EditorLogic logic = EditorLogic.fetch;
            if (logic == null || logic.ship == null)
                throw BridgeException.NotReady("editor is starting; poll get_status and retry");
            if (requestedFacility.HasValue)
            {
                if (EditorDriver.editorFacility != requestedFacility.Value)
                    throw BridgeException.NotReady("editor is switching to " + requestedFacility.Value + "; retry shortly");
                requestedFacility = null;
            }
            return logic.ship;
        }

        internal static EditorFacility ParseFacility(string facility)
        {
            if (string.Equals(facility, "VAB", StringComparison.OrdinalIgnoreCase))
                return EditorFacility.VAB;
            if (string.Equals(facility, "SPH", StringComparison.OrdinalIgnoreCase))
                return EditorFacility.SPH;
            throw BridgeException.BadRequest("facility must be VAB or SPH");
        }

        private object Enter(Dictionary<string, object> args)
        {
            EditorFacility facility = ParseFacility(args.Str("facility", required: true));
            RequireGame();
            GameScenes scene = HighLogic.LoadedScene;
            if (scene == GameScenes.EDITOR)
            {
                RequireEditor();
                if (EditorDriver.editorFacility == facility)
                    return Accepted(facility, already: true);
                // EditorDriver.SwitchEditor is protected; the public StartEditor reloads the editor scene for the other facility.
                context.Log("editor switch to " + facility);
                requestedFacility = facility;
                StartClean(facility);
                return Accepted(facility, already: false);
            }
            if (scene != GameScenes.SPACECENTER)
                throw BridgeException.NotReady("editor_enter needs the space center or the editor (now " + scene + "); use debug_space_center first");

            context.Log("editor enter " + facility);
            requestedFacility = facility;
            StartClean(facility);
            return Accepted(facility, already: false);
        }

        private object Leave()
        {
            RequireEditor();
            context.Log("editor leave to space center");
            requestedFacility = null;
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        /// <summary>Empty ship: reopen the editor of the current facility. The game's own "new" button asks through a dialog.</summary>
        private object NewShip()
        {
            RequireEditor();
            EditorFacility facility = EditorDriver.editorFacility;
            context.Log("editor new ship in " + facility);
            requestedFacility = facility;
            StartClean(facility);
            return Accepted(facility, already: false);
        }

        private object Crafts(Dictionary<string, object> args)
        {
            RequireGame();
            string facility = args.Str("facility");
            EditorFacility which;
            if (facility != null)
                which = ParseFacility(facility);
            else if (HighLogic.LoadedScene == GameScenes.EDITOR)
                which = EditorDriver.editorFacility;
            else
                throw BridgeException.BadRequest("facility (VAB or SPH) is required outside the editor");
            return CraftFiles.List(which);
        }

        /// <summary>
        /// Opens the editor of <paramref name="facility"/> with an empty ship. StartEditor only sets the facility; the startup
        /// behaviour is a separate static that stays LOAD_FROM_CACHE after returning from a flight, which would bring the
        /// last launched ship back.
        /// </summary>
        private static void StartClean(EditorFacility facility)
        {
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(facility);
        }

        private static Dictionary<string, object> Accepted(EditorFacility facility, bool already)
            => new Dictionary<string, object> { ["accepted"] = true, ["facility"] = facility.ToString(), ["already"] = already };
    }
}
