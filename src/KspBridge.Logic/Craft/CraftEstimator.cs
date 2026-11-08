using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Craft
{
    /// <summary>
    /// Rough cost/mass/Δv estimate from prefabs. Each firing stage burns the propellant of the parts dropped
    /// by the next decoupling stage (or everything left for the last one). Parallel core burn is ignored.
    /// </summary>
    public static class CraftEstimator
    {
        private const double G0 = 9.80665;

        public static double Cost(List<BuiltPart> parts) => parts.Sum(p => (double)p.Info.cost);

        public static double Mass(IEnumerable<BuiltPart> parts) => parts.Sum(p => DryMass(p) + ResourceMass(p, null));

        private static double DryMass(BuiltPart p) => p.Prefab.mass;

        private static double ResourceMass(BuiltPart p, HashSet<string> only)
        {
            double m = 0;
            foreach (ConfigNode r in p.Info.partConfig.GetNodes("RESOURCE"))
            {
                string name = r.GetValue("name");
                if (only != null && !only.Contains(name))
                {
                    continue;
                }
                PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(name);
                if (def == null)
                {
                    continue;
                }
                m += p.Amount(r) * def.density;
            }
            return m;
        }

        /// <summary>
        /// Attached parts that leave when the staged decouplers of <paramref name="stage"/> fire: each cuts the link at its
        /// explosive node (all links if omni, the parent link if radial) and everything no longer connected to the root drops.
        /// A heat shield (explosive node "bottom") therefore stays and only the parts below it drop.
        /// </summary>
        private static List<BuiltPart> Drops(List<BuiltPart> parts, HashSet<BuiltPart> attached, int stage)
        {
            HashSet<(BuiltPart, BuiltPart)> cut = new HashSet<(BuiltPart, BuiltPart)>();
            foreach (BuiltPart d in parts.Where(p => p.Istg == stage && p.Sidx >= 0 && p.IsDecoupler && attached.Contains(p)))
            {
                ModuleDecouple md = d.Prefab.FindModuleImplementing<ModuleDecouple>();
                IEnumerable<BuiltPart> others;
                if (d.IsRadialDecoupler)
                {
                    others = d.Parent == null ? Enumerable.Empty<BuiltPart>() : new[] { d.Parent };
                }
                else if (md.isOmniDecoupler)
                {
                    others = Neighbors(d);
                }
                else
                {
                    string id = md.explosiveNodeID;
                    others = d.OwnNode != null && d.OwnNode.id == id && d.Parent != null
                        ? new[] { d.Parent }
                        : d.Children.Where(c => !c.Surface && c.ParentNode != null && c.ParentNode.id == id);
                }
                foreach (BuiltPart o in others)
                {
                    cut.Add((d, o));
                    cut.Add((o, d));
                }
            }
            if (cut.Count == 0)
            {
                return new List<BuiltPart>();
            }

            BuiltPart root = parts.First(p => p.Parent == null);
            HashSet<BuiltPart> kept = new HashSet<BuiltPart> { root };
            Queue<BuiltPart> queue = new Queue<BuiltPart>(kept);
            while (queue.Count > 0)
            {
                BuiltPart p = queue.Dequeue();
                foreach (BuiltPart n in Neighbors(p))
                {
                    if (attached.Contains(n) && !cut.Contains((p, n)) && kept.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }
            return parts.Where(p => attached.Contains(p) && !kept.Contains(p)).ToList();
        }

        private static IEnumerable<BuiltPart> Neighbors(BuiltPart p) =>
            p.Parent == null ? (IEnumerable<BuiltPart>)p.Children : p.Children.Concat(new[] { p.Parent });

        public static List<object> Stages(List<BuiltPart> parts, List<string> warnings)
        {
            List<object> result = new List<object>();
            int top = parts.Max(p => p.Istg);
            HashSet<BuiltPart> attached = new HashSet<BuiltPart>(parts);
            HashSet<BuiltPart> ignited = new HashSet<BuiltPart>();

            for (int s = top; s >= 0; s--)
            {
                // Fire stage s: decouplers cut their explosive node, the side away from the root drops, engines ignite.
                List<BuiltPart> dropped = Drops(parts, attached, s);
                attached.ExceptWith(dropped);
                foreach (BuiltPart e in parts.Where(p => p.Istg == s && p.Sidx >= 0 && p.IsEngine && attached.Contains(p)))
                {
                    ignited.Add(e);
                }

                List<BuiltPart> enginesParts = ignited.Where(attached.Contains).ToList();
                Dictionary<string, object> control = CraftAnalysis.Control(
                    attached.Select(p => p.Prefab), enginesParts.Select(p => p.Prefab));
                CraftAnalysis.StageWarnings(s, dropped.Select(p => p.Prefab), enginesParts.Count > 0, (bool)control["steerable"], warnings);
                List<ModuleEngines> engines = enginesParts.Select(p => p.Prefab.FindModuleImplementing<ModuleEngines>()).ToList();
                // Thrust limiter scales thrust (and flow) but not Isp: delta-v is unchanged, TWR drops.
                double LimitedThrust(int i) => engines[i].maxThrust * (enginesParts[i].Spec.ThrustLimit ?? 100f) / 100.0;
                double m0 = Mass(attached);
                Dictionary<string, object> row = new Dictionary<string, object>
                {
                    ["stage"] = s,
                    ["parts"] = parts.Where(p => p.Istg == s && p.Sidx >= 0).Select(p => (object)p.Info.title).ToList(),
                    ["start_mass"] = m0,
                    ["drops"] = dropped.Select(p => (object)p.Info.title).ToList(),
                    ["control"] = control,
                };

                if (engines.Count == 0)
                {
                    row["dv_vac"] = 0.0;
                    row["dv_asl"] = 0.0;
                    row["twr_asl"] = 0.0;
                    result.Add(row);
                    continue;
                }

                HashSet<string> props = new HashSet<string>(engines.SelectMany(e => e.propellants.Select(pr => pr.name)));
                // Fuel burned now: in parts that the next decoupling stage drops, else everything left.
                int nextDrop = parts.Where(p => p.Istg < s && p.Sidx >= 0 && p.IsDecoupler && attached.Contains(p))
                    .Select(p => p.Istg).DefaultIfEmpty(-1).Max();
                IEnumerable<BuiltPart> burnSet = nextDrop >= 0
                    ? Drops(parts, attached, nextDrop)
                    : attached;
                double fuel = burnSet.Distinct().Sum(p => ResourceMass(p, props));

                IEnumerable<int> idx = Enumerable.Range(0, engines.Count);
                double thrustVac = idx.Sum(i => LimitedThrust(i));
                double ispVac = thrustVac / idx.Sum(i => LimitedThrust(i) / Math.Max(1e-3, engines[i].atmosphereCurve.Evaluate(0f)));
                double thrustAsl = idx.Sum(i => LimitedThrust(i) * engines[i].atmosphereCurve.Evaluate(1f) / Math.Max(1e-3, engines[i].atmosphereCurve.Evaluate(0f)));
                double ispAsl = thrustAsl / idx.Sum(i => LimitedThrust(i) * engines[i].atmosphereCurve.Evaluate(1f) / Math.Max(1e-3, engines[i].atmosphereCurve.Evaluate(0f)) / Math.Max(1e-3, engines[i].atmosphereCurve.Evaluate(1f)));
                double m1 = Math.Max(1e-3, m0 - fuel);

                row["fuel_mass"] = fuel;
                row["end_mass"] = m1;
                row["dv_vac"] = ispVac * G0 * Math.Log(m0 / m1);
                row["dv_asl"] = ispAsl * G0 * Math.Log(m0 / m1);
                row["twr_asl"] = thrustAsl / (m0 * G0);
                row["twr_vac"] = thrustVac / (m0 * G0);
                result.Add(row);

                // The burned parts leave the mass when their decoupler fires in a later iteration.
                if (nextDrop < 0)
                {
                    break;
                }
            }
            return result;
        }
    }
}
