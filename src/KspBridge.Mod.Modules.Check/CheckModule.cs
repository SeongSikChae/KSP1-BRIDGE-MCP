using KSP.UI.Screens;
using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>
    /// Checks the ship being built in the editor: per-stage mass, delta-v, TWR and burn time from the game's delta-v calculator,
    /// and structural warnings. Read-only: the ship is never changed; the game's delta-v app body is switched only while a check
    /// runs. The calculation takes several frames, so check/start answers {check, pending} and check/result is asked until done.
    /// </summary>
    public sealed class CheckModule : IBridgeModule
    {
        /// <summary>Read only; when missing (another game version) the readiness judgement refuses checks.</summary>
        private static readonly FieldInfo SortRoutineField =
            typeof(StageManager).GetField("sortRoutine", BindingFlags.NonPublic | BindingFlags.Instance);

        private ModuleContext context;
        private readonly CheckRunner runner = new CheckRunner();

        /// <summary>Frame of the last onEditorRestart/onEditorLoad (craft reload, new ship, in-game facility switch).</summary>
        private int? lastLoadFrame;

        public string Name => "check";

        public void Register(ModuleContext moduleContext)
        {
            context = moduleContext;
            context.Subscriptions.Add(GameEvents.onDeltaVCalcsCompleted, () => Guard("delta-v completed", runner.OnCalcsCompleted));
            context.Subscriptions.Add(GameEvents.onEditorRestart, () => lastLoadFrame = Time.frameCount);
            EventData<ShipConstruct, CraftBrowserDialog.LoadType>.OnEvent onLoad = (ship, type) => lastLoadFrame = Time.frameCount;
            GameEvents.onEditorLoad.Add(onLoad);
            context.Subscriptions.Add(() => GameEvents.onEditorLoad.Remove(onLoad));
            context.Subscriptions.Add(GameEvents.onGameSceneLoadRequested, scene => Guard("scene change", () =>
            {
                lastLoadFrame = null;
                runner.Cancel("the scene changed to " + scene);
            }));
            // Released when the generation stops (hot reload), so a reload never leaves the app body switched.
            context.Subscriptions.Add(() => Guard("module stop", () => runner.Cancel("the check module was reloaded")));

            context.AddHandler("start", Start);
            context.AddHandler("result", args =>
            {
                double value = args.Num("check", required: true).Value;
                if (double.IsNaN(value) || value != Math.Floor(value) || value < 1 || value > int.MaxValue)
                    throw BridgeException.BadRequest("'check' must be a check number from check/start");
                return runner.Result((int)value, Now);
            });
        }

        private static double Now => Time.realtimeSinceStartup;

        /// <summary>Game callbacks never throw into the game: a failure restores the body and is logged.</summary>
        private void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                context.Log("check " + what + " failed: " + ex);
                try
                {
                    runner.Restore();
                }
                catch (Exception restoreEx)
                {
                    context.Log("check restore failed: " + restoreEx);
                }
            }
        }

        private object Start(Dictionary<string, object> args)
        {
            object rawBody = args.TryGetValue("body", out object b) ? b : null;
            if (rawBody != null && !(rawBody is string))
                throw BridgeException.BadRequest("'body' must be a celestial body's internal name (text), e.g. Kerbin or Mun");
            ShipConstruct ship = RequireReady();
            CelestialBody body = CheckRunner.FindBody((string)rawBody);
            if (ship.parts.Count == 0)
                return Checks.EmptyResult(body.bodyName, ship.shipFacility.ToString());
            VesselDeltaV vd = CheckRunner.RequireCalculator(ship);
            int id = runner.Start(vd, body, Now, done => Checks.Run(ShipSnapshot.Take(ship, done, body)));
            return new Dictionary<string, object> { ["check"] = id, ["pending"] = true };
        }

        /// <summary>The editor ship when a check may start (C10), otherwise not_ready with the reason.</summary>
        private ShipConstruct RequireReady()
        {
            EditorLogic logic = HighLogic.LoadedScene == GameScenes.EDITOR ? EditorLogic.fetch : null;
            ShipConstruct ship = logic != null ? logic.ship : null;
            StageManager manager = StageManager.Instance;
            ReadinessInput input = new ReadinessInput
            {
                GameLoaded = HighLogic.CurrentGame != null,
                CoreBlockReason = context.Session.BlockReason(),
                SceneName = context.Session.Scene,
                EditorScene = HighLogic.LoadedScene == GameScenes.EDITOR,
                HasShip = ship != null,
                HasEditorDriver = EditorDriver.fetch != null,
                RestartingEditor = EditorDriver.fetch != null && EditorDriver.fetch.restartingEditor,
                FramesSinceLoad = lastLoadFrame.HasValue ? Time.frameCount - lastLoadFrame.Value : int.MaxValue,
                AllPartsStarted = ship != null && ship.parts.All(p => p != null && p.editorStarted),
                SortSignalFound = SortRoutineField != null,
                SortRunning = SortRoutineField != null && manager != null && SortRoutineField.GetValue(manager) != null,
            };
            string reason = Readiness.Judge(input);
            if (reason != null)
                throw BridgeException.NotReady(reason);
            return ship;
        }
    }
}
