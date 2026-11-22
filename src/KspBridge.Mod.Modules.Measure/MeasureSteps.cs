using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>
    /// Step "measure_start" (spec C4): starts a measurement on the physics frame the step begins and completes on that frame.
    /// The sample limit is checked at submit time, so a bad one rejects the plan with bad_request.
    /// </summary>
    internal sealed class MeasureStartStepType : IStepType
    {
        public const string TypeName = "measure_start";

        private readonly MeasureModule module;

        public MeasureStartStepType(MeasureModule module) => this.module = module;

        public string Name => TypeName;

        public string Describe()
            => "Start a measurement on this physics frame: the mod records the active vessel's values every physics frame until " +
               "measure_stop (read them with get_measurement). params.sample_limit: optional integer 1..180000 (default 30000); " +
               "the measurement ends with sample_limit when it is reached. Completes on the same frame and gives no control input. " +
               "Fails the plan (step_failed) when a measurement is already running or there is no active vessel. " +
               "The measurement keeps running after the plan ends.";

        public IStepRun Create(Dictionary<string, object> parameters) => new StartRun(module, MeasureArgs.SampleLimit(parameters));

        private sealed class StartRun : IStepRun
        {
            private readonly MeasureModule module;
            private readonly int sampleLimit;

            public StartRun(MeasureModule module, int sampleLimit)
            {
                this.module = module;
                this.sampleLimit = sampleLimit;
            }

            public void Begin(StepContext context)
            {
                if (context.Vessel == null)
                    throw new InvalidOperationException("there is no active vessel");
                module.StartMeasurement(context.UT, context.Vessel, sampleLimit);
            }

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input.
            }

            public void End(string reason)
            {
            }
        }
    }

    /// <summary>Step "measure_stop" (spec C4): ends the running measurement with stopped on the frame the step begins.</summary>
    internal sealed class MeasureStopStepType : IStepType
    {
        public const string TypeName = "measure_stop";

        private readonly MeasureModule module;

        public MeasureStopStepType(MeasureModule module) => this.module = module;

        public string Name => TypeName;

        public string Describe()
            => "End the running measurement (end reason stopped) on this physics frame. No params. Completes on the same frame and " +
               "gives no control input. Fails the plan (step_failed) when no measurement is running.";

        public IStepRun Create(Dictionary<string, object> parameters) => new StopRun(module);

        private sealed class StopRun : IStepRun
        {
            private readonly MeasureModule module;

            public StopRun(MeasureModule module) => this.module = module;

            public void Begin(StepContext context) => module.StopMeasurement(context.UT);

            public StepResult Judge(StepContext context) => StepResult.Done;

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input.
            }

            public void End(string reason)
            {
            }
        }
    }
}
