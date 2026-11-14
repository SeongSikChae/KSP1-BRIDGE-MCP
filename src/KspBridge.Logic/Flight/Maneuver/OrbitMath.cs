using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Maneuver
{
    /// <summary>
    /// Orbit helpers in world space. Orbit's own vectors (getRelativePositionAtUT, getOrbitalVelocityAtUT) are in the swizzled
    /// orbit frame; .xzy turns them into world vectors (the same as CircularizeMode). Unity's world is left-handed, so the
    /// physical angular momentum direction is -Cross(r, v) of world vectors (north for a prograde equatorial orbit).
    /// Search helpers never throw: a failure is NaN and the caller turns it into its own error (tools: 409 no_solution).
    /// </summary>
    public static class OrbitMath
    {
        /// <summary>Relative angle of two orbit planes (deg) below which nodes are treated as undefined.</summary>
        public const double CoplanarDeg = 0.01;

        /// <summary>Altitudes and shape of an orbit, the same names as get_flight.orbit (apoapsis null for a hyperbola).</summary>
        public static Dictionary<string, object> OrbitSummary(Orbit o)
        {
            return new Dictionary<string, object>
            {
                ["apoapsis"] = Num(o.eccentricity < 1 ? o.ApA : double.NaN),
                ["periapsis"] = Num(o.PeA),
                ["eccentricity"] = Num(o.eccentricity),
                ["inclination"] = Num(o.inclination),
                ["period"] = Num(o.eccentricity < 1 ? o.period : double.NaN),
                ["body"] = o.referenceBody != null ? o.referenceBody.bodyName : null,
            };
        }

        /// <summary>World position (relative to the body centre) and orbital velocity at ut.</summary>
        public static void StateWorld(Orbit o, double ut, out Vector3d r, out Vector3d v)
        {
            r = o.getRelativePositionAtUT(ut).xzy;
            v = o.getOrbitalVelocityAtUT(ut).xzy;
        }

        /// <summary>Unit normal of the orbit plane in world space along the physical angular momentum.</summary>
        public static Vector3d NormalWorld(Orbit o, double ut)
        {
            StateWorld(o, ut, out Vector3d r, out Vector3d v);
            return -Vector3d.Cross(r, v).normalized;
        }

        /// <summary>Reference plane for ascending/descending nodes: the target's orbit plane when the target orbits the same
        /// body (design 0032 decision 9), else the body's equator (rotation axis, north).</summary>
        public static Vector3d ReferencePlaneNormal(Vessel v, Orbit o, double ut)
        {
            Orbit t = v.targetObject?.GetOrbit();
            if (t != null && t.referenceBody == o.referenceBody)
            {
                return NormalWorld(t, ut);
            }
            return o.referenceBody.RotationAxis.normalized;
        }

        /// <summary>Angle between two plane normals (deg).</summary>
        public static double RelativeInclinationDeg(Vector3d n1, Vector3d n2) => Vector3d.Angle(n1, n2);

        /// <summary>
        /// UT of the next ascending (moving toward refNormal) or descending node of o relative to the plane with refNormal,
        /// after now. NaN when the planes are within CoplanarDeg, or the node is not reached (hyperbola past it).
        /// </summary>
        public static double NodeUt(Orbit o, Vector3d refNormal, bool ascending, double now)
        {
            Vector3d h = NormalWorld(o, now);
            if (RelativeInclinationDeg(h, refNormal) < CoplanarDeg || RelativeInclinationDeg(h, -refNormal) < CoplanarDeg)
            {
                return double.NaN;
            }
            Vector3d line = Vector3d.Cross(refNormal, h).normalized;   // one of the two node directions
            double ut1 = UtOfDirection(o, line, now);
            double ut2 = UtOfDirection(o, -line, now);
            double pick = double.NaN;
            foreach (double ut in new[] { ut1, ut2 })
            {
                if (double.IsNaN(ut))
                {
                    continue;
                }
                StateWorld(o, ut, out _, out Vector3d vel);
                bool up = Vector3d.Dot(vel, refNormal) > 0;
                if (up == ascending)
                {
                    pick = ut;
                }
            }
            return pick;
        }

        /// <summary>Next apoapsis (apo) or periapsis UT after now from the eccentricity vector, so it also works for a future
        /// patch (Orbit.timeToAp/timeToPe are only kept up to date on the vessel's current orbit). Near-circular: now + the
        /// orbit's own time fields.</summary>
        public static double ApsisUt(Orbit o, bool apo, double now)
        {
            double mu = o.referenceBody.gravParameter;
            StateWorld(o, now, out Vector3d r, out Vector3d v);
            Vector3d eVec = ((v.sqrMagnitude - mu / r.magnitude) * r - Vector3d.Dot(r, v) * v) / mu;
            if (eVec.magnitude < 1e-6)
            {
                return now + (apo ? o.timeToAp : o.timeToPe);
            }
            return UtOfDirection(o, apo ? -eVec.normalized : eVec.normalized, now);
        }

        /// <summary>UT after now at which the position on o points along dirWorld (a direction in the orbit plane).
        /// NaN when it is never reached (hyperbola) or the orbit is degenerate.</summary>
        public static double UtOfDirection(Orbit o, Vector3d dirWorld, double now)
        {
            double mu = o.referenceBody.gravParameter;
            StateWorld(o, now, out Vector3d r, out Vector3d v);
            Vector3d h = Vector3d.Cross(r, v);   // handedness does not matter: both angles below use the same h
            if (h.magnitude <= 0)
            {
                return double.NaN;
            }
            double e = o.eccentricity;
            Vector3d eVec = ((v.sqrMagnitude - mu / r.magnitude) * r - Vector3d.Dot(r, v) * v) / mu;
            Vector3d refDir = e > 1e-6 ? eVec.normalized : r.normalized;   // circular: measure from the current position
            double nuNow = SignedAngle(refDir, r, h);
            double nuTarget = SignedAngle(refDir, dirWorld, h);
            if (e < 1)
            {
                double a = o.semiMajorAxis;
                double n = Math.Sqrt(mu / (a * a * a));
                double dt = (MeanAnomaly(nuTarget, e) - MeanAnomaly(nuNow, e)) / n;
                double period = 2 * Math.PI / n;
                dt %= period;
                if (dt < 0)
                {
                    dt += period;
                }
                return now + dt;
            }
            double nuMax = Math.Acos(-1 / e);   // asymptote
            if (Math.Abs(nuTarget) >= nuMax)
            {
                return double.NaN;
            }
            double ah = -o.semiMajorAxis;
            double nh = Math.Sqrt(mu / (ah * ah * ah));
            double dth = (HyperbolicMeanAnomaly(nuTarget, e) - HyperbolicMeanAnomaly(nuNow, e)) / nh;
            return dth > 0 ? now + dth : double.NaN;
        }

        /// <summary>Angle from a to b about axis h, in (-π, π].</summary>
        private static double SignedAngle(Vector3d a, Vector3d b, Vector3d h)
        {
            Vector3d an = a.normalized, bn = Vector3d.Exclude(h, b).normalized;
            double ang = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(an, bn))));
            return Vector3d.Dot(Vector3d.Cross(an, bn), h) >= 0 ? ang : -ang;
        }

        private static double MeanAnomaly(double nu, double e)
        {
            double E = 2 * Math.Atan(Math.Sqrt((1 - e) / (1 + e)) * Math.Tan(nu / 2));
            return E - e * Math.Sin(E);
        }

        private static double HyperbolicMeanAnomaly(double nu, double e)
        {
            double x = Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(nu / 2);
            double H = 0.5 * Math.Log((1 + x) / (1 - x));   // atanh
            return e * Math.Sinh(H) - H;
        }

        /// <summary>World basis of the maneuver node frame at ut: prograde, normal (physical angular momentum), radial out.</summary>
        public static void NodeBasis(Orbit o, double ut, out Vector3d prograde, out Vector3d normal, out Vector3d radial)
        {
            StateWorld(o, ut, out Vector3d r, out Vector3d v);
            prograde = v.normalized;
            normal = -Vector3d.Cross(r, v).normalized;
            radial = Vector3d.Exclude(prograde, r).normalized;
        }

        // ---- analytic burns (plan_maneuver) -------------------------------------------------------------------------------

        /// <summary>A node to create: UT and Δv as a world vector.</summary>
        public struct NodeSpec
        {
            public double Ut;
            public Vector3d DvWorld;

            public NodeSpec(double ut, Vector3d dvWorld)
            {
                Ut = ut;
                DvWorld = dvWorld;
            }
        }

        /// <summary>Makes the node point an apsis and puts the other apsis at newR: the velocity becomes horizontal with the
        /// speed of the ellipse through rNow and newR. raiseApo: newR is the apoapsis (must be ≥ the node radius), else the
        /// periapsis (≤ the node radius); the opposite meaning is a 400.</summary>
        public static NodeSpec ChangeApsis(Orbit o, double ut, double newR, bool raiseApo)
        {
            StateWorld(o, ut, out Vector3d r, out Vector3d vel);
            double rNow = r.magnitude;
            if (raiseApo ? newR < rNow : newR > rNow)
            {
                double alt = rNow - o.referenceBody.Radius;
                throw ApiException.BadRequest((raiseApo ? "apoapsis below" : "periapsis above") + " the node point altitude "
                    + alt.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " m");
            }
            double a = (rNow + newR) / 2;
            double vNew = Math.Sqrt(o.referenceBody.gravParameter * (2 / rNow - 1 / a));
            Vector3d horiz = Vector3d.Exclude(r, vel).normalized;
            return new NodeSpec(ut, horiz * vNew - vel);
        }

        /// <summary>Circular orbit through the node point: horizontal velocity at the circular speed.</summary>
        public static NodeSpec Circularize(Orbit o, double ut)
        {
            StateWorld(o, ut, out Vector3d r, out Vector3d vel);
            double vCirc = Math.Sqrt(o.referenceBody.gravParameter / r.magnitude);
            Vector3d horiz = Vector3d.Exclude(r, vel).normalized;
            return new NodeSpec(ut, horiz * vCirc - vel);
        }

        /// <summary>The orbit after applying n to o (state vectors at n.Ut).</summary>
        public static Orbit After(Orbit o, NodeSpec n)
        {
            Vector3d pos = o.getRelativePositionAtUT(n.Ut);
            Vector3d vel = o.getOrbitalVelocityAtUT(n.Ut) + n.DvWorld.xzy;   // world → orbit frame (swizzle is its own inverse)
            Orbit after = new Orbit();
            after.UpdateFromStateVectors(pos, vel, o.referenceBody, n.Ut);
            return after;
        }

        /// <summary>Hohmann transfer to a circular orbit of radius r2: the transfer burn at ut1 and the circularization at the
        /// other apsis half a transfer period later.</summary>
        public static List<NodeSpec> Hohmann(Orbit o, double r2, double ut1)
        {
            StateWorld(o, ut1, out Vector3d r, out _);
            NodeSpec burn1 = ChangeApsis(o, ut1, r2, raiseApo: r2 > r.magnitude);
            Orbit transfer = After(o, burn1);
            double ut2 = ut1 + Math.PI * Math.Sqrt(Math.Pow(transfer.semiMajorAxis, 3) / o.referenceBody.gravParameter);
            return new List<NodeSpec> { burn1, Circularize(transfer, ut2) };
        }

        /// <summary>
        /// Plane change at ut (a node of o on the plane with refNormal, or any point when they are coplanar): keeps the radial
        /// and horizontal speeds and turns the horizontal direction about the radius so the new orbit normal is incDeg from
        /// refNormal, on the side nearer the current normal.
        /// </summary>
        public static NodeSpec ChangeInclination(Orbit o, double ut, Vector3d refNormal, double incDeg)
        {
            StateWorld(o, ut, out Vector3d r, out Vector3d vel);
            Vector3d rHat = r.normalized;
            Vector3d n = NormalWorld(o, ut);
            Vector3d refN = Vector3d.Exclude(rHat, refNormal).normalized;   // the node point lies in the plane: refN ⟂ r̂
            Vector3d side = Vector3d.Cross(rHat, refN).normalized;
            double i = incDeg * Math.PI / 180;
            Vector3d n1 = Math.Cos(i) * refN + Math.Sin(i) * side;
            Vector3d n2 = Math.Cos(i) * refN - Math.Sin(i) * side;
            Vector3d nNew = Vector3d.Dot(n1, n) >= Vector3d.Dot(n2, n) ? n1 : n2;
            double vr = Vector3d.Dot(vel, rHat);
            Vector3d vh = vel - vr * rHat;
            Vector3d hDir = Vector3d.Cross(nNew, rHat).normalized;   // ⟂ r̂ and ⟂ the new normal
            if (Vector3d.Dot(hDir, vh) < 0)
            {
                hDir = -hDir;
            }
            return new NodeSpec(ut, vr * rHat + vh.magnitude * hDir - vel);
        }

        internal static object Num(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, 3);

        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
