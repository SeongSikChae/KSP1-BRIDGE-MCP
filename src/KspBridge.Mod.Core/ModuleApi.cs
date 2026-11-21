using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// A feature module in a logic generation. Modules add step types and request handlers without touching the core.
    /// Every public non-abstract implementation in a generation assembly is created and registered at start.
    /// </summary>
    public interface IBridgeModule
    {
        /// <summary>Route prefix (POST /api/{Name}/{op}); same name as the paired server module.</summary>
        string Name { get; }

        void Register(ModuleContext context);
    }

    /// <summary>A kind of plan step. The Coordinator judges it; the Controller lets it control the vessel.</summary>
    public interface IStepType
    {
        string Name { get; }

        /// <summary>Short description with parameters, shown to the Planner by list_step_types.</summary>
        string Describe();

        /// <summary>Validates <paramref name="parameters"/> and creates a run; throw BridgeException.BadRequest for bad input.</summary>
        IStepRun Create(Dictionary<string, object> parameters);
    }

    /// <summary>One step being executed.</summary>
    public interface IStepRun
    {
        /// <summary>Called once on the physics frame the step starts.</summary>
        void Begin(StepContext context);

        /// <summary>Called by the Coordinator every physics frame.</summary>
        StepResult Judge(StepContext context);

        /// <summary>Called by the Controller from the vessel's OnFlyByWire while the step runs.</summary>
        void Control(FlightCtrlState state, StepContext context);

        /// <summary>Called once when the step ends; <paramref name="reason"/> is completed, failed or the plan's end reason.</summary>
        void End(string reason);
    }

    /// <summary>
    /// Optional on an <see cref="IStepRun"/>: how far the step is from its goal, shown by get_plan (step_progress).
    /// Steps that do not implement it show null.
    /// </summary>
    public interface IStepProgress
    {
        /// <summary>Called on request (main thread) with the context's UT set to now; return null when there is nothing to show.</summary>
        Dictionary<string, object> Progress(StepContext context);
    }

    public enum StepStatus
    {
        Running,
        Done,
        Failed,
    }

    public sealed class StepResult
    {
        private StepResult(StepStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public StepStatus Status { get; }
        public string Message { get; }

        public static readonly StepResult Running = new StepResult(StepStatus.Running, null);
        public static readonly StepResult Done = new StepResult(StepStatus.Done, null);
        public static StepResult Failed(string message) => new StepResult(StepStatus.Failed, message);
    }

    /// <summary>What a step sees each frame.</summary>
    public sealed class StepContext
    {
        public StepContext(Dictionary<string, object> parameters) => Parameters = parameters;

        public Dictionary<string, object> Parameters { get; }

        /// <summary>Game time (UT, seconds) now.</summary>
        public double UT { get; internal set; }

        /// <summary>UT when the step began.</summary>
        public double StartUT { get; internal set; }

        public double Elapsed => UT - StartUT;

        /// <summary>Active vessel; null outside flight.</summary>
        public Vessel Vessel { get; internal set; }
    }

    /// <summary>Handler for POST /api/{module}/{op}. Returns the data object; throw BridgeException for expected failures.</summary>
    public delegate object RequestHandler(Dictionary<string, object> args);

    /// <summary>What a module gets at registration.</summary>
    public sealed class ModuleContext
    {
        private readonly Router router;
        private readonly StepRegistry steps;

        internal ModuleContext(string moduleName, Router router, StepRegistry steps, Subscriptions subscriptions,
            ICoordinator coordinator, EventLog events, Action<string> log, FlightSession session)
        {
            ModuleName = moduleName;
            this.router = router;
            this.steps = steps;
            Subscriptions = subscriptions;
            Coordinator = coordinator;
            Events = events;
            Log = log;
            Session = session;
        }

        public string ModuleName { get; }

        /// <summary>Register every external subscription here; it is released when the generation stops.</summary>
        public Subscriptions Subscriptions { get; }

        public ICoordinator Coordinator { get; }
        public EventLog Events { get; }
        public Action<string> Log { get; }
        public FlightSession Session { get; }

        public void AddStepType(IStepType stepType) => steps.Add(stepType);

        public void AddHandler(string op, RequestHandler handler) => router.Add(ModuleName + "/" + op, handler);
    }
}
