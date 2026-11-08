using System.Collections.Generic;

namespace KspBridge.Handlers
{
    public static class ScienceHandler
    {
        /// <summary>GET /api/science</summary>
        public static object Get()
        {
            Vessel v = Guard.ActiveVessel();
            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(v);
            // Landed at a KSC site (LaunchPad, Runway, ...) the game uses the site as the biome.
            string biome = string.IsNullOrEmpty(v.landedAt)
                ? ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude)
                : Vessel.GetLandedAtString(v.landedAt);

            List<object> list = new List<object>();
            foreach (Part p in v.parts)
            {
                for (int i = 0; i < p.Modules.Count; i++)
                {
                    if (!(p.Modules[i] is ModuleScienceExperiment m))
                    {
                        continue;
                    }
                    ScienceExperiment exp = m.experiment ?? ResearchAndDevelopment.GetExperiment(m.experimentID);
                    bool available = exp != null && exp.IsAvailableWhile(sit, v.mainBody);
                    double expected = 0;
                    if (available && ResearchAndDevelopment.Instance != null)
                    {
                        ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(
                            exp, sit, v.mainBody, exp.BiomeIsRelevantWhile(sit) ? biome : "", "");
                        expected = ResearchAndDevelopment.GetScienceValue(exp.baseValue * exp.dataScale, subject)
                            * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
                    }

                    List<object> data = new List<object>();
                    foreach (ScienceData d in m.GetData())
                    {
                        ScienceSubject subj = ResearchAndDevelopment.GetSubjectByID(d.subjectID);
                        data.Add(new Dictionary<string, object>
                        {
                            ["title"] = d.title,
                            ["subject_id"] = d.subjectID,
                            ["amount"] = (double)d.dataAmount,
                            ["science_value"] = subj != null
                                ? (double)(ResearchAndDevelopment.GetScienceValue(d.dataAmount, subj) * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier)
                                : 0.0,
                        });
                    }

                    list.Add(new Dictionary<string, object>
                    {
                        ["id"] = p.persistentId + ":" + i,
                        ["part"] = p.partInfo.title,
                        ["experiment_id"] = m.experimentID,
                        ["title"] = exp != null ? exp.experimentTitle : m.experimentID,
                        ["deployed"] = m.Deployed,
                        ["inoperable"] = m.Inoperable,
                        ["rerunnable"] = m.rerunnable,
                        ["can_run_now"] = available && !m.Deployed && !m.Inoperable,
                        ["expected_science"] = expected,
                        ["data"] = data,
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["situation"] = sit.ToString(),
                ["biome"] = biome,
                ["experiments"] = list,
            };
        }

        /// <summary>POST /api/science/run {id}</summary>
        public static object Run(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            ModuleScienceExperiment m = Find(v, body.GetString("id"));
            if (m.Inoperable)
            {
                throw ApiException.BadRequest("experiment is inoperable (needs reset by a scientist)");
            }
            if (m.Deployed)
            {
                throw ApiException.BadRequest("experiment already holds data (discard it first)");
            }
            m.DeployExperiment();
            return new Dictionary<string, object> { ["accepted"] = true, ["note"] = "the game may show a result dialog; data stays in the part" };
        }

        /// <summary>POST /api/science/discard {id}</summary>
        public static object Discard(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            ModuleScienceExperiment m = Find(v, body.GetString("id"));
            int n = 0;
            foreach (ScienceData d in m.GetData())
            {
                m.DumpData(d);
                n++;
            }
            // non-rerunnable experiments (goo, materials bay) stay inoperable until a scientist cleans them, as in stock
            if (m.rerunnable && m.resettable && m.Deployed)
            {
                m.ResetExperiment();
            }
            return new Dictionary<string, object> { ["discarded"] = n, ["deployed"] = m.Deployed, ["inoperable"] = m.Inoperable };
        }

        private static ModuleScienceExperiment Find(Vessel v, string id)
        {
            string[] parts = id.Split(':');
            if (parts.Length != 2 || !uint.TryParse(parts[0], out uint pid) || !int.TryParse(parts[1], out int index))
            {
                throw ApiException.BadRequest("id must be '<part persistentId>:<module index>' from get_science");
            }
            foreach (Part p in v.parts)
            {
                if (p.persistentId == pid && index >= 0 && index < p.Modules.Count && p.Modules[index] is ModuleScienceExperiment m)
                {
                    return m;
                }
            }
            throw ApiException.NotFound("no experiment with id " + id + " on the active vessel");
        }
    }
}
