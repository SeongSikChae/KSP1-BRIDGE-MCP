using KspBridge.Server.Contracts;

// Pairs with the mod's warp module. It has no MCP tools: the Planner puts a warp step into a plan with submit_plan (its
// parameters are in list_step_types) and reads the warp state with get_flight.
[assembly: KspBridgeModule("warp")]
