using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>
    /// The execute_node step without the game (spec C5, C8): waits at rate 1 until the burn start decided once at the start
    /// (<see cref="NodeStart"/>), then burns along the delta-v left with the same rule as circularize (<see cref="BurnControl"/>).
    /// </summary>
    internal sealed class ExecuteNodeLogic
    {
        private readonly BurnControl burn;
        private bool burning;

        public ExecuteNodeLogic(ExecuteNodeArgs args) => burn = new BurnControl(args.ToleranceMps, args.AutoStage);

        public double StartUt { get; private set; }
        public double BurnSeconds { get; private set; }
        public double NodeUt { get; private set; }
        public double RemainingDv { get; private set; }

        /// <summary>UT of the first burning frame; null before it.</summary>
        public double? BurnStartUt { get; private set; }

        public string Phase => burning ? "burn" : "wait";

        /// <summary>Takes the start decided by <see cref="NodeStart.Judge"/> (no failure).</summary>
        public void Start(NodeStartResult start)
        {
            NodeUt = start.NodeUt;
            BurnSeconds = start.BurnSeconds;
            StartUt = start.StartUt;
        }

        /// <param name="remainingDv">ManeuverNode.GetBurnVector of the vessel's orbit (world).</param>
        public BurnCommand Tick(double ut, Vec remainingDv, AscentReading r)
        {
            RemainingDv = remainingDv.Length;
            if (!burning && ut >= StartUt)
            {
                burning = true;
                BurnStartUt = ut;
            }
            return burn.Tick(remainingDv, burning, r);
        }

        /// <summary>step_progress (spec C8).</summary>
        public Dictionary<string, object> Progress(int stages) => new Dictionary<string, object>
        {
            ["phase"] = Phase,
            ["start_ut"] = StartUt,
            ["burn_time_s"] = BurnSeconds,
            ["remaining_dv_mps"] = RemainingDv,
            ["nose_error_deg"] = burn.Last == null ? (double?)null : burn.NoseError,
            ["throttle"] = burn.Last?.Throttle,
            ["stages"] = stages,
        };
    }
}
