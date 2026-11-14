using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>SRB burnout prediction at one UT: seconds until it stops and the impulse (kN·s) it still delivers.</summary>
    public struct SrbForecast
    {
        public double Ut;
        public double BurnoutS;
        public double Impulse;

        public SrbForecast(double ut, double burnoutS, double impulse)
        {
            Ut = ut;
            BurnoutS = burnoutS;
            Impulse = impulse;
        }
    }

    /// <summary>
    /// Engine state of the active vessel. Per-tick paths loop over a cached engine array without LINQ or allocation.
    /// Main thread only.
    /// </summary>
    public static class EngineModel
    {
        // Engine array cache. Key: the vessel object (a reloaded scene keeps persistentId but has new modules), currentStage, part count.
        private static ModuleEngines[] _engines = new ModuleEngines[0];
        private static Vessel _vessel;
        private static int _stage = int.MinValue;
        private static int _parts = -1;

        // Propellant cache. Key: engine-array key + flamed-out ignited count + ignited count.
        private static bool _igKeyValid;
        private static int _igFlamedOut;
        private static int _igIgnited;
        private static int _ignitable;
        private static readonly Dictionary<ModuleEngines, bool> _hasProp = new Dictionary<ModuleEngines, bool>();

        /// <summary>Every engine on the vessel, recomputed only when the array key changes.</summary>
        public static ModuleEngines[] Engines(Vessel v)
        {
            if (ReferenceEquals(v, _vessel) && v.currentStage == _stage && v.parts.Count == _parts)
            {
                return _engines;
            }
            _vessel = v;
            _stage = v.currentStage;
            _parts = v.parts.Count;
            _engines = v.FindPartModulesImplementing<ModuleEngines>().ToArray();
            _igKeyValid = false;
            _srbCache.Clear();   // no references to dropped or other vessels' engines
            return _engines;
        }

        /// <summary>Ignited: lit including flamed out. Operational: can make thrust now (isOperational is false on flameout).</summary>
        public static bool IsIgnited(ModuleEngines e) => e.EngineIgnited;

        public static bool IsOperational(ModuleEngines e) => e.EngineIgnited && e.isOperational;

        public static bool AnyOperational(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                if (IsOperational(es[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Actual thrust of the operational engines, kN.</summary>
        public static double ThrustSum(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double sum = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsOperational(es[i]))
                {
                    sum += es[i].finalThrust;
                }
            }
            return sum;
        }

        /// <summary>Full-throttle thrust of the operational engines in the given air (thrust limiter applied), kN.</summary>
        public static double MaxThrustSum(Vessel v, float pressureAtm, double temperature, double density)
        {
            ModuleEngines[] es = Engines(v);
            double sum = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsOperational(es[i]))
                {
                    sum += es[i].MaxThrustOutputAtm(false, true, pressureAtm, temperature, density);
                }
            }
            return sum;
        }

        /// <summary>
        /// Every cached accessor (Ignitable, HasPropellantCached) checks the key itself, so none depends on another being called first.
        /// Key: engine-array key (vessel persistentId, currentStage, part count) + flamed-out ignited count + ignited count.
        /// </summary>
        private static void RefreshPropellant(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            int flamedOut = 0, ignited = 0;
            for (int i = 0; i < es.Length; i++)
            {
                // Engines that cannot restart leave Ignited when they flame out, so the ignited count is part of the key.
                if (es[i].EngineIgnited)
                {
                    ignited++;
                    if (es[i].flameout)
                    {
                        flamedOut++;
                    }
                }
            }
            if (_igKeyValid && flamedOut == _igFlamedOut && ignited == _igIgnited)
            {
                return;
            }
            _igKeyValid = true;
            _igFlamedOut = flamedOut;
            _igIgnited = ignited;
            _ignitable = 0;
            _hasProp.Clear();
            for (int i = 0; i < es.Length; i++)
            {
                bool has = HasPropellant(es[i]);
                _hasProp[es[i]] = has;
                // A flamed-out engine counts while still ignited: KSP relights it once propellant flows (a shut-down one never comes back, #13)
                if (has && (!es[i].flameout || es[i].EngineIgnited))
                {
                    _ignitable++;
                }
            }
        }

        /// <summary>Engines in this and later stages that still have propellant and are not flamed out.</summary>
        public static int Ignitable(Vessel v)
        {
            RefreshPropellant(v);
            return _ignitable;
        }

        /// <summary>Propellant check without a resource query on ticks where the cache key is unchanged.</summary>
        public static bool HasPropellantCached(ModuleEngines e)
        {
            RefreshPropellant(e.vessel);
            return _hasProp.TryGetValue(e, out bool has) && has;
        }

        // ---- Thrust change prediction (KSP 1.12 ModuleEngines.UpdateThrottle, decompile checked) ----

        private const double G0 = 9.80665;
        private const double KpaToAtm = 0.00986923;

        /// <summary>Engines whose thrust decays after throttle 0 (solid boosters excluded: throttle cannot stop them).</summary>
        private static bool IsResponsive(ModuleEngines e) =>
            IsOperational(e) && e.useEngineResponseTime && !e.throttleUseAlternate && !e.throttleLocked;

        /// <summary>Solid boosters: throttle-locked, burn until propellant runs out (thrustCurve only if present).</summary>
        internal static bool IsSolid(ModuleEngines e) => IsOperational(e) && e.throttleLocked;

        /// <summary>Drops every cached engine reference (AutoStager.Clear: an unloading logic must not hold part references).</summary>
        public static void ClearCaches()
        {
            _engines = new ModuleEngines[0];
            _vessel = null;
            _stage = int.MinValue;
            _parts = -1;
            _igKeyValid = false;
            _hasProp.Clear();
            _srbCache.Clear();
        }

        /// <summary>0→90% thrust time of the slowest responsive engine. ln 10 / k is a continuous approximation
        /// of the per-tick Lerp and differs from the discrete path by about 10%.</summary>
        public static double SpoolUpS(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double max = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsResponsive(es[i]) && es[i].engineAccelerationSpeed > 0)
                {
                    max = Math.Max(max, Math.Log(10) / es[i].engineAccelerationSpeed);
                }
            }
            return max;
        }

        /// <summary>Δv still added after commanding throttle 0 now: ∫ F·e^(−k t) dt / m = F / (k·m) (kN/t = m/s²).</summary>
        public static double TailoffDv(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double sum = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsResponsive(es[i]) && es[i].engineDecelerationSpeed > 0)
                {
                    sum += es[i].finalThrust / es[i].engineDecelerationSpeed;
                }
            }
            return v.totalMass > 0 ? sum / v.totalMass : 0;
        }

        /// <summary>Isp at the vessel's current static pressure.</summary>
        private static double IspNow(Vessel v, ModuleEngines e) => e.atmosphereCurve.Evaluate((float)(v.staticPressurekPa * KpaToAtm));

        /// <summary>Full-throttle thrust at that Isp: maxFuelFlow·Isp·g0 with the thrust limiter (maxThrust is only the vacuum rating).</summary>
        private static double ThrustNow(ModuleEngines e, double isp) => e.maxFuelFlow * e.thrustPercentage / 100 * isp * G0;

        /// <summary>Seconds to deliver dv at full throttle with the operational engines (rocket equation, thrust-weighted Isp).
        /// Infinity without thrust.</summary>
        public static double BurnTime(Vessel v, double dv)
        {
            ModuleEngines[] es = Engines(v);
            double f = 0, fOverIsp = 0;
            for (int i = 0; i < es.Length; i++)
            {
                ModuleEngines e = es[i];
                if (!IsOperational(e))
                {
                    continue;
                }
                double isp = IspNow(v, e);
                if (isp <= 0)
                {
                    continue;
                }
                double fe = ThrustNow(e, isp);
                f += fe;
                fOverIsp += fe / isp;
            }
            if (f <= 0)
            {
                return double.PositiveInfinity;
            }
            double ve = f / fOverIsp * G0;   // thrust-weighted Isp · g0
            return v.totalMass * (1 - Math.Exp(-dv / ve)) / (f / ve);
        }

        /// <summary>Full-throttle acceleration of the operational engines at the current pressure (m/s²).</summary>
        public static double MaxAccel(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double f = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsOperational(es[i]))
                {
                    double isp = IspNow(v, es[i]);
                    if (isp > 0)
                    {
                        f += ThrustNow(es[i], isp);
                    }
                }
            }
            return v.totalMass > 0 ? f / v.totalMass : 0;
        }

        private static readonly Dictionary<ModuleEngines, SrbForecast> _srbCache = new Dictionary<ModuleEngines, SrbForecast>();

        /// <summary>SRB burnout prediction from the remaining propellant, computed once per tick and cached per engine.
        /// Stock (no thrustCurve): closed form, constant flow until the fuel runs out. thrustCurve (mods): integrated.</summary>
        public static SrbForecast Srb(ModuleEngines e)
        {
            double ut = Planetarium.GetUniversalTime();
            if (_srbCache.TryGetValue(e, out SrbForecast c) && c.Ut == ut)
            {
                return c;   // AutoStager and gravity_turn in the same tick do not recompute
            }
            Propellants(e, out double f, out double cap);
            SrbForecast r;
            if (!e.useThrustCurve)
            {
                double mdot = e.maxFuelFlow * e.thrustPercentage / 100;   // t/s, constant
                double burn = mdot > 0 ? f * cap / mdot : 0;
                r = new SrbForecast(ut, burn, mdot * IspNow(e.vessel, e) * G0 * burn);
            }
            else
            {
                r = IntegrateCurve(e, f, cap, ut, 0.1, 600);   // longer than 600 s counts as 600 s (bad curve guard)
            }
            _srbCache[e] = r;
            return r;
        }

        /// <summary>thrustCurve path: the curve input is the remaining fraction; it stops at once below flameoutBar or when the
        /// fuel is under ignitionThreshold times what one step needs (decompile checked).</summary>
        private static SrbForecast IntegrateCurve(ModuleEngines e, double f, double cap, double ut, double dt, double maxS)
        {
            double t = 0, impulse = 0;
            double isp = IspNow(e.vessel, e);
            while (t < maxS && cap > 0)
            {
                double flow = e.thrustCurve.Evaluate((float)f);
                double mdot = e.maxFuelFlow * flow * e.thrustPercentage / 100;
                if (mdot <= 0 || flow < e.flameoutBar || f * cap < mdot * dt * e.ignitionThreshold)
                {
                    break;   // mdot <= 0: no flow, like the closed form's 0 (not 600 s)
                }
                double step = Math.Min(dt, f * cap / mdot);   // the last step burns only what is left
                impulse += mdot * isp * G0 * step;
                f -= mdot * step / cap;
                t += step;
            }
            return new SrbForecast(ut, t, impulse);
        }

        /// <summary>Remaining fraction (lowest over the propellants, ignoreForThrustCurve excluded like ModuleEngines' thrustCurve ratio)
        /// and total propellant capacity (t; ignoreForIsp excluded like the mixture density) of the engine's part.</summary>
        private static void Propellants(ModuleEngines e, out double fraction, out double capacity)
        {
            fraction = 1;
            capacity = 0;
            List<Propellant> props = e.propellants;
            for (int i = 0; i < props.Count; i++)
            {
                Propellant p = props[i];
                if (p.ignoreForIsp && p.ignoreForThrustCurve)
                {
                    continue;
                }
                e.part.GetConnectedResourceTotals(p.id, p.GetFlowMode(), out double amount, out double max);
                if (!p.ignoreForThrustCurve)
                {
                    fraction = Math.Min(fraction, max > 0 ? amount / max : 0);
                }
                if (!p.ignoreForIsp)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(p.id);
                    capacity += max * (def != null ? def.density : 0);
                }
            }
        }

        /// <summary>Δv the SRBs still add even at throttle 0 (approx: remaining impulse / current mass).</summary>
        public static double SrbRemainingDv(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double impulse = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsSolid(es[i]))
                {
                    impulse += Srb(es[i]).Impulse;
                }
            }
            return v.totalMass > 0 ? impulse / v.totalMass : 0;
        }

        /// <summary>Longest burnout (s) of the burning SRBs; 0 without.</summary>
        public static double SrbBurnoutS(Vessel v)
        {
            ModuleEngines[] es = Engines(v);
            double burnout = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (IsSolid(es[i]))
                {
                    burnout = Math.Max(burnout, Srb(es[i]).BurnoutS);
                }
            }
            return burnout;
        }

        /// <summary>SRB Δv minus the gravity loss while they burn: g·sin(γ)·burnout_s (never below 0).</summary>
        public static double SrbNetDv(Vessel v)
        {
            double remaining = SrbRemainingDv(v);
            if (remaining <= 0)
            {
                return 0;
            }
            double r = v.orbit.pos.magnitude, g = v.mainBody.gravParameter / (r * r);
            double sinGamma = v.obt_speed > 1 ? v.verticalSpeed / v.obt_speed : 1;   // flight path angle
            return Math.Max(0, remaining - g * sinGamma * SrbBurnoutS(v));
        }

        /// <summary>Apoapsis gain (m) from adding dv along the current orbital velocity (vis-viva). Infinity on an escape.</summary>
        public static double ApRise(Vessel v, double dv)
        {
            if (dv <= 0)
            {
                return 0;
            }
            Vector3d r = v.orbit.pos, vel = v.orbit.vel;
            double now = ApR(r, vel, v.mainBody.gravParameter);
            if (double.IsInfinity(now))
            {
                return double.PositiveInfinity;   // already escaping: infinity, not ∞ − ∞ = NaN
            }
            return ApR(r, vel + vel.normalized * dv, v.mainBody.gravParameter) - now;
        }

        private static double ApR(Vector3d r, Vector3d vel, double mu)
        {
            double eps = vel.sqrMagnitude / 2 - mu / r.magnitude;
            if (eps >= 0)
            {
                return double.PositiveInfinity;
            }
            double a = -mu / (2 * eps), h = Vector3d.Cross(r, vel).magnitude;
            double e = Math.Sqrt(Math.Max(0, 1 + 2 * eps * h * h / (mu * mu)));
            return a * (1 + e);   // radius from the body centre; the altitude difference equals the radius difference
        }

        /// <summary>
        /// KSP's CheckDeprived at full throttle (decompile checked): every propellant but ignoreForIsp has at least
        /// ratio × one full-throttle tick × ignitionThreshold above minResToLeave (and some left at all). thrustCurve engines also need
        /// the curve at the remaining fraction ≥ flameoutBar. Queries the part's resources by the engine's propellant definitions:
        /// Propellant.totalResourceAvailable is only refreshed for ignited engines (FixedUpdate: if (EngineIgnited) UpdatePropellantStatus()),
        /// so it is not used.
        /// </summary>
        public static bool HasPropellant(ModuleEngines e)
        {
            double need = NeedPerTick(e);
            double fraction = 1;
            List<Propellant> props = e.propellants;
            for (int i = 0; i < props.Count; i++)
            {
                Propellant p = props[i];
                if (p.ignoreForIsp)
                {
                    continue;
                }
                e.part.GetConnectedResourceTotals(p.id, p.GetFlowMode(), out double amount, out double max);
                if (!(amount > 0) || !(amount - p.minResToLeave >= p.ratio * need * e.ignitionThreshold))
                {
                    return false;
                }
                if (!p.ignoreForThrustCurve)
                {
                    fraction = Math.Min(fraction, max > 0 ? amount / max : 0);
                }
            }
            return !e.useThrustCurve || e.thrustCurve.Evaluate((float)fraction) >= e.flameoutBar;
        }

        /// <summary>Propellant volume one full-throttle physics tick asks for (RequiredPropellantMass without FuelUsage).
        /// flowMultiplier above 1 (velocity curves) raises KSP's need, so it counts; below 1 it is held at 1 (never under KSP's bar).</summary>
        private static double NeedPerTick(ModuleEngines e) =>
            e.maxFuelFlow * e.thrustPercentage / 100 * Math.Max(1.0, e.flowMultiplier) * e.multFlow * TimeWarp.fixedDeltaTime
            * e.mixtureDensityRecip;
    }
}
