using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class CraftTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "list_parts", ReadOnly = true)]
        [Description("Part catalog. Summary by default (name, title, category, cost, mass t, size, unlocked). detail=true adds attach nodes, surface attach, resources, engine thrust/Isp/propellants, decoupler/parachute flags — combine with category or name to keep the result small. In career, only unlocked parts by default.")]
        public Task<string> ListParts(
            [Description("Category filter, e.g. Pods, FuelTank, Engine, Coupling, Utility, Science, Aero, Structural")] string? category = null,
            [Description("Only unlocked parts (career). Default true.")] bool unlockedOnly = true,
            [Description("Include full details")] bool detail = false,
            [Description("Substring filter on part name or title")] string? name = null,
            CancellationToken ct = default)
        {
            string path = "api/parts?unlocked=" + (unlockedOnly ? "true" : "false") + "&detail=" + (detail ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(category))
            {
                path += "&category=" + Uri.EscapeDataString(category);
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                path += "&name=" + Uri.EscapeDataString(name);
            }
            return client.SendAsync(HttpMethod.Get, path, null, ct);
        }

        [McpServerTool(Name = "build_craft", Destructive = true)]
        [Description("""
            Build a craft from a design tree and save it to the save's Ships/VAB (or SPH). Use dryRun=true first to see errors, warnings, cost, mass and per-stage delta-v/TWR estimates.
            design = { "part": <root part name>, "children": [ { "node": <parent attach node, e.g. "bottom">, "part": ..., "children": [...], "radial": [...] } ],
                       "radial": [ { "part": ..., "symmetry": 1..8, "angle": deg, "height": m, "children": [ { "node": "srf", "part": ... } ] } ], "stage": optional int }
            Stack children hang below/above through attach nodes (child uses its opposite top/bottom node). Radial entries are surface-attached around the parent with symmetry
            (height is relative to the parent's mesh centre). A child with node "srf" is surface-attached on the outer face of its parent (e.g. booster on a radial decoupler).
            Staging is automatic (bottom engines first, parachutes last) unless "stage" is given.
            Engines (liquid or solid) take "thrust_limit": 0..100 (percent, the VAB thrust limiter): less thrust and G, same delta-v, longer burn.
            Any part takes "resources": { "<resource>": 0..1 } = initial fill fraction (e.g. { "SolidFuel": 0.08 } for a short SRB burn); default full.
            """)]
        public Task<string> BuildCraft(
            [Description("Craft name (file name)")] string name,
            [Description("Design tree (see tool description)")] JsonElement design,
            [Description("VAB or SPH")] string facility = "VAB",
            [Description("Validate and estimate only, do not save")] bool dryRun = true,
            [Description("Overwrite an existing craft with the same name")] bool overwrite = false,
            CancellationToken ct = default)
        {
            // Some clients send the untyped design argument as a JSON string: send the object it contains.
            if (design.ValueKind == JsonValueKind.String)
            {
                try
                {
                    design = JsonDocument.Parse(design.GetString()!).RootElement;
                }
                catch (JsonException e)
                {
                    return Task.FromResult("error: bad_request: design is a string but not valid JSON: " + e.Message);
                }
            }
            return client.SendAsync(HttpMethod.Post, "api/craft/build",
                new { name, facility, dry_run = dryRun, overwrite, root = design }, ct);
        }
    }
}
