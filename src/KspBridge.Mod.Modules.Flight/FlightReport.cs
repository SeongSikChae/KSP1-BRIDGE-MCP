using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Optional parts of the get_flight answer (include).</summary>
    [Flags]
    public enum FlightInclude
    {
        None = 0,
        Stages = 1,
        Resources = 2,
        Body = 4,
    }

    /// <summary>
    /// Builds the get_flight answer from <see cref="FlightData"/> (pure). The default answer is a light summary; the stage table,
    /// resource list and body constants are added only when asked for (D005). Delta-v values come from the game's calculator
    /// (D003) and are null with a reason while it has no values yet (C3).
    /// </summary>
    public static class FlightReport
    {
        public const string IncludeStages = "stages";
        public const string IncludeResources = "resources";
        public const string IncludeBody = "body";

        /// <summary>Parses the include argument (a list of names); unknown or non-text items are bad_request.</summary>
        public static FlightInclude ParseInclude(object raw)
        {
            if (raw == null)
                return FlightInclude.None;
            if (!(raw is List<object> items))
                throw BridgeException.BadRequest("'include' must be a list of names (stages, resources, body)");
            FlightInclude include = FlightInclude.None;
            foreach (object item in items)
            {
                switch (item as string)
                {
                    case IncludeStages:
                        include |= FlightInclude.Stages;
                        break;
                    case IncludeResources:
                        include |= FlightInclude.Resources;
                        break;
                    case IncludeBody:
                        include |= FlightInclude.Body;
                        break;
                    default:
                        throw BridgeException.BadRequest("unknown include item " + Describe(item) + "; use stages, resources or body");
                }
            }
            return include;
        }

        private static string Describe(object item) => item is string s ? "'" + s + "'" : (item == null ? "null" : item.GetType().Name);

        public static Dictionary<string, object> Build(FlightData d, FlightInclude include)
        {
            AttitudeAngles attitude = Attitude.Compute(d.Up, d.North, d.East, d.Nose, d.Top);
            bool open = !(d.Eccentricity < 1);
            double terrain = d.Ocean && d.TerrainHeight < 0 ? 0 : d.TerrainHeight;

            Dictionary<string, object> answer = new Dictionary<string, object>
            {
                ["vessel"] = d.Vessel,
                ["situation"] = d.Situation,
                ["body"] = d.Body,
                ["ut"] = d.UT,
                ["met_s"] = d.MissionTime,
                ["position"] = new Dictionary<string, object>
                {
                    ["latitude_deg"] = d.Latitude,
                    ["longitude_deg"] = NormalizeLongitude(d.Longitude),
                    ["altitude_m"] = d.Altitude,
                    ["terrain_altitude_m"] = d.Altitude - terrain,
                },
                ["speed"] = new Dictionary<string, object>
                {
                    ["surface_mps"] = d.SurfaceSpeed,
                    ["orbital_mps"] = d.OrbitalSpeed,
                    ["vertical_mps"] = d.VerticalSpeed,
                    ["horizontal_surface_mps"] = d.HorizontalSurfaceSpeed,
                },
                ["orbit"] = new Dictionary<string, object>
                {
                    ["apoapsis_m"] = open ? null : (object)d.ApA,
                    ["periapsis_m"] = d.PeA,
                    ["inclination_deg"] = d.Inclination,
                    ["eccentricity"] = d.Eccentricity,
                    ["semi_major_axis_m"] = d.SemiMajorAxis,
                    ["period_s"] = open ? null : (object)d.Period,
                    ["time_to_apoapsis_s"] = open ? null : (object)d.TimeToAp,
                    ["time_to_periapsis_s"] = d.TimeToPe,
                },
                ["attitude"] = new Dictionary<string, object>
                {
                    ["pitch_deg"] = attitude.Pitch,
                    ["heading_deg"] = attitude.Heading,
                    ["roll_deg"] = attitude.Roll,
                },
                ["control"] = new Dictionary<string, object>
                {
                    ["throttle"] = d.Throttle,
                    ["sas"] = d.Sas,
                    ["rcs"] = d.Rcs,
                },
                ["mass_t"] = d.Mass,
                ["thrust_kn"] = d.Thrust,
                ["twr"] = Twr(d.Thrust, d.Mass, d.Gravity),
                ["stage"] = StageSummary(d),
                ["delta_v_ready"] = d.DeltaVReady,
                ["delta_v_not_ready_reason"] = d.DeltaVReady ? null : d.DeltaVNotReadyReason,
            };

            if ((include & FlightInclude.Stages) != 0)
                answer["stages"] = d.DeltaVReady ? StageTable(d) : null;
            if ((include & FlightInclude.Resources) != 0)
                answer["resources"] = d.Resources.OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => (object)new Dictionary<string, object> { ["name"] = r.Name, ["amount"] = r.Amount, ["max"] = r.Max })
                    .ToList();
            if ((include & FlightInclude.Body) != 0)
                answer["body_info"] = BodyInfo(d.BodyInfo);
            return answer;
        }

        /// <summary>Current thrust over weight at the vessel's position; 0 when mass or gravity is 0.</summary>
        public static double Twr(double thrustKn, double massT, double gravity)
            => massT > 0 && gravity > 0 ? thrustKn / (massT * gravity) : 0;

        public static double NormalizeLongitude(double longitude)
        {
            double l = longitude % 360;
            if (l > 180)
                l -= 360;
            else if (l < -180)
                l += 360;
            return l;
        }

        private static Dictionary<string, object> StageSummary(FlightData d)
        {
            StageRefResult stage = StageRef.Judge(d.CurrentStage, d.StagedParts);
            Dictionary<string, object> summary = new Dictionary<string, object>
            {
                ["last_activated"] = stage.LastActivated,
                ["next"] = stage.Next,
                ["reference"] = stage.Reference,
            };
            if (!d.DeltaVReady)
            {
                summary["delta_v_mps"] = null;
                summary["delta_v_vac_mps"] = null;
                summary["burn_time_s"] = null;
                summary["twr"] = null;
                summary["total_delta_v_mps"] = null;
                summary["total_delta_v_vac_mps"] = null;
                return summary;
            }
            // A reference stage the calculator gives no values for (no engine, or no stage at all) shows 0.
            StageCalc calc = stage.Reference.HasValue ? d.Stages.FirstOrDefault(s => s.Stage == stage.Reference.Value) : null;
            summary["delta_v_mps"] = calc?.DvActual ?? 0;
            summary["delta_v_vac_mps"] = calc?.DvVac ?? 0;
            summary["burn_time_s"] = calc?.BurnTime ?? 0;
            summary["twr"] = calc?.TwrActual ?? 0;
            summary["total_delta_v_mps"] = d.TotalDvActual;
            summary["total_delta_v_vac_mps"] = d.TotalDvVac;
            return summary;
        }

        /// <summary>Calculator stages at or below the reference stage, first to fire first.</summary>
        private static List<object> StageTable(FlightData d)
        {
            int? reference = StageRef.Judge(d.CurrentStage, d.StagedParts).Reference;
            if (!reference.HasValue)
                return new List<object>();
            return d.Stages.Where(s => s.Stage <= reference.Value)
                .OrderByDescending(s => s.Stage)
                .Select(s => (object)new Dictionary<string, object>
                {
                    ["stage"] = s.Stage,
                    ["start_mass_t"] = s.StartMass,
                    ["end_mass_t"] = s.EndMass,
                    ["delta_v_mps"] = s.DvActual,
                    ["delta_v_asl_mps"] = s.DvAsl,
                    ["delta_v_vac_mps"] = s.DvVac,
                    ["twr"] = s.TwrActual,
                    ["twr_asl"] = s.TwrAsl,
                    ["twr_vac"] = s.TwrVac,
                    ["burn_time_s"] = s.BurnTime,
                })
                .ToList();
        }

        private static Dictionary<string, object> BodyInfo(BodyData b)
        {
            if (b == null)
                return null;
            return new Dictionary<string, object>
            {
                ["name"] = b.Name,
                ["radius_m"] = b.Radius,
                ["gm_m3ps2"] = b.Gm,
                ["rotation_period_s"] = b.RotationPeriod,
                ["atmosphere_top_m"] = b.AtmosphereTop,
                ["soi_m"] = double.IsInfinity(b.SphereOfInfluence) || double.IsNaN(b.SphereOfInfluence) ? null : (object)b.SphereOfInfluence,
            };
        }
    }
}
