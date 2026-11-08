using System.Collections.Generic;
using System.Linq;
using KspBridge.Handlers;

namespace KspBridge
{
    public static class Routes
    {
        public const string Version = "0.10.0";

        public static List<Route> Build(MainThreadDispatcher dispatcher)
        {
            List<Route> routes = new List<Route>
            {
                new Route("GET", "/api/vessel", (m, b, q) => VesselHandler.GetVessel()),
                new Route("POST", "/api/throttle", (m, b, q) => ControlHandler.SetThrottle(b)),
                new Route("POST", "/api/stage", (m, b, q) => ControlHandler.ActivateNextStage()),
                new Route("POST", "/api/sas", (m, b, q) => ControlHandler.SetSas(b)),
                new Route("POST", "/api/sas/mode", (m, b, q) => ControlHandler.SetSasMode(b)),
                new Route("POST", "/api/rcs", (m, b, q) => ControlHandler.SetRcs(b)),
                new Route("POST", "/api/action-group", (m, b, q) => ControlHandler.SetActionGroup(b)),
                new Route("GET", "/api/nodes", (m, b, q) => NodeHandler.List()),
                new Route("POST", "/api/nodes", (m, b, q) => NodeHandler.Add(b)),
                new Route("DELETE", "/api/nodes/(?<index>[^/]+)", (m, b, q) => NodeHandler.Remove(m.Groups["index"].Value)),
                new Route("GET", "/api/parts", (m, b, q) => PartsHandler.List(q)),
                new Route("POST", "/api/craft/build", (m, b, q) => CraftHandler.Build(b)),
                new Route("GET", "/api/craft", (m, b, q) => LaunchHandler.ListCraft(q)),
                new Route("GET", "/api/crew", (m, b, q) => LaunchHandler.ListCrew()),
                new Route("POST", "/api/launch", (m, b, q) => LaunchHandler.Launch(b)),
                new Route("POST", "/api/recover", (m, b, q) => LaunchHandler.Recover()),
                new Route("POST", "/api/revert", (m, b, q) => LaunchHandler.Revert()),
                new Route("GET", "/api/stages", (m, b, q) => StageHandler.GetStages()),
                new Route("POST", "/api/wait", (m, b, q) => WaitHandler.Wait(b, dispatcher), onHttpThread: true),
                new Route("POST", "/api/attitude", (m, b, q) => AttitudeHandler.Set(b)),
                new Route("POST", "/api/attitude/off", (m, b, q) => AttitudeHandler.Off()),
                new Route("GET", "/api/career", (m, b, q) => CareerHandler.GetCareer()),
                new Route("GET", "/api/contracts", (m, b, q) => CareerHandler.ListContracts(q)),
                new Route("POST", "/api/contracts/(?<id>-?[0-9]+)/(?<action>accept|decline)",
                    (m, b, q) => CareerHandler.Act(m.Groups["id"].Value, m.Groups["action"].Value)),
                new Route("GET", "/api/science", (m, b, q) => ScienceHandler.Get()),
                new Route("POST", "/api/science/run", (m, b, q) => ScienceHandler.Run(b)),
                new Route("POST", "/api/science/discard", (m, b, q) => ScienceHandler.Discard(b)),
                new Route("GET", "/api/dialogs", (m, b, q) => DialogHandler.List()),
                new Route("POST", "/api/dialogs/click", (m, b, q) => DialogHandler.Click(b)),
                new Route("POST", "/api/dialogs/close-experiment", (m, b, q) => DialogHandler.CloseExperiment()),
                new Route("GET", "/api/tech", (m, b, q) => TechHandler.List(q)),
                new Route("GET", "/api/facilities", (m, b, q) => FacilityHandler.List()),
                new Route("POST", "/api/facilities/(?<id>[a-z_]+)/upgrade", (m, b, q) => FacilityHandler.Upgrade(m.Groups["id"].Value)),
                new Route("GET", "/api/part-tests", (m, b, q) => PartTestHandler.List()),
                new Route("POST", "/api/part-tests/run", (m, b, q) => PartTestHandler.Run(b)),
                new Route("POST", "/api/part-tests/arm", (m, b, q) => PartTestHandler.Arm(b)),
                new Route("POST", "/api/warp/to",(m, b, q) => WarpHandler.To(b)),
                new Route("POST", "/api/warp/stop", (m, b, q) => WarpHandler.Stop()),
                new Route("POST", "/api/warp", (m, b, q) => WarpHandler.Set(b)),
                new Route("POST", "/api/triggers",(m, b, q) => Flight.TriggerManager.Add(b)),
                new Route("GET", "/api/triggers", (m, b, q) => Flight.TriggerManager.List()),
                new Route("DELETE", "/api/triggers", (m, b, q) => Flight.TriggerManager.Remove(null)),
                new Route("DELETE", "/api/triggers/(?<id>[^/]+)", (m, b, q) => Flight.TriggerManager.Remove(m.Groups["id"].Value)),
                new Route("GET", "/api/flights", (m, b, q) => FlightLogHandler.List(q)),
                new Route("GET", "/api/flights/(?<id>[^/]+)/log", (m, b, q) => FlightLogHandler.Log(m.Groups["id"].Value, q)),
                new Route("POST", "/api/tech/(?<id>[^/]+)/research", (m, b, q) => TechHandler.Research(m.Groups["id"].Value)),
                new Route("POST", "/api/broadcast/arm", (m, b, q) => BroadcastHandler.Arm(b)),
                new Route("GET", "/api/broadcast/events", (m, b, q) => BroadcastHandler.Events(q)),
                new Route("POST", "/api/broadcast/disarm", (m, b, q) => BroadcastHandler.Disarm()),
            };
            return routes.Select(Guarded).ToList();
        }

        /// <summary>Refuses requests that reach this logic after it was replaced by reload_mod.</summary>
        private static Route Guarded(Route r)
        {
            string pattern = r.Pattern.ToString();
            pattern = pattern.Substring(1, pattern.Length - "^".Length - "/?$".Length);
            RouteHandler inner = r.Handler;
            return new Route(r.Method, pattern, (m, b, q) =>
            {
                if (BridgeLogic.Stopped)
                {
                    throw new ApiException(503, "logic_replaced", "this request reached a replaced logic version; retry");
                }
                return inner(m, b, q);
            }, r.OnHttpThread);
        }
    }
}
