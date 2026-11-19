// Shared between KspBridge.Server (net10.0) and KspBridge.Mod.* (net472) as a linked source file.
// Keep it dependency-free and compatible with both targets.
namespace KspBridge.Shared
{
    /// <summary>Wire protocol between KspBridge.Server and the KSP mod (HTTP on loopback).</summary>
    internal static class Protocol
    {
        /// <summary>Bump when the HTTP contract between Server and Mod changes incompatibly.</summary>
        public const int Version = 1;

        /// <summary>Request header carrying <see cref="Version"/>.</summary>
        public const string VersionHeader = "X-KspBridge-Protocol";

        public const int DefaultModPort = 65530;
        public const int DefaultServerPort = 65531;

        public const string LoaderStatusPath = "/loader/status";
        public const string LoaderReloadPath = "/loader/reload";

        /// <summary>Prefix of module routes: POST /api/{module}/{op}.</summary>
        public const string ApiPrefix = "/api/";

        /// <summary>Name of the file a deploy writes last; hosts reload a generation when it changes.</summary>
        public const string ManifestFileName = "manifest.json";
    }

    /// <summary>Error codes in <c>{"ok":false,"error":{"code":...}}</c> responses.</summary>
    internal static class ErrorCodes
    {
        /// <summary>The Server could not connect to the mod HTTP endpoint (KSP not running or mod not loaded).</summary>
        public const string ModUnreachable = "mod_unreachable";

        /// <summary>The mod accepted the request but the game thread did not handle it in time.</summary>
        public const string ModBusy = "mod_busy";

        /// <summary>Server and Mod speak different <see cref="Protocol.Version"/>s.</summary>
        public const string ProtocolMismatch = "protocol_mismatch";

        /// <summary>No mod logic generation is loaded (load failed or not yet loaded).</summary>
        public const string LogicNotLoaded = "logic_not_loaded";

        /// <summary>A reload was refused or failed; the message says whether the previous generation keeps running.</summary>
        public const string ReloadFailed = "reload_failed";

        /// <summary>The game is in a state where the request cannot run (scene loading, no vessel).</summary>
        public const string NotReady = "not_ready";

        public const string UnknownRoute = "unknown_route";
        public const string BadRequest = "bad_request";
        public const string Internal = "internal";
    }
}
