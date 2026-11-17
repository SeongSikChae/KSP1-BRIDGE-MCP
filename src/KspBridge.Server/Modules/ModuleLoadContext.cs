using System.Reflection;
using System.Runtime.Loader;

namespace KspBridge.Server.Modules
{
    /// <summary>
    /// 모듈 세대 하나를 담는 collectible ALC. 파일이 잠기지 않도록 스트림으로 읽는다.
    /// 호스트가 가진 어셈블리(호스트, Contracts, MCP SDK, Microsoft.*, System.*)는 기본 ALC와 공유해 타입이 일치하게 한다.
    /// </summary>
    internal sealed class ModuleLoadContext : AssemblyLoadContext
    {
        private readonly string modulesDir;

        public ModuleLoadContext(string modulesDir, int generation)
            : base($"KspBridge.Modules.Gen{generation}", isCollectible: true)
        {
            this.modulesDir = modulesDir;
        }

        public Assembly LoadModuleFile(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return LoadFromStream(stream);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (IsShared(assemblyName.Name))
                return null;

            string candidate = Path.Combine(modulesDir, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadModuleFile(candidate) : null;
        }

        internal static bool IsShared(string? name)
        {
            if (name is null)
                return true;
            if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
                return true;
            bool hostOwned = name.StartsWith("KspBridge.Server", StringComparison.OrdinalIgnoreCase)
                             && !name.StartsWith("KspBridge.Server.Modules.", StringComparison.OrdinalIgnoreCase);
            return hostOwned
                   || name.StartsWith("ModelContextProtocol", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
