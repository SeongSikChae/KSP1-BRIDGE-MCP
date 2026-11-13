using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KspBridge
{
    /// <summary>
    /// Replaceable logic loaded from PluginData (KSP does not auto-load DLLs there).
    /// Implementations must not use [KSPAddon] and must undo everything they hooked in Stop.
    /// </summary>
    public interface ILogic
    {
        string Version { get; }

        /// <summary>Main thread. Hook events, return the logic routes.</summary>
        IEnumerable<Route> Start(LogicContext context);

        /// <summary>Main thread, every physics tick.</summary>
        void FixedUpdate();

        /// <summary>Main thread. Release triggers, locks, attitude holds and event hooks; return what was released.</summary>
        IList<string> Stop();

        /// <summary>Main thread. Extra fields merged into /api/status; null adds nothing.</summary>
        IDictionary<string, object> Status();
    }

    public sealed class LogicContext
    {
        public MainThreadDispatcher Dispatcher;
        public Settings Settings;
        /// <summary>JSON the stopping logic leaves for the next Start (reload_mod); the next logic clears it once it started.</summary>
        public string Handover;
    }

    /// <summary>Loads, swaps and drives the logic assembly. Main thread only.</summary>
    public static class LogicHost
    {
        public const string LoaderVersion = "0.0.4";

        private static ILogic _logic;
        private static LogicContext _context;
        private static HttpServer _server;
        private static int _loadCount;
        private static string _lastError;

        private static string LogicPath => KSPUtil.ApplicationRootPath + "GameData/KspBridge/PluginData/KspBridge.Logic.dll";

        public static void Init(HttpServer server, LogicContext context)
        {
            _server = server;
            _context = context;
            // A byte-loaded logic assembly references this loader by name; hand it the instance already loaded.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                new AssemblyName(args.Name).Name == typeof(LogicHost).Assembly.GetName().Name ? typeof(LogicHost).Assembly : null;
            try
            {
                ILogic logic = Create();
                List<Route> routes = logic.Start(context).ToList();
                _logic = logic;
                _loadCount++;
                _server.SetRoutes(BaseRoutes().Concat(routes).ToList());
                Debug.Log("[KspBridge] logic " + logic.Version + " loaded");
            }
            catch (Exception e)
            {
                _lastError = Describe(e);
                _server.SetRoutes(BaseRoutes().Concat(NotLoadedRoutes()).ToList());
                Debug.LogError("[KspBridge] logic load failed: " + e);
            }
        }

        public static void FixedUpdate()
        {
            _logic?.FixedUpdate();
        }

        public static void Shutdown()
        {
            try
            {
                _logic?.Stop();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] logic stop failed: " + e);
            }
            _logic = null;
        }

        /// <summary>Routes owned by the loader; always present.</summary>
        public static IEnumerable<Route> BaseRoutes()
        {
            yield return new Route("GET", "/api/status", (m, b, q) => Status());
            yield return new Route("POST", "/api/reload", (m, b, q) => Reload());
        }

        private static IEnumerable<Route> NotLoadedRoutes()
        {
            foreach (string method in new[] { "GET", "POST", "DELETE" })
            {
                yield return new Route(method, "/api/.+", (m, b, q) =>
                    throw new ApiException(503, "logic_not_loaded", "logic is not loaded: " + _lastError + " (fix and call reload_mod)"));
            }
        }

        private static object Status()
        {
            bool inFlight = HighLogic.LoadedScene == GameScenes.FLIGHT;
            Dictionary<string, object> status = new Dictionary<string, object>
            {
                ["scene"] = HighLogic.LoadedScene.ToString(),
                ["has_active_vessel"] = inFlight && FlightGlobals.ActiveVessel != null,
                ["version"] = _logic?.Version,
                ["loader_version"] = LoaderVersion,
                ["load_count"] = _loadCount,
                ["logic_error"] = _logic == null ? _lastError : null,
            };
            if (_logic != null)
            {
                try
                {
                    IDictionary<string, object> extra = _logic.Status();
                    if (extra != null)
                    {
                        foreach (KeyValuePair<string, object> kv in extra)
                        {
                            status[kv.Key] = kv.Value;
                        }
                    }
                }
                catch (Exception e)
                {
                    status["logic_status_error"] = Describe(e);
                }
            }
            return status;
        }

        private static object Reload()
        {
            ILogic next;
            try
            {
                next = Create();
            }
            catch (Exception e)
            {
                throw new ApiException(500, "reload_failed", "new logic could not be loaded, previous logic kept: " + Describe(e));
            }

            ILogic previous = _logic;
            IList<string> released = previous != null ? previous.Stop() : new List<string>();
            List<Route> routes;
            try
            {
                routes = next.Start(_context).ToList();
            }
            catch (Exception e)
            {
                string restore = "";
                if (previous != null)
                {
                    try
                    {
                        _server.SetRoutes(BaseRoutes().Concat(previous.Start(_context)).ToList());
                        restore = " (previous logic restarted)";
                    }
                    catch (Exception e2)
                    {
                        _logic = null;
                        _lastError = Describe(e2);
                        _server.SetRoutes(BaseRoutes().Concat(NotLoadedRoutes()).ToList());
                        DropHandover();
                        restore = " (previous logic could not be restarted: " + _lastError + ")";
                    }
                }
                throw new ApiException(500, "reload_failed", "new logic failed to start: " + Describe(e) + restore);
            }

            _logic = next;
            DropHandover();
            _loadCount++;
            _lastError = null;
            _server.SetRoutes(BaseRoutes().Concat(routes).ToList());
            Debug.Log("[KspBridge] logic reloaded: " + previous?.Version + " -> " + next.Version + " (load " + _loadCount + ")");
            return new Dictionary<string, object>
            {
                ["version_before"] = previous?.Version,
                ["version_after"] = next.Version,
                ["released"] = released.Cast<object>().ToList(),
                ["load_count"] = _loadCount,
            };
        }

        /// <summary>
        /// Nobody took the handover: if a plan was running, leave no throttle behind and restore the plan vessel's SAS group
        /// and the RCS the plan changed. Only a loaded vessel whose persistentId is the plan's vessel_id is touched.
        /// Without a plan key (no plan, minimal handover after a serialization failure) the user's manual throttle and SAS are left alone.
        /// </summary>
        private static void DropHandover()
        {
            if (_context.Handover == null) return;
            try
            {
                // Minimal parse: only {..., plan: {vessel_id, sas_before, rcs_before, rcs_touched}}. Parse failure or no plan does nothing.
                if (Json.Json.Parse(_context.Handover) is Dictionary<string, object> h
                    && h.TryGetValue("plan", out object p) && p is Dictionary<string, object> plan)
                {
                    string note = "throttle 0";
                    // Like Logic Coordinator.Safe: each failed step is logged as an error with its stack and the rest still run.
                    void Fail(string what, Exception ex)
                    {
                        Debug.LogError("[KspBridge] handover drop step failed (" + what + "): " + ex);
                        note += ", " + what + " failed";
                    }
                    try { FlightInputHandler.state.mainThrottle = 0; } catch (Exception e) { note = "input throttle not zeroed"; Fail("throttle", e); }
                    // Json numbers are double; persistentId (uint) converts to double exactly.
                    plan.TryGetValue("vessel_id", out object id);
                    Vessel v = null;
                    if (id == null) note += ", plan vessel_id missing";
                    else if (!(id is double d)) note += ", plan vessel_id not a number (" + id.GetType().Name + " " + id + ")";
                    else
                    {
                        try
                        {
                            v = FlightGlobals.VesselsLoaded?.FirstOrDefault(x => x != null && x.persistentId == d);
                            if (v == null) note += ", plan vessel " + d.ToString("R") + " not loaded";
                        }
                        catch (Exception e) { Fail("vessel lookup", e); }
                    }
                    if (v == null) note += ", vessel throttle/sas/rcs untouched";
                    else
                    {
                        try { v.ctrlState.mainThrottle = 0; } catch (Exception e) { Fail("vessel throttle", e); }
                        try
                        {
                            if (plan.TryGetValue("sas_before", out object s) && s is bool sas)
                            {
                                v.ActionGroups.SetGroup(KSPActionGroup.SAS, sas);
                                note += ", sas " + sas;
                            }
                            else note += ", sas untouched";
                        }
                        catch (Exception e) { Fail("sas", e); }
                        try
                        {
                            if (plan.TryGetValue("rcs_touched", out object t) && t is bool touched && touched
                                && plan.TryGetValue("rcs_before", out object r) && r is bool rcs && v.ActionGroups[KSPActionGroup.RCS] != rcs)
                            {
                                if (Overridden(v, KSPActionGroup.RCS)) note += ", rcs held by action group override " + v.GroupOverride;
                                else
                                {
                                    v.ActionGroups.SetGroup(KSPActionGroup.RCS, rcs);
                                    note += v.ActionGroups[KSPActionGroup.RCS] == rcs ? ", rcs " + rcs : ", rcs did not change (cooldown)";
                                }
                            }
                            else note += ", rcs untouched";
                        }
                        catch (Exception e) { Fail("rcs", e); }
                    }
                    Debug.Log("[KspBridge] handover dropped: " + note);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] handover drop failed: " + e);
            }
            _context.Handover = null;
        }

        // Same as Logic ManualControl.Overridden (the loader does not reference Logic).
        private static bool Overridden(Vessel v, KSPActionGroup g)
        {
            int n = v.GroupOverride;
            return GameSettings.ADDITIONAL_ACTION_GROUPS && n > 0 && v.OverrideDefault != null && n <= v.OverrideDefault.Length
                && (v.OverrideDefault[n - 1] || (v.OverrideActionControl[n - 1] & g) != 0);
        }

        private static ILogic Create()
        {
            if (!File.Exists(LogicPath))
            {
                throw new FileNotFoundException("logic DLL not found", LogicPath);
            }
            Assembly asm = Assembly.Load(File.ReadAllBytes(LogicPath));
            Type type = asm.GetTypes().FirstOrDefault(t => typeof(ILogic).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                ?? throw new InvalidOperationException("no ILogic implementation in " + asm.FullName);
            return (ILogic)Activator.CreateInstance(type);
        }

        private static string Describe(Exception e)
        {
            Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
            return inner.GetType().Name + ": " + inner.Message;
        }
    }
}
