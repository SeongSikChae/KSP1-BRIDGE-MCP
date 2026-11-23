using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Control;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Step "circularize": a burn centered on the next apoapsis to a circular orbit (spec C8, C9).</summary>
    internal sealed class CircularizeStepType : IStepType
    {
        public const string TypeName = "circularize";

        private readonly EventLog events;

        public CircularizeStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Burn at the next apoapsis to a circular orbit. Put a warp step before it (to apoapsis, lead_s a little more than half " +
               "the burn plus time to turn, e.g. 60): this step waits at rate 1 and does not warp. Optional params.tolerance_mps (> 0 to " +
               "100, default 0.5), params.inclination_deg (0..180, default none: the current orbit plane) and params.auto_stage (default " +
               "true, same rule as ascend). Give inclination_deg the same value as the ascend step to reach that inclination (ascend " +
               "alone ends short of it): the circular velocity then points along that inclination's heading at the latitude (the one " +
               "closer to the current motion; due east or west where the latitude cannot give it) and the plane change is part of the " +
               "delta-v. Turns SAS off and holds the attitude (hold " +
               "type circularize). Starts the burn half the estimated burn time before the apoapsis (at once when that has passed or the " +
               "vessel is already going down), along the delta-v from the current velocity to the circular velocity here, fixed once " +
               "below 10 m/s; no thrust while the nose is more than 5 deg off it; the throttle fades over the last 2 s of delta-v. " +
               "Completes when the delta-v left is at most tolerance_mps, or below 10 m/s it grows again while the engines thrust: " +
               "throttle 0, the attitude goes on along prograde (event circularize_completed). Do not use abort_on.no_thrust with this " +
               "step. Fails the plan (step_failed) at the start, changing nothing (throttle, SAS, attitude, stage), when there is no " +
               "active vessel or it is not controllable, it is landed, splashed or on the pad, the orbit is open, the apoapsis is outside " +
               "the sphere of influence or not above the atmosphere, or inclination_deg cannot be reached from the latitude (0.5 deg " +
               "slack). Fails it while running (step_failed, throttle 0, event throttle_cut) when the vessel becomes not controllable, " +
               "thrust is wanted but no engine runs and staging cannot light one (engines exhausted), or automatic staging activated a " +
               "stage but no part of it was activated. step_progress: phase (wait, burn)," +
               " start_ut, burn_time_s, remaining_dv_mps, nose_error_deg, throttle, stages.";

        public IStepRun Create(Dictionary<string, object> parameters) => new CircularizeRun(events, AscentArgs.Circularize(parameters));

        private sealed class CircularizeRun : IStepRun, IStepProgress
        {
            private readonly EventLog events;
            private readonly CircularizeLogic logic;
            private AttitudeHold hold;
            private AutoStager stager;
            private int planId;
            private int index;

            public CircularizeRun(EventLog events, CircularizeArgs args)
            {
                this.events = events;
                logic = new CircularizeLogic(args);
            }

            public void Begin(StepContext context)
            {
                planId = context.PlanId;
                index = context.StepIndex;
                Vessel vessel = ControlStep.RequireControllable(context);
                string failure = logic.StartFailure(AscentReader.Read(vessel));
                if (failure != null)
                    throw new InvalidOperationException(failure);

                stager = new AutoStager(events, planId);
                if (vessel.ActionGroups[KSPActionGroup.SAS])
                    vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                hold = new AttitudeHold(AttitudeTarget.Along(AttitudeDirection.Prograde), TypeName);
                context.Plan.SetHold(hold, TypeName);
                ThrottleControl.CutAtEnd(context.Plan, vessel, events);
            }

            public StepResult Judge(StepContext context)
            {
                Vessel vessel = ControlStep.RequireControllable(context);
                AscentReading r = AscentReader.Read(vessel);
                CircularizeCommand c = logic.Tick(r);
                hold.Target = AttitudeTarget.PitchHeading(Math.Max(-90, Math.Min(90, c.PitchDeg)), LaunchAzimuth.Normalize(c.HeadingDeg), null);
                if (c.Failure != null)
                    return StepResult.Failed(c.Failure);
                if (c.Done)
                {
                    ThrottleControl.Write(vessel, 0);
                    hold.Target = AttitudeTarget.Along(AttitudeDirection.Prograde);
                    hold.Type = AttitudeHold.AttitudeType;
                    events.Add("circularize_completed", planId, new Dictionary<string, object>
                    {
                        ["index"] = index,
                        ["apoapsis_m"] = double.IsInfinity(r.ApA) ? (double?)null : r.ApA,
                        ["periapsis_m"] = r.PeA,
                        ["eccentricity"] = r.Eccentricity,
                        ["inclination_deg"] = r.Inclination,
                        ["remaining_dv_mps"] = logic.RemainingDv,
                        ["burn_start_ut"] = logic.BurnStartUt,
                        ["ut"] = r.UT,
                        ["stages"] = stager.Count,
                    });
                    return StepResult.Done;
                }
                ThrottleControl.Write(vessel, c.Throttle);
                string staging = stager.Tick(r.UT, c.Stage, vessel);
                return staging != null ? StepResult.Failed(staging) : StepResult.Running;
            }

            public Dictionary<string, object> Progress(StepContext context) => stager == null ? null : logic.Progress(stager.Count);

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
