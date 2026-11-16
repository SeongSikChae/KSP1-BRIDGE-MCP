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

        /// <summary>Predicted brake start UT (#22 decision 10), NaN when not asked or not reached before the impact.</summary>
        public double BrakeUt = double.NaN;
    }

    /// <summary>
    /// #22 decision 12-2: the physics step KSP integrates the vessel with along a prediction. Unity's semi-implicit Euler is a
    /// leapfrog with the velocity half a step behind, so the predictor follows the exact path with V = u + (h/2)·a_U and the
    /// inertial acceleration taken at the late velocity V − (h/2)·a_U. default (On false) = plain RK4, the result before 12-2.
    /// </summary>
    public struct PhysStep
    {
        /// <summary>KSP's ×1 physics step (TimeWarp.fixedDeltaTime default); never read at run time.</summary>
        public const double BaseDt = 0.02;

        public bool On;

        /// <summary>Physics warp end T = brake start − 30 s (decision 11); +Infinity when not known (×2 in the whole atmosphere).</summary>
        public double WarpEndUt;

        /// <summary>DIAG (decision 12, U6 removes): the target land's ×1 comparison band 50–60 km (LandMode.DiagX1Low/High).</summary>
        public bool DiagBand;

        /// <summary>DIAG (decision 12-2 1st stage forward check, U6 removes): FixedH over the whole window instead of the policy,
        /// the step the vessel actually had on the check's start tick (0 on rails); windows whose warp changes are not judged.</summary>
        public bool Fixed;
        public double FixedH;

        /// <summary>The step at a predicted point: 0 above the atmosphere (rails, analytic orbit), else ×2 or ×1.</summary>
        public double H(CelestialBody body, double alt, double ut)
        {
            if (On && Fixed)
            {
                return FixedH;
            }
            if (!On || !(body.atmosphere && alt < body.atmosphereDepth))
            {
                return 0;
            }
            bool x1 = ut >= WarpEndUt || (DiagBand && alt > 50000 && alt < 60000);
            return (x1 ? 1 : 2) * BaseDt;
        }
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

        /// <summary>null: no impact within MaxS / MaxSteps, or the budget ran out (the caller checks the stopwatch).
        /// brakeDecel (m/s², net of gravity) > 0 also finds Impact.BrakeUt (#22 decision 10): the first predicted time the
        /// stopping distance of the air speed reaches the height over the local terrain minus finalAlt, interpolated between
        /// steps; NaN when not reached before the impact.</summary>
        public static Impact Predict(CelestialBody body, Vector3d r0, Vector3d v0, double ut0, double mass, DragModel drag,
                                     Stopwatch budget = null, double budgetMs = double.PositiveInfinity,
                                     double brakeDecel = double.NaN, double finalAlt = 0, PhysStep step = default)
        {
            double now = Planetarium.GetUniversalTime();
            double maxTerrain = MaxTerrain(body);
            double spin = SpinSign(body);
            Vector3d r = r0;
            Vector3d vel = v0;
            Integ k = Integ.Start(body, spin, mass, drag, step, ref r, ref vel, ut0);
            Vector3d prevR = r0;
            double t = 0;
            double prevT = 0;
            double prevAbove = double.NaN;
            bool findBrake = brakeDecel > 0;
            double brakeUt = double.NaN;
            double prevGap = double.NaN;
            for (int i = 0; i < MaxSteps && t < MaxS; i++)
            {
                if (budget != null && budget.Elapsed.TotalMilliseconds > budgetMs)
                {
                    return null;
                }
                double alt = r.magnitude - body.Radius;
                double rot = ut0 - now + t;   // body rotation seconds from now
                bool lookUp = alt < maxTerrain || (findBrake && double.IsNaN(brakeUt));
                double ground = lookUp ? Ground(body, r, rot, spin) : double.NegativeInfinity;
                double surface = body.ocean ? Math.Max(ground, 0) : ground;              // the brake test: local terrain always
                double groundImpact = alt < maxTerrain ? ground : double.NegativeInfinity;  // the impact test: unchanged (U2)
                double above = alt - (body.ocean ? Math.Max(groundImpact, 0) : groundImpact);
                if (findBrake && double.IsNaN(brakeUt))
                {
                    Vector3d air = vel - spin * Vector3d.Cross(body.angularVelocity, r);
                    // the #17 condition along the prediction: the descent speed (radial, down positive) stopping within the height;
                    // the whole air speed would open at once in orbit (U5b: 1920 m/s → 92 km stop distance at 76 km)
                    double vDown = -Vector3d.Dot(air, r.normalized);
                    double gap = (vDown > 0 ? vDown * vDown / (2 * brakeDecel) : 0) - (alt - surface - finalAlt);
                    if (gap >= 0)
                    {
                        brakeUt = i == 0 || double.IsNaN(prevGap) ? ut0 + t : ut0 + prevT + (t - prevT) * (-prevGap) / (gap - prevGap);
                    }
                    prevGap = gap;
                }
                if (above <= 0)
                {
                    if (i == 0)
                    {
                        return WithBrake(At(body, r, rot, spin, ut0 + t), brakeUt);   // the start is already at or below the ground
                    }
                    double f = prevAbove / (prevAbove - above);
                    Vector3d hit = prevR + (r - prevR) * f;
                    double tHit = prevT + (t - prevT) * f;
                    return WithBrake(At(body, hit, ut0 - now + tHit, spin, ut0 + tHit), brakeUt);
                }
                double dt = alt > Math.Max(body.atmosphere ? body.atmosphereDepth : 0, maxTerrain) ? 10
                    : alt > maxTerrain || above > SurfaceBand ? 1
                    : 0.2;
                dt = k.Clip(ut0 + t, dt);
                prevR = r;
                prevT = t;
                prevAbove = above;
                k.Step(ref r, ref vel, dt, ut0 + t + dt);
                t += dt;
            }
            return null;
        }

        /// <summary>
        /// #22 decision 12-2 integration state: the step h and the frame (inverse rotation) held over an RK4 step; at every
        /// change of either, the velocity gets (h₂/2)·a₂ − (h₁/2)·a₁ (both Unity accelerations at the same time and place).
        /// With step.On false nothing differs from the plain RK4.
        /// </summary>
        private struct Integ
        {
            private CelestialBody _body;
            private double _spin, _mass;
            private DragModel _drag;
            private PhysStep _step;
            private double _h;
            private bool _inv;

            public static Integ Start(CelestialBody body, double spin, double mass, DragModel drag, PhysStep step,
                                      ref Vector3d r, ref Vector3d vel, double ut)
            {
                var k = new Integ { _body = body, _spin = spin, _mass = mass, _drag = drag, _step = step };
                if (step.On)
                {
                    double alt = r.magnitude - body.Radius;
                    k._h = step.H(body, alt, ut);
                    k._inv = InverseRot(body, alt);
                    if (k._h > 0)
                    {
                        vel += UnityAccel(body, spin, mass, drag, k._inv, r, vel) * (k._h / 2);
                    }
                }
                return k;
            }

            /// <summary>Ends a step at the warp end T so the h change falls on its time (altitude bounds fall at step ends).</summary>
            public double Clip(double ut, double dt)
            {
                if (_step.On && ut < _step.WarpEndUt && ut + dt > _step.WarpEndUt && _step.WarpEndUt - ut > 1e-3)
                {
                    return _step.WarpEndUt - ut;
                }
                return dt;
            }

            public void Step(ref Vector3d r, ref Vector3d vel, double dt, double utEnd)
            {
                Rk4(_body, _spin, _mass, _drag, _h, _inv, ref r, ref vel, dt);
                if (!_step.On)
                {
                    return;
                }
                double alt = r.magnitude - _body.Radius;
                double h2 = _step.H(_body, alt, utEnd);
                bool inv2 = InverseRot(_body, alt);
                if (h2 != _h || inv2 != _inv)
                {
                    Vector3d a2 = h2 > 0 ? UnityAccel(_body, _spin, _mass, _drag, inv2, r, vel) * (h2 / 2) : Vector3d.zero;
                    Vector3d a1 = _h > 0 ? UnityAccel(_body, _spin, _mass, _drag, _inv, r, vel) * (_h / 2) : Vector3d.zero;
                    vel += a2 - a1;
                    _h = h2;
                    _inv = inv2;
                }
            }
        }

        /// <summary>KSP integrates in the rotating frame below the body's inverse rotation threshold (raised to the atmosphere
        /// depth on atmospheric bodies, CelestialBody 1.12 decompiled); the predicted vessel is the active one.</summary>
        private static bool InverseRot(CelestialBody body, double alt) => alt < body.inverseRotThresholdAltitude;

        /// <summary>The acceleration Unity integrates (VesselPrecalculate.integrationAccel + drag): inertial, or under inverse
        /// rotation the rotating-frame one with Coriolis and centrifugal terms (Ω = spin·ω, v_rot = the air velocity).</summary>
        private static Vector3d UnityAccel(CelestialBody body, double spin, double mass, DragModel drag, bool inv, Vector3d r, Vector3d vel) =>
            UnityAccel(body, spin, mass, drag, inv, r, vel, AirAt(body, mass, drag, r));

        private static Vector3d UnityAccel(CelestialBody body, double spin, double mass, DragModel drag, bool inv, Vector3d r, Vector3d vel, Air air)
        {
            Vector3d a = Accel(body, spin, mass, drag, r, vel, air);
            if (inv)
            {
                Vector3d omega = spin * body.angularVelocity;
                Vector3d vRot = vel - Vector3d.Cross(omega, r);
                a += -2 * Vector3d.Cross(omega, vRot) - Vector3d.Cross(omega, Vector3d.Cross(omega, r));
            }
            return a;
        }

        /// <summary>The exact path's (inertial) derivative: the inertial acceleration at the late velocity V − (h/2)·a_U.</summary>
        private static Vector3d EffAccel(CelestialBody body, double spin, double mass, DragModel drag, double h, bool inv,
                                         Vector3d r, Vector3d vel)
        {
            if (h == 0)
            {
                return Accel(body, spin, mass, drag, r, vel);
            }
            Air air = AirAt(body, mass, drag, r);   // one lookup for both velocities (same place)
            Vector3d aU = UnityAccel(body, spin, mass, drag, inv, r, vel, air);
            return Accel(body, spin, mass, drag, r, vel - aU * (h / 2), air);
        }

        private static Impact WithBrake(Impact i, double brakeUt)
        {
            i.BrakeUt = brakeUt;
            return i;
        }

        /// <summary>|(inertial − rotation velocity) − surface velocity| (m/s): small when the rotation axis and sign used for
        /// latitude/longitude and the air velocity match KSP.</summary>
        public static double FrameCheck(Vessel v)
        {
            CelestialBody body = v.mainBody;
            Vector3d r = v.CoMD - body.position;
            return ((v.obt_velocity - SpinSign(body) * Vector3d.Cross(body.angularVelocity, r)) - v.srf_velocity).magnitude;
        }

        /// <summary>DIAG (#22 U5, U5c): the states at seconds, seconds + step, ... (lat.Length samples) with the same dynamics and
        /// steps as Predict (no ground check): latitude/longitude after the body rotation from now, altitude above sea level.
        /// Called on the check's start tick, so the rotation is counted from the body orientation the start state was read in.</summary>
        internal static void Propagate(CelestialBody body, Vector3d r0, Vector3d v0, double ut0, double mass, DragModel drag,
                                       double seconds, double step, PhysStep phys, double[] lat, double[] lon, double[] alt)
        {
            double maxTerrain = MaxTerrain(body);
            double spin = SpinSign(body);
            Vector3d r = r0, vel = v0;
            Integ k = Integ.Start(body, spin, mass, drag, phys, ref r, ref vel, ut0);
            double t = 0;
            for (int i = 0; i < lat.Length; i++)
            {
                double until = seconds + i * step;
                while (t < until - 1e-9)
                {
                    double a = r.magnitude - body.Radius;
                    double dt = a > Math.Max(body.atmosphere ? body.atmosphereDepth : 0, maxTerrain) ? 10 : a > maxTerrain ? 1 : 0.2;
                    dt = k.Clip(ut0 + t, Math.Min(dt, until - t));
                    k.Step(ref r, ref vel, dt, ut0 + t + dt);
                    t += dt;
                }
                LatLon(body, r, until, spin, out lat[i], out lon[i]);
                alt[i] = r.magnitude - body.Radius;
            }
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

        private static void Rk4(CelestialBody body, double spin, double mass, DragModel drag, double h, bool inv,
                                ref Vector3d r, ref Vector3d v, double dt)
        {
            Vector3d a1 = EffAccel(body, spin, mass, drag, h, inv, r, v);
            Vector3d r2 = r + v * (dt / 2), v2 = v + a1 * (dt / 2);
            Vector3d a2 = EffAccel(body, spin, mass, drag, h, inv, r2, v2);
            Vector3d r3 = r + v2 * (dt / 2), v3 = v + a2 * (dt / 2);
            Vector3d a3 = EffAccel(body, spin, mass, drag, h, inv, r3, v3);
            Vector3d r4 = r + v3 * dt, v4 = v + a3 * dt;
            Vector3d a4 = EffAccel(body, spin, mass, drag, h, inv, r4, v4);
            r += (v + v2 * 2 + v3 * 2 + v4) * (dt / 6);
            v += (a1 + a2 * 2 + a3 * 2 + a4) * (dt / 6);
        }

        /// <summary>#22 decision 12: the air temperature KSP gives a vessel at body-relative position r (FlightIntegrator:
        /// GetFullTemperature(alt, offset) with offset from GetAtmoThermalStats; Assembly-CSharp 1.12 decompiled). The offset
        /// depends only on the directions to the sun, of the local up and of the body axis, so r of a predicted step (inertial,
        /// axes of this tick) with this tick's sun direction (fixed over a descent) gives that place's local solar time.</summary>
        public static double AtmoTemperature(CelestialBody body, Vector3d r, double alt)
        {
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            double rm = r.magnitude;
            if (sun == null || sun == body || sun.scaledBody == null || rm <= 0)
            {
                return body.GetTemperature(alt);
            }
            Vector3d up = r / rm;
            Vector3d toSun = (Vector3d)sun.scaledBody.transform.position - ScaledSpace.LocalToScaledSpace(body.position + r);
            double d = toSun.magnitude;
            Vector3d sunVector = d > 0 ? toSun / d : up;
            body.GetAtmoThermalStats(false, sun, sunVector, Vector3d.Dot(sunVector, up), up, alt, out double offset, out _, out _);
            return body.GetFullTemperature(alt, offset);
        }

        /// <summary>The air at a position (depends on r only): looked up once when one place is evaluated at two velocities
        /// (decision 12-2 EffAccel). Ok false: no drag there.</summary>
        private struct Air
        {
            public bool Ok;
            public double Rho, C;
        }

        private static Air AirAt(CelestialBody body, double mass, DragModel drag, Vector3d r)
        {
            double alt = r.magnitude - body.Radius;
            if (body.atmosphere && alt < body.atmosphereDepth && drag != null && mass > 0)
            {
                double p = body.GetPressure(alt);
                double rho = body.GetDensity(p, AtmoTemperature(body, r, alt));
                double c = body.GetSpeedOfSound(p, rho);
                return new Air { Ok = rho > 0 && c > 0, Rho = rho, C = c };   // decision 3 bounds: 0/0 just under the atmosphere top
            }
            return default;
        }

        private static Vector3d Accel(CelestialBody body, double spin, double mass, DragModel drag, Vector3d r, Vector3d vel) =>
            Accel(body, spin, mass, drag, r, vel, AirAt(body, mass, drag, r));

        private static Vector3d Accel(CelestialBody body, double spin, double mass, DragModel drag, Vector3d r, Vector3d vel, Air air)
        {
            double rm = r.magnitude;
            Vector3d a = -body.gravParameter / (rm * rm * rm) * r;
            if (air.Ok)
            {
                Vector3d vr = vel - spin * Vector3d.Cross(body.angularVelocity, r);   // relative to the rotating air
                double speed = vr.magnitude;
                double k = drag.K(speed / air.C, air.Rho * speed);
                if (!double.IsNaN(k) && !double.IsInfinity(k))
                {
                    a -= k * air.Rho * speed / mass * vr;
                }
            }
            return a;
        }
    }
}
