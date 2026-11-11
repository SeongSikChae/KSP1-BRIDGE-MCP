namespace KspBridge.App.Tests
{
    /// <summary>Tool names the tests expect; update here when KspBridge.App.Tools gains or loses a tool.</summary>
    internal static class ExpectedTools
    {
        /// <summary>Tools defined in KspBridge.App.Tools.dll, sorted.</summary>
        public static readonly string[] Loaded = ["add_part", "get_craft", "get_part", "get_status", "list_parts", "new_craft", "offset_part", "open_editor", "reload_mod", "remove_part", "rotate_part", "set_part_field", "set_resource"];

        /// <summary>Loaded tools plus the shell's reload_app, sorted.</summary>
        public static readonly string[] WithShell = [.. Loaded.Append("reload_app").Order()];
    }
}
