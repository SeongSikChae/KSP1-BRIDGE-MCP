using KspBridge.Debugging;
using System;
using System.Collections.Generic;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the debug tools (#7): POST /api/debug/&lt;name&gt; on the main thread, 409 debug_disabled with the flag off.</summary>
    public static class DebugHandler
    {
        public static IEnumerable<Route> Routes()
        {
            yield return Post("clear", b =>
            {
                DebugState.Clear();
                return DebugState.Status();
            });
        }

        private static Route Post(string name, Func<RequestBody, object> f)
            => new Route("POST", "/api/debug/" + name, (m, b, q) =>
            {
                DebugState.Require();
                return f(b);
            });
    }
}
