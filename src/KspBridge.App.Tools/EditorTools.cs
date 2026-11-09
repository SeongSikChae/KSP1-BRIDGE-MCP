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
    }
}
