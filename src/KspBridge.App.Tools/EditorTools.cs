using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KspBridge.App
{
    public sealed record NodeSpec(
        [property: JsonPropertyName("parent"), Description("Parent node id, e.g. bottom")] string Parent,
        [property: JsonPropertyName("child"), Description("Child node id; default the opposite top/bottom")] string? Child = null);

    public sealed record SurfaceSpec(
        [property: JsonPropertyName("azimuth"), Description("Degrees around the parent's +Y axis through its colliders' centre, 0 = parent +Z, 90 = parent +X")] double Azimuth,
        [property: JsonPropertyName("height"), Description("0..1 from the parent's bottom to top")] double Height,
        [property: JsonPropertyName("angle"), Description("Degrees about the surface normal")] double Angle = 0);

    public sealed record SymmetrySpec(
        [property: JsonPropertyName("mode"), Description("radial or mirror")] string Mode,
        [property: JsonPropertyName("count"), Description("1..8 copies in total (mirror: 2)")] int Count);

    /// <summary>VAB/SPH craft design tools. Each call is forwarded to the mod's /api/editor routes.</summary>
    [McpServerToolType]
    public sealed class EditorTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "open_editor", Destructive = true)]
        [Description("Open the VAB or SPH from the space center or the editor, optionally loading a saved craft (unsaved editor changes are discarded). " +
            "Returns {accepted} at once; poll get_status until editor.ready is true before using other editor tools.")]
        public Task<string> OpenEditor(
            [Description("VAB or SPH")] string facility,
            [Description("Saved craft name to load (from list_crafts); omit for an empty editor")] string? craft = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/open", new { facility, craft }, ct);

        [McpServerTool(Name = "new_craft", Destructive = true)]
        [Description("Start a new empty craft in the current editor (unsaved changes are discarded). " +
            "Returns {accepted} at once; poll get_status until editor.ready is true. The name is applied when the editor is ready.")]
        public Task<string> NewCraft(
            [Description("Craft name; omit to keep the game's default name")] string? name = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/new", new { name }, ct);

        [McpServerTool(Name = "get_craft", ReadOnly = true)]
        [Description("The craft in the VAB/SPH editor: editor state and every part with id (persistentId, use it in other editor tools), part name, " +
            "parent id, attach (root | node{parent_node, child_node} | surface), symmetry {mode, group ids}, stage, and position [x,y,z] m / " +
            "rotation [x,y,z] deg in root-part space (+Y = rocket up / plane nose, same in VAB and SPH). Requires a ready editor.")]
        public Task<string> GetCraft(CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, "api/editor/craft", null, ct);

        [McpServerTool(Name = "add_part", Destructive = false)]
        [Description("Add a part to the editor craft. Empty craft: give only part (it becomes the root). Otherwise give parent (id from get_craft) and " +
            "either node {parent: parent node id from get_part, child: child node id, default the opposite top/bottom} for a stack attach, " +
            "or surface {azimuth deg in the parent's own frame around its +Y axis through the centre of its colliders (0 = parent +Z, 90 = parent +X; " +
            "use get_craft rotation to find which parent direction faces outward), height 0..1 bottom to top of the parent, angle deg about the surface normal} " +
            "with optional symmetry {mode radial (count 1..8, copies turned about the parent's +Y) | mirror (count 2, reflected through the root part's up/forward plane)}. " +
            "All or nothing: on error the craft is unchanged. " +
            "Returns added parts (with ids) and structure warnings; one editor Undo step per call.")]
        public Task<string> AddPart(
            [Description("Part name from list_parts")] string part,
            [Description("Parent part id from get_craft; omit for the root of an empty craft")] uint? parent = null,
            [Description("Stack attach: parent node id and optional child node id")] NodeSpec? node = null,
            [Description("Surface attach position on the parent")] SurfaceSpec? surface = null,
            [Description("Symmetry for surface attach; VAB default radial, SPH default mirror")] SymmetrySpec? symmetry = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/parts", new { part, parent, node, surface, symmetry }, ct);

        [McpServerTool(Name = "remove_part", Destructive = true)]
        [Description("Remove a part from the editor craft together with its symmetry counterparts and every part attached below them. " +
            "The root part cannot be removed (use new_craft). Returns the removed ids; one editor Undo step per call " +
            "(an editor Undo reloads the craft and gives every part a new id: call get_craft again after one).")]
        public Task<string> RemovePart(
            [Description("Part id from get_craft")] uint id,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Delete, "api/editor/parts/" + id, null, ct);

        [McpServerTool(Name = "offset_part", Destructive = false)]
        [Description("Move a part (with its symmetry counterparts and the parts attached below them) by x, y, z metres in root-part space " +
            "(+Y = rocket up / plane nose). Counterparts move by the mirrored or turned distance; moving the root moves the whole craft. " +
            "Parts can be moved apart from their parent (no editor snap-back). Each value is limited to ±1000. " +
            "Returns the moved part and its counterparts (attached parts move with them but are not listed); one editor Undo step per call.")]
        public Task<string> OffsetPart(
            [Description("Part id from get_craft")] uint id,
            [Description("Metres along root-part X")] double x = 0,
            [Description("Metres along root-part Y")] double y = 0,
            [Description("Metres along root-part Z")] double z = 0,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/parts/" + id + "/offset", new { x, y, z }, ct);

        [McpServerTool(Name = "rotate_part", Destructive = false)]
        [Description("Turn a part (with its symmetry counterparts and the parts attached below them) by angle degrees about a root-part axis " +
            "through the point where it attaches to its parent (the part origin for the root). Mirror counterparts turn the mirrored way, " +
            "radial ones about their own turned axis. Angle is limited to ±1000. " +
            "Returns the turned part and its counterparts (attached parts turn with them but are not listed); one editor Undo step per call.")]
        public Task<string> RotatePart(
            [Description("Part id from get_craft")] uint id,
            [Description("x, y or z axis of root-part space")] string axis,
            [Description("Degrees; positive turns by the left-hand rule about the axis (Unity)")] double angle,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/parts/" + id + "/rotate", new { axis, angle }, ct);

        [McpServerTool(Name = "set_part_field", Destructive = false)]
        [Description("Change an editor field of a part module (the fields get_part lists, e.g. ModuleEngines thrustPercentage), " +
            "like the part action window: symmetry counterparts get the same value. value is a number, true/false or text " +
            "(enum or option name) matching the field, within the placed part's control range (get_part shows the catalog part's range). " +
            "With several modules of the same name, the first enabled one showing the field is changed. " +
            "Returns the changed part and its counterparts with values (each part's field value read back after the change); " +
            "one editor Undo step per call that changes something.")]
        public Task<string> SetPartField(
            [Description("Part id from get_craft")] uint id,
            [Description("Module name from get_part fields, e.g. ModuleEngines")] string module,
            [Description("Field name from get_part fields, e.g. thrustPercentage")] string field,
            [Description("New value: number, true/false or text")] JsonElement value,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/parts/" + id + "/field", new { module, field, value }, ct);

        [McpServerTool(Name = "set_resource", Destructive = false)]
        [Description("Fill a resource of a part (e.g. LiquidFuel, Oxidizer, SolidFuel) to a fraction of its maximum, like the part action " +
            "window slider: symmetry counterparts get the same amount. Returns the changed part and its counterparts with values " +
            "(each part's amount and max after the change); one editor Undo step per call.")]
        public Task<string> SetResource(
            [Description("Part id from get_craft")] uint id,
            [Description("Resource name from get_part resources")] string resource,
            [Description("0..1 of the maximum amount")] double fraction,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/editor/parts/" + id + "/resource", new { resource, fraction }, ct);

        [McpServerTool(Name = "list_parts", ReadOnly = true)]
        [Description("Part catalog summary: name (use it in add_part), title, category, cost, mass (t), size (bulkhead profiles) and unlocked. " +
            "Career/science list only unlocked, purchased parts unless unlocked=false. Works in any scene with a loaded save.")]
        public Task<string> ListParts(
            [Description("Category filter: Pods, FuelTank, Engine, Control, Structural, Coupling, Payload, Aero, Ground, Thermal, Electrical, Communication, Science, Cargo, Robotics, Utility")] string? category = null,
            [Description("Case-insensitive substring of the part name or title")] string? q = null,
            [Description("Only parts usable in this save (default true)")] bool unlocked = true,
            CancellationToken ct = default)
        {
            List<string> query = [];
            if (!string.IsNullOrWhiteSpace(category))
            {
                query.Add("category=" + Uri.EscapeDataString(category));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                query.Add("q=" + Uri.EscapeDataString(q));
            }
            if (!unlocked)
            {
                query.Add("unlocked=false");
            }
            return client.SendAsync(HttpMethod.Get, "api/parts" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), null, ct);
        }

        [McpServerTool(Name = "get_part", ReadOnly = true)]
        [Description("Part details: summary plus attach nodes (id, size, position, orientation in part space; use the ids in add_part node), " +
            "allow_stack, surface_attach (can be surface-attached), allow_surface_children, resources (amount/max) and " +
            "editor fields (module, field, value, min/max) that set_part_field can change.")]
        public Task<string> GetPart(
            [Description("Exact part name from list_parts, e.g. mk1pod.v2")] string name,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, "api/parts/" + Uri.EscapeDataString(name), null, ct);
    }
}
