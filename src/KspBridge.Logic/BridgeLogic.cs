using KspBridge.Editor;
using KspBridge.Flight;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KspBridge
{
    /// <summary>Entry point of the replaceable logic assembly (created by the loader's LogicHost).</summary>
    public sealed class BridgeLogic : ILogic
    {
        /// <summary>True after Stop: requests already queued for this (replaced) logic are refused.</summary>
        public static volatile bool Stopped;

        public string Version => Routes.Version;

        public IEnumerable<Route> Start(LogicContext context)
        {
            // A loader without the Handover field would silently lose the handover. The const is baked in at build time,
            // so read the value of the loader actually loaded by reflection.
            string loader = typeof(LogicHost).GetField("LoaderVersion")?.GetValue(null) as string ?? "0.0.0";
            if (new Version(loader) < new Version(0, 0, 3))
            {
                throw new InvalidOperationException("loader 0.0.3 required; restart KSP");
            }
            return StartCore(context);
        }

        /// <summary>
        /// The only place that may touch context.Handover: on a 0.0.2 loader the field is missing and JIT of this method throws
        /// MissingFieldException. Not inlined so that happens after the version check in Start, not before it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private IEnumerable<Route> StartCore(LogicContext context)
        {
            Stopped = false;
            // What can fail first; the hooks, the takeover and consuming the handover last.
            Settings.Reload();
            Debugging.DebugState.LoadFlag();   // never throws
            List<Route> routes = Routes.Build(context.Dispatcher);
            HandoverState h = Handover.Parse(context.Handover);   // never throws: a bad handover is h.Incompatible / no epoch
            try
            {
                FlightScene.Hook();
                FlightReset.Hook();
                Debugging.DebugState.Hook();   // only when debug.cfg enables it
            }
            catch
            {
                try
                {
                    FlightScene.Unhook();   // undo what was hooked; context.Handover stays for the previous logic's restart
                    FlightReset.Unhook();
                    Debugging.DebugState.Unhook();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[KspBridge] flight unhook after a failed hook failed: " + e);
                }
                throw;
            }
            Coordinator.Resume(h);   // never throws
            context.Handover = null;
            _context = context;      // Stop uses it
            return routes;
        }

        private LogicContext _context;

        public void FixedUpdate()
        {
            Coordinator.Tick();
            Flight.Control.ManualControl.Tick();   // never throws
            FlightReset.Tick();   // never throws
        }

        public IList<string> Stop()
        {
            Stopped = true;
            try
            {
                FlightScene.Unhook();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] flight unhook failed: " + e);
            }
            try
            {
                FlightReset.Unhook();   // its state is not handed over (0011 design: reload_mod)
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] flight reset unhook failed: " + e);
            }
            IList<string> released = Coordinator.Stop(_context);   // never throws
            try
            {
                Debugging.DebugState.Unhook();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] debug unhook failed: " + e);
            }
            try
            {
                IList<string> pulse = Flight.Control.ManualControl.Stop();   // the pulse is not handed over
                if (pulse.Count > 0)
                {
                    released = new List<string>(released ?? new List<string>());
                    foreach (string s in pulse)
                    {
                        released.Add(s);
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] rcs pulse stop failed: " + e);
            }
            return released;
        }

        public IDictionary<string, object> Status()
        {
            Dictionary<string, object> status = new Dictionary<string, object>
            {
                ["editor"] = EditorScene.Status(),
                ["flight"] = Coordinator.StatusSummary(),
            };
            try
            {
                status["input_locks"] = InputLocks.Describe();
            }
            catch (Exception e)   // a lock-listing failure must not hide editor (open_editor polls editor.ready)
            {
                status["input_locks"] = null;
                status["input_locks_error"] = e.GetType().Name + ": " + e.Message;
            }
            try
            {
                status["reset"] = FlightReset.Status();
            }
            catch (Exception e)   // isolated like input_locks
            {
                status["reset"] = null;
                status["reset_error"] = e.GetType().Name + ": " + e.Message;
            }
            try
            {
                status["translate"] = Flight.Control.ManualControl.Summary();   // any scene: leave/recover end in the space center
            }
            catch (Exception e)
            {
                status["translate"] = null;
                status["translate_error"] = e.GetType().Name + ": " + e.Message;
            }
            try
            {
                status["debug"] = Debugging.DebugState.Status();   // null with the flag off
            }
            catch (Exception e)
            {
                status["debug"] = null;
                status["debug_error"] = e.GetType().Name + ": " + e.Message;
            }
            return status;
        }
    }
}
