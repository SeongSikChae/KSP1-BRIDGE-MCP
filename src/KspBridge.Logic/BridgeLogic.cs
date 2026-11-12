using KspBridge.Editor;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
            // A loader without the Handover field would silently lose the handover. The const is baked in at build time,
            // so read the value of the loader actually loaded by reflection.
            string loader = typeof(LogicHost).GetField("LoaderVersion")?.GetValue(null) as string ?? "0.0.0";
            if (new Version(loader) < new Version(0, 0, 3))
            {
                throw new InvalidOperationException("loader 0.0.3 required; restart KSP");
            }
            return StartCore(context);
        }

        /// <summary>
        /// The only place that may touch context.Handover: on a 0.0.2 loader the field is missing and JIT of this method throws
        /// MissingFieldException. Not inlined so that happens after the version check in Start, not before it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private IEnumerable<Route> StartCore(LogicContext context)
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

        public IDictionary<string, object> Status() => new Dictionary<string, object> { ["editor"] = EditorScene.Status() };
    }
}
