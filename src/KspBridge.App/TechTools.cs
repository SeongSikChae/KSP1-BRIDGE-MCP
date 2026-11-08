using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class TechTools(KspBridgeClient client)
    {
        [McpServerTool(Name = "list_tech", ReadOnly = true)]
        [Description("Tech tree: science, R&D cost limit, and nodes (id, title, cost, state researched/researchable/locked, parents). Default: researched + researchable nodes with their parts; all=true: every node with part_count only. Career mode only.")]
        public Task<string> ListTech([Description("true = whole tree without part lists")] bool all = false, CancellationToken ct = default)
            => client.SendAsync(HttpMethod.Get, all ? "api/tech?all=true" : "api/tech", null, ct);

        [McpServerTool(Name = "research_tech", Destructive = true)]
        [Description("Research a tech node by id (from list_tech): spends science and unlocks its parts. Cannot be undone.")]
        public Task<string> ResearchTech(string id, CancellationToken ct)
            => client.SendAsync(HttpMethod.Post, $"api/tech/{Uri.EscapeDataString(id)}/research", new { }, ct);
    }
}
