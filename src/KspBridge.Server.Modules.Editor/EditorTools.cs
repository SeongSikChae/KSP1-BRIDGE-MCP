using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("editor")]

namespace KspBridge.Server.Modules.Editor
{
    /// <summary>
    /// VAB/SPH editor (paired with the mod's editor module): enter/leave, part catalog, the ship being built, craft
    /// save/load/new, and assembly editing (root, attach, remove, replace, move, part settings, stages).
    /// </summary>
    [McpServerToolType]
    public sealed class EditorTools
    {
        private const string Discards = "Unsaved changes in the editor are discarded; call editor_save first to keep them.";

        private readonly IBridgeClient bridge;

        public EditorTools(IBridgeClient bridge) => this.bridge = bridge;

        private async Task<string> Call(string op, JsonObject? args, CancellationToken cancellationToken)
            => (await bridge.CallAsync("editor", op, args, cancellationToken)).ToJson();

        [McpServerTool(Name = "editor_enter", Destructive = true)]
        [Description("Open the VAB or SPH editor from the space center, or switch facility while in the editor (reloads the editor scene). " +
                     "Check get_status first: someone else's flight may be running. Poll get_status until scene=EDITOR and ready=true. " +
                     Discards)]
        public Task<string> EditorEnter(
            [Description("VAB or SPH")] string facility,
            CancellationToken cancellationToken = default)
            => Call("enter", new JsonObject { ["facility"] = facility }, cancellationToken);

        [McpServerTool(Name = "editor_leave", Destructive = true)]
        [Description("Leave the editor for the space center. Poll get_status until scene=SPACECENTER and ready=true. " + Discards)]
        public Task<string> EditorLeave(CancellationToken cancellationToken = default)
            => Call("leave", null, cancellationToken);

