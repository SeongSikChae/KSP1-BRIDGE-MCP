using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>Step "attitude": turns SAS off, holds the target for the rest of the plan and completes once aligned (D001-D003).</summary>
    internal sealed class AttitudeStepType : IStepType
    {
        public const string TypeName = "attitude";

        public string Name => TypeName;

        public string Describe()
            => "Turn the nose to a target and keep holding it in the later steps of the plan. Target: params.pitch (-90..90 deg, up +) " +
               "with params.heading (0 to below 360 deg, clockwise from north) and optional params.roll (-180..180 deg), or " +
               "params.direction: prograde, retrograde, normal, antinormal, radial_out, radial_in (orbit), surface_prograde, " +
               "surface_retrograde (followed as it changes; no roll). Optional params.tolerance_deg (> 0, default 2) and " +
               "params.settle_rate_dps (> 0, default 0.5). Turns SAS off and leaves it off. Completes when the nose (and the roll if " +
               "given) is within tolerance_deg and the angular speed is at most settle_rate_dps; set timeout_s, otherwise it waits " +
               "forever when the vessel cannot get there. A direction that cannot be determined (speed below 1 m/s) counts as not " +
               "aligned: the turning is stopped and it waits. The hold ends with another attitude step, an ascend or circularize step, attitude_off, action_group " +
               "sas on, or the plan's end (event hold_released). step_progress: target, angle_error_deg, roll_error_deg, rate_dps, " +
               "tolerance_deg, settle_rate_dps. Fails the plan (step_failed) when there is no active vessel, or when the vessel is not " +
               "controllable (no working control source: no crew, power or signal) at the start or while it waits to align.";

        public IStepRun Create(Dictionary<string, object> parameters) => new AttitudeRun(ControlArgs.Attitude(parameters));

        private sealed class AttitudeRun : IStepRun, IStepProgress
        {
            private readonly AttitudeArgs args;

            public AttitudeRun(AttitudeArgs args) => this.args = args;

            public void Begin(StepContext context)
            {
                Vessel vessel = ControlStep.RequireControllable(context);
                if (vessel.ActionGroups[KSPActionGroup.SAS])
                    vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                context.Plan.SetHold(new AttitudeHold(args.Target), TypeName);
            }

            public StepResult Judge(StepContext context)
            {
                VesselAttitude now = VesselReader.Read(ControlStep.RequireControllable(context));
                AttitudeError error = AttitudeHold.Error(args.Target, now);
                return AttitudeLaw.Aligned(error, AttitudeLaw.RateDps(now.Rate), args.ToleranceDeg, args.SettleRateDps)
                    ? StepResult.Done
                    : StepResult.Running;
            }

            public Dictionary<string, object> Progress(StepContext context)
            {
                VesselAttitude now = VesselReader.Read(ControlStep.RequireVessel(context));
                AttitudeError error = AttitudeHold.Error(args.Target, now);
                return new Dictionary<string, object>
                {
                    ["target"] = args.Target.Describe(),
                    ["angle_error_deg"] = error.Defined ? error.AngleDeg : (double?)null,
                    ["roll_error_deg"] = error.RollDeg,
                    ["rate_dps"] = AttitudeLaw.RateDps(now.Rate),
                    ["tolerance_deg"] = args.ToleranceDeg,
                    ["settle_rate_dps"] = args.SettleRateDps,
                };
            }

            public void Control(FlightCtrlState state, StepContext context)
            {
                // The hold gives the input, here and in the later steps.
            }

            public void End(string reason)
            {
            }
        }
    }

    /// <summary>Step "attitude_off": releases the held attitude; SAS stays off (D003).</summary>
    internal sealed class AttitudeOffStepType : IStepType
    {
        public const string TypeName = "attitude_off";

        public string Name => TypeName;

        public string Describe()
            => "Release the attitude held by an earlier attitude step (event hold_released). No params. Completes on the same frame, " +
               "also when nothing is held. SAS stays off; turn it on with action_group sas.";

        public IStepRun Create(Dictionary<string, object> parameters) => new OffRun();

        private sealed class OffRun : IStepRun
        {
            public void Begin(StepContext context) => context.Plan.ReleaseHold(TypeName);

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
