using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>
    /// Runs the game's delta-v calculator for one check (KSP side). The game's delta-v app body decides the sea-level values in the
    /// editor, so it is switched to the requested body only while the check runs and switched back in <see cref="Restore"/>, the one
    /// place that does it. Completion is the calculator's completed event while its private calcsDirty is false: an event with the
    /// flag set belongs to a run that a newer request (ours or an edit) has already made stale.
    /// </summary>
    internal sealed class CheckRunner
    {
        /// <summary>Read only; when missing (another game version) a check is refused before anything changes.</summary>
        private static readonly FieldInfo CalcsDirtyField =
            typeof(VesselDeltaV).GetField("calcsDirty", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Both sets of the calculator's stage infos (D074). The public stage lists return one set only, and the next
        /// calculation uses the other, so the private list is read to clear the burn segments of both.
        /// </summary>
        private static readonly FieldInfo StageInfoEntriesField =
            typeof(DeltaVEngineStageSet).GetField("stageInfoEntries", BindingFlags.Instance | BindingFlags.NonPublic);

        private VesselDeltaV calculator;
        private CelestialBody originalBody;
        private bool bodySwitched;
        private Action<VesselDeltaV> onDone;

        private PendingCheck pending;
        private int nextId;

        // ---- the one check in progress (C10) ----

        /// <summary>
        /// Starts a check: clears a finished or overdue earlier check (restoring the body), refuses while another one is still
        /// within its 10 s, then asks the calculator for fresh values. <paramref name="snapshot"/> runs at the completed event
        /// and turns the calculator into the answer.
        /// </summary>
        public int Start(VesselDeltaV vd, CelestialBody body, double now, Func<VesselDeltaV, object> snapshot)
        {
            if (pending != null)
            {
                if (!pending.CanReplace(now))
                    throw BridgeException.NotReady("another check is running (check " + pending.Id + "); retry shortly");
                Restore();
                pending = null;
            }
            PendingCheck check = new PendingCheck(++nextId, now);
            pending = check;
            try
            {
                Begin(vd, body, done =>
                {
                    try
                    {
                        check.Complete(snapshot(done));
                    }
                    catch (Exception ex)
                    {
                        check.Cancel("the check failed: " + ex.Message);
                        throw;
                    }
                });
            }
            catch
            {
                // A game exception while starting: the body goes back and no check is left waiting.
                Restore();
                pending = null;
                throw;
            }
            return check.Id;
        }

        /// <summary>The answer of check <paramref name="id"/>: {pending: true}, the result, or not_ready (time-out, cancel).</summary>
        public object Result(int id, double now)
        {
            if (pending == null || pending.Id != id)
                throw BridgeException.NotReady("check " + id + " is not in progress (finished, replaced or never started); call check_ship again");
            if (pending.Expire(now))
                Restore();
            switch (pending.State)
            {
                case CheckState.Waiting:
                    return new Dictionary<string, object> { ["check"] = id, ["pending"] = true };
                case CheckState.Done:
                    object result = pending.Result;
                    pending = null;
                    return result;
                default:
                    string reason = pending.Reason;
                    pending = null;
                    throw BridgeException.NotReady(reason);
            }
        }

        /// <summary>A scene change or module stop: the waiting check ends with <paramref name="reason"/> and the body is restored.</summary>
        public void Cancel(string reason)
        {
            pending?.Cancel(reason);
            Restore();
        }

        /// <summary>The body by internal name (case-insensitive); the home body when the name is null or empty.</summary>
        public static CelestialBody FindBody(string name)
        {
            if (string.IsNullOrEmpty(name))
                return FlightGlobals.GetHomeBody();
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body != null && string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            throw BridgeException.BadRequest("no celestial body named '" + name + "' (use an internal name such as Kerbin or Mun)");
        }

        /// <summary>
        /// Refuses (not_ready, nothing changed) when the calculator cannot be used: no calculator on the ship yet, delta-v
        /// simulations switched off in the game settings, no delta-v app values, or the completion flag or the stage info list
        /// not found.
        /// </summary>
        public static VesselDeltaV RequireCalculator(ShipConstruct ship)
        {
            if (CalcsDirtyField == null)
                throw BridgeException.NotReady("cannot tell when the delta-v calculator has finished (VesselDeltaV.calcsDirty not found in this game version)");
            if (StageInfoEntriesField == null || StageInfoEntriesField.FieldType != typeof(List<DeltaVEngineStageSet.DeltaVStageInfoEntry>))
                throw BridgeException.NotReady("cannot clear the delta-v calculator's earlier burn segments (DeltaVEngineStageSet.stageInfoEntries not found in this game version)");
            if (!DeltaVGlobals.DoStockSimulations)
                throw BridgeException.NotReady("delta-v calculations are switched off in the game settings");
            if (DeltaVGlobals.DeltaVAppValues == null)
                throw BridgeException.NotReady("the game's delta-v app is not ready yet; retry shortly");
            VesselDeltaV vd = ship.vesselDeltaV;
            if (vd == null)
                throw BridgeException.NotReady("the ship has no delta-v calculator yet; retry shortly");
            return vd;
        }

        /// <summary>
        /// Switches the app body to <paramref name="body"/> when it differs, asks for a fresh calculation and remembers
        /// <paramref name="done"/> for the completed event. Always recalculates so a finished earlier run is never read.
        /// </summary>
        public void Begin(VesselDeltaV vd, CelestialBody body, Action<VesselDeltaV> done)
        {
            Restore();
            calculator = vd;
            onDone = done;
            originalBody = DeltaVGlobals.DeltaVAppValues.body;
            if (originalBody != body)
            {
                DeltaVGlobals.DeltaVAppValues.body = body;
                bodySwitched = true;
            }
            ClearSegments(vd);
            vd.SetCalcsDirty(resetPartCaches: false);
        }

        /// <summary>
        /// Empties the burn segments (deltaVCalcs) of both stage info sets (D074). The calculator refills them for every stage
        /// with an active engine and leaves skipped stages as they were, so without this a stage that lost its engines could
        /// still show an earlier calculation's segments. Nothing else in the game reads the list; an entry's missing set is skipped.
        /// </summary>
        private static void ClearSegments(VesselDeltaV vd)
        {
            if (vd.engineStageSet == null)
                return;
            List<DeltaVEngineStageSet.DeltaVStageInfoEntry> entries =
                (List<DeltaVEngineStageSet.DeltaVStageInfoEntry>)StageInfoEntriesField.GetValue(vd.engineStageSet);
            if (entries == null)
                return;
            foreach (DeltaVEngineStageSet.DeltaVStageInfoEntry entry in entries)
            {
                if (entry == null)
                    continue;
                entry.stageInfo_InstanceOne?.deltaVCalcs?.Clear();
                entry.stageInfo_InstanceTwo?.deltaVCalcs?.Clear();
            }
        }

        /// <summary>
        /// The calculator's completed event. Calls the waiting callback only when this calculator is clean (no newer request
        /// pending), then restores the body. The callback runs before the restore so it reads the requested body's values.
        /// </summary>
        public void OnCalcsCompleted()
        {
            if (onDone == null || calculator == null)
                return;
            if ((bool)CalcsDirtyField.GetValue(calculator))
                return;
            Action<VesselDeltaV> done = onDone;
            VesselDeltaV vd = calculator;
            onDone = null;
            try
            {
                done(vd);
            }
            finally
            {
                Restore();
            }
        }

        /// <summary>
        /// Stops waiting and puts the app body back (recalculating so the game's delta-v app shows the original body again).
        /// Safe to call at any time and more than once.
        /// </summary>
        public void Restore()
        {
            onDone = null;
            VesselDeltaV vd = calculator;
            calculator = null;
            if (!bodySwitched)
                return;
            bodySwitched = false;
            if (DeltaVGlobals.DeltaVAppValues != null)
                DeltaVGlobals.DeltaVAppValues.body = originalBody;
            originalBody = null;
            if (vd != null)
                vd.SetCalcsDirty(resetPartCaches: false);
        }
    }
}