        [McpServerTool(Name = "editor_list_parts", ReadOnly = true, Idempotent = true)]
        [Description("List parts available in the current game (unresearched parts are left out in career/science). Each part has name, " +
                     "title, category, attach sizes, dry mass (t), cost, resources, engine thrust/Isp (vacuum and sea level) and propellants, " +
                     "and module names. Returns total matches and one page sorted by category then name.")]
        public Task<string> EditorListParts(
            [Description("Category name, case-insensitive (e.g. Pods, FuelTank, Engine, Control, Structural, Aero, Utility, Science, Electrical, Coupling, Payload, Ground, Thermal, Communication)")] string? category = null,
            [Description("Case-insensitive text in the part name or title")] string? search = null,
            [Description("Page size, 1-200 (default 50)")] int? limit = null,
            [Description("Index of the first part (default 0)")] int? offset = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new();
            if (category is not null)
                args["category"] = category;
            if (search is not null)
                args["search"] = search;
            if (limit is not null)
                args["limit"] = limit;
            if (offset is not null)
                args["offset"] = offset;
            return Call("parts", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_get_ship", ReadOnly = true, Idempotent = true)]
        [Description("Describe the ship being built in the editor: name, facility, part count, mass, and per part its id (craftID), " +
                     "parent id, attach (stack/surface) and node ids, stage and symmetry partners. An empty editor gives part_count 0.")]
        public Task<string> EditorGetShip(CancellationToken cancellationToken = default)
            => Call("ship", null, cancellationToken);

        [McpServerTool(Name = "editor_save", Destructive = false)]
        [Description("Save the ship to the current save's Ships/<facility>/<name>.craft and return the path. An existing file is " +
                     "only replaced with overwrite=true. Names with / \\ : * ? \" < > |, '..' or edge spaces are refused, never rewritten.")]
        public Task<string> EditorSave(
            [Description("Craft name; default is the ship's current name")] string? name = null,
            [Description("Replace an existing craft file of that name")] bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["overwrite"] = overwrite };
            if (name is not null)
                args["name"] = name;
            return Call("save", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_load", Destructive = true)]
        [Description("Load a craft into the editor, replacing the ship being built. Looks in the current save's Ships/<facility> first, " +
                     "then the stock Ships/<facility>; see editor_list_crafts. An unreadable craft or one with parts that are not " +
                     "installed is refused before the current ship is touched. " + Discards)]
        public Task<string> EditorLoad(
            [Description("Craft name without .craft (stock example: Kerbal X)")] string name,
            [Description("VAB or SPH folder to look in; default is the current editor")] string? facility = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["name"] = name };
            if (facility is not null)
                args["facility"] = facility;
            return Call("load", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_new", Destructive = true)]
        [Description("Start a new empty ship in the current editor (reloads the editor scene; poll get_status until ready=true). " + Discards)]
        public Task<string> EditorNew(CancellationToken cancellationToken = default)
            => Call("new", null, cancellationToken);

        // ---- assembly editing ----

        private const string EditAnswer = "On success returns changed part ids, removed ids (remove/replace), symmetry_cleared " +
                                          "(set_root), dropped_held_part and the ship as editor_get_ship shows it. A refused edit " +
                                          "leaves the ship unchanged. Stage numbers are renumbered 0.. after every edit; use the " +
                                          "numbers from the latest answer.";

        private static readonly TimeSpan EditPoll = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan EditTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Runs a ship-changing op: the mod applies it and answers {edit, pending}; the final answer comes from edit_result
        /// once the editor has settled (the game finishes loading parts and sorting stages on later frames).
        /// </summary>
        private async Task<string> Edit(string op, JsonObject args, CancellationToken cancellationToken)
        {
            BridgeResult result = await bridge.CallAsync("editor", op, args, cancellationToken);
            if (!IsPending(result))
                return result.ToJson();
            JsonNode edit = result.Data!["edit"]!.DeepClone();
            DateTime deadline = DateTime.UtcNow + EditTimeout;
            while (true)
            {
                await Task.Delay(EditPoll, cancellationToken);
                result = await bridge.CallAsync("editor", "edit_result", new JsonObject { ["edit"] = edit.DeepClone() }, cancellationToken);
                if (!IsPending(result))
                    return result.ToJson();
                if (DateTime.UtcNow > deadline)
                    return BridgeResult.Failure("not_ready", "edit " + edit.ToJsonString() + " has not finished within " + EditTimeout.TotalSeconds +
                        " s; it finishes (or rolls back) on its own: check the ship with editor_get_ship before editing again").ToJson();
            }
        }

        private static bool IsPending(BridgeResult result)
            => result.Ok && result.Data is JsonObject data && data["pending"] is JsonValue pending && pending.TryGetValue(out bool p) && p;

        private static JsonArray Vec(double[] v) => new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

        [McpServerTool(Name = "editor_place_root", Destructive = false)]
        [Description("Place the first (root) part of an empty ship by part name (see editor_list_parts). Refused when the ship already " +
                     "has parts (use editor_new first, or editor_set_root). " + EditAnswer)]
        public Task<string> EditorPlaceRoot(
            [Description("Part name (internal name from editor_list_parts)")] string part,
            [Description("Stage for a staged part (engine, decoupler, parachute...): an existing stage number or the highest + 1 (0 when there are no stages); default is a new stage that fires first. Refused for unstaged parts.")] int? stage = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["part"] = part };
            if (stage is not null)
                args["stage"] = stage;
            return Edit("place_root", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_set_root", Destructive = false)]
        [Description("Make another part of the ship the root, with the game's own re-root (attachments flip direction; symmetry that " +
                     "no longer fits is cleared and listed in symmetry_cleared). Refused for parts that cannot be root. " + EditAnswer)]
        public Task<string> EditorSetRoot(
            [Description("Part id (craftID from editor_get_ship)")] long id,
            CancellationToken cancellationToken = default)
            => Edit("set_root", new JsonObject { ["id"] = id }, cancellationToken);

        [McpServerTool(Name = "editor_attach_node", Destructive = false)]
        [Description("Attach a new part to a free stack node (e.g. top, bottom) of a part in the ship. When the target is in a symmetry " +
                     "group, every member gets a copy and the copies form a symmetry group. Nodes of different sizes are allowed (see " +
                     "sizes in editor_list_parts). Refused for a used or missing node, or a part without nodes. " + EditAnswer)]
        public Task<string> EditorAttachNode(
            [Description("Id of the part to attach to")] long target,
            [Description("Node of the target part (e.g. bottom)")] string target_node,
            [Description("Part name to add")] string part,
            [Description("Node of the new part to use; default is the free node facing the target node")] string? node = null,
            [Description("Stage for a staged part; see editor_place_root")] int? stage = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["target"] = target, ["target_node"] = target_node, ["part"] = part };
            if (node is not null)
                args["node"] = node;
            if (stage is not null)
                args["stage"] = stage;
            return Edit("attach_node", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_attach_surface", Destructive = false)]
        [Description("Attach a new part to the surface of a part, at a height along the target's axis and an angle around it (0° = the " +
                     "target's local +Z, clockwise seen from above). symmetry 2-8 places that many copies evenly around the axis as one " +
                     "symmetry group. On a target that is itself in a symmetry group the part is copied to every member (symmetry must " +
                     "then be 1). Refused when the target does not accept surface parts, the part cannot be surface-attached, or no " +
                     "surface is found there. " + EditAnswer)]
        public Task<string> EditorAttachSurface(
            [Description("Id of the part to attach to")] long target,
            [Description("Part name to add")] string part,
            [Description("Height in m along the target's up axis from its origin (default 0)")] double height = 0,
            [Description("Angle in degrees around the axis (default 0)")] double angle = 0,
            [Description("Radial symmetry count 1-8 (default 1)")] int symmetry = 1,
            [Description("Stage for a staged part; see editor_place_root")] int? stage = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["target"] = target, ["part"] = part, ["height"] = height, ["angle"] = angle, ["symmetry"] = symmetry };
            if (stage is not null)
                args["stage"] = stage;
            return Edit("attach_surface", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_remove", Destructive = true)]
        [Description("Remove a part with its symmetry partners and everything attached below them; removed lists their ids. The root " +
                     "cannot be removed (use editor_new). " + EditAnswer)]
        public Task<string> EditorRemove(
            [Description("Part id")] long id,
            CancellationToken cancellationToken = default)
            => Edit("remove", new JsonObject { ["id"] = id }, cancellationToken);

        [McpServerTool(Name = "editor_replace", Destructive = true)]
        [Description("Replace a part (and its symmetry partners) with another part at the same place: attached by the same node name " +
                     "(or surface) to the same parent; stack children move onto the new part's node of the same name; surface " +
                     "children keep their position and may float off a smaller part. New parts get new ids; the old ids are in " +
                     "removed. Stage: kept when both are staged, a new first stage when only the new one is. Refused when a node or " +
                     "surface rule does not fit. " + EditAnswer)]
        public Task<string> EditorReplace(
            [Description("Part id to replace")] long id,
            [Description("Part name of the replacement")] string part,
            CancellationToken cancellationToken = default)
            => Edit("replace", new JsonObject { ["id"] = id, ["part"] = part }, cancellationToken);

        [McpServerTool(Name = "editor_move", Destructive = false)]
        [Description("Move (offset, in the parent part's axes) and/or turn (Euler degrees about the part's own axes) a part with " +
                     "everything attached below it. Attachments are kept; symmetry partners move symmetrically (radial: turned " +
                     "about the axis, mirror: reflected). The root cannot be moved. " + EditAnswer)]
        public Task<string> EditorMove(
            [Description("Part id")] long id,
            [Description("[x, y, z] metres in the parent's axes")] double[]? offset = null,
            [Description("[x, y, z] degrees about the part's own axes")] double[]? rotate = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["id"] = id };
            if (offset is not null)
                args["offset"] = Vec(offset);
            if (rotate is not null)
                args["rotate"] = Vec(rotate);
            return Edit("move", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_get_part_settings", ReadOnly = true, Idempotent = true)]
        [Description("Settings of a part that the editor lets a player change: resources (amount, max, tweakable) and module fields " +
                     "with an editor control (type float/bool/option/cycle, value, min/max/step or options), e.g. thrust limiter, " +
                     "gimbal, decoupler ejection force.")]
        public Task<string> EditorGetPartSettings(
            [Description("Part id")] long id,
            CancellationToken cancellationToken = default)
            => Call("part_settings", new JsonObject { ["id"] = id }, cancellationToken);

        [McpServerTool(Name = "editor_set_part_settings", Destructive = false)]
        [Description("Change resource amounts (0..max) and/or module fields of editor_get_part_settings. Values out of range or " +
                     "unknown settings are refused before anything changes. Symmetry partners get the same values unless " +
                     "symmetry=false. " + EditAnswer)]
        public Task<string> EditorSetPartSettings(
            [Description("Part id")] long id,
            [Description("[{\"name\": resource, \"amount\": number}]")] JsonArray? resources = null,
            [Description("[{\"module\": name or \"module_index\": n, \"field\": name, \"value\": number|bool|string}]")] JsonArray? fields = null,
            [Description("Apply to symmetry partners too (default true)")] bool symmetry = true,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new() { ["id"] = id, ["symmetry"] = symmetry };
            if (resources is not null)
                args["resources"] = resources.DeepClone();
            if (fields is not null)
                args["fields"] = fields.DeepClone();
            return Edit("set_part_settings", args, cancellationToken);
        }

        [McpServerTool(Name = "editor_get_stages", ReadOnly = true, Idempotent = true)]
        [Description("Staging of the ship: stages from the first to fire (highest number) to the last (0), each with its staged parts " +
                     "(engines, decouplers, parachutes...). Numbers match stage in editor_get_ship.")]
        public Task<string> EditorGetStages(CancellationToken cancellationToken = default)
            => Call("stages", null, cancellationToken);

        [McpServerTool(Name = "editor_set_stage", Destructive = false)]
        [Description("Move a staged part (with its symmetry partners) to another stage: an existing stage number or the highest + 1 " +
                     "for a new stage that fires first. Empty stages disappear and stages are renumbered 0... Refused for unstaged " +
                     "parts. " + EditAnswer)]
        public Task<string> EditorSetStage(
            [Description("Part id")] long id,
            [Description("Target stage")] int stage,
            CancellationToken cancellationToken = default)
            => Edit("set_stage", new JsonObject { ["id"] = id, ["stage"] = stage }, cancellationToken);

        [McpServerTool(Name = "editor_swap_stages", Destructive = false)]
        [Description("Exchange the order of two existing, different stages. " + EditAnswer)]
        public Task<string> EditorSwapStages(
            [Description("Stage number")] int a,
            [Description("Other stage number")] int b,
            CancellationToken cancellationToken = default)
            => Edit("swap_stages", new JsonObject { ["a"] = a, ["b"] = b }, cancellationToken);

        [McpServerTool(Name = "editor_list_crafts", ReadOnly = true, Idempotent = true)]
        [Description("List saved crafts of a facility: name, source (save or stock) and part count.")]
        public Task<string> EditorListCrafts(
            [Description("VAB or SPH; required outside the editor, default is the current editor")] string? facility = null,
            CancellationToken cancellationToken = default)
        {
            JsonObject args = new();
            if (facility is not null)
                args["facility"] = facility;
            return Call("crafts", args, cancellationToken);
        }
    }
}
