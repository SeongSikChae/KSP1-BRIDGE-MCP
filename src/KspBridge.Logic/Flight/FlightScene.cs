namespace KspBridge.Flight
{
    /// <summary>
    /// Scene, active-vessel and vessel-destroyed hooks that abort a running plan. Hook in Start, Unhook in Stop.
    /// Handlers are instance methods: KSP's EvtDelegate reads evt.Target.GetType(), which throws for static methods (decompile checked).
    /// </summary>
    public sealed class FlightScene
    {
        private static FlightScene _hooked;

        public static void Hook()
        {
            Unhook();
            FlightScene s = new FlightScene();
            GameEvents.onGameSceneLoadRequested.Add(s.OnScene);
            GameEvents.onVesselChange.Add(s.OnVesselChange);
            GameEvents.onVesselWillDestroy.Add(s.OnVesselDestroy);
            GameEvents.onPartCouple.Add(s.OnPartCouple);
            _hooked = s;
        }

        public static void Unhook()
        {
            FlightScene s = _hooked;
            if (s == null)
            {
                return;
            }
            _hooked = null;
            GameEvents.onGameSceneLoadRequested.Remove(s.OnScene);
            GameEvents.onVesselChange.Remove(s.OnVesselChange);
            GameEvents.onVesselWillDestroy.Remove(s.OnVesselDestroy);
            GameEvents.onPartCouple.Remove(s.OnPartCouple);
        }

        // Only a running plan is aborted: Abort releases control, which must not touch the user's throttle otherwise.
        private void OnScene(GameScenes scene)
        {
            if (Coordinator.Running)
            {
                Coordinator.Abort("scene", null, null);
            }
            Control.ManualControl.StopPulse("scene");   // independent of the plan check (a plan and a pulse never run together)
        }

        /// <summary>Always aborts, even when separation switched the vessel; a plan is never carried over to another vessel.</summary>
        private void OnVesselChange(Vessel v)
        {
            if (Coordinator.Running)
            {
                Coordinator.Abort("vessel_changed", null, null);
            }
            Control.ManualControl.StopPulse("vessel_changed");
        }

        /// <summary>onVesselWillDestroy: Vessel.Die fires it first for every vessel. onVesselDestroy is not used because Die keeps
        /// the active vessel's object (state DEAD only), so it never fires for our target (decompile checked).</summary>
        private void OnVesselDestroy(Vessel v)
        {
            if (Coordinator.Running && v != null && v.persistentId == Coordinator.TargetVesselId)
            {
                Coordinator.Abort("vessel_lost", null, null);
            }
            if (v != null && Control.ManualControl.IsPulseVessel(v))
            {
                Control.ManualControl.StopPulse("vessel_lost");
            }
        }

        /// <summary>Docking: a pulse vessel that stays the dominant vessel gets no vessel change or destroy event.</summary>
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> a)
        {
            if ((a.from?.vessel != null && Control.ManualControl.IsPulseVessel(a.from.vessel))
                || (a.to?.vessel != null && Control.ManualControl.IsPulseVessel(a.to.vessel)))
            {
                Control.ManualControl.StopPulse("docked");
            }
        }
    }
}
