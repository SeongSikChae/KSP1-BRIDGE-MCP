using System;
using System.Linq;

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// Realtime control: while a plan runs, hooks the active vessel's OnFlyByWire and lets the plan's hold and then the running
    /// step write FlightCtrlState every physics frame. Detached when the plan ends, the vessel changes, or the generation stops,
    /// so no control input outlives the plan or a reload.
    /// </summary>
    public sealed class Controller
    {
        private readonly Coordinator coordinator;
        private Vessel attached;

        public Controller(Coordinator coordinator)
        {
            this.coordinator = coordinator;
            coordinator.PlanStarted += Attach;
            coordinator.PlanEnded += _ => Detach();
        }

        public bool Attached => attached != null;

        public string AttachedVessel => attached != null ? attached.GetDisplayName() : null;

        public void Attach()
        {
            Vessel vessel = FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            if (vessel == attached)
                return;
            Detach();
            if (vessel == null)
                return;
            vessel.OnFlyByWire += Fly;
            attached = vessel;
        }

        public void Detach()
        {
            if (attached == null)
                return;
            try
            {
                attached.OnFlyByWire -= Fly;
            }
            catch (Exception)
            {
                // vessel already destroyed
            }
            attached = null;
        }

        private void Fly(FlightCtrlState state) => coordinator.Control(state);

        /// <summary>
        /// OnFlyByWire handlers from any KspBridge generation on loaded vessels. After a reload this must be 0
        /// (an old generation's handler left behind would keep steering).
        /// </summary>
        public static int CountBridgeHandlers()
        {
            if (FlightGlobals.fetch == null || FlightGlobals.VesselsLoaded == null)
                return 0;
            return FlightGlobals.VesselsLoaded
                .Where(v => v != null && v.OnFlyByWire != null)
                .SelectMany(v => v.OnFlyByWire.GetInvocationList())
                .Count(d => d.Method.DeclaringType != null
                            && d.Method.DeclaringType.Assembly.GetName().Name.StartsWith("KspBridge.", StringComparison.Ordinal));
        }
    }
}
