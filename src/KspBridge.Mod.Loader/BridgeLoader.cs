using KspBridge.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KspBridge.Mod.Loader
{
    /// <summary>
    /// Fixed host loaded once by KSP. Owns the HTTP port, forwards Update/FixedUpdate to the current logic generation,
    /// and swaps generations when manifest.json changes or POST /loader/reload arrives. Swaps happen at the start of Update.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class BridgeLoader : MonoBehaviour, ILoaderHost
    {
        private const float ManifestPollSeconds = 1f;
        private const float ManifestSettleSeconds = 0.5f;

        private HttpBridge http;
        private GenerationLoader loader;
        private ILogic logic;
        private string carry = "";
        private string seenStamp;
        private string pendingStamp;
        private float pendingSince;
        private float nextPoll;
        private bool firstLoadDone;
        private int updateErrors;

        public string LoaderVersion { get; } = typeof(BridgeLoader).Assembly.GetName().Version.ToString();
        public int Generation { get; private set; }
        public DateTime LoadedAtUtc { get; private set; }
        public string LastError { get; private set; }
        public IList<Assembly> GenerationAssemblies { get; private set; } = new List<Assembly>();

        public void Log(string message) => Debug.Log("[KspBridge] " + message);
        public void LogError(string message) => Debug.LogError("[KspBridge] " + message);

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            string root = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KspBridge");
            loader = new GenerationLoader(Path.GetFullPath(Path.Combine(root, "PluginData/modules")));
            int port = ReadPort(Path.Combine(root, "settings.cfg"));
            http = new HttpBridge(port, LogError);
            try
            {
                http.Start();
                Log("loader " + LoaderVersion + " (API " + LoaderApi.Version + ", protocol " + Protocol.Version + ") listening on 127.0.0.1:" + port);
            }
            catch (Exception ex)
            {
                LastError = "HTTP listener failed on port " + port + ": " + ex.Message;
                LogError(LastError);
            }
        }

        private void OnDestroy()
        {
            http?.Stop();
            StopLogic();
            loader?.Dispose();
        }

        private void Update()
        {
            if (!firstLoadDone)
            {
                firstLoadDone = true;
                seenStamp = loader.ManifestStamp();
                Swap("startup");
            }
            PollManifest();
            DrainRequests();
            Forward(l => l.Update());
        }

        private void FixedUpdate() => Forward(l => l.FixedUpdate());

        private void Forward(Action<ILogic> call)
        {
            if (logic == null)
                return;
            try
            {
                call(logic);
            }
            catch (Exception ex)
            {
                // Rate-limit: a broken generation would otherwise flood the log every frame.
                if (updateErrors++ < 5 || updateErrors % 500 == 0)
                    LogError("logic generation " + Generation + " frame error (" + updateErrors + "): " + ex);
            }
        }

        /// <summary>A new deploy writes manifest.json last; reload once its stamp is stable for a moment.</summary>
        private void PollManifest()
        {
            if (Time.realtimeSinceStartup < nextPoll)
                return;
            nextPoll = Time.realtimeSinceStartup + ManifestPollSeconds;
            string stamp = loader.ManifestStamp();
            if (stamp == null || stamp == seenStamp)
            {
                pendingStamp = null;
                return;
            }
            if (stamp != pendingStamp)
            {
                pendingStamp = stamp;
                pendingSince = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - pendingSince >= ManifestSettleSeconds)
            {
                seenStamp = stamp;
                pendingStamp = null;
                Swap("manifest");
            }
        }

        private bool Swap(string reason)
        {
            ILogic next;
            List<Assembly> assemblies;
            try
            {
                next = loader.Load(out assemblies);
            }
            catch (Exception ex)
            {
                LastError = "generation " + (Generation + 1) + " load failed (" + reason + "): " + ex.Message;
                LogError(LastError + "\n" + ex);
                return false;
            }

            StopLogic();
            Generation++;
            LoadedAtUtc = DateTime.UtcNow;
            GenerationAssemblies = assemblies;
            LastError = null;
            updateErrors = 0;
            try
            {
                next.Start(this, carry);
                logic = next;
                Log("logic generation " + Generation + " started (" + reason + "): " + string.Join(", ", assemblies.ConvertAll(a => a.GetName().Name + " " + a.GetName().Version)));
                return true;
            }
            catch (Exception ex)
            {
                LastError = "generation " + Generation + " start failed: " + ex.Message;
                LogError(LastError + "\n" + ex);
                try { next.Stop(); } catch (Exception) { }
                logic = null;
                return false;
            }
        }

        private void StopLogic()
        {
            if (logic == null)
                return;
            try
            {
                carry = logic.Stop() ?? "";
            }
            catch (Exception ex)
            {
                carry = "";
                LogError("logic generation " + Generation + " stop failed: " + ex);
            }
            logic = null;
        }

        private void DrainRequests()
        {
            while (http != null && http.TryDequeue(out PendingRequest request))
            {
                if (!request.TryTake())
                    continue;   // already answered mod_busy: must not run
                try
                {
                    Route(request);
                }
                catch (Exception ex)
                {
                    request.Status = 500;
                    request.Response = LoaderJson.Error(ErrorCodes.Internal, ex.GetType().Name + ": " + ex.Message);
                    LogError("request " + request.Path + " failed: " + ex);
                }
                request.Done.Set();
            }
        }

        private void Route(PendingRequest request)
        {
            // Loader status answers every protocol version: it is how a mismatched server learns the mod's versions.
            if (request.Path == Protocol.LoaderStatusPath)
            {
                request.Response = LoaderJson.Ok(StatusJson());
                return;
            }

            if (request.ProtocolHeader != null && request.ProtocolHeader != Protocol.Version.ToString())
            {
                request.Status = 409;
                request.Response = LoaderJson.Error(ErrorCodes.ProtocolMismatch,
                    "Protocol mismatch: server " + request.ProtocolHeader + ", mod " + Protocol.Version + ". Deploy matching builds.");
                return;
            }
            if (request.Path == Protocol.LoaderReloadPath && request.Method == "POST")
            {
                seenStamp = loader.ManifestStamp();
                bool ok = Swap("request");
                request.Response = ok
                    ? LoaderJson.Ok(StatusJson())
                    : LoaderJson.Error(ErrorCodes.ReloadFailed, LastError + (logic != null
                        ? " Generation " + Generation + " keeps running."
                        : " No logic generation is running."));
                return;
            }
            if (request.Path.StartsWith(Protocol.ApiPrefix, StringComparison.Ordinal) && request.Method == "POST")
            {
                if (logic == null)
                {
                    request.Status = 503;
                    request.Response = LoaderJson.Error(ErrorCodes.LogicNotLoaded,
                        "No logic generation is loaded." + (LastError != null ? " Last error: " + LastError : ""));
                    return;
                }
                request.Response = logic.Handle(request.Path.Substring(Protocol.ApiPrefix.Length), request.Body ?? "");
                return;
            }
            request.Status = 404;
            request.Response = LoaderJson.Error(ErrorCodes.UnknownRoute, request.Method + " " + request.Path);
        }

        private string StatusJson()
            => "{\"loader_version\":" + LoaderJson.Quote(LoaderVersion)
               + ",\"loader_api\":" + LoaderApi.Version
               + ",\"protocol\":" + Protocol.Version
               + ",\"generation\":" + Generation
               + ",\"loaded_at\":" + (Generation > 0 ? LoaderJson.Quote(LoadedAtUtc.ToString("o", CultureInfo.InvariantCulture)) : "null")
               + ",\"logic_loaded\":" + (logic != null ? "true" : "false")
               + ",\"last_error\":" + LoaderJson.Quote(LastError) + "}";

        private static int ReadPort(string path)
        {
            try
            {
                ConfigNode root = ConfigNode.Load(path);
                ConfigNode node = root?.GetNode("KSPBRIDGE");
                if (node != null && int.TryParse(node.GetValue("port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
                    return port;
            }
            catch (Exception)
            {
            }
            return Protocol.DefaultModPort;
        }
    }
}
