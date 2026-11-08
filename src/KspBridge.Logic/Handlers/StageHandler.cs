using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Handlers
{
    public static class StageHandler
    {
        /// <summary>GET /api/stages — the game's own per-stage delta-v (VesselDeltaV).</summary>
        public static object GetStages()
        {
            Vessel v = Guard.ActiveVessel();
            VesselDeltaV dv = v.VesselDeltaV;
            if (dv == null || !dv.IsReady)
            {
                throw new ApiException(503, "not_ready", "delta-v is not calculated yet, retry in a moment");
            }

            List<object> stages = new List<object>();
            foreach (DeltaVStageInfo s in dv.OperatingStageInfo)
            {
                stages.Add(new Dictionary<string, object>
                {
                    ["stage"] = s.stage,
                    ["dv_vac"] = (double)s.deltaVinVac,
                    ["dv_asl"] = (double)s.deltaVatASL,
                    ["dv_actual"] = (double)s.deltaVActual,
                    ["twr_vac"] = (double)s.TWRVac,
                    ["twr_asl"] = (double)s.TWRASL,
                    ["twr_actual"] = (double)s.TWRActual,
                    ["burn_time"] = s.stageBurnTime,
                    ["start_mass"] = (double)s.startMass,
                    ["end_mass"] = (double)s.endMass,
                    ["parts"] = v.parts.Where(p => p.inverseStage == s.stage && p.hasStagingIcon)
                        .Select(p => (object)p.partInfo.title).ToList(),
                });
            }
            return new Dictionary<string, object>
            {
                ["current_stage"] = v.currentStage,
                ["total_dv_vac"] = dv.TotalDeltaVVac,
                ["total_dv_asl"] = dv.TotalDeltaVASL,
                ["total_dv_actual"] = dv.TotalDeltaVActual,
                ["stages"] = stages,
            };
        }
    }
}
