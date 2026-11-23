using KspBridge.Mod.Modules.Ascent;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>Reads the active vessel's maneuver nodes into values (KSP side).</summary>
    public static class NodeReader
    {
        /// <summary>
        /// The first node's start for execute_node and warp to node (spec C5, C6, D014): <see cref="NodeStart.Judge"/> on the values
        /// read now. The vessel must not be null.
        /// </summary>
        public static NodeStartResult FirstNodeStart(Vessel vessel) => NodeStart.Judge(StartReading(vessel));

        internal static NodeStartReading StartReading(Vessel vessel)
        {
            NodeStartReading r = new NodeStartReading
            {
                Now = Planetarium.GetUniversalTime(),
                Unavailable = NodeAvailability.Usable(Facilities(vessel)),
                OnSurface = OnSurface(vessel),
            };
            if (r.Unavailable != null)
                return r;
            ManeuverNode first = Nodes(vessel).FirstOrDefault();
            if (first == null)
                return r;
            r.HasNode = true;
            r.NodeUt = first.UT;
            r.HasAfter = first.nextPatch != null;
            if (r.HasAfter)
            {
                AscentReading a = AscentReader.Read(vessel);
                r.BurnSeconds = BurnEstimate.Seconds(first.GetBurnVector(vessel.orbit).magnitude, a.Stages, Accel(a));
            }
            return r;
        }

        /// <summary>The nodes in UT order (the solver keeps them sorted after each update; sorted again here to be sure).</summary>
        internal static List<ManeuverNode> Nodes(Vessel vessel)
        {
            PatchedConicSolver solver = vessel.patchedConicSolver;
            if (solver == null || solver.maneuverNodes == null)
                return new List<ManeuverNode>();
            return solver.maneuverNodes.Where(n => n != null).OrderBy(n => n.UT).ToList();
        }

        /// <summary>Every node as values (spec C3).</summary>
        internal static List<NodeInfo> Read(Vessel vessel)
        {
            List<ManeuverNode> nodes = Nodes(vessel);
            List<NodeInfo> infos = new List<NodeInfo>();
            for (int i = 0; i < nodes.Count; i++)
                infos.Add(Info(vessel, nodes[i], i));
            return infos;
        }

        internal static NodeInfo Info(Vessel vessel, ManeuverNode node, int index)
        {
            NodeInfo info = new NodeInfo
            {
                Index = index,
                Ut = node.UT,
                RadialMps = node.DeltaV.x,
                NormalMps = node.DeltaV.y,
                ProgradeMps = node.DeltaV.z,
                After = After(node),
            };
            if (index == 0 && info.After != null)
                info.RemainingDvMps = node.GetBurnVector(vessel.orbit).magnitude;
            return info;
        }

        internal static AfterOrbit After(ManeuverNode node)
        {
            Orbit o = node.nextPatch;
            if (o == null)
                return null;
            bool open = !(o.eccentricity < 1);
            double obt = o.getObtAtUT(node.UT);
            return new AfterOrbit
            {
                Body = o.referenceBody != null ? o.referenceBody.bodyName : null,
                ApoapsisM = open ? (double?)null : o.ApA,
                PeriapsisM = o.PeA,
                Eccentricity = o.eccentricity,
                InclinationDeg = o.inclination,
                NextApoapsisUt = ConicTimes.NextApoapsis(node.UT, o.eccentricity, o.period, obt),
                NextPeriapsisUt = ConicTimes.NextPeriapsis(node.UT, o.eccentricity, o.period, obt),
            };
        }

        /// <summary>The burn estimate inputs of the vessel: the delta-v calculator stages and the running engines' acceleration.</summary>
        internal static void EstimateInputs(Vessel vessel, out IReadOnlyList<StageBurn> stages, out double accel)
        {
            AscentReading a = AscentReader.Read(vessel);
            stages = a.Stages;
            accel = Accel(a);
        }

        private static double Accel(AscentReading a) => a.Mass > 0 ? a.MaxThrustVac / a.Mass : 0;

        internal static bool OnSurface(Vessel vessel) => vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH;

        /// <summary>The facility and solver part of the readiness input (D008).</summary>
        internal static NodeReadinessInput Facilities(Vessel vessel)
        {
            NodeReadinessInput input = new NodeReadinessInput
            {
                HasFacilityLevels = ScenarioUpgradeableFacilities.Instance != null,
                HasSolver = vessel.patchedConicSolver != null,
            };
            if (input.HasFacilityLevels && GameVariables.Instance != null)
            {
                input.TrackingAllowsConics = GameVariables.Instance.GetOrbitDisplayMode(
                    ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation)) == GameVariables.OrbitDisplayMode.PatchedConics;
                input.FlightPlanningUnlocked = GameVariables.Instance.UnlockedFlightPlanning(
                    ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl));
            }
            return input;
        }

        /// <summary>The game's state vectors at a UT, swizzled as the game passes them to the node frame (xzy).</summary>
        internal static void State(Orbit orbit, double ut, out Vec position, out Vec velocity)
        {
            orbit.GetOrbitalStateVectorsAtUT(ut, out Orbit.State s);
            position = V(s.pos.xzy);
            velocity = V(s.vel.xzy);
        }

        internal static Vec V(Vector3d v) => new Vec(v.x, v.y, v.z);
    }
}
