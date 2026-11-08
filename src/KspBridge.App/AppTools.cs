using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    internal sealed class AppTools(ToolHost host)
    {
        [McpServerTool(Name = "reload_app", Destructive = true)]
        [Description("Reload the KspBridge MCP tools DLL (Tools/KspBridge.App.Tools.dll next to the MCP server) without restarting the MCP client. The result lists the versions before/after, the loaded tools and load_count. If the new DLL fails to load, the previous tools are kept.")]
        public string ReloadApp() => host.Reload();
    }
}
