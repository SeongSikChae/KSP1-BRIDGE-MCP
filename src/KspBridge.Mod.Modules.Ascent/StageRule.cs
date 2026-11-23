using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    internal enum StageReason
    {
        None,

        /// <summary>Parallel boosters: every engine the next stage drops is out while another engine runs on (boosters_out).</summary>
        BoostersOut,

        /// <summary>No engine runs: first ignition, or a spent stage to drop and the next to light (no_running_engine).</summary>
        NoRunningEngine,
    }

    /// <summary>When the next stage is activated automatically and when the engines are exhausted (spec C6, C7, D001, D008, D015).</summary>
    internal static class StageRule
    {
        public const string BoostersOutName = "boosters_out";
        public const string NoRunningEngineName = "no_running_engine";

        public static string Name(StageReason reason)
            => reason == StageReason.BoostersOut ? BoostersOutName : reason == StageReason.NoRunningEngine ? NoRunningEngineName : null;

        /// <summary>The engine is dropped when <paramref name="next"/> is activated.</summary>
        public static bool Dropped(EngineInfo e, int next) => e.Separation >= next;

        /// <summary>
        /// A usable engine (spec terms): propellant reaches it, it is not flamed out and <paramref name="next"/> does not drop it; and it
        /// runs, or it has not been ignited, staging is on, its stage is not past (stage ≤ next) and it is not dropped before it lights.
        /// An engine of a stage already activated that is not ignited (shut down by hand) does not light by staging.
        /// </summary>
        public static bool Usable(EngineInfo e, int next)
        {
            if (!e.Propellant || e.FlameOut || Dropped(e, next))
                return false;
            if (e.Operational)
                return true;
            return !e.Ignited && e.StagingOn && e.Stage <= next && e.Separation < e.Stage;
        }

        /// <summary>Why the next stage should be activated now; None when it should not.</summary>
        public static StageReason Judge(bool wantThrust, bool autoStage, int? next, IReadOnlyList<EngineInfo> engines)
        {
            if (!wantThrust || !autoStage || !next.HasValue || !AnyUsable(next.Value, engines))
                return StageReason.None;
            int n = next.Value;
            bool anyRunning = false;
            bool anyDropped = false;
            bool droppedBusy = false;
            bool keptRunning = false;
            foreach (EngineInfo e in engines)
            {
                if (e.Operational)
                    anyRunning = true;
                if (Dropped(e, n))
                {
                    anyDropped = true;
                    if (e.Operational || !e.Ignited)
                        droppedBusy = true;
                }
                else if (e.Operational)
                {
                    keptRunning = true;
                }
            }
            if (!anyRunning)
                return StageReason.NoRunningEngine;
            return anyDropped && !droppedBusy && keptRunning ? StageReason.BoostersOut : StageReason.None;
        }

        /// <summary>Engines exhausted (spec C7): thrust wanted, no engine runs, and staging cannot bring one.</summary>
        public static bool Exhausted(bool wantThrust, bool autoStage, int? next, IReadOnlyList<EngineInfo> engines)
        {
            if (!wantThrust)
                return false;
            foreach (EngineInfo e in engines)
            {
                if (e.Operational)
                    return false;
            }
            return !autoStage || !next.HasValue || !AnyUsable(next.Value, engines);
        }

        private static bool AnyUsable(int next, IReadOnlyList<EngineInfo> engines)
        {
            foreach (EngineInfo e in engines)
            {
                if (Usable(e, next))
                    return true;
            }
            return false;
        }
    }
}
