using System.Collections.Generic;

namespace KspBridge
{
    /// <summary>Entry point of the replaceable logic assembly (created by the loader's LogicHost).</summary>
    public sealed class BridgeLogic : ILogic
    {
        /// <summary>True after Stop: requests already queued for this (replaced) logic are refused.</summary>
        public static volatile bool Stopped;

        public string Version => Routes.Version;

        public IEnumerable<Route> Start(LogicContext context)
        {
            Stopped = false;
            Settings.Reload();
            return Routes.Build(context.Dispatcher);
        }

        public void FixedUpdate()
        {
        }

        public IList<string> Stop()
        {
            Stopped = true;
            return new List<string>();
        }
    }
}
