using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Craft
{
    /// <summary>
    /// A part for craft analysis, either a prefab placed by <see cref="CraftBuilder"/> (design) or a live part (flight).
    /// Positions are in the root part's frame; "up" (+Y) is the craft axis as stacked in the VAB.
    /// </summary>
    public sealed class AnalysisPart
    {
        public Part Part;
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public double Mass;
        public int Istg;

        /// <summary>Maps a world point of <see cref="Part"/>'s own transform space into the root frame.</summary>
        public Func<Vector3, Vector3> ToRoot;

        /// <summary>Craft "up" in <see cref="Part"/>'s world space.</summary>
        public Vector3 WorldUp;

        public bool IsEngine => Part.FindModuleImplementing<ModuleEngines>() != null;
        public bool IsLaunchClamp => Part.FindModuleImplementing<LaunchClamp>() != null;
    }

    /// <summary>Steering, center of mass/lift, lowest point and the warnings derived from them.</summary>
    public static class CraftAnalysis
    {
        private const float PadMargin = 0.05f;

        public static List<AnalysisPart> FromBuilt(List<BuiltPart> parts)
        {
            Vector3 origin = parts.Count == 0 ? Vector3.zero : parts[0].Pos;
            return parts.Select(b =>
            {
                Transform t = b.Prefab.transform;
                Vector3 pos = b.Pos - origin;
                Quaternion rot = b.Rot;
                return new AnalysisPart
                {
                    Part = b.Prefab,
                    Pos = pos,
                    Rot = rot,
                    Istg = b.Istg,
                    Mass = CraftEstimator.Mass(new[] { b }),
                    ToRoot = x => pos + rot * t.InverseTransformPoint(x),
                    WorldUp = t.rotation * (Quaternion.Inverse(rot) * Vector3.up),
                };
            }).ToList();
        }

        public static List<AnalysisPart> FromVessel(Vessel v)
        {
            Transform root = v.rootPart.transform;
            return v.parts.Select(p => new AnalysisPart
            {
                Part = p,
                Pos = root.InverseTransformPoint(p.transform.position),
                Rot = Quaternion.Inverse(root.rotation) * p.transform.rotation,
                Istg = p.inverseStage,
                Mass = p.mass + p.GetResourceMass(),
                ToRoot = x => root.InverseTransformPoint(x),
                WorldUp = root.up,
            }).ToList();
        }

        /// <summary>Steering available to the attached parts while the burning engines fire.</summary>
        public static Dictionary<string, object> Control(IEnumerable<Part> attached, IEnumerable<Part> burning)
        {
            List<Part> parts = attached.ToList();
            List<ModuleGimbal> gimbals = burning.Select(p => p.FindModuleImplementing<ModuleGimbal>()).Where(g => g != null).ToList();
            double torque = parts.SelectMany(p => p.FindModulesImplementing<ModuleReactionWheel>()).Sum(w => (double)w.PitchTorque);
            int surfaces = parts.Count(p => p.FindModuleImplementing<ModuleControlSurface>() != null);
            int rcs = parts.Count(p => p.FindModuleImplementing<ModuleRCS>() != null);
            return new Dictionary<string, object>
            {
                ["gimbal_engines"] = gimbals.Count,
                ["gimbal_range_deg"] = gimbals.Count == 0 ? 0.0 : gimbals.Max(g => (double)g.gimbalRange),
                ["reaction_wheel_torque"] = torque,
                ["control_surfaces"] = surfaces,
                ["rcs_thrusters"] = rcs,
                ["steerable"] = gimbals.Count > 0 || torque > 0 || surfaces > 0 || rcs > 0,
            };
        }

        public static void StageWarnings(int stage, IEnumerable<Part> dropped, bool hasEngine, bool steerable, List<string> warnings)
        {
            if (hasEngine && !steerable)
            {
                warnings.Add("stage " + stage + ": no steering (no gimbal, reaction wheel, control surface or RCS)");
            }
            foreach (Part p in dropped.Where(IsProtected))
            {
                warnings.Add("stage " + stage + " drops protected part: " + p.partInfo.title);
            }
        }

        private static bool IsProtected(Part p) => p.FindModuleImplementing<ModuleAblator>() != null
            || p.FindModuleImplementing<ModuleParachute>() != null
            || p.FindModuleImplementing<ModuleCommand>() != null;

        /// <summary>Live parts that leave the vessel when the decouplers of <paramref name="stage"/> fire.</summary>
        public static List<Part> Drops(Vessel v, int stage, HashSet<Part> attached)
        {
            HashSet<(Part, Part)> cut = new HashSet<(Part, Part)>();
            foreach (Part d in attached.Where(p => p.inverseStage == stage))
            {
                ModuleDecouple sd = d.FindModuleImplementing<ModuleDecouple>();
                if (sd != null)
                {
                    IEnumerable<Part> others = sd.isOmniDecoupler
                        ? Neighbors(d)
                        : new[] { d.FindAttachNode(sd.explosiveNodeID)?.attachedPart }.Where(p => p != null);
                    foreach (Part o in others)
                    {
                        cut.Add((d, o));
                        cut.Add((o, d));
                    }
                }
                else if (d.FindModuleImplementing<ModuleAnchoredDecoupler>() != null && d.parent != null)
                {
                    cut.Add((d, d.parent));
                    cut.Add((d.parent, d));
                }
            }
            if (cut.Count == 0)
            {
                return new List<Part>();
            }

            HashSet<Part> kept = new HashSet<Part>();
            Queue<Part> queue = new Queue<Part>();
            if (attached.Contains(v.rootPart))
            {
                kept.Add(v.rootPart);
                queue.Enqueue(v.rootPart);
            }
            while (queue.Count > 0)
            {
                Part p = queue.Dequeue();
                foreach (Part n in Neighbors(p))
                {
                    if (attached.Contains(n) && !cut.Contains((p, n)) && kept.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }
            return attached.Where(p => !kept.Contains(p)).ToList();
        }

        private static IEnumerable<Part> Neighbors(Part p) =>
            p.parent == null ? (IEnumerable<Part>)p.children : p.children.Concat(new[] { p.parent });

        /// <summary>Center of mass/lift and the lowest point of the whole craft; adds craft-level warnings.</summary>
        public static Dictionary<string, object> Analyze(List<AnalysisPart> parts, List<string> warnings)
        {
            double mass = parts.Sum(p => p.Mass);
            Vector3 com = mass <= 0
                ? Vector3.zero
                : parts.Aggregate(Vector3.zero, (a, p) => a + p.Pos * (float)p.Mass) / (float)mass;

            Vector3? col = CenterOfLift(parts);
            double? colMinusCom = col.HasValue ? col.Value.y - com.y : (double?)null;
            if (colMinusCom > 0)
            {
                warnings.Add("center of lift is " + colMinusCom.Value.ToString("F2")
                    + " m ahead of center of mass (unstable in atmosphere)");
            }

            AnalysisPart lowest = null;
            float lowestY = float.MaxValue;
            float lowestEngineY = float.MaxValue;
            int firstStage = parts.Where(p => p.IsEngine).Select(p => p.Istg).DefaultIfEmpty(-1).Max();
            Dictionary<AnalysisPart, float> bottoms = new Dictionary<AnalysisPart, float>();
            foreach (AnalysisPart p in parts.Where(p => !p.IsLaunchClamp))
            {
                float? y = Bottom(p);
                if (!y.HasValue)
                {
                    continue;
                }
                bottoms[p] = y.Value;
                if (y.Value < lowestY)
                {
                    lowestY = y.Value;
                    lowest = p;
                }
                if (p.IsEngine && p.Istg == firstStage && y.Value < lowestEngineY)
                {
                    lowestEngineY = y.Value;
                }
            }
            if (lowestEngineY < float.MaxValue)
            {
                // One warning per part type (symmetry copies sit at the same height).
                foreach (IGrouping<string, KeyValuePair<AnalysisPart, float>> g in bottoms
                    .Where(kv => !kv.Key.IsEngine && kv.Value < lowestEngineY - PadMargin)
                    .GroupBy(kv => kv.Key.Part.partInfo.title))
                {
                    warnings.Add(g.Key + " sits " + (lowestEngineY - g.Min(kv => kv.Value)).ToString("F2")
                        + " m below the lowest engine (may hit the pad)");
                }
            }

            return new Dictionary<string, object>
            {
                ["com"] = Vec(com),
                ["col"] = col.HasValue ? Vec(col.Value) : null,
                ["col_minus_com"] = colMinusCom,
                ["stable"] = colMinusCom.HasValue ? colMinusCom.Value <= 0 : (bool?)null,
                ["lowest_part"] = lowest?.Part.partInfo.title,
                ["lowest_y"] = lowest == null ? (double?)null : lowestY,
                ["lowest_engine_y"] = lowestEngineY < float.MaxValue ? lowestEngineY : (double?)null,
            };
        }

        /// <summary>Lift-weighted center from the game's own lift providers (as the VAB CoL marker).</summary>
        private static Vector3? CenterOfLift(List<AnalysisPart> parts)
        {
            CenterOfLiftQuery q = new CenterOfLiftQuery();
            Vector3 sum = Vector3.zero;
            float lift = 0;
            foreach (AnalysisPart p in parts)
            {
                foreach (ILiftProvider lp in p.Part.FindModulesImplementing<ILiftProvider>())
                {
                    try
                    {
                        q.Reset();
                        q.refVector = p.WorldUp;
                        q.refAltitude = 0;
                        q.refStaticPressure = 101.325;
                        q.refAirDensity = 1.225;
                        lp.OnCenterOfLiftQuery(q);
                    }
                    catch (Exception)
                    {
                        continue; // prefab modules may not answer outside the editor
                    }
                    if (q.lift > 0 && !float.IsNaN(q.pos.x))
                    {
                        sum += p.ToRoot(q.pos) * q.lift;
                        lift += q.lift;
                    }
                }
            }
            return lift > 0 ? sum / lift : (Vector3?)null;
        }

        /// <summary>Lowest point along the craft axis of the part's meshes, in the root frame.</summary>
        private static float? Bottom(AnalysisPart p)
        {
            float? min = null;
            foreach (MeshFilter mf in p.Part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                {
                    continue;
                }
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = p.ToRoot(mf.transform.TransformPoint(c)).y;
                    if (!min.HasValue || y < min.Value)
                    {
                        min = y;
                    }
                }
            }
            return min;
        }

        private static List<object> Vec(Vector3 v) => new List<object> { (double)v.x, (double)v.y, (double)v.z };
    }
}
