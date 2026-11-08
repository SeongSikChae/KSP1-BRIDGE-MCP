using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>Condition fields shared by wait_until and triggers (main thread reads).</summary>
    public static class FlightFields
    {
        public static readonly HashSet<string> NumericFields = new HashSet<string>
        {
            "altitude_asl", "altitude_terrain", "apoapsis", "periapsis", "time_to_ap", "speed_surface", "speed_vertical", "met", "ut", "g_force", "g_load_max",
            "thrust_kn", "max_accel", "twr", "drag_accel", "stop_time", "stop_distance", "stop_dv", "time_to_impact", "burn_margin",
        };

        /// <summary>Builds the comparison; body must hold "value" (number, string for situation, bool for stage_flameout).</summary>
        public static Func<object, bool> BuildTest(string field, string op, RequestBody body)
        {
            if (NumericFields.Contains(field))
            {
                double target = body.GetDouble("value");
                switch (op)
                {
                    case ">=": return v => v is double d && d >= target;
                    case "<=": return v => v is double d && d <= target;
                    case "==": return v => v is double d && Math.Abs(d - target) < 1e-6;
                    default: throw ApiException.BadRequest("op must be >=, <= or ==");
                }
            }
            if (op != "==")
            {
                throw ApiException.BadRequest("field '" + field + "' supports only ==");
            }
            if (field == "situation")
            {
                string target = body.GetString("value").ToUpperInvariant();
                return v => v is string s && s == target;
            }
            if (field == "stage_flameout" || field == "any_flameout")
            {
                bool target = body.GetBool("value");
                return v => v is bool b && b == target;
            }
            throw ApiException.BadRequest("unknown field '" + field + "'. Valid: " + string.Join(", ", NumericFields) + ", situation, stage_flameout, any_flameout");
        }

        public static object Read(Vessel v, string field)
        {
            switch (field)
            {
                case "altitude_asl": return v.altitude;
                case "altitude_terrain": return (double)v.heightFromTerrain;
                case "apoapsis": return v.orbit.ApA;
                case "periapsis": return v.orbit.PeA;
                case "time_to_ap": return v.orbit.timeToAp;
                case "speed_surface": return v.srfSpeed;
                case "speed_vertical": return v.verticalSpeed;
                case "met": return v.missionTime;
                case "ut": return Planetarium.GetUniversalTime();
                case "g_force": return v.geeForce;
                case "g_load_max": return Handlers.VesselHandler.MaxGLoad(v);
                case "situation": return v.situation.ToString();
                case "stage_flameout": return StageFlameout(v);
                case "any_flameout": return AnyFlameout(v);
                case "thrust_kn": return Prediction.Get(v).ThrustKn;
                case "max_accel": return Prediction.Get(v).MaxAccel;
                case "twr": return Prediction.Get(v).Twr;
                case "drag_accel": return Prediction.Get(v).DragAccel;
                case "stop_time": return Prediction.Get(v).StopTime;
                case "stop_distance": return Prediction.Get(v).StopDistance;
                case "stop_dv": return Prediction.Get(v).StopDv;
                case "time_to_impact": return Prediction.Get(v).TimeToImpact;
                case "burn_margin": return Prediction.Get(v).BurnMargin;
                default: return null;
            }
        }

        /// <summary>True when at least one engine is ignited and every ignited engine has flamed out.</summary>
        public static bool StageFlameout(Vessel v)
        {
            bool any = false;
            foreach (Part p in v.parts)
            {
                foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                {
                    if (!e.getIgnitionState)
                    {
                        continue;
                    }
                    any = true;
                    if (!e.flameout)
                    {
                        return false;
                    }
                }
            }
            return any;
        }

        /// <summary>True when at least one ignited engine has flamed out (e.g. radial SRBs empty while the core still burns).</summary>
        public static bool AnyFlameout(Vessel v)
        {
            foreach (Part p in v.parts)
            {
                foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                {
                    if (e.getIgnitionState && e.flameout)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
