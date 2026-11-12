using KspBridge.Check;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Flight-plan condition variables of a vessel, refilled in place (no per-tick instance). NaN = value not known.
    /// Every key in NumVars / StrVars is filled on every capture. Main thread only.
    /// </summary>
    public sealed class Telemetry
    {
        public static readonly string[] NumVars =
        {
            "altitude", "altitude_radar", "vertical_speed", "surface_speed", "orbital_speed",
            "apoapsis", "periapsis", "time_to_apoapsis", "time_to_periapsis", "eccentricity", "inclination",
            "pitch", "heading", "roll", "attitude_error", "dynamic_pressure",
            "stage", "stage_dv", "total_dv", "twr", "throttle", "thrust", "ignitable_engines",
            "step_time", "met",
        };

        public static readonly string[] StrVars = { "staging", "situation" };

        public readonly Dictionary<string, double> Num = new Dictionary<string, double>();
        public readonly Dictionary<string, string> Str = new Dictionary<string, string>();

        private Vessel.Situations _situation;
        private string _situationName;

        public Telemetry()
        {
            foreach (string k in NumVars)
            {
                Num[k] = double.NaN;
            }
            foreach (string k in StrVars)
            {
                Str[k] = "";
            }
        }

        public void Capture(Vessel v, double ut)
        {
            Orbit o = v.orbit;
            CelestialBody body = v.mainBody;

            Num["altitude"] = v.altitude;
            Num["altitude_radar"] = v.radarAltitude;
            Num["vertical_speed"] = v.verticalSpeed;
            Num["surface_speed"] = v.srfSpeed;
            Num["orbital_speed"] = v.obt_speed;

            Num["apoapsis"] = o.ApA;
            Num["periapsis"] = o.PeA;
            Num["time_to_apoapsis"] = o.timeToAp;
            Num["time_to_periapsis"] = o.timeToPe;
            Num["eccentricity"] = o.eccentricity;
            Num["inclination"] = o.inclination;

            SurfaceAttitude(v, out double pitch, out double heading, out double roll);
            Num["pitch"] = pitch;
            Num["heading"] = heading;
            Num["roll"] = roll;
            Num["attitude_error"] = double.NaN;   // unit 4: attitude target and AttitudeController.ErrorDeg
            Num["dynamic_pressure"] = v.dynamicPressurekPa;

            Num["stage"] = v.currentStage;
            VesselDeltaV dv = v.VesselDeltaV;
            bool dvReady = PerformanceCheck.UpToDate(dv);
            DeltaVStageInfo stageInfo = dvReady ? dv.GetStage(v.currentStage) : null;
            Num["stage_dv"] = stageInfo != null ? stageInfo.deltaVActual : double.NaN;
            Num["total_dv"] = dvReady ? dv.TotalDeltaVActual : double.NaN;

            double r = body.Radius + v.altitude;
            double g = body.gravParameter / (r * r);
            double maxThrust = EngineModel.MaxThrustSum(v, (float)(v.staticPressurekPa * PhysicsGlobals.KpaToAtmospheres),
                v.externalTemperature, v.atmDensity);
            Num["twr"] = v.totalMass > 0 && g > 0 ? maxThrust / (v.totalMass * g) : double.NaN;
            Num["throttle"] = v.ctrlState.mainThrottle;   // unit 3: the commanded throttle
            Num["thrust"] = EngineModel.ThrustSum(v);
            Num["ignitable_engines"] = EngineModel.Ignitable(v);

            Num["step_time"] = double.NaN;   // unit 3: set by the Coordinator
            Num["met"] = v.missionTime;

            if (v.situation != _situation || _situationName == null)
            {
                _situation = v.situation;
                _situationName = v.situation.ToString();   // enum ToString allocates: only when the situation changes
            }
            Str["situation"] = _situationName;
            Str["staging"] = "none";         // unit 8: AutoStager.StateName
        }

        /// <summary>Pitch (deg above the horizon), heading (deg from north) and roll (deg) of the control reference in the surface frame.</summary>
        private static void SurfaceAttitude(Vessel v, out double pitch, out double heading, out double roll)
        {
            Vector3d com = v.CoMD;
            Vector3d up = (com - v.mainBody.position).normalized;
            Vector3d north = Vector3d.Exclude(up, v.mainBody.position + (Vector3d)v.mainBody.transform.up * v.mainBody.Radius - com).normalized;
            Quaternion surface = Quaternion.LookRotation(north, up);
            Quaternion vesselSurface = Quaternion.Inverse(Quaternion.Euler(90, 0, 0) * Quaternion.Inverse(v.ReferenceTransform.rotation) * surface);
            Vector3 e = vesselSurface.eulerAngles;
            heading = e.y;
            pitch = e.x > 180 ? 360.0 - e.x : -e.x;
            roll = e.z > 180 ? e.z - 360.0 : e.z;
        }
    }
}
