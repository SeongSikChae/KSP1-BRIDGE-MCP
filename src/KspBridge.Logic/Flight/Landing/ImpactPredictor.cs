using System;
using System.Diagnostics;

namespace KspBridge.Flight.Landing
{
    /// <summary>Predicted impact point: latitude/longitude (deg, longitude -180..180), UT and terrain altitude there.</summary>
    public sealed class Impact
    {
        public double Lat;
        public double Lon;
        public double Ut;
        public double Alt;
    }

    /// <summary>
    /// Unpowered impact prediction (#22 design decision 4): RK4 of gravity and drag from a position relative to the body
    /// centre and an inertial velocity, both in world axes at "now". The start UT may be in the future (deorbit node): the body
    /// rotation from now to each sample is applied when converting to latitude/longitude. Main thread only.
    /// </summary>
    public static class ImpactPredictor
    {
        private const double MaxS = 3600;
        private const int MaxSteps = 20000;

        /// <summary>Height above the terrain (m) below which the fine step is used.</summary>
        private const double SurfaceBand = 2000;

        /// <summary>null: no impact within MaxS / MaxSteps, or the budget ran out (the caller checks the stopwatch).</summary>
        public static Impact Predict(CelestialBody body, Vector3d r0, Vector3d v0, double ut0, double mass, DragModel drag,
                                     Stopwatch budget = null, double budgetMs = double.PositiveInfinity)
        {
            double now = Planetarium.GetUniversalTime();
            double maxTerrain = MaxTerrain(body);
            double spin = SpinSign(body);
            Vector3d r = r0;
            Vector3d vel = v0;
            Vector3d prevR = r0;
            double t = 0;
            double prevT = 0;
            double prevAbove = double.NaN;
            for (int i = 0; i < MaxSteps && t < MaxS; i++)
            {
                if (budget != null && budget.Elapsed.TotalMilliseconds > budgetMs)
                {
                    return null;
                }
                double alt = r.magnitude - body.Radius;
                double rot = ut0 - now + t;   // body rotation seconds from now
                double ground = alt < maxTerrain ? Ground(body, r, rot, spin) : double.NegativeInfinity;
                double above = alt - (body.ocean ? Math.Max(ground, 0) : ground);
                if (above <= 0)
                {
                    if (i == 0)
                    {
                        return At(body, r, rot, spin, ut0 + t);   // the start is already at or below the ground
                    }
                    double f = prevAbove / (prevAbove - above);
                    Vector3d hit = prevR + (r - prevR) * f;
                    double tHit = prevT + (t - prevT) * f;
                    return At(body, hit, ut0 - now + tHit, spin, ut0 + tHit);
                }
                double dt = alt > Math.Max(body.atmosphere ? body.atmosphereDepth : 0, maxTerrain) ? 10
                    : alt > maxTerrain || above > SurfaceBand ? 1
                    : 0.2;
                prevR = r;
                prevT = t;
                prevAbove = above;
                Rk4(body, spin, mass, drag, ref r, ref vel, dt);
                t += dt;
            }
            return null;
        }

        /// <summary>|(inertial − rotation velocity) − surface velocity| (m/s): small when the rotation axis and sign used for
        /// latitude/longitude and the air velocity match KSP.</summary>
        public static double FrameCheck(Vessel v)
        {
            CelestialBody body = v.mainBody;
            Vector3d r = v.CoMD - body.position;
            return ((v.obt_velocity - SpinSign(body) * Vector3d.Cross(body.angularVelocity, r)) - v.srf_velocity).magnitude;
        }

        /// <summary>Latitude/longitude of a body-relative position after rotSeconds of body rotation from now.</summary>
        public static void LatLon(CelestialBody body, Vector3d r, double rotSeconds, out double lat, out double lon)
        {
            LatLon(body, r, rotSeconds, SpinSign(body), out lat, out lon);
        }

        /// <summary>Great-circle distance (m) on the body's sea-level sphere.</summary>
        public static double Distance(CelestialBody body, double lat1, double lon1, double lat2, double lon2)
        {
            double d2r = Math.PI / 180;
            double p1 = lat1 * d2r, p2 = lat2 * d2r;
            double dp = p2 - p1, dl = (lon2 - lon1) * d2r;
            double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return 2 * body.Radius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        }

