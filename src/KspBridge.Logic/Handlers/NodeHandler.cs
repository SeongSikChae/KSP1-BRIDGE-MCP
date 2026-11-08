using System;
using System.Collections.Generic;

namespace KspBridge.Handlers
{
    public static class NodeHandler
    {
        public static object List()
        {
            PatchedConicSolver solver = Solver(Guard.ActiveVessel());
            double now = Planetarium.GetUniversalTime();
            List<object> nodes = new List<object>();
            for (int i = 0; i < solver.maneuverNodes.Count; i++)
            {
                nodes.Add(Describe(solver.maneuverNodes[i], i, now));
            }
            return new Dictionary<string, object> { ["nodes"] = nodes, ["transfers"] = Transfers(Guard.ActiveVessel()) };
        }

        public static object Add(RequestBody body)
        {
            PatchedConicSolver solver = Solver(Guard.ActiveVessel());
            double now = Planetarium.GetUniversalTime();

            bool hasUt = body.Has("ut");
            if (hasUt == body.Has("in_seconds"))
            {
                throw ApiException.BadRequest("give exactly one of 'ut' or 'in_seconds'");
            }
            double ut = hasUt ? body.GetDouble("ut") : now + body.GetDouble("in_seconds");
            if (ut <= now)
            {
                throw ApiException.BadRequest("node time must be in the future (now ut=" + now + ")");
            }

            // ManeuverNode.DeltaV is (radial, normal, prograde).
            Vector3d dv = new Vector3d(body.GetDouble("radial", 0), body.GetDouble("normal", 0), body.GetDouble("prograde", 0));
            ManeuverNode node = solver.AddManeuverNode(ut);
            node.OnGizmoUpdated(dv, ut);
            solver.UpdateFlightPlan();

            Dictionary<string, object> result = Describe(node, solver.maneuverNodes.IndexOf(node), now);
            result["transfers"] = Transfers(Guard.ActiveVessel());
            return result;
        }

        public static object Remove(string indexText)
        {
            PatchedConicSolver solver = Solver(Guard.ActiveVessel());
            int index = Guard.ParseIndex(indexText);
            if (index < 0 || index >= solver.maneuverNodes.Count)
            {
                throw ApiException.NotFound("no maneuver node at index " + index + " (count " + solver.maneuverNodes.Count + ")");
            }
            solver.maneuverNodes[index].RemoveSelf();
            return new Dictionary<string, object> { ["removed"] = index, ["remaining"] = solver.maneuverNodes.Count };
        }

        private static PatchedConicSolver Solver(Vessel v)
        {
            if (v.patchedConicSolver == null)
            {
                throw ApiException.BadRequest("maneuver nodes are not available (upgrade the Tracking Station / Mission Control)");
            }
            return v.patchedConicSolver;
        }

        private static Dictionary<string, object> Describe(ManeuverNode node, int index, double now)
        {
            Orbit after = node.nextPatch;
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["ut"] = node.UT,
                ["time_to"] = node.UT - now,
                ["prograde"] = node.DeltaV.z,
                ["normal"] = node.DeltaV.y,
                ["radial"] = node.DeltaV.x,
                ["delta_v"] = node.DeltaV.magnitude,
                ["orbit_after"] = after == null ? null : new Dictionary<string, object>
                {
                    ["apoapsis"] = after.eccentricity >= 1.0 ? null : (object)after.ApA,
                    ["periapsis"] = after.PeA,
                    ["body"] = after.referenceBody != null ? after.referenceBody.bodyName : null,
                },
                ["patches"] = Patches(after),
            };
        }

        /// <summary>
        /// Predicted path after the node, one entry per conic patch: body, start/end UT, how the patch ends
        /// (encounter = enters a moon's SOI, escape = leaves the body's SOI, maneuver = next node, final = none),
        /// periapsis/apoapsis above the body's surface. In an encounter patch the periapsis is the closest approach.
        /// </summary>
        private static List<object> Patches(Orbit first)
        {
            List<object> list = new List<object>();
            for (Orbit p = first; p != null && list.Count < 8; p = p.nextPatch)
            {
                if (!p.activePatch)
                {
                    break;
                }
                list.Add(new Dictionary<string, object>
                {
                    ["body"] = p.referenceBody != null ? p.referenceBody.bodyName : null,
                    ["start_ut"] = p.StartUT,
                    ["end_ut"] = p.EndUT,
                    ["end"] = p.patchEndTransition.ToString().ToLowerInvariant(),
                    ["periapsis"] = p.PeA,
                    ["apoapsis"] = p.eccentricity >= 1.0 ? null : (object)p.ApA,
                    ["eccentricity"] = p.eccentricity,
                    ["inclination"] = p.inclination,
                });
                if (p.patchEndTransition == Orbit.PatchTransitionType.FINAL || p.patchEndTransition == Orbit.PatchTransitionType.INITIAL)
                {
                    break;
                }
            }
            return list;
        }

        /// <summary>
        /// Moons of the body the vessel orbits: phase angle (deg the moon is ahead of the vessel in its direction of motion, 0..360),
        /// the Hohmann phase angle for a transfer from the vessel's current radius, seconds until that phase,
        /// and the prograde delta-v of that Hohmann burn. Estimates for a near-circular, near-equatorial orbit.
        /// </summary>
        private static List<object> Transfers(Vessel v)
        {
            List<object> list = new List<object>();
            Orbit o = v.orbit;
            CelestialBody main = v.mainBody;
            if (o == null || main == null || o.eccentricity >= 1.0)
            {
                return list;
            }
            double mu = main.gravParameter;
            Vector3d rv = o.pos;
            Vector3d h = Vector3d.Cross(o.pos, o.vel);
            double r1 = rv.magnitude;
            double wv = 360.0 / o.period;
            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == main || b.referenceBody != main || b.orbit == null)
                {
                    continue;
                }
                Vector3d rb = b.orbit.pos;
                double phase = Math.Atan2(Vector3d.Dot(h.normalized, Vector3d.Cross(rv, rb)), Vector3d.Dot(rv, rb)) * 180.0 / Math.PI;
                phase = Wrap(phase);
                double r2 = b.orbit.semiMajorAxis;
                double at = (r1 + r2) / 2;
                double transferTime = Math.PI * Math.Sqrt(at * at * at / mu);
                double wb = 360.0 / b.orbit.period;
                double hohmann = Wrap(180.0 - wb * transferTime);
                double rate = wv - wb;
                double? timeToWindow = rate > 1e-9 ? (double?)(Wrap(phase - hohmann) / rate) : null;
                list.Add(new Dictionary<string, object>
                {
                    ["body"] = b.bodyName,
                    ["phase"] = phase,
                    ["hohmann_phase"] = hohmann,
                    ["time_to_window"] = timeToWindow,
                    ["window_ut"] = timeToWindow.HasValue ? (object)(Planetarium.GetUniversalTime() + timeToWindow.Value) : null,
                    ["hohmann_dv"] = Math.Sqrt(mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1),
                    ["transfer_time"] = transferTime,
                });
            }
            return list;
        }

        private static double Wrap(double deg) => ((deg % 360.0) + 360.0) % 360.0;
    }
}
