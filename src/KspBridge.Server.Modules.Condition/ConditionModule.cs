using KspBridge.Server.Contracts;

// Pairs with the mod's condition module. It has no MCP tools: the Planner puts wait_until into a plan with submit_plan (its
// conditions are in list_step_types) and turns the safety aborts on or off with the plan's abort_on.
[assembly: KspBridgeModule("condition")]
