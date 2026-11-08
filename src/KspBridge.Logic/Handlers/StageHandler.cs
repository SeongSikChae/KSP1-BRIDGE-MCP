using System.Collections.Generic;
using System.Linq;
using KspBridge.Craft;

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
            List<string> warnings = new List<string>();
            List<object> staging = Staging(v, warnings);
            Dictionary<string, object> analysis = CraftAnalysis.Analyze(CraftAnalysis.FromVessel(v), warnings);
            return new Dictionary<string, object>
            {
                ["current_stage"] = v.currentStage,
                ["total_dv_vac"] = dv.TotalDeltaVVac,
                ["total_dv_asl"] = dv.TotalDeltaVASL,
                ["total_dv_actual"] = dv.TotalDeltaVActual,
                ["stages"] = stages,
                ["staging"] = staging,
                ["analysis"] = analysis,
                ["warnings"] = warnings,
            };
        }

        /// <summary>Every stage still to fire (including decouple-only stages): parts dropped and steering left.</summary>
        private static List<object> Staging(Vessel v, List<string> warnings)
        {
            List<object> staging = new List<object>();
            HashSet<Part> attached = new HashSet<Part>(v.parts);
            for (int s = v.currentStage - 1; s >= 0; s--)
            {
                List<Part> dropped = CraftAnalysis.Drops(v, s, attached);
                attached.ExceptWith(dropped);
                List<Part> burning = attached.Where(p => p.inverseStage >= s && p.FindModuleImplementing<ModuleEngines>() != null).ToList();
                Dictionary<string, object> control = CraftAnalysis.Control(attached, burning);
                CraftAnalysis.StageWarnings(s, dropped, burning.Count > 0, (bool)control["steerable"], warnings);
                staging.Add(new Dictionary<string, object>
                {
                    ["stage"] = s,
                    ["drops"] = dropped.Select(p => (object)p.partInfo.title).ToList(),
                    ["control"] = control,
                });
            }
            return staging;
        }
    }
}
