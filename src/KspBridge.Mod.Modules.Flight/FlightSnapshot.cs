using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Reads the active vessel's state from the game into the pure <see cref="FlightData"/> (KSP side).</summary>
    internal static class FlightSnapshot
    {
        /// <summary>The editor_get_stages rule: the part has a staging icon and staging is on. A separator that has fired
        /// removes its own icon and turns staging off, so it is not counted (D020).</summary>
        public static bool IsStaged(Part part)
            => (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;

        public static FlightData Take(Vessel v)
        {
            CelestialBody body = v.mainBody;
            Orbit orbit = v.orbit;
            Transform reference = v.ReferenceTransform;

            FlightData d = new FlightData
            {
                Vessel = v.GetDisplayName(),
                Situation = v.situation.ToString(),
                Body = body.bodyName,
                UT = Planetarium.GetUniversalTime(),
                MissionTime = v.missionTime,
                Latitude = v.latitude,
                Longitude = v.longitude,
                Altitude = v.altitude,
                TerrainHeight = body.TerrainAltitude(v.latitude, v.longitude, allowNegative: true),
                Ocean = body.ocean,
                SurfaceSpeed = v.srfSpeed,
                OrbitalSpeed = v.obt_speed,
                VerticalSpeed = v.verticalSpeed,
                HorizontalSurfaceSpeed = v.horizontalSrfSpeed,
                ApA = orbit.ApA,
                PeA = orbit.PeA,
                Inclination = orbit.inclination,
                Eccentricity = orbit.eccentricity,
                SemiMajorAxis = orbit.semiMajorAxis,
                Period = orbit.period,
                TimeToAp = orbit.timeToAp,
                TimeToPe = orbit.timeToPe,
                Up = V(v.up),
                North = V(v.north),
                East = V(v.east),
                Nose = reference != null ? V(reference.up) : Vec.Zero,
                Top = reference != null ? -V(reference.forward) : Vec.Zero,
                Throttle = v.ctrlState != null ? v.ctrlState.mainThrottle : 0,
                Sas = v.ActionGroups[KSPActionGroup.SAS],
                Rcs = v.ActionGroups[KSPActionGroup.RCS],
                Mass = v.GetTotalMass(),
                Gravity = FlightGlobals.getGeeForceAtPosition(v.CoMD).magnitude,
                CurrentStage = v.currentStage,
                BodyInfo = new BodyData
                {
                    Name = body.bodyName,
                    Radius = body.Radius,
                    Gm = body.gravParameter,
                    RotationPeriod = body.rotationPeriod,
                    AtmosphereTop = body.atmosphere ? body.atmosphereDepth : (double?)null,
                    SphereOfInfluence = body.sphereOfInfluence,
                },
            };

            Dictionary<string, ResourceTotal> resources = new Dictionary<string, ResourceTotal>();
            double thrust = 0;
            foreach (Part p in v.parts)
            {
                if (p == null)
                    continue;
                foreach (ModuleEngines engine in p.FindModulesImplementing<ModuleEngines>())
                    thrust += engine.finalThrust;
                if (IsStaged(p))
                    d.StagedParts.Add(new StagedPart(p.inverseStage, p.State == PartStates.ACTIVE || p.State == PartStates.DEACTIVATED));
                foreach (PartResource r in p.Resources)
                {
                    if (r == null || r.info == null)
                        continue;
                    if (!resources.TryGetValue(r.resourceName, out ResourceTotal total))
                        resources[r.resourceName] = total = new ResourceTotal { Name = r.resourceName };
                    total.Amount += r.amount;
                    total.Max += r.maxAmount;
                }
            }
            d.Thrust = thrust;
            d.Resources.AddRange(resources.Values);

            VesselDeltaV vd = v.VesselDeltaV;
            if (vd == null)
            {
                d.DeltaVNotReadyReason = "the game's delta-v calculator is not attached to the vessel yet";
            }
            else if (!vd.IsReady)
            {
                d.DeltaVNotReadyReason = "the game's delta-v calculator has not finished its first calculation yet";
            }
            else
            {
                d.DeltaVReady = true;
                d.TotalDvActual = vd.TotalDeltaVActual;
                d.TotalDvVac = vd.TotalDeltaVVac;
                foreach (DeltaVStageInfo st in vd.OperatingStageInfo)
                {
                    if (st == null)
                        continue;
                    d.Stages.Add(new StageCalc
                    {
                        Stage = st.stage,
                        StartMass = st.startMass,
                        EndMass = st.endMass,
                        DvActual = st.deltaVActual,
                        DvAsl = st.deltaVatASL,
                        DvVac = st.deltaVinVac,
                        TwrActual = st.TWRActual,
                        TwrAsl = st.TWRASL,
                        TwrVac = st.TWRVac,
                        BurnTime = st.stageBurnTime,
                    });
                }
            }
            return d;
        }

        private static Vec V(Vector3d v) => new Vec(v.x, v.y, v.z);

        private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);
    }
}
