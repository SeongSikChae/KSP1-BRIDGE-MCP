using KspBridge.Flight.Control;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Maneuver
{
    /// <summary>
    /// The active vessel's KSP maneuver nodes (patchedConicSolver): listing for get_flight.maneuver_nodes and the
    /// add/update/remove tools. node.DeltaV is (radial, normal, prograde) in KSP's node frame. Main thread only.
    /// </summary>
    public static class ManeuverNodes
    {
        /// <summary>at values add_maneuver_node accepts (auto is resolved by plan_maneuver kinds only).</summary>
        public static readonly string[] AllNonAuto = { "apoapsis", "periapsis", "ascending_node", "descending_node", "ut" };

        /// <summary>Sign of the node frame's normal axis against OrbitMath.NodeBasis normal (physical angular momentum).
        /// Fixed by the unit 2 measurement (inclination change of a normal burn).</summary>
        internal const double NormalSign = 1;

        public static PatchedConicSolver Solver(Vessel v)
        {
            PatchedConicSolver s = v.patchedConicSolver;
            if (s == null)
            {
                throw new ApiException(409, "nodes_unavailable", "maneuver nodes are not available (tracking station level or no patched conics)");
            }
            return s;
        }

        /// <summary>The nodes for get_flight (empty list without a solver).</summary>
        public static List<object> List(Vessel v)
        {
            List<object> list = new List<object>();
            PatchedConicSolver s = v.patchedConicSolver;
            if (s == null)
            {
                return list;
            }
            for (int i = 0; i < s.maneuverNodes.Count; i++)
            {
                list.Add(Describe(v, s.maneuverNodes[i], i));
            }
            return list;
        }

        public static Dictionary<string, object> Describe(Vessel v, ManeuverNode n, int index)
        {
            double ut = Planetarium.GetUniversalTime();
            double dv = n.DeltaV.magnitude;
            double bt = EngineModel.BurnTimeStaged(v, dv, out bool shortDv);
            Orbit next = n.nextPatch;
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["ut"] = OrbitMath.Num(n.UT),
                ["time_to"] = OrbitMath.Num(n.UT - ut),
                ["prograde"] = OrbitMath.Num(n.DeltaV.z),
                ["normal"] = OrbitMath.Num(n.DeltaV.y),
                ["radial"] = OrbitMath.Num(n.DeltaV.x),
                ["dv"] = OrbitMath.Num(dv),
                ["burn_time"] = OrbitMath.Num(bt),   // +∞ (no stage can fire) is null
                ["dv_short"] = shortDv,
                ["orbit_after"] = next == null ? null : OrbitMath.OrbitSummary(next),
            };
        }

        private static object Result(Vessel v) => new Dictionary<string, object> { ["maneuver_nodes"] = List(v) };

        public static object Add(Vessel v, RequestBody b)
        {
            ManualControl.RequireManual(v);
            PatchedConicSolver s = Solver(v);
            double now = Planetarium.GetUniversalTime();
            double ut = ResolveUt(v, v.orbit, b, "at", AllNonAuto, null, now);
            if (ut < now)
            {
                throw ApiException.BadRequest("ut is in the past");
            }
            Vector3d dv = new Vector3d(b.GetDouble("radial", 0), b.GetDouble("normal", 0), b.GetDouble("prograde", 0));
            ManeuverNode n = s.AddManeuverNode(ut);
            n.OnGizmoUpdated(dv, ut);
            s.UpdateFlightPlan();
            return Result(v);
        }

        public static object Update(Vessel v, RequestBody b)
        {
            ManualControl.RequireManual(v);
            PatchedConicSolver s = Solver(v);
            ManeuverNode n = At(s, b.GetDouble("index"));
            double ut = n.UT;
            if (b.Has("ut"))
            {
                ut = b.GetDouble("ut");
                if (ut < Planetarium.GetUniversalTime())
                {
                    throw ApiException.BadRequest("ut is in the past");
                }
            }
            Vector3d dv = n.DeltaV;
            dv = new Vector3d(b.GetDouble("radial", dv.x), b.GetDouble("normal", dv.y), b.GetDouble("prograde", dv.z));
            n.OnGizmoUpdated(dv, ut);
            s.UpdateFlightPlan();
            return Result(v);
        }

        public static object Remove(Vessel v, RequestBody b)
        {
            ManualControl.RequireManual(v);
            PatchedConicSolver s = Solver(v);
            bool all = b.Has("all") && b.GetBool("all");
            if (all == b.Has("index"))
            {
                throw ApiException.BadRequest("give index or all: true");
            }
            if (all)
            {
                Clear(s);
            }
            else
            {
                At(s, b.GetDouble("index")).RemoveSelf();
            }
            s.UpdateFlightPlan();
            return Result(v);
        }

        public static void Clear(PatchedConicSolver s)
        {
            for (int i = s.maneuverNodes.Count - 1; i >= 0; i--)
            {
                s.maneuverNodes[i].RemoveSelf();
            }
        }

        private static ManeuverNode At(PatchedConicSolver s, double index)
        {
            if (index != Math.Floor(index) || index < 0 || index >= s.maneuverNodes.Count)
            {
                throw ApiException.BadRequest("index must be an integer in [0, " + (s.maneuverNodes.Count - 1) + "]");
            }
            return s.maneuverNodes[(int)index];
        }

        /// <summary>Adds a node whose Δv is given as a world vector: projected on the node frame at ut.</summary>
        public static ManeuverNode AddWorld(PatchedConicSolver s, Orbit o, double ut, Vector3d dvWorld)
        {
            OrbitMath.NodeBasis(o, ut, out Vector3d pro, out Vector3d nor, out Vector3d rad);
            Vector3d dv = new Vector3d(Vector3d.Dot(dvWorld, rad), NormalSign * Vector3d.Dot(dvWorld, nor), Vector3d.Dot(dvWorld, pro));
            ManeuverNode n = s.AddManeuverNode(ut);
            n.OnGizmoUpdated(dv, ut);
            return n;
        }

        /// <summary>
        /// at: apoapsis | periapsis | ascending_node | descending_node | ut (with "ut") | auto. Exactly one of at/ut (or the
        /// default). auto is resolved by the caller (returns NaN here). 400 for a value the kind does not allow, 409 no_solution
        /// when the time is undefined.
        /// </summary>
        public static double ResolveUt(Vessel v, Orbit o, RequestBody b, string name, string[] allowed, string defaultAt, double now)
        {
            bool hasAt = b.Has(name), hasUt = b.Has("ut");
            if (hasAt && hasUt && b.GetString(name) != "ut")
            {
                throw ApiException.BadRequest("give " + name + " or ut, not both");
            }
            string at = hasAt ? b.GetString(name) : hasUt ? "ut" : defaultAt ?? throw ApiException.BadRequest(name + " or ut is required");
            if (Array.IndexOf(allowed, at) < 0)
            {
                throw ApiException.BadRequest(name + " " + at + " is not allowed here (" + string.Join(", ", allowed) + ")");
            }
            double ut;
            switch (at)
            {
                case "ut":
                    ut = b.GetDouble("ut");
                    break;
                case "apoapsis":
                    if (o.eccentricity >= 1)
                    {
                        throw ApiException.BadRequest("no apoapsis on a hyperbolic orbit (use ut)");
                    }
                    ut = OrbitMath.ApsisUt(o, true, now);
                    break;
                case "periapsis":
                    ut = OrbitMath.ApsisUt(o, false, now);   // NaN on a hyperbola past its periapsis
                    break;
                case "ascending_node":
                case "descending_node":
                    ut = OrbitMath.NodeUt(o, OrbitMath.ReferencePlaneNormal(v, o, now), at == "ascending_node", now);
                    break;
                default:
                    return double.NaN;   // auto: the kind decides
            }
            if (!OrbitMath.Finite(ut))
            {
                throw new ApiException(409, "no_solution", "node time is undefined (" + at + ")");
            }
            return ut;
        }
    }
}
