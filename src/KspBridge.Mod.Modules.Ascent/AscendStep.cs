using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Control;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Step "ascend": a gravity turn from the pad to a target apoapsis, completing above the atmosphere (spec C3-C7).</summary>
    internal sealed class AscendStepType : IStepType
    {
        public const string TypeName = "ascend";

        private readonly EventLog events;

        public AscendStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Climb from the pad to a target apoapsis with a gravity turn and finish above the atmosphere. Plan it as ascend -> warp " +
               "(to apoapsis, lead_s e.g. 60) -> circularize. params.apoapsis_m (required, > 0, sea level m, above the atmosphere); " +
               "params.heading (0 to below 360 deg, default 90) or params.inclination_deg (0..180) with optional params.launch (north or " +
               "south, default north): the heading then takes out the speed the rotation already gives, and due east or west where the " +
               "latitude cannot give that inclination; this step ends slower than orbit, so give circularize the same inclination_deg " +
               "to finish the plane; params.max_q_kpa (> 0, default " +
               "20): the throttle fades from 80% of it to 0 at it; params.max_aoa_deg (> 0 to 90, default 5): the angle to the surface " +
               "velocity is kept within it x max(1, 10 / q) above 1 kPa; params.turn_start_m (>= 0, height above the start, default 500, " +
               "50 without air) and params.turn_end_m (> 0, sea level, default 65% of the atmosphere top, without air the turn start + 10% of (target apoapsis - turn start)): pitch = 90 - 90 f^0.6 with f the " +
               "smaller of the altitude share and the apoapsis share; params.auto_stage (default true). Turns SAS off and holds the " +
               "attitude (hold type ascend). Once the apoapsis reaches the target the engines stop; it follows the surface velocity in the " +
               "atmosphere and tops the apoapsis up to the target when it falls below 99%. Completes above the atmosphere with the " +
               "apoapsis at 99% or more and no thrust left (solid boosters burn out first): throttle 0, the attitude goes on along " +
               "prograde (event ascend_completed). Automatic staging activates the next stage when every engine it drops is out while " +
               "another runs (boosters_out) or no engine runs (no_running_engine, also the first ignition), only if an engine is left to " +
               "use after it, at most once a second (event stage_activated with reason); a capsule separation or parachute stage with no " +
               "engine after it is left alone. Solid boosters do not follow the throttle: set their thrust limiter when building " +
               "(editor_set_part_settings). Do not use abort_on.no_thrust with this step: it ends the plan as a stage burns out, before " +
               "the next is staged. Fails the plan (step_failed) at the start, changing nothing (throttle, SAS, attitude, stage), when " +
               "there is no active vessel or it is not controllable, the target is not above the atmosphere or outside the sphere of " +
               "influence, the turn end is not above the turn start, or the inclination cannot be reached from the latitude (0.5 deg " +
               "slack). Fails it while running (step_failed, throttle 0, event throttle_cut) when the vessel becomes not controllable, " +
               "the nose stays more than 30 deg off the target for 3 s in the atmosphere at 1 kPa or more or while the engines thrust " +
               "(control lost), thrust is wanted but no engine runs and staging cannot light one (engines exhausted), or automatic " +
               "staging activated a stage but no part of it was activated. step_progress: phase (climb, coast), target_apoapsis_m, " +
               "apoapsis_m, pitch_deg, heading_deg, " +
               "nose_error_deg, throttle, q_kpa, aoa_deg, stages.";

        public IStepRun Create(Dictionary<string, object> parameters) => new AscendRun(events, AscentArgs.Ascend(parameters));

        private sealed class AscendRun : IStepRun, IStepProgress
        {
            private readonly EventLog events;
            private readonly AscendArgs args;
            private AscendLogic logic;
            private AttitudeHold hold;
            private AutoStager stager;
            private int planId;
            private int index;

            public AscendRun(EventLog events, AscendArgs args)
            {
                this.events = events;
                this.args = args;
            }

            public void Begin(StepContext context)
            {
                planId = context.PlanId;
                index = context.StepIndex;
                Vessel vessel = ControlStep.RequireControllable(context);
                AscentReading r = AscentReader.Read(vessel);
                AscendLogic candidate = new AscendLogic(args, r);
                string failure = candidate.StartFailure(r);
                if (failure != null)
                    throw new InvalidOperationException(failure);

                logic = candidate;
                stager = new AutoStager(events, planId);
                if (vessel.ActionGroups[KSPActionGroup.SAS])
                    vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                hold = new AttitudeHold(AttitudeTarget.PitchHeading(90, args.Heading ?? 90, null), TypeName);
                context.Plan.SetHold(hold, TypeName);
                ThrottleControl.CutAtEnd(context.Plan, vessel, events);
            }

            public StepResult Judge(StepContext context)
            {
                Vessel vessel = ControlStep.RequireControllable(context);
                AscentReading r = AscentReader.Read(vessel);
                AscendCommand c = logic.Tick(r);
                hold.Target = AttitudeTarget.PitchHeading(Math.Max(-90, Math.Min(90, c.PitchDeg)), LaunchAzimuth.Normalize(c.HeadingDeg), null);
                if (c.Failure != null)
                    return StepResult.Failed(c.Failure);
                if (c.Done)
                {
                    ThrottleControl.Write(vessel, 0);
                    hold.Target = AttitudeTarget.Along(AttitudeDirection.Prograde);
                    hold.Type = AttitudeHold.AttitudeType;
                    events.Add("ascend_completed", planId, new Dictionary<string, object>
                    {
                        ["index"] = index,
                        ["apoapsis_m"] = double.IsInfinity(r.ApA) ? (double?)null : r.ApA,
                        ["periapsis_m"] = r.PeA,
                        ["inclination_deg"] = r.Inclination,
                        ["altitude_m"] = r.Altitude,
                        ["max_q_kpa"] = logic.MaxQKpa,
                        ["max_aoa_deg"] = logic.MaxAoaDeg,
                        ["stages"] = stager.Count,
                    });
                    return StepResult.Done;
                }
                ThrottleControl.Write(vessel, c.Throttle);
                string staging = stager.Tick(r.UT, c.Stage, vessel);
                return staging != null ? StepResult.Failed(staging) : StepResult.Running;
            }

            public Dictionary<string, object> Progress(StepContext context) => logic?.Progress(stager.Count);

            public void Control(FlightCtrlState state, StepContext context)
            {
                // The hold gives the input.
            }

            public void End(string reason)
            {
            }
        }
    }
}
