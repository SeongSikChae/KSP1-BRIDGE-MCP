using KspBridge.Server.Contracts;

// Pairs with the mod's ascent module. It has no MCP tools: the Planner puts ascend and circularize steps into a plan with
// submit_plan (their parameters are in list_step_types).
[assembly: KspBridgeModule("ascent")]
