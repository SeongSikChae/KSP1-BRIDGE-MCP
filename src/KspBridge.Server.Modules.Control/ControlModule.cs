using KspBridge.Server.Contracts;

// Pairs with the mod's control module. It has no MCP tools: the Planner puts its step types (throttle, attitude, attitude_off,
// stage, action_group) into a plan with submit_plan and reads their params from list_step_types.
[assembly: KspBridgeModule("control")]
