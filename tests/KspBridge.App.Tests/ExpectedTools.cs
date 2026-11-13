namespace KspBridge.App.Tests
{
    /// <summary>Tool names the tests expect; update here when KspBridge.App.Tools gains or loses a tool.</summary>
    internal static class ExpectedTools
    {
        /// <summary>Tools defined in KspBridge.App.Tools.dll, sorted.</summary>
        public static readonly string[] Loaded = ["abort_flight", "add_part", "check_craft", "debug_add_part_timeout", "debug_clear", "debug_engine_stop", "debug_input_lock", "debug_input_locks_error", "debug_reload_fault", "debug_stage_delay", "debug_throttle_hold", "get_craft", "get_flight", "get_flight_events", "get_part", "get_status", "launch_craft", "leave_flight", "list_crafts", "list_parts", "new_craft", "offset_part", "open_editor", "rcs_translate", "recover_vessel", "reload_mod", "remove_part", "render_craft", "replace_flight_steps", "revert_flight", "rotate_part", "save_craft", "set_part_field", "set_rcs", "set_resource", "set_sas", "set_stage", "set_time_warp", "submit_flight_plan"];

        /// <summary>Loaded tools plus the shell's reload_app, sorted.</summary>
        public static readonly string[] WithShell = [.. Loaded.Append("reload_app").Order()];
    }
}