        public static double MaxTerrain(CelestialBody body) =>
            body.pqsController != null ? Math.Max(0, body.pqsController.mapMaxHeight) : 0;

        /// <summary>+1 or −1 so that spin·(ω × p) is KSP's surface rotation velocity (getRFrmVel); the same sign turns the
        /// latitude/longitude conversion back by the rotation.</summary>
        private static double SpinSign(CelestialBody body)
        {
            Vector3d omega = body.angularVelocity;
            if (omega.sqrMagnitude < 1e-20)
            {
                return 1;
            }
            Vector3d p = Vector3d.Cross(omega, new Vector3d(1, 0, 0));
            if (p.sqrMagnitude < 1e-20)
            {
                p = Vector3d.Cross(omega, new Vector3d(0, 0, 1));
            }
            p = p.normalized * body.Radius;
            return Vector3d.Dot(Vector3d.Cross(omega, p), body.getRFrmVel(body.position + p)) >= 0 ? 1 : -1;
        }

        private static void LatLon(CelestialBody body, Vector3d r, double rotSeconds, double spin, out double lat, out double lon)
        {
            Vector3d omega = body.angularVelocity;
            double w = omega.magnitude;
            Vector3d p = r;
            if (w > 0 && rotSeconds != 0)
            {
                // Rodrigues: rotate back by the body rotation over rotSeconds (forward rotation velocity = spin·ω × p)
                Vector3d k = omega / w;
                double th = -spin * w * rotSeconds;
                double c = Math.Cos(th), s = Math.Sin(th);
                p = r * c + Vector3d.Cross(k, r) * s + k * Vector3d.Dot(k, r) * (1 - c);
            }
            Vector3d world = body.position + p;
            lat = body.GetLatitude(world, false);
            lon = LandingTarget.Wrap180(body.GetLongitude(world, false));
        }

        private static double Ground(CelestialBody body, Vector3d r, double rot, double spin)
        {
            LatLon(body, r, rot, spin, out double lat, out double lon);
            return body.TerrainAltitude(lat, lon, true);
        }

        private static Impact At(CelestialBody body, Vector3d r, double rot, double spin, double ut)
        {
            LatLon(body, r, rot, spin, out double lat, out double lon);
            double ground = body.TerrainAltitude(lat, lon, true);
            return new Impact { Lat = lat, Lon = lon, Ut = ut, Alt = body.ocean ? Math.Max(ground, 0) : ground };
        }

        private static void Rk4(CelestialBody body, double spin, double mass, DragModel drag, ref Vector3d r, ref Vector3d v, double dt)
        {
            Vector3d a1 = Accel(body, spin, mass, drag, r, v);
            Vector3d r2 = r + v * (dt / 2), v2 = v + a1 * (dt / 2);
            Vector3d a2 = Accel(body, spin, mass, drag, r2, v2);
            Vector3d r3 = r + v2 * (dt / 2), v3 = v + a2 * (dt / 2);
            Vector3d a3 = Accel(body, spin, mass, drag, r3, v3);
            Vector3d r4 = r + v3 * dt, v4 = v + a3 * dt;
            Vector3d a4 = Accel(body, spin, mass, drag, r4, v4);
            r += (v + v2 * 2 + v3 * 2 + v4) * (dt / 6);
            v += (a1 + a2 * 2 + a3 * 2 + a4) * (dt / 6);
        }

        private static Vector3d Accel(CelestialBody body, double spin, double mass, DragModel drag, Vector3d r, Vector3d vel)
        {
            double rm = r.magnitude;
            Vector3d a = -body.gravParameter / (rm * rm * rm) * r;
            double alt = rm - body.Radius;
            if (body.atmosphere && alt < body.atmosphereDepth && drag != null && mass > 0)
            {
                double rho = body.GetDensity(body.GetPressure(alt), body.GetTemperature(alt));
                Vector3d vr = vel - spin * Vector3d.Cross(body.angularVelocity, r);   // relative to the rotating air
                a -= drag.K * rho * vr.magnitude / mass * vr;
            }
            return a;
        }
    }
}
