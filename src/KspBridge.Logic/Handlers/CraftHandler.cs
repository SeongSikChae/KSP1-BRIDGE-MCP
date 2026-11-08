using System.Collections.Generic;
using System.IO;
using KspBridge.Craft;

namespace KspBridge.Handlers
{
    public static class CraftHandler
    {
        /// <summary>POST /api/craft/build {name, facility, overwrite, dry_run, root}</summary>
        public static object Build(RequestBody body)
        {
            if (HighLogic.CurrentGame == null || PartLoader.Instance == null)
            {
                throw new ApiException(409, "scene_not_allowed", "no game is loaded");
            }

            string name = Fmt.SafeName(body.GetString("name"));
            string facility = body.Has("facility") ? body.GetString("facility").ToUpperInvariant() : "VAB";
            if (facility != "VAB" && facility != "SPH")
            {
                throw ApiException.BadRequest("facility must be VAB or SPH");
            }
            bool overwrite = body.Has("overwrite") && body.GetBool("overwrite");
            bool dryRun = body.Has("dry_run") && body.GetBool("dry_run");
            PartSpec root = PartSpec.Parse(body.GetObject("root"), "root");

            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            CraftBuilder builder = new CraftBuilder(errors, warnings);
            bool built = builder.Build(root);

            string path = ShipPath(facility, name);
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                ["name"] = name,
                ["facility"] = facility,
                ["path"] = path,
                ["saved"] = false,
            };

            if (built)
            {
                List<BuiltPart> parts = builder.Parts;
                double cost = CraftEstimator.Cost(parts);
                double mass = CraftEstimator.Mass(parts);
                Limits(facility, out int maxParts, out float maxMass);

                if (maxParts > 0 && parts.Count > maxParts)
                {
                    errors.Add("part count " + parts.Count + " exceeds the facility limit " + maxParts);
                }
                if (maxMass > 0 && mass > maxMass)
                {
                    errors.Add("mass " + mass.ToString("F2") + " t exceeds the launch site limit " + maxMass + " t");
                }
                if (Career.IsCareer && Funding.Instance != null && Funding.Instance.Funds < cost)
                {
                    warnings.Add("not enough funds (" + Funding.Instance.Funds.ToString("F0") + " < " + cost.ToString("F0") + ")");
                }
                if (!parts.Exists(p => p.Prefab.FindModuleImplementing<ModuleCommand>() != null))
                {
                    warnings.Add("no command part (pod or probe core)");
                }

                List<object> stages = CraftEstimator.Stages(parts);
                if (stages.Count > 0 && stages[0] is Dictionary<string, object> first
                    && first.TryGetValue("twr_asl", out object twr) && twr is double t && t < 1.2)
                {
                    warnings.Add("first stage TWR (sea level) " + t.ToString("F2") + " < 1.2");
                }

                result["part_count"] = parts.Count;
                result["cost"] = cost;
                result["mass"] = mass;
                result["limits"] = new Dictionary<string, object> { ["max_parts"] = maxParts, ["max_mass"] = (double)maxMass };
                result["stages"] = stages;

                if (File.Exists(path) && !overwrite)
                {
                    errors.Add("craft '" + name + "' already exists (set overwrite=true)");
                }
                if (errors.Count == 0 && !dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    builder.ToConfigNode(name, facility).Save(path);
                    result["saved"] = true;
                }
            }

            result["errors"] = errors;
            result["warnings"] = warnings;
            return result;
        }

        public static string ShipPath(string facility, string name)
        {
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "saves"), HighLogic.SaveFolder), "Ships"),
                Path.Combine(facility, name + ".craft"));
        }

        private static void Limits(string facility, out int maxParts, out float maxMass)
        {
            maxParts = 0;
            maxMass = 0;
            if (!Career.IsCareer || GameVariables.Instance == null)
            {
                return;
            }
            bool vab = facility == "VAB";
            float editor = ScenarioUpgradeableFacilities.GetFacilityLevel(vab ? SpaceCenterFacility.VehicleAssemblyBuilding : SpaceCenterFacility.SpaceplaneHangar);
            float site = ScenarioUpgradeableFacilities.GetFacilityLevel(vab ? SpaceCenterFacility.LaunchPad : SpaceCenterFacility.Runway);
            maxParts = GameVariables.Instance.GetPartCountLimit(editor, vab);
            maxMass = GameVariables.Instance.GetCraftMassLimit(site, vab);
            if (maxParts == int.MaxValue)
            {
                maxParts = 0;
            }
            if (float.IsInfinity(maxMass) || maxMass >= float.MaxValue)
            {
                maxMass = 0;
            }
        }
    }
}
