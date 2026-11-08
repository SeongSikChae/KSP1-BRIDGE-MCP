using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using KspBridge.Attitude;

namespace KspBridge.Handlers
{
    public static class VesselHandler
    {
        public static object GetVessel()
        {
            Vessel v = Guard.ActiveVessel();
            Orbit o = v.orbit;
            bool escape = o.eccentricity >= 1.0;

            return new Dictionary<string, object>
            {
                ["name"] = v.vesselName,
                ["situation"] = v.situation.ToString(),
                ["ut"] = Planetarium.GetUniversalTime(),
                ["met"] = v.missionTime,
                ["body"] = v.mainBody.bodyName,
                ["altitude_asl"] = v.altitude,
                ["altitude_terrain"] = (double)v.heightFromTerrain,
                ["speed_surface"] = v.srfSpeed,
                ["speed_orbital"] = v.obt_speed,
                ["speed_vertical"] = v.verticalSpeed,
                ["mass"] = v.totalMass,
                ["orbit"] = new Dictionary<string, object>
                {
                    ["apoapsis"] = escape ? null : (object)o.ApA,
                    ["periapsis"] = o.PeA,
                    ["time_to_ap"] = escape ? null : (object)o.timeToAp,
                    ["time_to_pe"] = o.timeToPe,
                    ["inclination"] = o.inclination,
                    ["eccentricity"] = o.eccentricity,
                    ["period"] = escape ? null : (object)o.period,
                    ["sma"] = o.semiMajorAxis,
                },
                ["attitude"] = Attitude(v),
                ["autopilot"] = AttitudeController.Describe(v),
                ["prediction"] = KspBridge.Flight.Prediction.Get(v).ToDictionary(),
                ["control"] = new Dictionary<string, object>
                {
                    ["throttle"] = (double)FlightInputHandler.state.mainThrottle,
                    ["throttle_hold"] = Flight.ThrottleLock.Active(v),
                    ["sas"] = v.ActionGroups[KSPActionGroup.SAS],
                    ["sas_mode"] = SasModes.ToName(v.Autopilot.Mode),
                    ["rcs"] = v.ActionGroups[KSPActionGroup.RCS],
                    ["gear"] = v.ActionGroups[KSPActionGroup.Gear],
                    ["light"] = v.ActionGroups[KSPActionGroup.Light],
                    ["brakes"] = v.ActionGroups[KSPActionGroup.Brakes],
                },
                ["stage"] = new Dictionary<string, object>
                {
                    ["current"] = StageManager.CurrentStage,
                    ["count"] = StageManager.StageCount,
                },
                ["resources"] = Resources(v),
                ["warp"] = WarpHandler.Describe(v),
                ["g_force"] = v.geeForce,
                ["crew"] = Crew(v),
                ["sas"] = Flight.SasMonitor.Describe(v),
            };
        }

        /// <summary>Crew consciousness and G load (g_load = accumulated G normalized, 1 = blackout threshold).</summary>
        private static List<object> Crew(Vessel v)
        {
            double now = Planetarium.GetUniversalTime();
            List<object> list = new List<object>();
            foreach (ProtoCrewMember c in v.GetVesselCrew())
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = c.name,
                    ["trait"] = c.trait,
                    ["level"] = c.experienceLevel,
                    ["conscious"] = !c.inactive,
                    ["inactive_for"] = c.inactive ? (object)Math.Max(0, c.inactiveTimeEnd - now) : null,
                    ["g_load"] = c.GExperiencedNormalized,
                    ["out_due_to_g"] = c.outDueToG,
                });
            }
            return list;
        }

        /// <summary>Highest crew g_load, null without crew (condition field g_load_max).</summary>
        internal static object MaxGLoad(Vessel v)
        {
            List<ProtoCrewMember> crew = v.GetVesselCrew();
            return crew.Count == 0 ? null : (object)crew.Max(c => c.GExperiencedNormalized);
        }

        /// <summary>Surface-relative pitch/heading/roll (MechJeb convention).</summary>
        private static Dictionary<string, object> Attitude(Vessel v)
        {
            SurfaceFrame.GetAttitude(v, out double pitch, out double heading, out double roll);
            return new Dictionary<string, object>
            {
                ["pitch"] = pitch,
                ["heading"] = heading,
                ["roll"] = roll,
            };
        }

        private static List<object> Resources(Vessel v)
        {
            Dictionary<string, double[]> totals = new Dictionary<string, double[]>();
            List<string> order = new List<string>();
            foreach (Part part in v.parts)
            {
                for (int i = 0; i < part.Resources.Count; i++)
                {
                    PartResource r = part.Resources[i];
                    if (!totals.TryGetValue(r.resourceName, out double[] sum))
                    {
                        sum = new double[2];
                        totals[r.resourceName] = sum;
                        order.Add(r.resourceName);
                    }
                    sum[0] += r.amount;
                    sum[1] += r.maxAmount;
                }
            }

            List<object> result = new List<object>();
            foreach (string name in order)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["amount"] = totals[name][0],
                    ["max"] = totals[name][1],
                });
            }
            return result;
        }
    }
}
