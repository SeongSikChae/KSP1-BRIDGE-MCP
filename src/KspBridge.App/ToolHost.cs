using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace KspBridge.App
{
    /// <summary>
    /// Loads the tools DLL into a collectible context and swaps its tools in the server's tool collection.
    /// Tools owned by the shell (reload_app) are never touched.
    /// </summary>
    internal sealed class ToolHost(
        string toolsPath, IServiceProvider services, McpServerPrimitiveCollection<McpServerTool> collection, ILogger<ToolHost> logger)
    {
        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Tools", "KspBridge.App.Tools.dll");

        private readonly Lock _gate = new();
        private AssemblyLoadContext? _context;
        private List<McpServerTool> _tools = [];
        private string? _version;
        private int _loadCount;

        private sealed class ToolsLoadContext() : AssemblyLoadContext("KspBridge.App.Tools", isCollectible: true)
        {
            // Core, the MCP SDK and the BCL must resolve to the shell's copies so tool types match the shell's.
            protected override Assembly? Load(AssemblyName assemblyName) => null;
        }

        public string Reload()
        {
            lock (_gate)
            {
                ToolsLoadContext context = new();
                List<McpServerTool> tools;
                string version;
                try
                {
                    Assembly assembly;
                    using (FileStream stream = File.OpenRead(toolsPath))
                    {
                        assembly = context.LoadFromStream(stream);
                    }
                    tools = CreateTools(assembly);
                    version = DescribeVersion(assembly);
                }
                catch (Exception ex)
                {
                    context.Unload();
                    logger.LogError(ex, "Tools load failed: {Path}", toolsPath);
                    return $"error: reload_failed: {Describe(ex)} (previous tools kept)";
                }

                // One list_changed notification per reload; a same-named tool is removed and re-added back to back
                // so a concurrent call sees it missing only for that instant.
                using (collection.DeferChangedEvents())
                {
                    Dictionary<string, McpServerTool> previous = _tools.ToDictionary(t => t.ProtocolTool.Name);
                    foreach (McpServerTool tool in tools)
                    {
                        if (previous.Remove(tool.ProtocolTool.Name, out McpServerTool? old))
                        {
                            collection.Remove(old);
                        }
                        collection.Add(tool);
                    }
                    foreach (McpServerTool old in previous.Values)
                    {
                        collection.Remove(old);
                    }
                }

                string? versionBefore = _version;
                _context?.Unload();
                _context = context;
                _tools = tools;
                _version = version;
                _loadCount++;

                List<string> names = tools.Select(t => t.ProtocolTool.Name).ToList();
                logger.LogInformation("Tools loaded: {Version} ({Tools}), load {LoadCount}", version, string.Join(", ", names), _loadCount);
                return JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["version_before"] = versionBefore,
                    ["version_after"] = version,
                    ["tools"] = names,
                    ["load_count"] = _loadCount,
                });
            }
        }

        private List<McpServerTool> CreateTools(Assembly assembly)
        {
            List<McpServerTool> tools = [];
            foreach (Type type in assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
                {
                    tools.Add(McpServerTool.Create(
                        method,
                        _ => ActivatorUtilities.CreateInstance(services, type),
                        new McpServerToolCreateOptions { Services = services }));
                }
            }

            HashSet<string> owned = _tools.Select(t => t.ProtocolTool.Name).ToHashSet();
            HashSet<string> seen = [];
            foreach (string name in tools.Select(t => t.ProtocolTool.Name))
            {
                if (!seen.Add(name))
                {
                    throw new InvalidOperationException($"duplicate tool name '{name}'");
                }
                if (collection.PrimitiveNames.Contains(name) && !owned.Contains(name))
                {
                    throw new InvalidOperationException($"tool name '{name}' is reserved by the server");
                }
            }
            return tools;
        }

        private string DescribeVersion(Assembly assembly)
        {
            string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
            return $"{informational}@{File.GetLastWriteTimeUtc(toolsPath):yyyyMMddHHmmss}";
        }

        private static string Describe(Exception ex)
        {
            Exception inner = ex is TargetInvocationException { InnerException: { } innerException } ? innerException : ex;
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }
}
