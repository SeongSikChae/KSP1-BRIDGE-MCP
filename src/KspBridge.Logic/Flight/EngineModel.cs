using System.Collections.Generic;

namespace KspBridge.Flight
{
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
                if (!es[i].flameout && has)
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

        /// <summary>
        /// Queries the part's resources by the engine's propellant definitions (every propellant but ignoreForIsp must be above 0).
        /// Propellant.totalResourceAvailable is only refreshed for ignited engines (FixedUpdate: if (EngineIgnited) UpdatePropellantStatus()),
        /// so it is not used.
        /// </summary>
        public static bool HasPropellant(ModuleEngines e)
        {
            List<Propellant> props = e.propellants;
            for (int i = 0; i < props.Count; i++)
            {
                Propellant p = props[i];
                if (p.ignoreForIsp)
                {
                    continue;
                }
                e.part.GetConnectedResourceTotals(p.id, p.GetFlowMode(), out double amount, out double _);
                if (!(amount > 0))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
