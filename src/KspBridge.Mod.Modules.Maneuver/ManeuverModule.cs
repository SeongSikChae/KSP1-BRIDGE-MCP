using KspBridge.Mod.Core;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>
    /// Maneuver nodes (spec C1-C5): maneuver/create, maneuver/get and maneuver/delete for the tools create_node, get_nodes and
    /// delete_node, and the step type execute_node.
    /// </summary>
    public sealed class ManeuverModule : IBridgeModule
    {
        private ModuleContext context;

        public string Name => "maneuver";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.AddStepType(new ExecuteNodeStepType(context.Events));
            context.AddHandler("create", Create);
            context.AddHandler("get", Get);
            context.AddHandler("delete", Delete);
        }

        private object Create(Dictionary<string, object> args)
        {
            CreateNodeArgs a = NodeArgs.Create(args);
            Vessel vessel = RequireReady();
            RequireNoPlan();
            ManeuverNode node = NodeEditor.Create(vessel, a);
            int index = NodeReader.Nodes(vessel).IndexOf(node);
            NodeReader.EstimateInputs(vessel, out var stages, out double accel);
            return NodeReader.Info(vessel, node, index).Item(Planetarium.GetUniversalTime(), stages, accel);
        }

        private object Get(Dictionary<string, object> args)
        {
            if (args != null && args.Count > 0)
                throw BridgeException.BadRequest("get_nodes takes no parameters (got '" + args.Keys.First() + "')");
            return List(RequireReady());
        }

        private object Delete(Dictionary<string, object> args)
        {
            DeleteNodeArgs a = NodeArgs.Delete(args);
            Vessel vessel = RequireReady();
            RequireNoPlan();
            NodeEditor.Delete(vessel, a);
            return List(vessel);
        }

        private static Dictionary<string, object> List(Vessel vessel)
        {
            double now = Planetarium.GetUniversalTime();
            NodeReader.EstimateInputs(vessel, out var stages, out double accel);
            return new Dictionary<string, object>
            {
                ["ut"] = now,
                ["nodes"] = NodeReader.Read(vessel).Select(n => (object)n.Item(now, stages, accel)).ToList(),
            };
        }

        /// <summary>D007: nodes are not changed under a running plan.</summary>
        private void RequireNoPlan()
        {
            if (context.Coordinator.Running)
                throw BridgeException.BadRequest("a plan is running; nodes cannot be changed until it ends (abort_plan to stop it)");
        }

        /// <summary>The active vessel when nodes can be used now, otherwise not_ready with the reason (spec C2-C4, D008).</summary>
        private Vessel RequireReady()
        {
            bool inFlight = HighLogic.LoadedScene == GameScenes.FLIGHT;
            Vessel vessel = inFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            NodeReadinessInput input = vessel != null ? NodeReader.Facilities(vessel) : new NodeReadinessInput();
            input.GameLoaded = HighLogic.CurrentGame != null;
            input.CoreBlockReason = context.Session.BlockReason();
            input.SceneName = context.Session.Scene;
            input.InFlight = inFlight;
            input.HasActiveVessel = vessel != null;
            string reason = NodeAvailability.Judge(input);
            if (reason != null)
                throw BridgeException.NotReady(reason);
            return vessel;
        }
    }
}
