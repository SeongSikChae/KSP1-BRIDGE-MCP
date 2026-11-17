using System;
using System.Collections.Generic;
using System.Reflection;

namespace KspBridge.Mod.Loader
{
    /// <summary>
    /// Fixed contract between the loader (never reloaded) and a logic generation (reloaded).
    /// Bump <see cref="Version"/> on any change; a generation built against another version is refused.
    /// </summary>
    public static class LoaderApi
    {
        public const int Version = 1;
    }

    /// <summary>Entry of a logic generation. All calls come on the Unity main thread.</summary>
    public interface ILogic
    {
        /// <summary>Starts the generation. <paramref name="carry"/> is what the previous generation's <see cref="Stop"/> returned ("" on first load).</summary>
        void Start(ILoaderHost host, string carry);

        /// <summary>Stops the generation: ends running work, releases every subscription and control hook, returns state for the next one.</summary>
        string Stop();

        void Update();

        void FixedUpdate();

        /// <summary>Handles POST /api/{route}. <paramref name="route"/> is "module/op". Returns the full response JSON ({"ok":...}).</summary>
        string Handle(string route, string body);
    }

    /// <summary>Services the loader offers to a logic generation.</summary>
    public interface ILoaderHost
    {
        string LoaderVersion { get; }
        int Generation { get; }
        DateTime LoadedAtUtc { get; }
        string LastError { get; }

        /// <summary>Assemblies of this generation, in manifest order.</summary>
        IList<Assembly> GenerationAssemblies { get; }

        void Log(string message);
        void LogError(string message);
    }

    /// <summary>Marks the logic assembly and its <see cref="ILogic"/> type.</summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class LogicEntryAttribute : Attribute
    {
        public LogicEntryAttribute(Type entryType, int apiVersion)
        {
            EntryType = entryType;
            ApiVersion = apiVersion;
        }

        public Type EntryType { get; }
        public int ApiVersion { get; }
    }
}
