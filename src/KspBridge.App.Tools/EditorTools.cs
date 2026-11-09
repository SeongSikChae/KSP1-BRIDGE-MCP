using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
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
