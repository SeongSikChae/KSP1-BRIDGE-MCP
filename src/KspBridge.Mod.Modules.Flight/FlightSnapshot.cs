using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>Reads the active vessel's state from the game into the pure <see cref="FlightData"/> (KSP side).</summary>
    public static class FlightSnapshot
    {
        /// <summary>The editor_get_stages rule: the part has a staging icon and staging is on. A separator that has fired
        /// removes its own icon and turns staging off, so it is not counted (D020).</summary>
        internal static bool IsStaged(Part part)
            => (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;

        /// <summary>Everything get_flight answers, with the staged parts, the resources and the game's delta-v values.</summary>
        internal static FlightData Take(Vessel v)
        {
            FlightData d = TakeBasic(v);
            AddPartLists(v, d);
            AddDeltaV(v, d);
            return d;
        }

        /// <summary>
        /// The values that cost no list per part, so a module may read them every physics frame. Staged parts, resources and
        /// delta-v are left empty; FlightReport gives the same answer for the rest.
        /// </summary>
        public static FlightData TakeBasic(Vessel v)
        {
            CelestialBody body = v.mainBody;
            Orbit orbit = v.orbit;
            Transform reference = v.ReferenceTransform;

            return new FlightData
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
                Warp = Warp(v),
                Throttle = v.ctrlState != null ? v.ctrlState.mainThrottle : 0,
                Sas = v.ActionGroups[KSPActionGroup.SAS],
                Rcs = v.ActionGroups[KSPActionGroup.RCS],
                Mass = v.GetTotalMass(),
                Thrust = Thrust(v),
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
        }

        /// <summary>The game's time warp now and whether <paramref name="v"/> is on rails.</summary>
        public static WarpState Warp(Vessel v) => new WarpState
        {
            Rate = TimeWarp.CurrentRate,
            Physics = TimeWarp.WarpMode == TimeWarp.Modes.LOW,
            OnRails = v.packed,
        };

        private static double Thrust(Vessel v)
        {
            double thrust = 0;
            foreach (Part p in v.parts)
            {
                if (p == null)
                    continue;
                PartModuleList modules = p.Modules;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i] is ModuleEngines engine)
                        thrust += engine.finalThrust;
                }
            }
            return thrust;
        }

        private static void AddPartLists(Vessel v, FlightData d)
        {
            Dictionary<string, ResourceTotal> resources = new Dictionary<string, ResourceTotal>();
            foreach (Part p in v.parts)
            {
                if (p == null)
                    continue;
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
            d.Resources.AddRange(resources.Values);
        }

        private static void AddDeltaV(Vessel v, FlightData d)
        {
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
        }

        private static Vec V(Vector3d v) => new Vec(v.x, v.y, v.z);

        private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);
    }
}
