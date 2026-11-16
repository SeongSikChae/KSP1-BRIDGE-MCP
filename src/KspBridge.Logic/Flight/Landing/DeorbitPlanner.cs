using KspBridge.Flight.Maneuver;
using System;
using System.Diagnostics;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// plan_maneuver kind: deorbit (#22 design decision 7): a retrograde burn that lowers the periapsis to peR, at the burn
    /// time over one period whose predicted impact has zero downrange error to the target. 37 samples over one period find the
    /// − → + sign change, bisection narrows it to 1 m. The crossrange error is left to the land correction burn (limit 5 km).
    /// Main thread only.
    /// </summary>
    public static class DeorbitPlanner
    {
        private const int Samples = 36;

        /// <summary>The first sample burn time after the planning start (s): time to turn and half the burn.</summary>
        private const double LeadS = 60;

        private const double BudgetMs = 2000;

        private const double DoneM = 1;

        private const int MaxBisect = 40;

        private const double MaxCrossrangeM = 5000;

        /// <summary>Default periapsis as a fraction of the atmosphere depth (Kerbin about 10.5 km; U5b: 30 km flew about 90° of
        /// longitude through the air and was too sensitive to the drag model).</summary>
        private const double DefaultPeFraction = 0.15;

        public sealed class Solution
        {
            public OrbitMath.NodeSpec Node;
            public Impact Impact;
            public double DownrangeM;
            public double CrossrangeM;
            public double ComputeMs;
        }

        /// <summary>periapsis argument → altitude (m). With an atmosphere 0 ≤ alt &lt; depth (default 0.15·depth), without one
        /// 0 ≤ alt &lt; the highest terrain (default 0); outside: 400.</summary>
        public static double PeriapsisAlt(CelestialBody body, RequestBody b)
        {
            double top = body.atmosphere ? body.atmosphereDepth : ImpactPredictor.MaxTerrain(body);
            double alt = b.GetDouble("periapsis", body.atmosphere ? DefaultPeFraction * body.atmosphereDepth : 0);
            if (double.IsNaN(alt) || alt < 0 || alt >= top)
            {
                throw ApiException.BadRequest("periapsis must be in [0, " + top.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + ") m (" + (body.atmosphere ? "below the atmosphere top" : "below the highest terrain") + " of " + body.bodyName + ")");
            }
            return alt;
        }

        public static Solution Solve(Vessel v, Orbit o, double start, double peR, LandingTarget target)
        {
            CelestialBody body = o.referenceBody;
            if (o.eccentricity >= 1 || !OrbitMath.Finite(o.period))
            {
                throw new ApiException(409, "no_solution", "deorbit needs a closed orbit");
            }
            if (o.PeR <= peR)
            {
                throw new ApiException(409, "no_solution", "the periapsis is already at or below the requested one");
            }
            ResolveTarget(target, body, out double tLat, out double tLon);
            double tAlt = Math.Max(body.ocean ? 0 : double.NegativeInfinity, body.TerrainAltitude(tLat, tLon, true));
            DragModel drag = DragModel.Initial(v);
            double mass = v.totalMass * 1000;
            Stopwatch sw = Stopwatch.StartNew();

            double prevUt = double.NaN, prevE = double.NaN;
            double loUt = double.NaN, hiUt = double.NaN;
            for (int i = 0; i <= Samples; i++)   // one full period, both ends: a crossing at the wrap is found too
            {
                double ut = start + LeadS + o.period * i / Samples;
                Sample s = At(o, ut, peR, mass, drag, body, tLat, tLon, tAlt, sw);
                if (s != null && !double.IsNaN(prevE) && prevE < 0 && s.Downrange >= 0)
                {
                    loUt = prevUt;
                    hiUt = ut;
                    break;
                }
                prevUt = s == null ? double.NaN : ut;
                prevE = s == null ? double.NaN : s.Downrange;
            }
            if (double.IsNaN(loUt))
            {
                throw new ApiException(409, "no_solution", "no burn time over one period brings the impact onto the target's downrange");
            }

            Sample best = null;
            for (int k = 0; k < MaxBisect; k++)
            {
                double mid = (loUt + hiUt) / 2;
                Sample s = At(o, mid, peR, mass, drag, body, tLat, tLon, tAlt, sw)
                    ?? throw new ApiException(409, "no_solution", "no impact inside the search bracket");
                best = s;
                if (Math.Abs(s.Downrange) < DoneM || hiUt - loUt < 1e-3)
                {
                    break;
                }
                if (s.Downrange < 0)
                {
                    loUt = mid;
                }
                else
                {
                    hiUt = mid;
                }
            }
            if (Math.Abs(best.Crossrange) > MaxCrossrangeM)
            {
                throw new ApiException(409, "no_solution", "crossrange error " + best.Crossrange.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " m is over " + MaxCrossrangeM.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " m: the orbit's ground track does not pass near the target (change the inclination first)");
            }
            UnityEngine.Debug.Log("[KspBridge] deorbit: ut=" + best.Node.Ut.ToString("F1") + " dv=" + best.Node.DvWorld.magnitude.ToString("F2")
                + " down=" + best.Downrange.ToString("F1") + " cross=" + best.Crossrange.ToString("F1") + " ms=" + sw.Elapsed.TotalMilliseconds.ToString("F0"));
            return new Solution
            {
                Node = best.Node,
                Impact = best.Impact,
                DownrangeM = best.Downrange,
                CrossrangeM = best.Crossrange,
                ComputeMs = sw.Elapsed.TotalMilliseconds,
            };
        }

        private sealed class Sample
        {
            public OrbitMath.NodeSpec Node;
            public Impact Impact;
            public double Downrange;   // signed (m): + beyond the target along the ground track
            public double Crossrange;  // signed (m): + toward the orbit normal
        }

        /// <summary>Retrograde node at ut and its predicted impact; null without an impact. 409 past the budget or for a
        /// non-retrograde burn.</summary>
        private static Sample At(Orbit o, double ut, double peR, double mass, DragModel drag, CelestialBody body,
                                 double tLat, double tLon, double tAlt, Stopwatch sw)
        {
            OrbitMath.NodeSpec n = OrbitMath.ChangeApsis(o, ut, peR, raiseApo: false);
            OrbitMath.StateWorld(o, ut, out Vector3d r, out Vector3d vel);
            if (Vector3d.Dot(n.DvWorld, vel) >= 0)
            {
                throw new ApiException(409, "no_solution", "the burn is not retrograde");
            }
            Impact imp = ImpactPredictor.Predict(body, r, vel + n.DvWorld, ut, mass, drag, sw, BudgetMs);
            if (sw.Elapsed.TotalMilliseconds > BudgetMs)
            {
                throw new ApiException(409, "no_solution", "deorbit search exceeded " + BudgetMs.ToString("F0") + " ms");
            }
            if (imp == null)
            {
                return null;
            }
            // both points body-fixed, expressed in world axes now; the ground track direction at the target from the orbit normal
            Vector3d pt = (body.GetWorldSurfacePosition(tLat, tLon, tAlt) - body.position).normalized;
            Vector3d pi = (body.GetWorldSurfacePosition(imp.Lat, imp.Lon, imp.Alt) - body.position).normalized;
            Vector3d normal = Vector3d.Cross(r, vel).normalized;
            Vector3d along = Vector3d.Cross(normal, pt).normalized;   // (r × v) × r ∝ v
            return new Sample
            {
                Node = n,
                Impact = imp,
                Downrange = Math.Atan2(Vector3d.Dot(pi, along), Vector3d.Dot(pi, pt)) * body.Radius,
                // impact's angle off the orbit plane minus the target's (the target can be off the plane)
                Crossrange = (Math.Asin(Clamp1(Vector3d.Dot(pi, normal))) - Math.Asin(Clamp1(Vector3d.Dot(pt, normal)))) * body.Radius,
            };
        }

        private static double Clamp1(double x) => Math.Max(-1, Math.Min(1, x));

        /// <summary>Coordinates as given; a vessel target must exist, be on this body and be landed (409 no_target).</summary>
        private static void ResolveTarget(LandingTarget target, CelestialBody body, out double lat, out double lon)
        {
            if (!target.IsVessel)
            {
                lat = target.Latitude;
                lon = target.Longitude;
                return;
            }
            Vessel tv = Targeting.FindVessel(target.VesselId);
            if (tv == null)
            {
                throw new ApiException(409, "no_target", "no vessel " + target.VesselId.ToString("F0"));
            }
            if (tv.mainBody != body)
            {
                throw new ApiException(409, "no_target", tv.vesselName + " is on " + tv.mainBody.bodyName + ", not " + body.bodyName);
            }
            if (tv.situation != Vessel.Situations.LANDED && tv.situation != Vessel.Situations.SPLASHED)
            {
                throw new ApiException(409, "no_target", tv.vesselName + " is not landed (" + tv.situation + ")");
            }
            lat = tv.latitude;
            lon = LandingTarget.Wrap180(tv.longitude);
        }
    }
}
