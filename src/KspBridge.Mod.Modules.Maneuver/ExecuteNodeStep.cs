using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Control;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>Step "execute_node": burns the first maneuver node centered on its time and removes it (spec C5, C8).</summary>
    internal sealed class ExecuteNodeStepType : IStepType
    {
        public const string TypeName = "execute_node";

        private readonly EventLog events;

        public ExecuteNodeStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Burn the first maneuver node (make nodes with create_node) and remove it. Put a warp step before it: warp (to node, lead_s " +
               "a little more than the time to turn, e.g. 60) then execute_node; repeat the pair for each node. This step waits at rate 1 " +
               "and does not warp. Optional params.tolerance_mps (> 0 to 100, default 0.5: delta-v left at completion) and " +
               "params.auto_stage (default true, same rule as ascend). Turns SAS off and holds the attitude (hold type execute_node, no " +
               "roll). The burn start is decided once at the start: node UT − half the estimated burn time (from the delta-v left, as " +
               "circularize estimates it); when it has passed the burn starts at once. The burn points along the delta-v left " +
               "(ManeuverNode.GetBurnVector), fixed once below 10 m/s; no thrust while the nose is more than 5 deg off it; the " +
               "throttle fades over the last 2 s of delta-v. Completes when the delta-v left is at most tolerance_mps, or below 10 m/s " +
               "it grows again while the engines thrust (same as circularize): throttle 0, the node is removed, the attitude goes on " +
               "along prograde. Event node_executed: index, node_ut, node_dv_mps, burn_time_s, burn_start_ut, ut, stages, " +
               "remaining_dv_mps and remaining_prograde_mps/normal_mps/radial_mps (the orbit at completion), planned (apoapsis_m, " +
               "periapsis_m, inclination_deg of the orbit after the node at the start) and actual (apoapsis_m, periapsis_m, " +
               "eccentricity, inclination_deg). Do not use abort_on.no_thrust with this step. Fails the plan (step_failed) at the start, " +
               "changing nothing (throttle, SAS, attitude, stage, nodes), when there is no active vessel or it is not controllable, " +
               "maneuver nodes are not usable, there is no node, the vessel is landed, splashed or on the pad, the first node's time " +
               "has passed, the game has no orbit after the first node, or the burn time cannot be estimated. Fails it while running " +
               "(step_failed, throttle 0, event throttle_cut; the node stays) when the vessel becomes not controllable, the first node " +
               "is removed or replaced, its orbit after it is gone, thrust is wanted but no engine runs and staging cannot light one " +
               "(engines exhausted), or automatic staging activated a stage but no part of it was activated. Hohmann transfer: " +
               "create_node (at periapsis, goal.apoapsis_m) and create_node (at apoapsis, goal.circular true), then warp (to node) → " +
               "execute_node → warp (to node) → execute_node. step_progress: phase (wait, burn), start_ut, burn_time_s, " +
               "remaining_dv_mps, nose_error_deg, throttle, stages.";

        public IStepRun Create(Dictionary<string, object> parameters) => new ExecuteNodeRun(events, NodeArgs.Execute(parameters));

        private sealed class ExecuteNodeRun : IStepRun, IStepProgress
        {
            private readonly EventLog events;
            private readonly ExecuteNodeLogic logic;
            private AttitudeHold hold;
            private AutoStager stager;
            private ManeuverNode node;
            private double plannedAp;
            private double plannedPe;
            private double plannedInc;
            private int planId;
            private int index;

            public ExecuteNodeRun(EventLog events, ExecuteNodeArgs args)
            {
                this.events = events;
                logic = new ExecuteNodeLogic(args);
            }

            public void Begin(StepContext context)
            {
                planId = context.PlanId;
                index = context.StepIndex;
                Vessel vessel = ControlStep.RequireControllable(context);
                Refresh(vessel);
                NodeStartResult start = NodeReader.FirstNodeStart(vessel);
                if (start.Failure != null)
                    throw new InvalidOperationException(start.Failure);
                node = NodeReader.Nodes(vessel).First();
                plannedAp = node.nextPatch.ApA;
                plannedPe = node.nextPatch.PeA;
                plannedInc = node.nextPatch.inclination;
                logic.Start(start);

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
                if (logic.Phase == "wait")
                    Refresh(vessel);
                if (NodeReader.Nodes(vessel).FirstOrDefault() != node)
                    return StepResult.Failed("the first maneuver node was removed or replaced");
                if (node.nextPatch == null)
                    return StepResult.Failed("the game no longer has an orbit after the node");

                AscentReading r = AscentReader.Read(vessel);
                Vec dv = NodeReader.V(node.GetBurnVector(vessel.orbit));
                BurnCommand c = logic.Tick(r.UT, dv, r);
                hold.Target = AttitudeTarget.PitchHeading(Math.Max(-90, Math.Min(90, c.PitchDeg)), LaunchAzimuth.Normalize(c.HeadingDeg), null);
                if (c.Failure != null)
                    return StepResult.Failed(c.Failure);
                if (c.Done)
                {
                    ThrottleControl.Write(vessel, 0);
                    hold.Target = AttitudeTarget.Along(AttitudeDirection.Prograde);
                    hold.Type = AttitudeHold.AttitudeType;
                    events.Add("node_executed", planId, Executed(vessel, r, dv));
                    NodeEditor.Remove(vessel.patchedConicSolver, node);
                    return StepResult.Done;
                }
                ThrottleControl.Write(vessel, c.Throttle);
                string staging = stager.Tick(r.UT, c.Stage, vessel);
                return staging != null ? StepResult.Failed(staging) : StepResult.Running;
            }

            /// <summary>
            /// Recomputes the orbit after the node from the vessel's orbit now (D016). The game keeps the orbit after a node from when
            /// the flight plan was last updated (node edits only, decompiled PatchedConicSolver.Update / UpdateFlightPlan), while the
            /// vessel's orbit can change in between (seen in the game across a warp), so the target is refreshed at the start and
            /// while waiting. Not while burning: the delta-v left is measured against a fixed target.
            /// </summary>
            private static void Refresh(Vessel vessel) => vessel.patchedConicSolver?.UpdateFlightPlan();

            private Dictionary<string, object> Executed(Vessel vessel, AscentReading r, Vec dv)
            {
                NodeReader.State(vessel.orbit, r.UT, out Vec position, out Vec velocity);
                NodeFrame frame = NodeFrame.At(position, velocity);
                Orbit o = vessel.orbit;
                return new Dictionary<string, object>
                {
                    ["index"] = index,
                    ["node_ut"] = logic.NodeUt,
                    ["node_dv_mps"] = node.DeltaV.magnitude,
                    ["burn_time_s"] = logic.BurnSeconds,
                    ["burn_start_ut"] = logic.BurnStartUt,
                    ["ut"] = r.UT,
                    ["stages"] = stager.Count,
                    ["remaining_dv_mps"] = dv.Length,
                    ["remaining_prograde_mps"] = frame.ProgradeOf(dv),
                    ["remaining_normal_mps"] = frame.NormalOf(dv),
                    ["remaining_radial_mps"] = frame.RadialOf(dv),
                    ["planned"] = new Dictionary<string, object>
                    {
                        ["apoapsis_m"] = plannedAp,
                        ["periapsis_m"] = plannedPe,
                        ["inclination_deg"] = plannedInc,
                    },
                    ["actual"] = new Dictionary<string, object>
                    {
                        ["apoapsis_m"] = o.eccentricity < 1 ? o.ApA : (double?)null,
                        ["periapsis_m"] = o.PeA,
                        ["eccentricity"] = o.eccentricity,
                        ["inclination_deg"] = o.inclination,
                    },
                };
            }

            public Dictionary<string, object> Progress(StepContext context) => stager == null ? null : logic.Progress(stager.Count);

            public void Control(FlightCtrlState state, StepContext context)
            {
                // The hold gives the input.
            }

            public void End(string reason)
            {
                // The node stays when the plan ends otherwise (spec C5, C9).
            }
        }
    }
}
