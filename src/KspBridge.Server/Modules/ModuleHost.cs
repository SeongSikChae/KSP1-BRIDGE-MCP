using System.Diagnostics.CodeAnalysis;
using KspBridge.Server.Contracts;
using KspBridge.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace KspBridge.Server.Modules
{
    /// <summary>
    /// 서버 모듈 세대를 관리한다. 모든 MCP 요청이 공유하는 도구 컬렉션 <see cref="Tools"/>를 갖고,
    /// 새 세대를 원자적으로 읽어 도구를 교체한 뒤 이전 세대 ALC를 내린다. 읽기에 실패하면 이전 세대를 유지한다.
    /// </summary>
    public sealed class ModuleHost : IServerHostInfo
    {
        private readonly IServiceProvider services;
        private readonly ILogger<ModuleHost> logger;
        private readonly object gate = new();
        private ModuleLoadContext? context;
        private List<McpServerTool> moduleTools = [];
        private IReadOnlyList<ModuleVersion> modules = [];

        public ModuleHost(IServiceProvider services, AppConfiguration configuration, ILogger<ModuleHost> logger)
        {
            this.services = services;
            this.logger = logger;
            ModulesDir = configuration.ResolveModulesDir();
            foreach (McpServerTool tool in CreateTools(typeof(HostTools)))
                Tools.Add(tool);
        }

        public string ModulesDir { get; }

        /// <summary>모든 MCP 요청이 공유하는 도구 컬렉션(호스트 도구 + 현재 세대 모듈 도구).</summary>
        public McpServerPrimitiveCollection<McpServerTool> Tools { get; } = new();

        public string HostVersion { get; } = typeof(ModuleHost).Assembly.GetName().Version?.ToString() ?? "0";
        public int ProtocolVersion => Protocol.Version;
        public int Generation { get; private set; }
        public DateTimeOffset? LoadedAt { get; private set; }
        public IReadOnlyList<ModuleVersion> Modules => modules;
        public string? LastError { get; private set; }

        /// <summary>마지막으로 내린 세대의 ALC. 언로드 확인(테스트)용.</summary>
        internal WeakReference? LastUnloadedContext { get; private set; }

        /// <summary>매니페스트를 읽어 새 세대를 올린다. 성공하면 true.</summary>
        public bool Reload()
        {
            lock (gate)
            {
                int next = Generation + 1;
                if (TryLoad(next, out List<McpServerTool>? newTools, out List<ModuleVersion>? newModules, out ModuleLoadContext? candidate))
                {
                    Swap(newTools);
                    ModuleLoadContext? previous = context;
                    context = candidate;
                    moduleTools = newTools;
                    modules = newModules;
                    Generation = next;
                    LoadedAt = DateTimeOffset.Now;
                    LastError = null;
                    Unload(previous);
                    logger.LogInformation("Loaded server module generation {Generation}: {Modules} ({Tools} tools)",
                        next, string.Join(", ", newModules.Select(m => $"{m.Name} {m.Version}")), newTools.Count);
                    return true;
                }
                return false;
            }
        }

        private bool TryLoad(int next,
            [NotNullWhen(true)] out List<McpServerTool>? newTools,
            [NotNullWhen(true)] out List<ModuleVersion>? newModules,
            [NotNullWhen(true)] out ModuleLoadContext? candidate)
        {
            newTools = null;
            newModules = null;
            candidate = null;
            string manifestPath = Path.Combine(ModulesDir, Protocol.ManifestFileName);
            try
            {
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException("Module manifest not found.", manifestPath);

                ModuleManifest manifest = ModuleManifest.Read(manifestPath);
                candidate = new ModuleLoadContext(ModulesDir, next);
                // 세대마다 직렬화 옵션을 새로 만든다. 공유 기본 옵션의 타입 캐시가 모듈 타입을 잡아 ALC 언로드를 막기 때문이다.
                JsonSerializerOptions serializerOptions = new(McpJsonUtilities.DefaultOptions);
                List<McpServerTool> tools = [];
                List<ModuleVersion> loaded = [];
                foreach (string file in manifest.Modules)
                {
                    Assembly assembly = candidate.LoadModuleFile(Path.Combine(ModulesDir, file));
                    KspBridgeModuleAttribute marker = assembly.GetCustomAttribute<KspBridgeModuleAttribute>()
                        ?? throw new InvalidDataException($"{file} has no [assembly: KspBridgeModule].");
                    loaded.Add(new ModuleVersion(marker.Name, assembly.GetName().Version?.ToString() ?? "0"));
                    foreach (Type type in assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
                        tools.AddRange(CreateTools(type, serializerOptions));
                }

                EnsureUniqueNames(tools);
                newTools = tools;
                newModules = loaded;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                logger.LogError(ex, "Server module generation {Generation} failed to load; keeping generation {Current}", next, Generation);
                candidate?.Unload();
                candidate = null;
                return false;
            }
        }

        private void Swap(List<McpServerTool> newTools)
        {
            // 도구 교체를 한 번의 list_changed 알림으로 묶는다.
            using IDisposable defer = Tools.DeferChangedEvents();
            foreach (McpServerTool tool in moduleTools)
                Tools.Remove(tool);
            foreach (McpServerTool tool in newTools)
                Tools.Add(tool);
        }

        private void EnsureUniqueNames(List<McpServerTool> newTools)
        {
            HashSet<string> hostNames = Tools.Except(moduleTools).Select(t => t.ProtocolTool.Name).ToHashSet();
            List<string> duplicates = newTools.Select(t => t.ProtocolTool.Name)
                .GroupBy(n => n).Where(g => g.Count() > 1 || hostNames.Contains(g.Key)).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
                throw new InvalidDataException($"Duplicate tool names: {string.Join(", ", duplicates)}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Unload(ModuleLoadContext? previous)
        {
            if (previous is null)
                return;
            LastUnloadedContext = new WeakReference(previous);
            previous.Unload();
        }

        private IEnumerable<McpServerTool> CreateTools(Type type, JsonSerializerOptions? serializerOptions = null)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                    continue;
                McpServerToolCreateOptions options = new() { Services = services, SerializerOptions = serializerOptions };
                yield return method.IsStatic
                    ? McpServerTool.Create(method, (object?)null, options)
                    : McpServerTool.Create(method, request => ActivatorUtilities.CreateInstance(request.Services ?? services, type), options);
            }
        }
    }
}
