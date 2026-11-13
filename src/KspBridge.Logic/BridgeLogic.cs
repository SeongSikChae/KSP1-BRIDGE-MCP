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
        /// context.Handover is touched only here and in what this method calls (DebugState.ConsumeStartFault): on a 0.0.2
        /// loader the field is missing and JIT of the code touching it throws MissingFieldException. Not inlined so that
        /// happens after the version check in Start, not before it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private IEnumerable<Route> StartCore(LogicContext context)
        {
            Debugging.DebugState.LoadFlag();   // never throws; ConsumeStartFault needs the flag
            // Before Stopped = false: a previous logic restarted after a failed reload (same assembly, Stopped true since its
            // Stop) stays Stopped when it refuses too. A new assembly's Stopped is false, but its routes are never installed.
            Debugging.DebugState.ConsumeStartFault(context);
            Stopped = false;
            // What can fail first; the hooks, the takeover and consuming the handover last.
            Settings.Reload();
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
                // undo what was hooked, each on its own; context.Handover stays for the previous logic's restart
                UnhookAfterFailedStart("flight scene", FlightScene.Unhook);
                UnhookAfterFailedStart("flight reset", FlightReset.Unhook);
                UnhookAfterFailedStart("debug", Debugging.DebugState.Unhook);
                throw;
            }
            Coordinator.Resume(h);   // never throws
            context.Handover = null;
            _context = context;      // Stop uses it
            return routes;
        }

        private LogicContext _context;

        private static void UnhookAfterFailedStart(string what, Action unhook)
        {
            try
            {
                unhook();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] " + what + " unhook after a failed hook failed: " + e);
            }
        }

        public void FixedUpdate()
        {
            Debugging.DebugState.Tick();   // never throws; before Coordinator.Tick reads currentThrottle
            Coordinator.Tick();
            Debugging.DebugState.AfterTick();   // never throws; undoes this tick's hold write when the plan just ended
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
