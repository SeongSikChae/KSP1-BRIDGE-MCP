using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class DialogTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "list_dialogs", ReadOnly = true)]
        [Description("Open game popups (index, name, title, message, button texts) and the experiment results dialog (pages with result text and science values).")]
        public Task<string> ListDialogs(CancellationToken ct)
            => client.SendAsync(HttpMethod.Get, "api/dialogs", null, ct);

        [McpServerTool(Name = "click_dialog_button", Destructive = true)]
        [Description("Press a button on an open popup. dialog: name/title or index (optional when only one popup is open). button: button text or index. The button's action runs as if clicked (it may launch, recover, delete, etc.).")]
        public Task<string> ClickDialogButton(
            [Description("Button text (exact or unique substring) or index as a string number")] string button,
            [Description("Popup name/title or index; omit when only one popup is open")] string? dialog = null,
            CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Post, "api/dialogs/click", new { dialog = Key(dialog), button = Key(button) }, ct);

        [McpServerTool(Name = "close_experiment_dialog")]
        [Description("Close the experiment results dialog. The data stays in the parts.")]
        public Task<string> CloseExperimentDialog(CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, "api/dialogs/close-experiment", new { }, ct);

        /// <summary>"2" → index 2, other text stays text.</summary>
        internal static object? Key(string? value)
            => value is null ? null : int.TryParse(value, out int i) ? i : value;
    }
}
