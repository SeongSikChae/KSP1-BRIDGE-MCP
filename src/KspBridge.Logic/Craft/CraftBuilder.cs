using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Craft
{
    /// <summary>A part placed in the craft (positions in craft space, all stack parts unrotated).</summary>
    public sealed class BuiltPart
    {
        public AvailablePart Info;
        public Part Prefab => Info.partPrefab;
        public PartSpec Spec;
        public string Ref;
        public uint Pid;
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public BuiltPart Parent;
        public readonly List<BuiltPart> Children = new List<BuiltPart>();
        public readonly List<BuiltPart> SymPeers = new List<BuiltPart>();
        public bool Surface;
        public AttachNode OwnNode;      // stack: own node used to attach to the parent
        public AttachNode ParentNode;   // stack: parent's node
        public int StackDepth;          // number of stack decouplers from the root (a decoupler counts itself)
        public int Istg = -1;
        public int Dstg;
        public int Sidx = -1;

        public bool IsEngine => Prefab.FindModuleImplementing<ModuleEngines>() != null;
        public bool IsStackDecoupler => Prefab.FindModuleImplementing<ModuleDecouple>() != null && !Surface;
        public bool IsRadialDecoupler => Prefab.FindModuleImplementing<ModuleAnchoredDecoupler>() != null
                                         || (Prefab.FindModuleImplementing<ModuleDecouple>() != null && Surface);
        public bool IsDecoupler => IsStackDecoupler || IsRadialDecoupler;
        public bool IsParachute => Prefab.FindModuleImplementing<ModuleParachute>() != null;
        public bool IsStaged => IsEngine || IsDecoupler || IsParachute;

        /// <summary>Initial amount of a part RESOURCE node: the design's fill fraction × maxAmount, else the part's default.</summary>
        public double Amount(ConfigNode r)
        {
            double max = ParseNum(r.GetValue("maxAmount"));
            if (Spec.Resources != null && Spec.Resources.TryGetValue(r.GetValue("name"), out double fill))
            {
                return max * fill;
            }
            return r.HasValue("amount") ? ParseNum(r.GetValue("amount")) : max;
        }

        private static double ParseNum(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

        public IEnumerable<BuiltPart> Subtree()
        {
            yield return this;
            foreach (BuiltPart c in Children)
            {
                foreach (BuiltPart d in c.Subtree())
                {
                    yield return d;
                }
            }
        }
    }

    public sealed class CraftBuilder
    {
        private readonly List<string> _errors;
        private readonly List<string> _warnings;
        private uint _nextId = 4294000000u;
        private readonly System.Random _random = new System.Random();

        public CraftBuilder(List<string> errors, List<string> warnings)
        {
            _errors = errors;
            _warnings = warnings;
        }

        public List<BuiltPart> Parts { get; } = new List<BuiltPart>();

        /// <summary>Places all parts. Returns false when the tree has errors (positions may be partial).</summary>
        public bool Build(PartSpec root)
        {
            BuiltPart r = Create(root, "root");
            if (r == null)
            {
                return false;
            }
            r.Pos = Vector3.zero;
            Expand(r, "root");
            if (_errors.Count > 0)
            {
                return false;
            }

            // Lift so the lowest part origin sits well above the editor floor (stock crafts use ~12-15 m).
            float minY = Parts.Min(p => p.Pos.y);
            Vector3 lift = new Vector3(0, 12f - minY, 0);
            foreach (BuiltPart p in Parts)
            {
                p.Pos += lift;
            }

            AssignStages();
            return true;
        }

        private BuiltPart Create(PartSpec spec, string path)
        {
            AvailablePart info = PartLoader.getPartInfoByName(spec.Part);
            if (info == null || info.partPrefab == null)
            {
                _errors.Add(path + ": unknown part '" + spec.Part + "'");
                return null;
            }
            if (!Handlers.PartsHandler.IsUnlocked(info))
            {
                _errors.Add(path + ": part '" + spec.Part + "' is not unlocked in this career");
            }
            if (spec.ThrustLimit.HasValue && info.partPrefab.FindModuleImplementing<ModuleEngines>() == null)
            {
                _errors.Add(path + ".thrust_limit: part '" + spec.Part + "' is not an engine");
            }
            if (spec.Resources != null)
            {
                HashSet<string> has = new HashSet<string>(info.partConfig.GetNodes("RESOURCE").Select(r => r.GetValue("name")));
                foreach (string res in spec.Resources.Keys.Where(k => !has.Contains(k)))
                {
                    _errors.Add(path + ".resources." + res + ": part '" + spec.Part + "' has no such resource (has: " + string.Join(", ", has) + ")");
                }
            }
            BuiltPart bp = new BuiltPart
            {
                Info = info,
                Spec = spec,
                Pid = (uint)_random.Next(1, int.MaxValue),
            };
            bp.Ref = info.name + "_" + Fmt.U(_nextId--);
            Parts.Add(bp);
            return bp;
        }

        private void Expand(BuiltPart parent, string path)
        {
            for (int i = 0; i < parent.Spec.Children.Count; i++)
            {
                PartSpec cs = parent.Spec.Children[i];
                string cpath = path + ".children[" + i + "]";
                if (cs.Node == "srf")
                {
                    AttachOuterSurface(parent, cs, cpath);
                }
                else
                {
                    AttachStack(parent, cs, cpath);
                }
            }
            for (int i = 0; i < parent.Spec.Radial.Count; i++)
            {
                AttachRadial(parent, parent.Spec.Radial[i], path + ".radial[" + i + "]");
            }
        }

        private void AttachStack(BuiltPart parent, PartSpec cs, string path)
        {
            AttachNode pn = parent.Prefab.attachNodes.FirstOrDefault(n => n.id == cs.Node);
            if (pn == null)
            {
                _errors.Add(path + ": parent '" + parent.Info.name + "' has no node '" + cs.Node + "' (has: "
                    + string.Join(", ", parent.Prefab.attachNodes.Select(n => n.id).ToArray()) + ")");
                return;
            }
            if (parent.Children.Any(c => c.ParentNode != null && c.ParentNode.id == cs.Node))
            {
                _errors.Add(path + ": node '" + cs.Node + "' of '" + parent.Info.name + "' is already used");
                return;
            }

            BuiltPart child = Create(cs, path);
            if (child == null)
            {
                return;
            }
            string want = cs.Node.StartsWith("bottom", StringComparison.Ordinal) ? "top" : "bottom";
            AttachNode cn = child.Prefab.attachNodes.FirstOrDefault(n => n.id == want)
                ?? child.Prefab.attachNodes.FirstOrDefault(n => n.id.StartsWith(want, StringComparison.Ordinal));
            if (cn == null)
            {
                _errors.Add(path + ": part '" + child.Info.name + "' has no '" + want + "' node to attach with");
                return;
            }
            if (cn.size != pn.size)
            {
                _warnings.Add(path + ": node size mismatch (" + parent.Info.name + "." + pn.id + "=" + pn.size
                    + ", " + child.Info.name + "." + cn.id + "=" + cn.size + ")");
            }

            child.Parent = parent;
            child.ParentNode = pn;
            child.OwnNode = cn;
            child.Rot = parent.Rot;
            child.Pos = parent.Pos + parent.Rot * pn.position - child.Rot * cn.position;
            parent.Children.Add(child);
            Expand(child, path);
        }

        /// <summary>Surface-attaches symmetry copies around the parent at its collider radius.</summary>
        private void AttachRadial(BuiltPart parent, PartSpec rs, string path)
        {
            Bounds b = LocalBounds(parent.Prefab);
            float height = b.center.y + rs.Height;
            List<BuiltPart> copies = new List<BuiltPart>();
            for (int i = 0; i < rs.Symmetry; i++)
            {
                BuiltPart child = Create(rs, path + "#" + i);
                if (child == null)
                {
                    return;
                }
                if (child.Prefab.srfAttachNode == null)
                {
                    _errors.Add(path + ": part '" + child.Info.name + "' cannot be surface-attached");
                    return;
                }
                float ang = (rs.Angle + 360f * i / rs.Symmetry) * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
                float radius = SurfaceRadius(parent.Prefab, height, Quaternion.Inverse(parent.Rot) * outward)
                    ?? Mathf.Max(b.extents.x, b.extents.z);
                PlaceOnSurface(parent, child, outward, radius, height);
                copies.Add(child);
            }
            foreach (BuiltPart c in copies)
            {
                c.SymPeers.AddRange(copies.Where(o => o != c));
                Expand(c, path);
            }
        }

        /// <summary>"srf" child of a part: surface-attached on the parent's outer face (the side away from the craft axis).</summary>
        private void AttachOuterSurface(BuiltPart parent, PartSpec cs, string path)
        {
            BuiltPart child = Create(cs, path);
            if (child == null)
            {
                return;
            }
            if (child.Prefab.srfAttachNode == null)
            {
                _errors.Add(path + ": part '" + child.Info.name + "' cannot be surface-attached");
                return;
            }
            Vector3 outward = new Vector3(parent.Pos.x, 0, parent.Pos.z);
            outward = outward.sqrMagnitude < 1e-6f ? Vector3.forward : outward.normalized;
            Bounds b = LocalBounds(parent.Prefab);
            Vector3 localOut = Quaternion.Inverse(parent.Rot) * outward;
            float height = b.center.y + cs.Height;
            float? meshOffset = SurfaceDistance(parent.Prefab, height, localOut);
            float extent = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(localOut.x), Mathf.Abs(localOut.y), Mathf.Abs(localOut.z))));
            float offset = meshOffset ?? (Vector3.Dot(b.center, localOut) + extent);
            PlaceOnSurface(parent, child, outward, offset, height);
            Expand(child, path);
        }

        private static void PlaceOnSurface(BuiltPart parent, BuiltPart child, Vector3 outward, float distance, float height)
        {
            AttachNode srf = child.Prefab.srfAttachNode;
            // Attach side = direction of the node position from the part centre (SRBs define position -z with orientation +z);
            // fall back to the orientation when the node sits at the centre (e.g. thermometer).
            Vector3 o = srf.position.sqrMagnitude > 1e-6f ? srf.position.normalized
                : srf.orientation.sqrMagnitude > 1e-6f ? srf.orientation.normalized : Vector3.forward;
            // Rotate so the node's normal points back at the parent and the part keeps "up" vertical.
            Quaternion rot = Quaternion.LookRotation(-outward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(o, Vector3.up));
            child.Surface = true;
            child.Parent = parent;
            child.Rot = rot;
            Vector3 anchor = parent.Pos + parent.Rot * new Vector3(0, height, 0) + outward * distance;
            child.Pos = anchor - rot * srf.position;
            parent.Children.Add(child);
        }

        /// <summary>
        /// Horizontal distance from the part axis to its mesh surface at the given local height, in the given local direction
        /// (vertices within ±25° of the direction and a height band). Null when no mesh data is readable.
        /// </summary>
        private static float? SurfaceRadius(Part prefab, float height, Vector3 localDir)
        {
            Vector3 dir = new Vector3(localDir.x, 0, localDir.z);
            if (dir.sqrMagnitude < 1e-6f)
            {
                return null;
            }
            dir.Normalize();
            float cosLimit = Mathf.Cos(25f * Mathf.Deg2Rad);
            // Nearest vertex layer above and below the height (largest distance per layer), then interpolate by height:
            // taking only the vertices of one side picks the narrow end of a cone (e.g. Mk1 pod) and buries the part.
            float upY = float.MaxValue, upD = -1, downY = float.MinValue, downD = -1;
            const float layer = 0.01f;
            foreach (Vector3 p in LocalVertices(prefab))
            {
                if (Mathf.Abs(p.y - height) > 0.4f)
                {
                    continue;
                }
                Vector3 h = new Vector3(p.x, 0, p.z);
                float r = h.magnitude;
                if (r <= 1e-3f || Vector3.Dot(h / r, dir) < cosLimit)
                {
                    continue;
                }
                float d = Vector3.Dot(h, dir);
                if (p.y >= height)
                {
                    if (p.y < upY - layer) { upY = p.y; upD = d; }
                    else if (p.y <= upY + layer) { upD = Mathf.Max(upD, d); }
                }
                if (p.y <= height)
                {
                    if (p.y > downY + layer) { downY = p.y; downD = d; }
                    else if (p.y >= downY - layer) { downD = Mathf.Max(downD, d); }
                }
            }
            if (upD > 0 && downD > 0)
            {
                float span = upY - downY;
                return span < 1e-4f ? Mathf.Max(upD, downD) : Mathf.Lerp(downD, upD, (height - downY) / span);
            }
            if (upD > 0 || downD > 0)
            {
                return Mathf.Max(upD, downD);
            }
            return null;
        }

        /// <summary>Distance from the part origin to its mesh surface along a local direction (any direction).</summary>
        private static float? SurfaceDistance(Part prefab, float height, Vector3 localDir)
        {
            float? r = SurfaceRadius(prefab, height, localDir);
            if (r != null)
            {
                return r;
            }
            float best = float.MinValue;
            foreach (Vector3 p in LocalVertices(prefab))
            {
                best = Mathf.Max(best, Vector3.Dot(p, localDir.normalized));
            }
            return best == float.MinValue ? (float?)null : best;
        }

        private static IEnumerable<Vector3> LocalVertices(Part prefab)
        {
            Matrix4x4 toPart = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }
                Matrix4x4 m = toPart * mf.transform.localToWorldMatrix;
                Vector3[] verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    yield return m.MultiplyPoint3x4(verts[i]);
                }
            }
        }

        private static Bounds LocalBounds(Part prefab)
        {
            bool any = false;
            Bounds mesh = new Bounds();
            foreach (Vector3 p in LocalVertices(prefab))
            {
                if (!any)
                {
                    mesh = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    mesh.Encapsulate(p);
                }
            }
            if (any && mesh.extents.sqrMagnitude > 1e-6f)
            {
                return mesh;
            }

            try
            {
                Bounds[] all = PartGeometryUtil.GetPartColliderBounds(prefab);
                if (all != null && all.Length > 0)
                {
                    Bounds merged = PartGeometryUtil.MergeBounds(all, prefab.transform);
                    if (merged.extents.sqrMagnitude > 1e-6f)
                    {
                        return merged;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] bounds for " + prefab.partInfo?.name + ": " + e.Message);
            }
            // Fallback: radius from bulkhead size (size0=0.625, size1=1.25, size2=2.5 ...)
            float d = prefab.attachNodes.Count > 0 ? Mathf.Max(0.625f, prefab.attachNodes.Max(n => n.size) * 1.25f) : 1.25f;
            return new Bounds(Vector3.zero, new Vector3(d, d, d));
        }

        /// <summary>
        /// Automatic staging, bottom first: for each stack level (deepest first) ignite its engines (core + radial boosters)
        /// together with the stack decouplers that drop the level below; then fire the radial decouplers of that level.
        /// Parachutes go last. Parts with an explicit "stage" keep it.
        /// </summary>
        private void AssignStages()
        {
            BuiltPart root = Parts[0];
            SetDepth(root, 0);

            int maxDepth = Parts.Max(p => p.StackDepth);
            List<List<BuiltPart>> events = new List<List<BuiltPart>>();
            for (int sd = maxDepth; sd >= 0; sd--)
            {
                List<BuiltPart> ignite = Parts.Where(p => p.Spec.Stage == null &&
                    ((p.IsEngine && !p.IsDecoupler && p.StackDepth == sd) || (p.IsStackDecoupler && p.StackDepth == sd + 1))).ToList();
                if (ignite.Count > 0)
                {
                    events.Add(ignite);
                }
                List<BuiltPart> radial = Parts.Where(p => p.Spec.Stage == null && p.IsRadialDecoupler && p.StackDepth == sd).ToList();
                if (radial.Count > 0)
                {
                    events.Add(radial);
                }
            }
            List<BuiltPart> chutes = Parts.Where(p => p.Spec.Stage == null && p.IsParachute && !p.IsDecoupler).ToList();
            if (chutes.Count > 0)
            {
                events.Add(chutes);
            }

            int n = events.Count;
            for (int k = 0; k < n; k++)
            {
                int istg = n - 1 - k;
                for (int i = 0; i < events[k].Count; i++)
                {
                    events[k][i].Istg = istg;
                    events[k][i].Sidx = i;
                }
            }
            foreach (BuiltPart p in Parts.Where(p => p.Spec.Stage != null))
            {
                p.Istg = p.Spec.Stage.Value;
                p.Sidx = 0;
            }

            // Non-staged parts inherit the parent's stage; dstg = stage of the decoupler that drops the part.
            Propagate(root, -1, 0);
        }

        private static void SetDepth(BuiltPart p, int parentDepth)
        {
            p.StackDepth = parentDepth + (p.IsStackDecoupler ? 1 : 0);
            foreach (BuiltPart c in p.Children)
            {
                SetDepth(c, p.StackDepth);
            }
        }

        private static void Propagate(BuiltPart p, int parentIstg, int dropStage)
        {
            if (p.IsDecoupler && p.Istg >= 0)
            {
                dropStage = p.Istg;
            }
            if (!p.IsStaged || p.Istg < 0)
            {
                p.Istg = parentIstg;
                p.Sidx = -1;
            }
            p.Dstg = dropStage;
            foreach (BuiltPart c in p.Children)
            {
                Propagate(c, p.Istg, dropStage);
            }
        }

        public ConfigNode ToConfigNode(string name, string facility)
        {
            ConfigNode ship = new ConfigNode("SHIP");
            ship.AddValue("ship", name);
            ship.AddValue("version", Versioning.GetVersionString());
            ship.AddValue("description", "Generated by KspBridge");
            ship.AddValue("type", facility);
            ship.AddValue("size", Fmt.V(Size()));
            ship.AddValue("persistentId", Fmt.U((uint)_random.Next(1, int.MaxValue)));
            ship.AddValue("rot", "0,0,0,1");
            ship.AddValue("missionFlag", HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flagURL : "Squad/Flags/default");
            ship.AddValue("vesselType", "Ship");

            foreach (BuiltPart p in Parts)
            {
                ConfigNode n = ship.AddNode("PART");
                Vector3 attPos0 = p.Parent == null ? p.Pos : Quaternion.Inverse(p.Parent.Rot) * (p.Pos - p.Parent.Pos);
                Quaternion attRot0 = p.Parent == null ? p.Rot : Quaternion.Inverse(p.Parent.Rot) * p.Rot;
                n.AddValue("part", p.Ref);
                n.AddValue("partName", "Part");
                n.AddValue("persistentId", Fmt.U(p.Pid));
                n.AddValue("pos", Fmt.V(p.Pos));
                n.AddValue("attPos", "0,0,0");
                n.AddValue("attPos0", Fmt.V(attPos0));
                n.AddValue("rot", Fmt.Q(p.Rot));
                n.AddValue("attRot", "0,0,0,1");
                n.AddValue("attRot0", Fmt.Q(attRot0));
                n.AddValue("mir", "1,1,1");
                n.AddValue("symMethod", "Radial");
                n.AddValue("autostrutMode", "Off");
                n.AddValue("rigidAttachment", "False");
                n.AddValue("istg", Fmt.I(p.Istg));
                n.AddValue("resPri", "0");
                n.AddValue("dstg", Fmt.I(p.Dstg));
                n.AddValue("sidx", Fmt.I(p.Sidx));
                n.AddValue("sqor", Fmt.I(p.Sidx >= 0 ? p.Istg : -1));
                n.AddValue("sepI", "-1");
                n.AddValue("attm", p.Surface ? "1" : "0");
                n.AddValue("modCost", "0");
                n.AddValue("modMass", "0");
                n.AddValue("modSize", "0,0,0");
                foreach (BuiltPart c in p.Children)
                {
                    n.AddValue("link", c.Ref);
                }
                foreach (BuiltPart s in p.SymPeers)
                {
                    n.AddValue("sym", s.Ref);
                }
                if (p.Parent != null && !p.Surface)
                {
                    n.AddValue("attN", p.OwnNode.id + "," + p.Parent.Ref + "_0|" + Fmt.F(p.OwnNode.position.y) + "|0");
                }
                foreach (BuiltPart c in p.Children.Where(c => !c.Surface))
                {
                    n.AddValue("attN", c.ParentNode.id + "," + c.Ref + "_0|" + Fmt.F(c.ParentNode.position.y) + "|0");
                }
                if (p.Surface)
                {
                    n.AddValue("srfN", "srfAttach," + p.Parent.Ref);
                }
                n.AddNode("EVENTS");
                n.AddNode("ACTIONS");
                n.AddNode("PARTDATA");
                // MODULE nodes are omitted (the loader keeps the prefab's module defaults), except the engine's
                // thrust limiter when the design sets one; the loader finds the module by name.
                if (p.Spec.ThrustLimit.HasValue)
                {
                    ModuleEngines engine = p.Prefab.FindModuleImplementing<ModuleEngines>();
                    ConfigNode m = n.AddNode("MODULE");
                    m.AddValue("name", engine.moduleName);
                    m.AddValue("isEnabled", "True");
                    m.AddValue("thrustPercentage", Fmt.F(p.Spec.ThrustLimit.Value));
                }
                // A decoupler given a stage whose staging is off by default (e.g. heat shield jettison): turn staging on,
                // otherwise the game drops its stage as empty and renumbers the stages above it.
                ModuleDecouplerBase decoupler = p.IsDecoupler && p.Sidx >= 0 ? p.Prefab.FindModuleImplementing<ModuleDecouplerBase>() : null;
                if (decoupler != null && !decoupler.stagingEnabled)
                {
                    ConfigNode m = n.AddNode("MODULE");
                    m.AddValue("name", decoupler.moduleName);
                    m.AddValue("isEnabled", "True");
                    m.AddValue("stagingEnabled", "True");
                }
                foreach (ConfigNode r in p.Info.partConfig.GetNodes("RESOURCE"))
                {
                    ConfigNode rn = n.AddNode("RESOURCE");
                    rn.AddValue("name", r.GetValue("name"));
                    string max = r.GetValue("maxAmount");
                    rn.AddValue("amount", p.Amount(r).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    rn.AddValue("maxAmount", max);
                    rn.AddValue("flowState", "True");
                    rn.AddValue("isTweakable", "True");
                    rn.AddValue("hideFlow", "False");
                    rn.AddValue("isVisible", "True");
                    rn.AddValue("flowMode", "Both");
                }
            }
            return ship;
        }

        private Vector3 Size()
        {
            float minX = Parts.Min(p => p.Pos.x), maxX = Parts.Max(p => p.Pos.x);
            float minY = Parts.Min(p => p.Pos.y), maxY = Parts.Max(p => p.Pos.y);
            float minZ = Parts.Min(p => p.Pos.z), maxZ = Parts.Max(p => p.Pos.z);
            return new Vector3(maxX - minX + 1.25f, maxY - minY + 1.25f, maxZ - minZ + 1.25f);
        }
    }
}
