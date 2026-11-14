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

        private static readonly string[] CorrectAt = { "ut", "auto" };

        /// <summary>rendezvous_phase limits (design 0032 decision 14, initial values) and the closest distance default (decision 15).</summary>
        private const double MaxEcc = 0.05;
        private const double MaxRelIncDeg = 1.0;
        private const double MinAltDiffM = 1000;
        private const double DefaultDistanceM = 200;

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
            Dictionary<string, object> extra = null;
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
                case "transfer_body":
                {
                    CelestialBody body = TargetBody(v, o);
                    double pe = b.GetDouble("periapsis");
                    if (pe <= (body.atmosphere ? body.atmosphereDepth : 0) || body.Radius + pe >= body.sphereOfInfluence)
                    {
                        throw ApiException.BadRequest("periapsis must be above the " + (body.atmosphere ? "atmosphere" : "surface") + " of " + body.bodyName + " and inside its SOI");
                    }
                    if (o.eccentricity > 0.1)
                    {
                        throw new ApiException(409, "no_solution", "circularize first (eccentricity " + o.eccentricity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ")");
                    }
                    OrbitMath.TransferResult tr = OrbitMath.TransferBody(o, body, pe, start + MinLeadS);
                    if (!OrbitMath.Finite(tr.Node.Ut))
                    {
                        throw new ApiException(409, "no_solution", "transfer search exceeded its time budget");
                    }
                    if (double.IsNaN(tr.PeriapsisAlt))
                    {
                        throw new ApiException(409, "no_encounter", "no SOI entry of " + body.bodyName + " found");
                    }
                    nodes = One(tr.Node);
                    extra = new Dictionary<string, object>
                    {
                        ["body"] = body.bodyName,
                        ["computed_periapsis"] = OrbitMath.Num(tr.PeriapsisAlt),
                        ["computed_soi_entry_ut"] = OrbitMath.Num(tr.EntryUt),
                    };
                    break;
                }
                case "rendezvous_phase":
                {
                    Orbit tgt = TargetVesselOrbit(v, o);
                    double distance = Distance(b);
                    double rel = OrbitMath.RelativeInclinationDeg(OrbitMath.NormalWorld(o, start), OrbitMath.NormalWorld(tgt, start));
                    if (o.eccentricity > MaxEcc || tgt.eccentricity > MaxEcc || rel > MaxRelIncDeg)
                    {
                        throw new ApiException(409, "orbits_not_coplanar_circular", "both orbits must be near-circular (e ≤ " + MaxEcc + ") and within "
                            + MaxRelIncDeg + "° of each other (now e " + F(o.eccentricity, 3) + " / " + F(tgt.eccentricity, 3) + ", " + F(rel, 2) + "°): circularize / change_inclination first");
                    }
                    if (Math.Abs(o.semiMajorAxis - tgt.semiMajorAxis) < MinAltDiffM)
                    {
                        throw new ApiException(409, "no_transfer_window", "same altitude as the target (within " + MinAltDiffM + " m): raise or lower first (hohmann)");
                    }
                    OrbitMath.NodeSpec n = OrbitMath.RendezvousPhase(o, tgt, distance, start + MinLeadS);
                    if (!OrbitMath.Finite(n.Ut))
                    {
                        throw new ApiException(409, "no_solution", "rendezvous search exceeded its time budget");
                    }
                    nodes = One(n);
                    extra = Closest(o, n, tgt);
                    break;
                }
                case "correct_approach":
                {
                    Orbit tgt = TargetVesselOrbit(v, o);
                    double distance = Distance(b);
                    double ut = ManeuverNodes.ResolveUt(v, o, b, "at", CorrectAt, "auto", start);
                    if (double.IsNaN(ut))
                    {
                        ut = start + AutoLeadS;
                    }
                    OrbitMath.NodeSpec n = OrbitMath.CorrectApproach(o, tgt, ut, distance);
                    if (!OrbitMath.Finite(n.Ut))
                    {
                        throw new ApiException(409, "no_solution", "no correction improves the closest approach");
                    }
                    nodes = One(n);
                    extra = Closest(o, n, tgt);
                    break;
                }
                case "capture":
                {
                    // At the periapsis of the current orbit (a hyperbola after an SOI entry included): the apoapsis goes to
                    // altitude (default: the periapsis altitude, a circular orbit).
                    double ut = OrbitMath.ApsisUt(o, false, start);
                    if (!OrbitMath.Finite(ut))
                    {
                        throw new ApiException(409, "no_solution", "no periapsis ahead on this orbit");
                    }
                    double peR = o.getRelativePositionAtUT(ut).magnitude;
                    double apoR = b.Has("altitude") ? TargetR(o, b, "altitude") : peR;
                    nodes = One(OrbitMath.ChangeApsis(o, ut, apoR, raiseApo: true));   // apoapsis below the periapsis: 400
                    break;
                }
                default:
                    throw ApiException.BadRequest("unknown kind " + kind + " (change_apoapsis, change_periapsis, circularize, change_inclination, hohmann, transfer_body, capture, rendezvous_phase, correct_approach)");
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
            return Result(v, s, nodes, extra);
        }

        /// <summary>The target vessel's orbit (a docking port target: its vessel) about the same body (409 no_target otherwise).</summary>
        private static Orbit TargetVesselOrbit(Vessel v, Orbit o)
        {
            Vessel tv = v.targetObject is ModuleDockingNode port ? port.vessel : v.targetObject as Vessel;
            if (tv == null || tv == v || tv.orbit == null)
            {
                throw new ApiException(409, "no_target", "this kind needs a vessel or docking port target (set_target vessel_id)");
            }
            if (tv.orbit.referenceBody != o.referenceBody)
            {
                throw new ApiException(409, "no_target", tv.vesselName + " orbits " + tv.orbit.referenceBody.bodyName + ", not " + o.referenceBody.bodyName);
            }
            return tv.orbit;
        }

        private static double Distance(RequestBody b)
        {
            double d = b.GetDouble("distance", DefaultDistanceM);
            if (d < 0 || d > 100000)
            {
                throw ApiException.BadRequest("distance must be in [0, 100000] m");
            }
            return d;
        }

        /// <summary>Our own closest-approach prediction after node n (the response's predicted gets KSP's orbit too).</summary>
        private static Dictionary<string, object> Closest(Orbit o, OrbitMath.NodeSpec n, Orbit tgt)
        {
            double d = OrbitMath.ClosestAfter(o, n, tgt, out double cut);
            Orbit after = OrbitMath.After(o, n);
            return new Dictionary<string, object>
            {
                ["closest_distance"] = OrbitMath.Num(d),
                ["closest_ut"] = OrbitMath.Num(cut),
                ["relative_speed_at_closest"] = OrbitMath.Finite(cut) ? OrbitMath.Num(OrbitMath.RelativeSpeed(after, tgt, cut)) : null,
            };
        }

        private static string F(double x, int digits) => x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The current target as a child body of the orbit's reference body (409 no_target otherwise).</summary>
        private static CelestialBody TargetBody(Vessel v, Orbit o)
        {
            if (!(v.targetObject is CelestialBody body))
            {
                throw new ApiException(409, "no_target", "transfer_body needs a body target (set_target body)");
            }
            if (body.referenceBody != o.referenceBody || body == o.referenceBody)
            {
                throw new ApiException(409, "no_target", body.bodyName + " does not orbit " + o.referenceBody.bodyName);
            }
            return body;
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

        private static object Result(Vessel v, PatchedConicSolver s, List<OrbitMath.NodeSpec> nodes, Dictionary<string, object> extra)
        {
            double total = 0;
            foreach (OrbitMath.NodeSpec n in nodes)
            {
                total += n.DvWorld.magnitude;
            }
            double bt = EngineModel.BurnTimeStaged(v, total, out bool shortDv);
            ManeuverNode last = s.maneuverNodes.Count > 0 ? s.maneuverNodes[s.maneuverNodes.Count - 1] : null;
            Dictionary<string, object> predicted = last?.nextPatch == null ? null : OrbitMath.OrbitSummary(last.nextPatch);
            if (predicted != null && extra != null && extra.TryGetValue("body", out object bodyName))
            {
                // KSP's own prediction of the encounter: the patch after the node ends in an SOI entry of that body.
                Orbit p = last.nextPatch;
                bool enc = p.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER && p.nextPatch != null
                    && p.nextPatch.referenceBody != null && p.nextPatch.referenceBody.bodyName == (string)bodyName;
                predicted["encounter"] = enc;
                predicted["encounter_periapsis"] = enc ? OrbitMath.Num(p.nextPatch.PeA) : null;
            }
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                ["maneuver_nodes"] = ManeuverNodes.List(v),
                ["created"] = nodes.Count,
                ["total_dv"] = OrbitMath.Num(total),
                ["burn_time"] = OrbitMath.Num(bt),
                ["dv_short"] = shortDv,
                ["predicted"] = predicted,
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> kv in extra)
                {
                    result[kv.Key] = kv.Value;
                }
            }
            return result;
        }
    }
}
