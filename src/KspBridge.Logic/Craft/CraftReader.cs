using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace KspBridge.Craft
{
    /// <summary>
    /// Reads a .craft file back into the build_craft design tree (docs/ksp-bridge/0040, 0041 unit 5).
    /// Approximates what the tree cannot express and reports every part whose rebuilt position or rotation differs.
    /// </summary>
    public static class CraftReader
    {
        private const float PosTolerance = 0.05f;
        private const float RotToleranceDeg = 2f;

        private sealed class RawPart
        {
            public string Ref, Name;
            public Vector3 Pos;
            public Quaternion Rot;
            public int Istg;
            public ConfigNode Node;
            public RawPart Parent;
            public bool Surface;
            public readonly List<RawPart> Children = new List<RawPart>();
            public readonly List<string> SymRefs = new List<string>();
            public readonly Dictionary<string, string> NodeTo = new Dictionary<string, string>(); // own node id -> attached part ref
        }

        public static Dictionary<string, object> Read(string path, List<string> warnings)
        {
            ConfigNode root = ConfigNode.Load(path) ?? throw ApiException.NotFound("cannot read craft file " + path);
            List<RawPart> parts = root.GetNodes("PART").Select(Parse).ToList();
            if (parts.Count == 0)
            {
                throw ApiException.BadRequest("craft has no parts");
            }
            Dictionary<string, RawPart> byRef = new Dictionary<string, RawPart>();
            foreach (RawPart p in parts)
            {
                byRef[p.Ref] = p;
            }
            foreach (RawPart p in parts)
            {
                foreach (string l in p.Node.GetValues("link"))
                {
                    if (byRef.TryGetValue(l.Trim(), out RawPart c))
                    {
                        c.Parent = p;
                        p.Children.Add(c);
                    }
                }
            }
            RawPart top = parts.FirstOrDefault(p => p.Parent == null) ?? parts[0];

            HashSet<RawPart> covered = new HashSet<RawPart>();
            Dictionary<string, object> design = ToSpec(top, "root", byRef, covered, warnings);
            foreach (RawPart p in parts.Where(p => !covered.Contains(p)))
            {
                warnings.Add(p.Name + ": not in the design tree (unreachable from the root)");
            }

            RoundTrip(design, parts, top, warnings);
            return new Dictionary<string, object> { ["design"] = design, ["part_count"] = parts.Count };
        }

        private static RawPart Parse(ConfigNode n)
        {
            string full = (n.GetValue("part") ?? "").Trim();
            int i = full.LastIndexOf('_');
            RawPart p = new RawPart
            {
                Ref = full,
                Name = i > 0 ? full.Substring(0, i) : full,
                Pos = n.HasValue("pos") ? ConfigNode.ParseVector3(n.GetValue("pos")) : Vector3.zero,
                Rot = n.HasValue("rot") ? ConfigNode.ParseQuaternion(n.GetValue("rot")) : Quaternion.identity,
                Istg = int.TryParse(n.GetValue("istg"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int istg) ? istg : -1,
                Node = n,
                Surface = n.HasValue("srfN"),
            };
            foreach (string a in n.GetValues("attN"))
            {
                // "bottom,fuelTank_4293999995_0|-0.55525|0": own node id, then the attached part's ref + "_<n>|y|z" (or "Null_0|...").
                int comma = a.IndexOf(',');
                if (comma <= 0)
                {
                    continue;
                }
                string target = a.Substring(comma + 1);
                int bar = target.IndexOf('|');
                if (bar >= 0)
                {
                    target = target.Substring(0, bar);
                }
                int us = target.LastIndexOf('_');
                if (us > 0)
                {
                    target = target.Substring(0, us);
                }
                p.NodeTo[a.Substring(0, comma).Trim()] = target.Trim();
            }
            foreach (string s in n.GetValues("sym"))
            {
                p.SymRefs.Add(s.Trim());
            }
            return p;
        }

        private static Dictionary<string, object> ToSpec(RawPart p, string path, Dictionary<string, RawPart> byRef,
            HashSet<RawPart> covered, List<string> warnings)
        {
            covered.Add(p);
            Dictionary<string, object> d = new Dictionary<string, object> { ["part"] = p.Name };
            AddSettings(p, d);

            List<object> children = new List<object>();
            List<object> radial = new List<object>();
            HashSet<RawPart> done = new HashSet<RawPart>();
            foreach (RawPart c in p.Children)
            {
                if (done.Contains(c))
                {
                    continue;
                }
                done.Add(c);
                if (!c.Surface)
                {
                    string node = p.NodeTo.FirstOrDefault(kv => kv.Value == c.Ref).Key;
                    if (node == null)
                    {
                        warnings.Add(path + ": stack child " + c.Name + " has no matching attach node on " + p.Name + "; used 'bottom'");
                        node = "bottom";
                    }
                    Dictionary<string, object> cs = ToSpec(c, path + "." + c.Name, byRef, covered, warnings);
                    cs = WithFirst(cs, "node", node);
                    children.Add(cs);
                    continue;
                }

                if (p.Surface)
                {
                    // Surface child of a surface part: placed on the parent's outer face, one per parent copy (the builder repeats it).
                    Dictionary<string, object> cs = ToSpec(c, path + "." + c.Name, byRef, covered, warnings);
                    children.Add(WithFirst(cs, "node", "srf"));
                    continue;
                }

                // Radial group: this part and its symmetry peers attached to the same parent.
                List<RawPart> group = new List<RawPart> { c };
                foreach (string r in c.SymRefs)
                {
                    if (byRef.TryGetValue(r, out RawPart peer) && peer.Parent == p && !group.Contains(peer))
                    {
                        group.Add(peer);
                    }
                }
                foreach (RawPart g in group)
                {
                    done.Add(g);
                    covered.Add(g);
                }
                Dictionary<string, object> rs = ToSpec(c, path + "." + c.Name, byRef, covered, warnings);
                MarkSubtree(group.Skip(1), covered);
                Place(p, c, out double angle, out double height);
                Dictionary<string, object> entry = new Dictionary<string, object> { ["part"] = c.Name };
                if (group.Count > 1)
                {
                    entry["symmetry"] = (double)group.Count; // design numbers are doubles, as parsed from JSON
                }
                if (Math.Abs(angle) > 0.01)
                {
                    entry["angle"] = Math.Round(angle, 2);
                }
                if (Math.Abs(height) > 0.005)
                {
                    entry["height"] = Math.Round(height, 3);
                }
                foreach (KeyValuePair<string, object> kv in rs.Where(kv => kv.Key != "part"))
                {
                    entry[kv.Key] = kv.Value;
                }
                string sig = Signature(c);
                if (group.Skip(1).Any(g => Signature(g) != sig))
                {
                    warnings.Add(path + "." + c.Name + ": symmetry copies carry different parts; the first copy is used for all " + group.Count);
                }
                radial.Add(entry);
            }
            if (children.Count > 0)
            {
                d["children"] = children;
            }
            if (radial.Count > 0)
            {
                d["radial"] = radial;
            }
            return d;
        }

        private static void MarkSubtree(IEnumerable<RawPart> roots, HashSet<RawPart> covered)
        {
            foreach (RawPart r in roots)
            {
                covered.Add(r);
                MarkSubtree(r.Children, covered);
            }
        }

        private static string Signature(RawPart p) =>
            p.Name + "(" + string.Join(",", p.Children.Select(Signature).OrderBy(s => s)) + ")";

        private static Dictionary<string, object> WithFirst(Dictionary<string, object> d, string key, object value)
        {
            Dictionary<string, object> r = new Dictionary<string, object> { [key] = value };
            foreach (KeyValuePair<string, object> kv in d)
            {
                r[kv.Key] = kv.Value;
            }
            return r;
        }

        /// <summary>Inverse of CraftBuilder.AttachRadial: angle of the surface attach point around the parent (craft frame), height above the parent's mesh centre.</summary>
        private static void Place(RawPart parent, RawPart child, out double angle, out double height)
        {
            Part childPrefab = PartLoader.getPartInfoByName(child.Name)?.partPrefab;
            Part parentPrefab = PartLoader.getPartInfoByName(parent.Name)?.partPrefab;
            Vector3 anchor = child.Pos;
            if (childPrefab != null && childPrefab.srfAttachNode != null)
            {
                anchor += child.Rot * childPrefab.srfAttachNode.position;
            }
            Vector3 outward = anchor - parent.Pos;
            angle = Math.Atan2(outward.x, outward.z) * 180.0 / Math.PI;
            Vector3 local = Quaternion.Inverse(parent.Rot) * outward;
            float centre = parentPrefab != null ? CraftBuilder.LocalBounds(parentPrefab).center.y : 0f;
            height = local.y - centre;
        }

        /// <summary>stage for staged parts (engine, decoupler, parachute), thrust_limit, resources below full.</summary>
        private static void AddSettings(RawPart p, Dictionary<string, object> d)
        {
            Part prefab = PartLoader.getPartInfoByName(p.Name)?.partPrefab;
            bool staged = prefab != null && (prefab.FindModuleImplementing<ModuleEngines>() != null
                || prefab.FindModuleImplementing<ModuleDecouplerBase>() != null || prefab.FindModuleImplementing<ModuleParachute>() != null);
            if (staged && p.Istg >= 0)
            {
                d["stage"] = (double)p.Istg;
            }
            foreach (ConfigNode m in p.Node.GetNodes("MODULE"))
            {
                string name = m.GetValue("name") ?? "";
                if (name.StartsWith("ModuleEngines", StringComparison.Ordinal)
                    && float.TryParse(m.GetValue("thrustPercentage"), NumberStyles.Float, CultureInfo.InvariantCulture, out float tp) && tp < 99.95f)
                {
                    d["thrust_limit"] = Math.Round(tp, 1);
                }
            }
            Dictionary<string, object> res = new Dictionary<string, object>();
            foreach (ConfigNode r in p.Node.GetNodes("RESOURCE"))
            {
                if (double.TryParse(r.GetValue("amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
                    && double.TryParse(r.GetValue("maxAmount"), NumberStyles.Float, CultureInfo.InvariantCulture, out double max)
                    && max > 0 && amount / max < 0.999)
                {
                    res[r.GetValue("name")] = Math.Round(amount / max, 3);
                }
            }
            if (res.Count > 0)
            {
                d["resources"] = res;
            }
        }

        /// <summary>Rebuilds the design and compares each part with the nearest unmatched original of the same name (root-relative).</summary>
        private static void RoundTrip(Dictionary<string, object> design, List<RawPart> originals, RawPart top, List<string> warnings)
        {
            List<string> errors = new List<string>();
            List<string> ignored = new List<string>();
            CraftBuilder builder = new CraftBuilder(errors, ignored);
            PartSpec spec;
            try
            {
                spec = PartSpec.Parse(design, "root");
            }
            catch (ApiException e)
            {
                warnings.Add("rebuild: " + e.Message);
                return;
            }
            if (!builder.Build(spec))
            {
                warnings.AddRange(errors.Select(e => "rebuild: " + e));
                return;
            }
            BuiltPart broot = builder.Parts[0];
            Quaternion invTop = Quaternion.Inverse(top.Rot);
            Quaternion invB = Quaternion.Inverse(broot.Rot);
            List<RawPart> unused = new List<RawPart>(originals);
            foreach (BuiltPart b in builder.Parts)
            {
                Vector3 bpos = invB * (b.Pos - broot.Pos);
                RawPart best = null;
                float bestD = float.MaxValue;
                foreach (RawPart o in unused.Where(o => o.Name == b.Info.name))
                {
                    float dist = (invTop * (o.Pos - top.Pos) - bpos).magnitude;
                    if (dist < bestD)
                    {
                        bestD = dist;
                        best = o;
                    }
                }
                if (best == null)
                {
                    warnings.Add("rebuilt " + b.Info.name + " has no counterpart in the original");
                    continue;
                }
                unused.Remove(best);
                float rot = Quaternion.Angle(invTop * best.Rot, invB * b.Rot);
                if (bestD > PosTolerance || rot > RotToleranceDeg)
                {
                    warnings.Add(best.Name + " (" + best.Ref + "): rebuilt position differs " + bestD.ToString("F2", CultureInfo.InvariantCulture)
                        + " m, rotation " + rot.ToString("F1", CultureInfo.InvariantCulture) + " deg (offset/rotate edit or unsupported placement)");
                }
            }
            foreach (RawPart o in unused)
            {
                warnings.Add(o.Name + " (" + o.Ref + "): missing from the rebuilt craft");
            }
        }
    }
}
