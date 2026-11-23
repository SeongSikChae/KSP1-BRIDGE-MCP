using KspBridge.Mod.Modules.Ascent;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>The orbit after a node (spec terms "노드 뒤 궤도"), as values.</summary>
    internal sealed class AfterOrbit
    {
        public string Body;

        /// <summary>Apoapsis altitude (m); null when the orbit is open.</summary>
        public double? ApoapsisM;

        public double PeriapsisM;
        public double Eccentricity;
        public double InclinationDeg;

        /// <summary>First apoapsis after the node UT on the conic (D011, D012); null when open.</summary>
        public double? NextApoapsisUt;

        /// <summary>First periapsis after the node UT on the conic; null for an open orbit past it.</summary>
        public double? NextPeriapsisUt;
    }

    /// <summary>One node as values, read by the module and made into a get_nodes item here (spec C3).</summary>
    internal sealed class NodeInfo
    {
        public const string NoAfterReason = "the game computed no orbit after this node (it is outside the predicted orbit segments)";

        public int Index;
        public double Ut;

        /// <summary>The game's DeltaV: x radial, y normal, z prograde (m/s).</summary>
        public double RadialMps;
        public double NormalMps;
        public double ProgradeMps;

        /// <summary>Null when the game has no orbit after the node.</summary>
        public AfterOrbit After;

        /// <summary>The delta-v still to burn (first node only, spec terms "남은 Δv"); null without an orbit after it.</summary>
        public double? RemainingDvMps;

        public double DvMps => Math.Sqrt(RadialMps * RadialMps + NormalMps * NormalMps + ProgradeMps * ProgradeMps);

        public bool First => Index == 0;

        /// <summary>
        /// The estimated burn time (spec terms "예상 연소 시간"): the first node for the delta-v left, the others for their whole
        /// delta-v, as circularize estimates it; null when it cannot be estimated or there is no orbit after the node.
        /// </summary>
        public double? BurnSeconds(IReadOnlyList<StageBurn> stages, double accel)
        {
            if (After == null)
                return null;
            double dv = First ? RemainingDvMps ?? 0 : DvMps;
            return BurnEstimate.Seconds(dv, stages, accel);
        }

        /// <summary>The get_nodes item (spec C3).</summary>
        public Dictionary<string, object> Item(double now, IReadOnlyList<StageBurn> stages, double accel)
        {
            double? burn = BurnSeconds(stages, accel);
            Dictionary<string, object> item = new Dictionary<string, object>
            {
                ["index"] = Index,
                ["ut"] = Ut,
                ["in_s"] = Ut - now,
                ["prograde_mps"] = ProgradeMps,
                ["normal_mps"] = NormalMps,
                ["radial_mps"] = RadialMps,
                ["dv_mps"] = DvMps,
                ["burn_time_s"] = burn,
                ["burn_start_ut"] = burn.HasValue ? Ut - burn.Value / 2 : (double?)null,
                ["after"] = After == null ? null : AfterItem(After),
            };
            if (After == null)
                item["after_missing"] = NoAfterReason;
            if (First)
                item["remaining_dv_mps"] = After == null ? null : RemainingDvMps;
            return item;
        }

        private static Dictionary<string, object> AfterItem(AfterOrbit a) => new Dictionary<string, object>
        {
            ["body"] = a.Body,
            ["apoapsis_m"] = a.ApoapsisM,
            ["periapsis_m"] = a.PeriapsisM,
            ["eccentricity"] = a.Eccentricity,
            ["inclination_deg"] = a.InclinationDeg,
            ["next_apoapsis_ut"] = a.NextApoapsisUt,
            ["next_periapsis_ut"] = a.NextPeriapsisUt,
        };
    }
}
