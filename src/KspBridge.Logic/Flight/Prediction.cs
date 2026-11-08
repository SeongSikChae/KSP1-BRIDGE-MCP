using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Thrust/drag based predictions for the active vessel (main thread only).
    /// Vertical 1-D integration: horizontal speed and attitude changes are ignored, so end burns on real state, not on these numbers.
    /// </summary>
    public sealed class Prediction
    {
        private const double G0 = 9.80665;
        private const double Dt = 0.1;
        private const double MaxTime = 600;
        private const double MinDynamicPressure = 50; // Pa; below this the drag coefficient is not estimated

        public double ThrustKn;
        public double MaxAccel;
        public double Twr;
        public double DragAccel;
        public double? StopTime;
        public double? StopDistance;
        public double? StopDv;
        public double? TimeToImpact;
        public double? BurnMargin;

        private static Prediction _cached;
        private static Guid _cachedVessel;
        private static float _cachedTime = -1;
        private static Guid _dragVessel;
        private static double _dragK; // drag acceleration per Pa of dynamic pressure, last reliable estimate

        public static void Invalidate()
        {
            _cached = null;
        }

        /// <summary>Computed at most once per physics tick per vessel.</summary>
        public static Prediction Get(Vessel v)
        {
            if (_cached != null && _cachedVessel == v.id && _cachedTime == Time.fixedTime)
            {
                return _cached;
            }
            _cached = Compute(v);
            _cachedVessel = v.id;
            _cachedTime = Time.fixedTime;
            return _cached;
        }

        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["thrust_kn"] = ThrustKn,
            ["max_accel"] = MaxAccel,
            ["twr"] = Twr,
            ["drag_accel"] = DragAccel,
            ["stop_time"] = StopTime,
            ["stop_distance"] = StopDistance,
            ["stop_dv"] = StopDv,
            ["time_to_impact"] = TimeToImpact,
            ["burn_margin"] = BurnMargin,
        };

        private static Prediction Compute(Vessel v)
        {
            CelestialBody body = v.mainBody;
            List<ModuleEngines> engines = new List<ModuleEngines>();
            HashSet<string> propellants = new HashSet<string>();
            foreach (Part p in v.parts)
            {
                foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                {
                    if (!e.EngineIgnited || e.flameout || !e.isOperational)
                    {
                        continue;
                    }
                    engines.Add(e);
                    foreach (Propellant pr in e.propellants)
                    {
                        propellants.Add(pr.name);
                    }
                }
            }

            double flow = 0; // t/s at full throttle
            foreach (ModuleEngines e in engines)
            {
                flow += e.maxFuelFlow * e.thrustPercentage / 100.0;
            }
            double fuel = 0; // t
            foreach (Part p in v.parts)
            {
                foreach (PartResource res in p.Resources)
                {
                    if (propellants.Contains(res.resourceName) && res.info.density > 0 && res.flowState)
                    {
                        fuel += res.amount * res.info.density;
                    }
                }
            }

            double mass = v.GetTotalMass();
            double h0 = v.altitude;
            double ground = GroundAltitude(v);
            double ThrustAt(double h)
            {
                if (engines.Count == 0)
                {
                    return 0;
                }
                float atm = (float)(body.atmosphere && h < body.atmosphereDepth ? body.GetPressureAtm(h) : 0);
                double t = 0;
                foreach (ModuleEngines e in engines)
                {
                    t += e.maxFuelFlow * e.thrustPercentage / 100.0 * e.atmosphereCurve.Evaluate(atm) * G0;
                }
                return t; // kN
            }
            double Gravity(double h)
            {
                double rad = body.Radius + h;
                return body.gravParameter / (rad * rad);
            }
            double Density(double h)
            {
                if (!body.atmosphere || h >= body.atmosphereDepth)
                {
                    return 0;
                }
                return body.GetDensity(body.GetPressure(h), body.GetTemperature(h));
            }

            Prediction r = new Prediction();
            r.ThrustKn = ThrustAt(h0);
            r.MaxAccel = mass > 0 ? r.ThrustKn / mass : 0;
            r.Twr = r.MaxAccel / Gravity(h0);

            // Measured drag: non-gravitational acceleration minus the current thrust (along the nose).
            double currentThrust = 0;
            foreach (ModuleEngines e in engines)
            {
                currentThrust += e.finalThrust;
            }
            Vector3d nose = v.GetTransform().up;
            Vector3d aDrag = v.perturbation - nose * (mass > 0 ? currentThrust / mass : 0);
            double speed = v.srfSpeed;
            r.DragAccel = speed > 0.1 ? Math.Max(0, -Vector3d.Dot(aDrag, v.srf_velocity / speed)) : 0;
            double q = 0.5 * v.atmDensity * speed * speed;
            if (q >= MinDynamicPressure)
            {
                _dragK = r.DragAccel / q;
                _dragVessel = v.id;
            }
            double k = _dragVessel == v.id ? _dragK : 0;

            double vz0 = v.verticalSpeed;
            if (vz0 >= 0)
            {
                return r;
            }

            // Burn now at full thrust until the descent stops.
            {
                double h = h0, vz = vz0, m = mass, f = fuel, t = 0, dv = 0;
                bool ok = engines.Count > 0;
                while (ok && vz < 0 && t < MaxTime)
                {
                    double thrust = f > 0 ? ThrustAt(h) : 0;
                    if (thrust <= 0)
                    {
                        ok = false;
                        break;
                    }
                    double a = -Gravity(h) + thrust / m + k * 0.5 * Density(h) * vz * vz;
                    dv += thrust / m * Dt;
                    vz += a * Dt;
                    h += vz * Dt;
                    m = Math.Max(m - flow * Dt, 0.001);
                    f -= flow * Dt;
                    t += Dt;
                }
                if (ok && vz >= 0)
                {
                    r.StopTime = t;
                    r.StopDistance = h0 - h;
                    r.StopDv = dv;
                    r.BurnMargin = (h0 - ground) - (h0 - h);
                }
            }

            // Free fall (no thrust) until the ground.
            {
                double h = h0, vz = vz0, t = 0;
                while (h > ground && t < MaxTime)
                {
                    double a = -Gravity(h) + k * 0.5 * Density(h) * vz * vz;
                    vz += a * Dt;
                    h += vz * Dt;
                    t += Dt;
                }
                if (h <= ground)
                {
                    r.TimeToImpact = t;
                }
            }
            return r;
        }

        internal static double GroundAltitude(Vessel v)
        {
            double terrain = v.terrainAltitude;
            if (v.mainBody.ocean && terrain < 0)
            {
                terrain = 0;
            }
            return terrain;
        }
    }
}
