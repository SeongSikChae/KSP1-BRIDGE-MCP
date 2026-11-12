using KspBridge.Handlers;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge
{
    public static class Routes
    {
        public const string Version = "0.2.18";

        public static List<Route> Build(MainThreadDispatcher dispatcher)
        {
            List<Route> routes = new List<Route>();
            routes.AddRange(EditorHandler.Routes(dispatcher));
            routes.AddRange(FlightHandler.Routes());
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
