using KspBridge.Flight.Control;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Maneuver
{
    /// <summary>
    /// plan_maneuver: computes KSP maneuver nodes for a target orbit (design 0032 decisions 2, 9~16). By default the existing
    /// nodes are replaced; append: true plans from the orbit after the last node and adds behind it. Main thread only.
    /// </summary>
    public static class ManeuverPlanner
    {
        private static readonly string[] Apsides = { "apoapsis", "periapsis", "ut" };
        private static readonly string[] InclinationAt = { "ascending_node", "descending_node", "auto" };
        private static readonly string[] HohmannAt = { "apoapsis", "periapsis", "ut", "auto" };

        /// <summary>auto start of a hohmann: this long after the planning start (s).</summary>
        private const double AutoLeadS = 60;

        /// <summary>An orbit-position node (apsis, AN/DN) closer than this to the planning start moves one period later: the
        /// burn start (node − burn/2) would already be past and the vessel needs time to turn (unit 4 measurement: an AN 0.3 s
        /// ahead with a 3.5 s burn).</summary>
        private const double MinLeadS = 30;

        public static object Plan(Vessel v, RequestBody b)
        {
            ManualControl.RequireManual(v);
            string kind = b.GetString("kind");
            bool append = b.Has("append") && b.GetBool("append");
            PatchedConicSolver s = ManeuverNodes.Solver(v);
            double start = Planetarium.GetUniversalTime();
            Orbit o = v.orbit;
            if (append && s.maneuverNodes.Count > 0)
            {
                ManeuverNode last = s.maneuverNodes[s.maneuverNodes.Count - 1];
                o = last.nextPatch ?? throw new ApiException(409, "no_solution", "the last node has no orbit after it yet");
                start = last.UT;
            }
            List<OrbitMath.NodeSpec> nodes;
            switch (kind)
            {
                case "change_apoapsis":
                    nodes = One(OrbitMath.ChangeApsis(o, Lead(o, b, ManeuverNodes.ResolveUt(v, o, b, "at", Apsides, "periapsis", start), start), TargetR(o, b, "altitude"), raiseApo: true));
                    break;
                case "change_periapsis":
                    nodes = One(OrbitMath.ChangeApsis(o, Lead(o, b, ManeuverNodes.ResolveUt(v, o, b, "at", Apsides, "apoapsis", start), start), TargetR(o, b, "altitude"), raiseApo: false));
                    break;
                case "circularize":
                    nodes = One(OrbitMath.Circularize(o, Lead(o, b, ManeuverNodes.ResolveUt(v, o, b, "at", Apsides, "apoapsis", start), start)));
                    break;
                case "change_inclination":
                    nodes = One(Inclination(v, o, b, start));
                    break;
                case "hohmann":
                {
                    double ut = ManeuverNodes.ResolveUt(v, o, b, "at", HohmannAt, "auto", start);
                    nodes = OrbitMath.Hohmann(o, TargetR(o, b, "altitude"), double.IsNaN(ut) ? start + AutoLeadS : Lead(o, b, ut, start));
                    break;
                }
                default:
                    throw ApiException.BadRequest("unknown kind " + kind + " (change_apoapsis, change_periapsis, circularize, change_inclination, hohmann)");
            }
            foreach (OrbitMath.NodeSpec n in nodes)
            {
                if (!OrbitMath.Finite(n.Ut) || !OrbitMath.Finite(n.DvWorld.x) || !OrbitMath.Finite(n.DvWorld.y) || !OrbitMath.Finite(n.DvWorld.z))
                {
                    throw new ApiException(409, "no_solution", "the computed node is not finite");
                }
                if (n.Ut < Planetarium.GetUniversalTime())
                {
                    throw new ApiException(409, "no_solution", "the computed node time is in the past");
                }
            }
            if (!append)
            {
                ManeuverNodes.Clear(s);
            }
            Orbit from = o;
            foreach (OrbitMath.NodeSpec n in nodes)
            {
                ManeuverNodes.AddWorld(s, from, n.Ut, n.DvWorld);
                from = OrbitMath.After(from, n);   // the next node's frame is on the orbit after this one
            }
            s.UpdateFlightPlan();
            return Result(v, s, nodes);
        }

        private static List<OrbitMath.NodeSpec> One(OrbitMath.NodeSpec n) => new List<OrbitMath.NodeSpec> { n };

        /// <summary>An orbit-position node within MinLeadS of start moves one period later (ellipse only). An explicit ut is
        /// the caller's choice and stays.</summary>
        private static double Lead(Orbit o, RequestBody b, double ut, double start)
        {
            bool explicitUt = b.Has("ut") && (!b.Has("at") || b.GetString("at") == "ut");
            if (explicitUt || !OrbitMath.Finite(ut) || o.eccentricity >= 1 || ut >= start + MinLeadS)
            {
                return ut;
            }
            return ut + o.period;
        }

        /// <summary>change_inclination: at a node of the reference plane (target orbit plane or equator; auto = the node at the
        /// larger radius, where the turn costs less), or now + AutoLeadS when the planes are within CoplanarDeg (any point works).</summary>
        private static OrbitMath.NodeSpec Inclination(Vessel v, Orbit o, RequestBody b, double start)
        {
            double inc = b.GetDouble("inclination");
            if (inc < 0 || inc > 180)
            {
                throw ApiException.BadRequest("inclination must be in [0, 180] deg");
            }
            Vector3d refN = OrbitMath.ReferencePlaneNormal(v, o, start);
            double rel = OrbitMath.RelativeInclinationDeg(OrbitMath.NormalWorld(o, start), refN);
            double ut;
            if (rel < OrbitMath.CoplanarDeg)
            {
                ut = start + AutoLeadS;
            }
            else
            {
                ut = ManeuverNodes.ResolveUt(v, o, b, "at", InclinationAt, "auto", start);
                if (double.IsNaN(ut))   // auto
                {
                    double an = Lead(o, b, OrbitMath.NodeUt(o, refN, true, start), start);
                    double dn = Lead(o, b, OrbitMath.NodeUt(o, refN, false, start), start);
                    ut = !OrbitMath.Finite(dn) ? an
                        : !OrbitMath.Finite(an) ? dn
                        : o.getRelativePositionAtUT(an).magnitude >= o.getRelativePositionAtUT(dn).magnitude ? an : dn;
                }
                else
                {
                    ut = Lead(o, b, ut, start);
                }
            }
            return OrbitMath.ChangeInclination(o, ut, refN, inc);
        }

        /// <summary>Altitude argument → radius; 400 at or below the surface (or the atmosphere top) and beyond the SOI.</summary>
        private static double TargetR(Orbit o, RequestBody b, string name)
        {
            CelestialBody body = o.referenceBody;
            double alt = b.GetDouble(name);
            double floor = body.atmosphere ? body.atmosphereDepth : 0;
            if (alt <= floor || body.Radius + alt >= body.sphereOfInfluence)
            {
                throw ApiException.BadRequest(name + " must be above " + floor.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " m (" + (body.atmosphere ? "atmosphere" : "surface") + ") and inside the SOI of " + body.bodyName);
            }
            return body.Radius + alt;
        }

        private static object Result(Vessel v, PatchedConicSolver s, List<OrbitMath.NodeSpec> nodes)
        {
            double total = 0;
            foreach (OrbitMath.NodeSpec n in nodes)
            {
                total += n.DvWorld.magnitude;
            }
            double bt = EngineModel.BurnTimeStaged(v, total, out bool shortDv);
            ManeuverNode last = s.maneuverNodes.Count > 0 ? s.maneuverNodes[s.maneuverNodes.Count - 1] : null;
            return new Dictionary<string, object>
            {
                ["maneuver_nodes"] = ManeuverNodes.List(v),
                ["created"] = nodes.Count,
                ["total_dv"] = OrbitMath.Num(total),
                ["burn_time"] = OrbitMath.Num(bt),
                ["dv_short"] = shortDv,
                ["predicted"] = last?.nextPatch == null ? null : OrbitMath.OrbitSummary(last.nextPatch),
            };
        }
    }
}
