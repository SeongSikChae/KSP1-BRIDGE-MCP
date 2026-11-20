using KspBridge.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace KspBridge.Mod.Loader
{
    /// <summary>
    /// Loads a logic generation from PluginData/modules (KSP's own loader ignores PluginData).
    /// Mono cannot unload assemblies, so each generation is loaded from bytes (no file lock) and the old one stays in memory.
    /// References inside a generation resolve to the current generation through AppDomain.AssemblyResolve.
    /// </summary>
    internal sealed class GenerationLoader
    {
        private static readonly Regex ModulesArray = new Regex("\"modules\"\\s*:\\s*\\[(?<items>[^\\]]*)\\]", RegexOptions.Compiled);
        private static readonly Regex StringItem = new Regex("\"(?<v>[^\"]+)\"", RegexOptions.Compiled);

        private readonly Dictionary<string, Assembly> current = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loadedFullNames = new HashSet<string>(StringComparer.Ordinal);

        public GenerationLoader(string modulesDir)
        {
            ModulesDir = modulesDir;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public string ModulesDir { get; }
        public string ManifestPath => Path.Combine(ModulesDir, Protocol.ManifestFileName);

        public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

        /// <summary>Loads the assemblies listed in the manifest and creates (but does not start) the logic entry.</summary>
        public ILogic Load(out List<Assembly> assemblies)
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException("Module manifest not found: " + ManifestPath);

            string manifest = File.ReadAllText(ManifestPath);
            Match match = ModulesArray.Match(manifest);
            if (!match.Success)
                throw new InvalidDataException("Manifest has no \"modules\" array: " + ManifestPath);
            List<string> files = StringItem.Matches(match.Groups["items"].Value).Cast<Match>().Select(m => m.Groups["v"].Value).ToList();
            if (files.Count == 0)
                throw new InvalidDataException("Manifest lists no modules.");

            // Read every file first so a half-copied deploy fails before anything is loaded.
            List<byte[]> images = files.Select(f => File.ReadAllBytes(Path.Combine(ModulesDir, f))).ToList();

            assemblies = new List<Assembly>();
            Dictionary<string, Assembly> next = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (byte[] image in images)
            {
                // Unity Mono returns the already loaded assembly when the simple name matches (version ignored).
                Assembly assembly = Assembly.Load(image);
                string name = assembly.GetName().Name;
                bool sameAsCurrent = current.TryGetValue(name, out Assembly running) && ReferenceEquals(running, assembly);
                if (loadedFullNames.Contains(assembly.FullName) && !sameAsCurrent)
                {
                    throw new InvalidOperationException("Assembly " + assembly.FullName + " belongs to an earlier generation. " +
                                                        "Publish a Mod project (dotnet publish src/KspBridge.Mod.Core), which gives every publish a unique assembly name.");
                }
                // sameAsCurrent: a reload without a new deploy restarts the logic on the current generation's assemblies.
                next[name] = assembly;
                assemblies.Add(assembly);
            }

            // Make the new generation resolvable before touching its types.
            Dictionary<string, Assembly> previous = new Dictionary<string, Assembly>(current, StringComparer.OrdinalIgnoreCase);
            current.Clear();
            foreach (KeyValuePair<string, Assembly> pair in next)
                current[pair.Key] = pair.Value;

            try
            {
                LogicEntryAttribute entry = assemblies
                    .Select(a => (LogicEntryAttribute)Attribute.GetCustomAttribute(a, typeof(LogicEntryAttribute)))
                    .FirstOrDefault(a => a != null);
                if (entry == null)
                    throw new InvalidDataException("No assembly in the manifest has [assembly: LogicEntry].");
                if (entry.ApiVersion != LoaderApi.Version)
                {
                    throw new InvalidOperationException("Logic was built for loader API " + entry.ApiVersion + " but this loader is API " +
                                                        LoaderApi.Version + ". Restart KSP to load the new loader.");
                }
                ILogic logic = (ILogic)Activator.CreateInstance(entry.EntryType);
                foreach (Assembly assembly in assemblies)
                    loadedFullNames.Add(assembly.FullName);
                return logic;
            }
            catch
            {
                current.Clear();
                foreach (KeyValuePair<string, Assembly> pair in previous)
                    current[pair.Key] = pair.Value;
                throw;
            }
        }

        private Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            return current.TryGetValue(name, out Assembly assembly) ? assembly : null;
        }

        /// <summary>Manifest timestamp and size, to detect a new deploy by polling.</summary>
        public string ManifestStamp()
        {
            try
            {
                FileInfo info = new FileInfo(ManifestPath);
                return info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
