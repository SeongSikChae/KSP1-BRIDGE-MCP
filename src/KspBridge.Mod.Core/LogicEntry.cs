using KspBridge.Mod.Core;
using KspBridge.Mod.Loader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

[assembly: LogicEntry(typeof(LogicEntry), LoaderApi.Version)]

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// Logic generation entry. Builds the core (router, Coordinator, Controller, session), registers every
    /// <see cref="IBridgeModule"/> in the generation, and on Stop ends the plan (reason reload), detaches control,
    /// releases all subscriptions, and hands state to the next generation.
    /// </summary>
    public sealed class LogicEntry : ILogic
    {
        private CoreModule core;

        internal ILoaderHost Host { get; private set; }
        internal Router Router { get; private set; }
        internal StepRegistry Steps { get; private set; }
        internal Subscriptions Subscriptions { get; private set; }
        internal EventLog Events { get; private set; }
        internal Coordinator Coordinator { get; private set; }
        internal Controller Controller { get; private set; }
        internal FlightSession Session { get; private set; }

        public void Start(ILoaderHost host, string carry)
        {
            Host = host;
            Router = new Router(host.LogError);
            Steps = new StepRegistry();
            Subscriptions = new Subscriptions(host.LogError);
            Events = new EventLog(Clock);
            Session = new FlightSession();
            Coordinator = new Coordinator(Steps, Events, Clock, ActiveVessel, Session.PlanBlockReason, () => Session.Id);
            Controller = new Controller(Coordinator);
            Subscriptions.Add(() => Controller.Detach());

            Restore(carry);
            Session.Hook(Subscriptions, Coordinator);

            core = new CoreModule(this);
            core.Register(Router);
            RegisterModules(host.GenerationAssemblies);
            Events.Add("logic_started", data: new Dictionary<string, object> { ["generation"] = host.Generation });
        }

        public string Stop()
        {
            Coordinator?.Abort(EndReasons.Reload, "logic generation " + Host?.Generation + " stopped");
            Controller?.Detach();
            Subscriptions?.ReleaseAll();
            return Json.Write(new Dictionary<string, object>
            {
                ["next_plan_id"] = Coordinator?.NextPlanId ?? 1,
                ["last_plan"] = Coordinator?.LastSummary,
                ["last_plan_session"] = Coordinator?.LastSession ?? -1,
                ["session"] = Session?.Snapshot(),
                ["events"] = Events?.Snapshot(),
            });
        }

        public void Update()
        {
        }

        public void FixedUpdate() => Coordinator.FixedUpdate();

        public string Handle(string route, string body) => Router.Handle(route, body);

        private void RegisterModules(IList<Assembly> assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in SafeTypes(assembly).Where(t => typeof(IBridgeModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
                {
                    IBridgeModule module = (IBridgeModule)Activator.CreateInstance(type);
                    module.Register(new ModuleContext(module.Name, Router, Steps, Subscriptions, Coordinator, Events, Host.Log, Session));
                    Host.Log("module " + module.Name + " registered (" + assembly.GetName().Name + ")");
                }
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
        }

        private void Restore(string carry)
        {
            Dictionary<string, object> state;
            try
            {
                state = Json.ParseObject(carry);
            }
            catch (FormatException ex)
            {
                Host.LogError("carry-over state ignored: " + ex.Message);
                return;
            }
            if (state.Num("next_plan_id") is double next)
                Coordinator.NextPlanId = (int)next;
            Coordinator.LastSummary = state.Obj("last_plan");
            if (state.Num("last_plan_session") is double lastSession)
                Coordinator.LastSession = (int)lastSession;
            Session.Restore(state.Obj("session"));
            Events.Restore(state.Obj("events"));
        }

        private static double Clock() => Planetarium.fetch != null ? Planetarium.GetUniversalTime() : 0;

        private static Vessel ActiveVessel() => FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
    }
}
