using KspBridge.Mod.Core;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>
    /// Flight observation: flight/get answers the active vessel's state (position, speed, orbit, attitude, control state, mass,
    /// thrust, stages and delta-v from the game's calculator). Read-only; the vessel is never changed.
    /// </summary>
    public sealed class FlightModule : IBridgeModule
    {
        private ModuleContext context;

        public string Name => "flight";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.AddHandler("get", Get);
        }

        private object Get(Dictionary<string, object> args)
        {
            FlightInclude include = FlightReport.ParseInclude(args.TryGetValue("include", out object raw) ? raw : null);
            Vessel vessel = RequireReady();
            return FlightReport.Build(FlightSnapshot.Take(vessel), include);
        }

        /// <summary>The active vessel when the flight scene can be read (C10), otherwise not_ready with the reason.</summary>
        private Vessel RequireReady()
        {
            bool inFlight = HighLogic.LoadedScene == GameScenes.FLIGHT;
            Vessel vessel = inFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            string reason = FlightReadiness.Judge(new FlightReadinessInput
            {
                GameLoaded = HighLogic.CurrentGame != null,
                CoreBlockReason = context.Session.BlockReason(),
                SceneName = context.Session.Scene,
                InFlight = inFlight,
                HasActiveVessel = vessel != null,
            });
            if (reason != null)
                throw BridgeException.NotReady(reason);
            return vessel;
        }
    }
}
