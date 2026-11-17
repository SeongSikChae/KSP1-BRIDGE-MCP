using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

[assembly: KspBridgeModule("editor")]

namespace KspBridge.Server.Modules.Editor
{
    /// <summary>
    /// VAB/SPH editor basics (paired with the mod's editor module): enter/leave, part catalog, the ship being built,
    /// craft save/load/new. Assembly (attaching parts) comes in a later module version.
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
