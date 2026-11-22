using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>
    /// Measurement for game checks (spec C1): measure/start, measure/stop and measure/get, and the plan steps measure_start and
    /// measure_stop. While a measurement runs, a TimingManager physics-frame hook (BetterLateThanNever, D009) reads one sample
    /// of the active vessel per frame. Scene loads and active-vessel changes end it with a reason (C9). The vessel and the
    /// running plan are never changed.
    /// </summary>
    public sealed class MeasureModule : IBridgeModule
    {
        private const TimingManager.TimingStage Stage = TimingManager.TimingStage.BetterLateThanNever;

        private readonly Recorder recorder = new Recorder();
        private readonly SampleReader reader = new SampleReader();
        private ModuleContext context;
        private TimingManager.UpdateAction callback;

        public string Name => "measure";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            callback = OnPhysicsFrame;
            context.AddStepType(new MeasureStartStepType(this));
            context.AddStepType(new MeasureStopStepType(this));
            context.AddHandler("start", Start);
            context.AddHandler("stop", Stop);
            context.AddHandler("get", Get);
            context.Subscriptions.Add(GameEvents.onGameSceneLoadRequested, OnLoadRequested);
            context.Subscriptions.Add(GameEvents.onVesselChange, OnVesselChange);
            context.Subscriptions.Add(Unhook);
        }

        /// <summary>Starts a measurement at <paramref name="ut"/> (tool or step); bad_request while one runs.</summary>
        internal Measurement StartMeasurement(double ut, Vessel vessel, int sampleLimit)
        {
            Measurement m = recorder.Start(ut, vessel.GetDisplayName(), sampleLimit);
            Hook();
            return m;
        }

        /// <summary>Ends the running measurement with stopped at <paramref name="ut"/>; bad_request when none runs.</summary>
        internal Measurement StopMeasurement(double ut)
        {
            Measurement m = recorder.Stop(MeasureEndReasons.Stopped, ut, null)
                ?? throw BridgeException.BadRequest("no measurement is running; start one with measure_start");
            Unhook();
            return m;
        }

        private object Start(Dictionary<string, object> args)
        {
            int sampleLimit = MeasureArgs.SampleLimit(args);
            Vessel vessel = RequireReady();
            return MeasureReport.StartAnswer(StartMeasurement(Planetarium.GetUniversalTime(), vessel, sampleLimit));
        }

        private object Stop(Dictionary<string, object> args)
        {
            if (!recorder.IsRunning)
                throw BridgeException.BadRequest("no measurement is running; start one with measure_start");
            return MeasureReport.StopAnswer(StopMeasurement(Planetarium.GetUniversalTime()));
        }

        /// <summary>Never not_ready: answers from the recorder in any scene (C10).</summary>
        private object Get(Dictionary<string, object> args)
            => MeasureReport.GetAnswer(recorder.Current, MeasureReport.ParseQuery(args));

        /// <summary>The active vessel when the tool may start a measurement (C10), otherwise not_ready with the reason.</summary>
        private Vessel RequireReady()
        {
            bool inFlight = HighLogic.LoadedScene == GameScenes.FLIGHT;
            Vessel vessel = inFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            string reason = MeasureArgs.Readiness(new MeasureReadinessInput
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

        /// <summary>One physics frame: one sample of the active vessel. Never throws into the game; a failure ends with error.</summary>
        private void OnPhysicsFrame()
        {
            if (!recorder.IsRunning)
            {
                Unhook();
                return;
            }
            try
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (vessel == null)
                {
                    End(MeasureEndReasons.Error, "there is no active vessel");
                    return;
                }
                if (recorder.Add(reader.Read(vessel)))
                    Unhook();
            }
            catch (Exception ex)
            {
                End(MeasureEndReasons.Error, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnLoadRequested(GameScenes scene)
        {
            if (recorder.IsRunning)
                End(MeasureArgs.EndReason(MeasureEvent.SceneLoadRequested, HighLogic.LoadedScene == GameScenes.FLIGHT, scene == GameScenes.FLIGHT), null);
        }

        private void OnVesselChange(Vessel vessel)
        {
            if (recorder.IsRunning)
                End(MeasureArgs.EndReason(MeasureEvent.VesselChange, HighLogic.LoadedScene == GameScenes.FLIGHT, false), null);
        }

        /// <summary>Ends the running measurement at the current UT (or its last sample's UT when the clock cannot be read).</summary>
        private void End(string reason, string error)
        {
            try
            {
                recorder.Stop(reason, Now(), error);
            }
            catch (Exception ex)
            {
                context.Log?.Invoke("measure: ending the measurement failed: " + ex);
            }
            Unhook();
        }

        private double Now()
        {
            try
            {
                return Planetarium.GetUniversalTime();
            }
            catch (Exception)
            {
                Measurement m = recorder.Running;
                return m == null ? 0 : (m.Samples.Count > 0 ? m.Samples[m.Samples.Count - 1].Ut : m.StartUt);
            }
        }

        /// <summary>Installs the physics-frame hook once (FixedUpdateAdd combines delegates, so a second add would run it twice).</summary>
        private void Hook()
        {
            TimingManager.FixedUpdateRemove(Stage, callback);
            TimingManager.FixedUpdateAdd(Stage, callback);
        }

        private void Unhook()
        {
            try
            {
                TimingManager.FixedUpdateRemove(Stage, callback);
            }
            catch (Exception ex)
            {
                context.Log?.Invoke("measure: removing the physics-frame hook failed: " + ex);
            }
        }
    }
}
