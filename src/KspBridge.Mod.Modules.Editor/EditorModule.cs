using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
            editor = new ShipEditor(context.Log);
            // A scene change we did not ask for (the player left the editor) drops a pending edit; ours finish it first.
            context.Subscriptions.Add(GameEvents.onGameSceneLoadRequested, scene => editor.DropPending("the scene changed to " + scene));

            context.AddHandler("enter", Enter);
            context.AddHandler("leave", _ => Leave());
            context.AddHandler("new", _ => NewShip());
            context.AddHandler("parts", args => PartCatalog.List(args, RequireGame));
            context.AddHandler("ship", _ => ShipView.Describe(RequireEditor()));
            context.AddHandler("save", args =>
            {
                ShipConstruct ship = RequireEditor();
                editor.RequireNoPending();
                return CraftFiles.Save(ship, args);
            });
            context.AddHandler("load", args =>
            {
                RequireEditor();
                editor.RequireNoPending();
                return CraftFiles.Load(args, context.Log);
            });
            context.AddHandler("crafts", Crafts);

            // Assembly editing (C1-C10). Ship-changing ops answer {edit, pending}; the server asks edit_result until done.
            context.AddHandler("place_root", args => editor.PlaceRoot(RequireEditor(), args.Str("part", required: true), args.Num("stage")));
            context.AddHandler("set_root", args => editor.SetRoot(RequireEditor(), Id(args, "id")));
            context.AddHandler("attach_node", args => editor.AttachNode(RequireEditor(), Id(args, "target"),
                args.Str("target_node", required: true), args.Str("part", required: true), args.Str("node"), args.Num("stage")));
            context.AddHandler("attach_surface", args => editor.AttachSurface(RequireEditor(), Id(args, "target"),
                args.Str("part", required: true), args.Num("height") ?? 0, args.Num("angle") ?? 0, Symmetry(args), args.Num("stage")));
            context.AddHandler("remove", args => editor.Remove(RequireEditor(), Id(args, "id")));
            context.AddHandler("replace", args => editor.Replace(RequireEditor(), Id(args, "id"), args.Str("part", required: true)));
            context.AddHandler("move", args => editor.Move(RequireEditor(), Id(args, "id"), Vector(args, "offset"), Vector(args, "rotate")));
            context.AddHandler("part_settings", args => ShipEditor.PartSettingsOf(RequireEditor(), Id(args, "id")));
            context.AddHandler("set_part_settings", args => editor.SetPartSettings(RequireEditor(), Id(args, "id"),
                args.Arr("resources"), args.Arr("fields"), args.Bool("symmetry", true)));
            context.AddHandler("stages", _ => Staging.Describe(RequireEditor()));
            context.AddHandler("set_stage", args => editor.SetStage(RequireEditor(), Id(args, "id"), args.Num("stage", required: true).Value));
            context.AddHandler("swap_stages", args => editor.SwapStages(RequireEditor(), args.Num("a", required: true).Value, args.Num("b", required: true).Value));
            context.AddHandler("edit_result", args =>
            {
                RequireGame();
                return editor.Result((int)Id(args, "edit"));
            });
        }

        private ShipEditor editor;

        /// <summary>A part id or edit number: a whole number from 0 to uint.MaxValue.</summary>
        private static uint Id(Dictionary<string, object> args, string key)
        {
            double value = args.Num(key, required: true).Value;
            if (double.IsNaN(value) || value != Math.Floor(value) || value < 0 || value > uint.MaxValue)
                throw BridgeException.BadRequest("'" + key + "' must be a part id (whole number)");
            return (uint)value;
        }

        private static int Symmetry(Dictionary<string, object> args)
        {
            double value = args.Num("symmetry") ?? 1;
            if (double.IsNaN(value) || value != Math.Floor(value) || value < 1 || value > 8)
                throw BridgeException.BadRequest("symmetry must be a whole number 1 to 8");
            return (int)value;
        }

        /// <summary>[x, y, z] or null when absent.</summary>
        private static Vec3? Vector(Dictionary<string, object> args, string key)
        {
            List<object> list = args.Arr(key);
            if (list == null)
                return null;
            if (list.Count != 3 || list.Any(v => !(v is double d) || double.IsNaN(d) || double.IsInfinity(d)))
                throw BridgeException.BadRequest("'" + key + "' must be [x, y, z] numbers");
            return new Vec3((double)list[0], (double)list[1], (double)list[2]);
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
                editor.RequireNoPending();
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
            editor.RequireNoPending();
            context.Log("editor leave to space center");
            requestedFacility = null;
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        /// <summary>Empty ship: reopen the editor of the current facility. The game's own "new" button asks through a dialog.</summary>
        private object NewShip()
        {
            RequireEditor();
            editor.RequireNoPending();
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

        /// <summary>Opens the editor of <paramref name="facility"/> with an empty ship (see ShipEditor.StartClean).</summary>
        private static void StartClean(EditorFacility facility) => ShipEditor.StartClean(facility);

        private static Dictionary<string, object> Accepted(EditorFacility facility, bool already)
            => new Dictionary<string, object> { ["accepted"] = true, ["facility"] = facility.ToString(), ["already"] = already };
    }
}
