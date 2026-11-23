using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>Step "warp": time-warps to a moment and completes there at rate 1, off rails. Gives no control input.</summary>
    internal sealed class WarpStepType : IStepType
    {
        public const string TypeName = "warp";

        private readonly EventLog events;

        public WarpStepType(EventLog events) => this.events = events;

        public string Name => TypeName;

        public string Describe()
            => "Time-warp to a moment and complete there at rate 1 and off rails. Give exactly one target: params.seconds (> 0, game " +
               "seconds from the start of the step), params.ut (UT, s; a UT already past completes at once) or params.to (apoapsis, " +
               "periapsis or node, with optional params.lead_s >= 0 seconds before it, default 0). to node is the first maneuver node's " +
               "burn start (node UT − half the estimated burn time, as get_nodes shows it); put execute_node after it. The target UT is decided once when the step " +
               "starts: later thrust or drag does not move it. Optional params.max_rate (>= 1): highest rails rate, default the " +
               "game's highest. Uses rails warp as fast as the game allows and slows down before the target. Where the game allows no " +
               "rails warp (in the atmosphere, under acceleration, moving over the surface, ...) it goes on at rate 1 and warps again " +
               "by itself when possible; optional params.physics_rate (2, 3 or 4) uses physics warp up to that rate there (default " +
               "none; it makes physics frames longer). The rate is the step's while it runs: changes by the player or the game are " +
               "corrected (abort_plan to stop). Gives no control input; a held attitude rests on rails and goes on afterwards. The " +
               "throttle reads 0 on rails and is put back when the step completes. When the plan ends otherwise the rate goes to 1 " +
               "(event warp_released) and the throttle may stay 0. Event warp_completed: target_ut, ut, error_s, max_rails_rate, " +
               "max_physics_rate, real_s, blocked (why rails warp was not possible on the way). step_progress: target_ut, remaining_s, " +
               "time_warp, blocked. timeout_s is game time, so it also runs faster. Fails the plan (step_failed) when there is no " +
               "active vessel, or params.to is used while landed, splashed or on the pad, for the apoapsis of an open orbit or one " +
               "outside the sphere of influence, or for a periapsis an open orbit has passed; for to node also when maneuver nodes are " +
               "not usable, there is no node, the first node's time has passed, the game has no orbit after it, or the burn time " +
               "cannot be estimated.";

        public IStepRun Create(Dictionary<string, object> parameters) => new WarpRun(events, WarpArgs.Parse(parameters));

        private sealed class WarpRun : IStepRun, IStepProgress
        {
            private readonly EventLog events;
            private readonly GameWarp game = new GameWarp();
            private readonly WarpSession session;
            private int planId;
            private int index;

            public WarpRun(EventLog events, WarpArgs args)
            {
                this.events = events;
                session = new WarpSession(game, args);
            }

            public void Begin(StepContext context)
            {
                planId = context.PlanId;
                index = context.StepIndex;
                game.Vessel = context.Vessel;
                session.Start(context.UT);
            }

            public StepResult Judge(StepContext context)
            {
                game.Vessel = context.Vessel;
                switch (session.Tick(context.UT))
                {
                    case WarpProgress.Failed:
                        return StepResult.Failed(session.Failure);
                    case WarpProgress.Arrived:
                        events.Add("warp_completed", planId, new Dictionary<string, object>
                        {
                            ["index"] = index,
                            ["target_ut"] = session.TargetUt,
                            ["ut"] = context.UT,
                            ["error_s"] = context.UT - session.TargetUt,
                            ["max_rails_rate"] = session.MaxRailsRate,
                            ["max_physics_rate"] = session.MaxPhysicsRate,
                            ["real_s"] = session.RealSeconds,
                            ["blocked"] = session.Blocked.Cast<object>().ToList(),
                        });
                        return StepResult.Done;
                    default:
                        return StepResult.Running;
                }
            }

            public Dictionary<string, object> Progress(StepContext context)
            {
                if (context.Vessel == null)
                    return null;
                game.Vessel = context.Vessel;
                return new Dictionary<string, object>
                {
                    ["target_ut"] = session.TargetUt,
                    ["remaining_s"] = session.Remaining(context.UT),
                    ["time_warp"] = FlightReport.TimeWarp(game.State),
                    ["blocked"] = session.BlockedNow,
                };
            }

            public void Control(FlightCtrlState state, StepContext context)
            {
                // No input: the throttle is the game's own value and a held attitude is the plan's hold.
            }

            public void End(string reason)
            {
                double? released = session.Stop();
                if (released.HasValue)
                {
                    events.Add("warp_released", planId, new Dictionary<string, object>
                    {
                        ["index"] = index,
                        ["reason"] = reason,
                        ["rate"] = released.Value,
                    });
                }
            }
        }
    }
}
