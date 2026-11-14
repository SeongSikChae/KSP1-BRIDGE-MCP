namespace KspBridge.App.Tests
{
    /// <summary>Tool names the tests expect; update here when KspBridge.App.Tools gains or loses a tool.</summary>
    internal static class ExpectedTools
    {
        /// <summary>Tools defined in KspBridge.App.Tools.dll, sorted.</summary>
        public static readonly string[] Loaded = ["abort_flight", "add_maneuver_node", "add_part", "check_craft", "debug_add_part_timeout", "debug_clear", "debug_dock_reject","debug_engine_stop", "debug_input_lock", "debug_input_locks_error", "debug_reload_fault", "debug_stage_delay", "debug_throttle_hold", "get_action_groups", "get_commnet", "get_controller_trace", "get_craft", "get_flight", "get_flight_events", "get_part", "get_resource_transfer", "get_status", "get_vessel_parts", "invoke_part_event", "launch_craft", "leave_flight", "list_crafts", "list_parts", "list_vessels", "new_craft", "offset_part", "open_editor", "open_tracking_station", "plan_maneuver", "rcs_translate", "recover_vessel", "reload_mod", "remove_maneuver_node", "remove_part", "render_craft", "replace_flight_steps", "revert_flight", "rotate_part", "save_craft", "set_action_group", "set_part_field", "set_rcs", "set_resource", "set_resource_flow", "set_sas", "set_stage", "set_target", "set_time_warp", "set_vessel_part_field", "start_resource_transfer", "stop_resource_transfer", "submit_flight_plan", "switch_vessel", "update_maneuver_node"];

        /// <summary>Loaded tools plus the shell's reload_app, sorted.</summary>
        public static readonly string[] WithShell = [.. Loaded.Append("reload_app").Order()];
    }
}
