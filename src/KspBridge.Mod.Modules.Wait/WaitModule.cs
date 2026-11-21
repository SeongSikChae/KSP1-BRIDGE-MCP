using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Wait
{
    /// <summary>
    /// Wait feature: the "wait" step type (holds for game seconds, gives no control input) and wait/submit,
    /// which submits a one-step wait plan.
    /// </summary>
    public sealed class WaitModule : IBridgeModule
    {
        public string Name => "wait";

        public void Register(ModuleContext context)
        {
            context.AddStepType(new WaitStepType());
            context.AddHandler("submit", args =>
            {
                double seconds = args.Num("seconds", required: true).Value;
                Dictionary<string, object> plan = new Dictionary<string, object>
                {
                    ["steps"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = WaitStepType.TypeName,
                            ["params"] = new Dictionary<string, object> { ["seconds"] = seconds },
                        },
                    },
                };
                return context.Coordinator.Submit(plan, args.Bool("replace"));
            });
        }
    }

    internal sealed class WaitStepType : IStepType
    {
        public const string TypeName = "wait";

        public string Name => TypeName;

        public string Describe() => "Hold for params.seconds of game time (UT; time warp counts). Gives no control input.";

        public IStepRun Create(Dictionary<string, object> parameters)
        {
            double seconds = parameters.Num("seconds", required: true).Value;
            if (!(seconds > 0))
                throw BridgeException.BadRequest("seconds must be > 0 (got " + seconds.ToString(CultureInfo.InvariantCulture) + ")");
            return new WaitRun(seconds);
        }
    }

    internal sealed class WaitRun : IStepRun, IStepProgress
    {
        private readonly double seconds;

        public WaitRun(double seconds) => this.seconds = seconds;

        /// <summary>Goal and time left (game seconds, never below 0), shown by get_plan.</summary>
        public Dictionary<string, object> Progress(StepContext context) => new Dictionary<string, object>
        {
            ["target_s"] = seconds,
            ["remaining_s"] = Math.Max(0, seconds - context.Elapsed),
        };

        public void Begin(StepContext context)
        {
        }

        public StepResult Judge(StepContext context) => context.Elapsed >= seconds ? StepResult.Done : StepResult.Running;

        public void Control(FlightCtrlState state, StepContext context)
        {
            // No input: the player's (or SAS) control passes through unchanged.
        }

        public void End(string reason)
        {
        }
    }
}
