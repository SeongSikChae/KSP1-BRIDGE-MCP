using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>Attach rules of a part kind (from the part's prefab).</summary>
    public sealed class PartRules
    {
        /// <summary>The part can be the root.</summary>
        public bool AllowRoot { get; set; } = true;

        /// <summary>The part can be surface-attached to another (srfAttach rule and a surface node).</summary>
        public bool CanSurfaceAttach { get; set; }

        /// <summary>Other parts may be surface-attached to this one (allowSrfAttach).</summary>
        public bool AcceptsSurface { get; set; } = true;
    }

    /// <summary>A part kind to add: its PART node with the game's defaults, rules and surface node.</summary>
    public sealed class PartTemplate
    {
        public string Name { get; set; }
        public CfgNode Node { get; set; }
        public PartRules Rules { get; set; } = new PartRules();

        /// <summary>The surface attach node in part space, or null when the part has none.</summary>
        public NodeDef? SurfaceNode { get; set; }
    }

    /// <summary>One surface point to attach to (world space).</summary>
    public readonly struct SurfaceHit
    {
        public SurfaceHit(Vec3 point, Vec3 normal)
        {
            Point = point;
            Normal = normal;
        }

        public Vec3 Point { get; }
        public Vec3 Normal { get; }
    }

    /// <summary>The attach relations of a part as editor_get_ship shows them (compared before/after and after a reload).</summary>
    public sealed class PartRelation : IEquatable<PartRelation>
    {
        public string Name { get; set; }
        public uint? Parent { get; set; }
        public string Attach { get; set; }
        public string ParentNode { get; set; }
        public string Node { get; set; }
        public List<uint> Symmetry { get; set; } = new List<uint>();

        public bool Equals(PartRelation other)
            => other != null && Name == other.Name && Parent == other.Parent && Attach == other.Attach
               && ParentNode == other.ParentNode && Node == other.Node
               && Symmetry.OrderBy(x => x).SequenceEqual(other.Symmetry.OrderBy(x => x));

        public override bool Equals(object obj) => Equals(obj as PartRelation);

        public override int GetHashCode() => (Name ?? "").GetHashCode() ^ (Parent?.GetHashCode() ?? 0);

        public override string ToString()
            => Name + " parent=" + (Parent?.ToString() ?? "none") + " attach=" + (Attach ?? "-") + " parent_node=" + (ParentNode ?? "-")
               + " node=" + (Node ?? "-") + " symmetry=[" + string.Join(",", Symmetry.OrderBy(x => x).Select(x => x.ToString()).ToArray()) + "]";
    }

    /// <summary>What an edit did: parts added, parts removed, parts the request itself changed.</summary>
    public sealed class EditOutcome
    {
        public List<uint> Added { get; } = new List<uint>();
        public List<uint> Removed { get; } = new List<uint>();

        /// <summary>Parts the request moved, re-staged or otherwise changed directly (with symmetry partners).</summary>
        public HashSet<uint> Touched { get; } = new HashSet<uint>();

        /// <summary>New parts the request put into an explicitly given stage (refused if the game does not stage them).</summary>
        public HashSet<uint> StageRequested { get; } = new HashSet<uint>();
    }

    /// <summary>How a failed edit is undone: by reloading the original craft, or (no parts) by reopening an empty editor.</summary>
    public enum RollbackMode
    {
        ReloadOriginal,
        ReopenEmpty,
    }

    /// <summary>
    /// KSP-independent editing model of a craft node (the SaveShip format). Parts are PART nodes addressed by craftID
    /// ("part = name_cid"); parent → child is "link", partners are "sym", stack attachments are "attN" on both sides and a
    /// surface attachment is the child's "srfN". Values and child nodes the model does not use are kept as they are.
    /// Every operation checks everything first and only then changes the node, so a refused request leaves it untouched;
    /// each ends by normalizing the stages of staged parts.
    /// </summary>
    public sealed class CraftDoc
    {
        private readonly Func<string, PartRules> rules;
        private readonly Dictionary<uint, bool> staged;

        /// <param name="craft">The craft root node (edited in place).</param>
        /// <param name="rules">Attach rules of a part kind by name.</param>
        /// <param name="staged">Whether each existing part (by craftID) is a staged part, as the game judges it.</param>
        public CraftDoc(CfgNode craft, Func<string, PartRules> rules, IDictionary<uint, bool> staged)
        {
            Craft = craft;
            this.rules = rules;
            this.staged = new Dictionary<uint, bool>(staged);
        }

        public CfgNode Craft { get; }

        public List<CfgNode> PartNodes => Craft.GetNodes("PART");

        public int PartCount => PartNodes.Count;

        // ---- reading ----

        public static uint IdOf(CfgNode part) => SplitRef(part.GetValue("part")).id;

        public static string NameOf(CfgNode part) => SplitRef(part.GetValue("part")).name;

        /// <summary>"name_cid" → (name, cid).</summary>
        public static (string name, uint id) SplitRef(string value)
        {
            if (value == null)
                throw BridgeException.BadRequest("a PART has no part value");
            int at = value.LastIndexOf('_');
            if (at <= 0 || !uint.TryParse(value.Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                throw BridgeException.BadRequest("bad part reference '" + value + "'");
            return (value.Substring(0, at), id);
        }

        private static string Ref(string name, uint id) => name + "_" + id.ToString(CultureInfo.InvariantCulture);

        public CfgNode Find(uint id) => PartNodes.FirstOrDefault(p => IdOf(p) == id);

        private CfgNode Require(uint id)
            => Find(id) ?? throw BridgeException.BadRequest("no part with id " + id + " in the ship; see editor_get_ship");

        public static List<uint> Links(CfgNode part) => part.GetValues("link").Select(v => SplitRef(v).id).ToList();

        public static List<uint> Syms(CfgNode part) => part.GetValues("sym").Select(v => SplitRef(v).id).ToList();

        public CfgNode ParentOf(CfgNode part)
        {
            uint id = IdOf(part);
            return PartNodes.FirstOrDefault(p => Links(p).Contains(id));
        }

        public CfgNode Root => PartNodes.FirstOrDefault(p => ParentOf(p) == null);

        public static Pose PoseOf(CfgNode part)
            => new Pose(Geo.ParseVec(part.GetValue("pos"), "pos"), Geo.ParseQuat(part.GetValue("rot"), "rot"));

        private static void SetPose(CfgNode part, Pose pose)
        {
            part.SetValue("pos", Geo.Format(pose.Position));
            part.SetValue("rot", Geo.Format(pose.Rotation.Normalized));
        }

        public static bool IsSurface(CfgNode part) => part.GetValue("attm") == "1";

        private static int Int(CfgNode part, string key, int fallback)
            => int.TryParse(part.GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        /// <summary>One "attN" value: node id, attached part (0 = none), and the node's four vectors kept as text.</summary>
        public sealed class AttN
        {
            public string NodeId;
            public string PartName;
            public uint PartId;
            public string OriginalPosition;
            public string OriginalOrientation;
            public string Position;
            public string Orientation;

            public static AttN Parse(string value)
            {
                int comma = value.IndexOf(',');
                if (comma <= 0)
                    throw BridgeException.BadRequest("bad attN '" + value + "'");
                string[] parts = value.Substring(comma + 1).Split('_');
                if (parts.Length != 6 || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                    throw BridgeException.BadRequest("bad attN '" + value + "'");
                return new AttN
                {
                    NodeId = value.Substring(0, comma),
                    PartName = parts[0],
                    PartId = id,
                    OriginalPosition = parts[2],
                    OriginalOrientation = parts[3],
                    Position = parts[4],
                    Orientation = parts[5],
                };
            }

            public override string ToString()
                => NodeId + "," + (PartId == 0 ? "Null" : PartName) + "_" + PartId.ToString(CultureInfo.InvariantCulture) + "_"
                   + OriginalPosition + "_" + OriginalOrientation + "_" + Position + "_" + Orientation;

            public NodeDef Def => new NodeDef(NodeId, Geo.ParseVec(Position, "attN position"), Geo.ParseVec(Orientation, "attN orientation"));
        }

        public static List<AttN> AttNs(CfgNode part) => part.GetValues("attN").Select(AttN.Parse).ToList();

        private static void SetAttNs(CfgNode part, IEnumerable<AttN> nodes) => part.SetValues("attN", nodes.Select(n => n.ToString()));

        /// <summary>The "srfN" value: node id, parent part, mesh name and the node vectors kept as text.</summary>
        public sealed class SrfN
        {
            public string NodeId;
            public string PartName;
            public uint PartId;
            public string Mesh;
            public string Rest;

            public static SrfN Parse(string value)
            {
                string[] f = value.Split(new[] { ',' }, 4);
                if (f.Length < 4)
                    throw BridgeException.BadRequest("bad srfN '" + value + "'");
                (string name, uint id) = SplitRef(f[1]);
                return new SrfN { NodeId = f[0], PartName = name, PartId = id, Mesh = f[2], Rest = f[3] };
            }

            public override string ToString() => NodeId + "," + Ref(PartName, PartId) + "," + Mesh + "," + Rest;
        }

        public static SrfN SrfNOf(CfgNode part)
        {
            string value = part.GetValue("srfN");
            return value != null ? SrfN.Parse(value) : null;
        }

        // ---- relations, as editor_get_ship shows them ----

        public Dictionary<uint, PartRelation> Relations()
        {
            Dictionary<uint, PartRelation> result = new Dictionary<uint, PartRelation>();
            foreach (CfgNode part in PartNodes)
            {
                uint id = IdOf(part);
                CfgNode parent = ParentOf(part);
                PartRelation r = new PartRelation { Name = NameOf(part), Symmetry = Syms(part) };
                if (parent != null)
                {
                    uint parentId = IdOf(parent);
                    r.Parent = parentId;
                    if (IsSurface(part))
                    {
                        r.Attach = "surface";
                        r.Node = SrfNOf(part)?.NodeId;
                    }
                    else
                    {
                        r.Attach = "stack";
                        r.ParentNode = AttNs(parent).FirstOrDefault(n => n.PartId == id)?.NodeId;
                        r.Node = AttNs(part).FirstOrDefault(n => n.PartId == parentId)?.NodeId;
                    }
                }
                result[id] = r;
            }
            return result;
        }

        /// <summary>
        /// Parts whose relations differ between <paramref name="before"/> and <paramref name="after"/>, plus added and touched
        /// parts; removed parts are left out. Stage renumbering alone does not count (stages are not in the relations).
        /// </summary>
        public static List<uint> Changed(Dictionary<uint, PartRelation> before, Dictionary<uint, PartRelation> after, EditOutcome outcome)
        {
            HashSet<uint> changed = new HashSet<uint>(outcome.Added);
            changed.UnionWith(outcome.Touched);
            foreach (KeyValuePair<uint, PartRelation> pair in after)
            {
                if (!before.TryGetValue(pair.Key, out PartRelation old) || !old.Equals(pair.Value))
                    changed.Add(pair.Key);
            }
            changed.ExceptWith(outcome.Removed);
            changed.IntersectWith(after.Keys);
            return changed.OrderBy(x => x).ToList();
        }

        // ---- stages ----

        public bool IsStaged(uint id) => staged.TryGetValue(id, out bool s) && s;

        /// <summary>Stage entries of the staged parts (symmetry group = smallest id of the part and its partners).</summary>
        public List<StageEntry> StageEntries()
        {
            List<StageEntry> entries = new List<StageEntry>();
            foreach (CfgNode part in PartNodes)
            {
                uint id = IdOf(part);
                if (!IsStaged(id))
                    continue;
                uint group = Syms(part).Where(IsStaged).Concat(new[] { id }).Min();
                entries.Add(new StageEntry(id, Int(part, "istg", 0), Int(part, "sidx", 0), group));
            }
            return entries;
        }

        private void WriteStages(IEnumerable<StageEntry> entries)
        {
            foreach (StageEntry e in entries)
            {
                CfgNode part = Find(e.Id);
                part.SetValue("istg", e.Stage.ToString(CultureInfo.InvariantCulture));
                part.SetValue("sidx", e.Index.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void NormalizeStages()
        {
            List<StageEntry> entries = StageEntries();
            Staging.Normalize(entries);
            WriteStages(entries);
        }

        // ---- integrity (checked before the craft is written; the game's loader shows a dialog on a broken link) ----

        /// <summary>Why the craft cannot be loaded safely, or null.</summary>
        public string IntegrityError()
        {
            List<CfgNode> parts = PartNodes;
            if (parts.Count == 0)
                return null;
            HashSet<uint> ids = new HashSet<uint>();
            foreach (CfgNode p in parts)
            {
                if (!ids.Add(IdOf(p)))
                    return "duplicate part id " + IdOf(p);
            }
            Dictionary<uint, int> parents = new Dictionary<uint, int>();
            foreach (CfgNode p in parts)
            {
                uint id = IdOf(p);
                foreach (uint link in Links(p))
                {
                    if (!ids.Contains(link))
                        return "part " + id + " links to missing part " + link;
                    parents[link] = parents.TryGetValue(link, out int n) ? n + 1 : 1;
                }
                foreach (uint sym in Syms(p))
                {
                    if (!ids.Contains(sym))
                        return "part " + id + " has missing symmetry partner " + sym;
                    if (!Syms(Find(sym)).Contains(id))
                        return "symmetry between " + id + " and " + sym + " is one-sided";
                }
                foreach (AttN n in AttNs(p))
                {
                    if (n.PartId != 0 && !ids.Contains(n.PartId))
                        return "part " + id + " node " + n.NodeId + " points to missing part " + n.PartId;
                }
                SrfN s = SrfNOf(p);
                if (s != null && s.PartId != 0 && !ids.Contains(s.PartId))
                    return "part " + id + " surface node points to missing part " + s.PartId;
            }
            if (parents.Any(pair => pair.Value > 1))
                return "part " + parents.First(pair => pair.Value > 1).Key + " has more than one parent";
            List<CfgNode> roots = parts.Where(p => !parents.ContainsKey(IdOf(p))).ToList();
            if (roots.Count != 1)
                return "the ship must have exactly one root part (found " + roots.Count + ")";
            if (roots[0] != parts[0])
                return "the root part must be the first part";
            return Staging.NormalizationError(StageEntries());
        }

        public void RequireIntegrity()
        {
            string error = IntegrityError();
            if (error != null)
                throw BridgeException.BadRequest("the edited ship would be broken (" + error + "); nothing was changed");
        }

        /// <summary>How to undo an edit of a craft that had <paramref name="originalPartCount"/> parts.</summary>
        public static RollbackMode RollbackFor(int originalPartCount)
            => originalPartCount == 0 ? RollbackMode.ReopenEmpty : RollbackMode.ReloadOriginal;

        // ---- new parts ----

        /// <summary>
        /// An id above every id in the ship (from 100001 up to the game's own range), so an id the Planner saw removed is not
        /// handed to a different part right after; wraps to the smallest free id only when the top of the range is used.
        /// </summary>
        private uint NewId(HashSet<uint> taken)
        {
            uint top = taken.Count == 0 ? 100000 : taken.Max();
            uint id = top < uint.MaxValue && top >= 100000 ? top + 1 : 100001;
            while (taken.Contains(id))
                id++;
            taken.Add(id);
            return id;
        }

        private HashSet<uint> TakenIds() => new HashSet<uint>(PartNodes.Select(IdOf));

        private static CfgNode NewPart(PartTemplate template, uint id, Pose pose)
        {
            CfgNode node = template.Node.Clone();
            node.SetValue("part", Ref(template.Name, id));
            SetPose(node, pose);
            node.SetValue("attPos", "0,0,0");
            node.SetValue("attPos0", Geo.Format(pose.Position));
            node.SetValue("attRot", "0,0,0,1");
            node.SetValue("attRot0", Geo.Format(pose.Rotation.Normalized));
            node.RemoveValues("link");
            node.RemoveValues("sym");
            node.RemoveValues("srfN");
            return node;
        }

        private static List<NodeDef> NodesOf(CfgNode part) => AttNs(part).Select(n => n.Def).ToList();

        private void AddLink(CfgNode parent, CfgNode child) => parent.AddValue("link", Ref(NameOf(child), IdOf(child)));

        private static void SetSyms(IList<CfgNode> group)
        {
            foreach (CfgNode p in group)
                p.SetValues("sym", group.Where(o => o != p).Select(o => Ref(NameOf(o), IdOf(o))));
        }

        /// <summary>
        /// Stage new parts are written into: the requested one (existing or highest + 1) or the new first stage. Whether a
        /// new part is staged at all is decided by the game when it starts the part (modules set the staging icon in
        /// OnStart), so every new part is written as possibly staged and the settled ship is compared on the parts the game
        /// staged (Staging.StageMismatch).
        /// </summary>
        private int StageForNew(double? stage) => Staging.RequireTarget(StageEntries(), stage);

        private void PutInStage(IEnumerable<CfgNode> parts, int stage)
        {
            List<CfgNode> list = parts.ToList();
            foreach (CfgNode p in list)
                staged[IdOf(p)] = true;
            List<StageEntry> entries = StageEntries();
            Staging.Move(entries, list.Select(IdOf).ToList(), stage);
            WriteStages(entries);
            foreach (CfgNode p in list)
                p.SetValue("dstg", stage.ToString(CultureInfo.InvariantCulture));
        }

        // ---- operations ----

        /// <summary>The first part of an empty ship, at <paramref name="position"/> facing up.</summary>
        public EditOutcome PlaceRoot(PartTemplate template, Vec3 position, double? stage)
        {
            if (PartCount != 0)
                throw BridgeException.BadRequest("the ship already has parts; a root can only be placed in an empty editor (editor_new), or use editor_set_root");
            if (!template.Rules.AllowRoot)
                throw BridgeException.BadRequest("part '" + template.Name + "' cannot be the root part");
            int target = StageForNew(stage);

            EditOutcome outcome = new EditOutcome();
            CfgNode part = NewPart(template, NewId(TakenIds()), new Pose(position, Quat.Identity));
            part.SetValue("attm", "0");
            Craft.AddNode(part);
            outcome.Added.Add(IdOf(part));
            PutInStage(new[] { part }, target);
            if (stage.HasValue)
                outcome.StageRequested.Add(IdOf(part));
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>
        /// Stack-attaches a new part to node <paramref name="targetNode"/> of <paramref name="targetId"/>, and to the same node
        /// of each symmetry partner; the new parts form one symmetry group with the target's method and mirror vectors.
        /// </summary>
        public EditOutcome AttachNode(uint targetId, string targetNode, PartTemplate template, string childNode, double? stage)
        {
            CfgNode target = Require(targetId);
            List<CfgNode> group = new[] { target }.Concat(Syms(target).Select(Require)).ToList();
            List<NodeDef> childNodes = NodesOf(template.Node);
            if (childNodes.Count == 0)
                throw BridgeException.BadRequest("part '" + template.Name + "' has no attach nodes; use editor_attach_surface");

            List<AttN> targetNodes = AttNs(target);
            AttN tn = targetNodes.FirstOrDefault(n => n.NodeId == targetNode);
            if (tn == null)
                throw BridgeException.BadRequest("part " + targetId + " has no node '" + targetNode + "' (nodes: "
                    + (targetNodes.Count == 0 ? "none" : string.Join(", ", targetNodes.Select(n => n.NodeId).ToArray())) + ")");
            NodeDef pick;
            if (childNode != null)
            {
                NodeDef? named = childNodes.Cast<NodeDef?>().FirstOrDefault(n => n.Value.Id == childNode);
                if (!named.HasValue)
                    throw BridgeException.BadRequest("part '" + template.Name + "' has no node '" + childNode + "' (nodes: "
                        + string.Join(", ", childNodes.Select(n => n.Id).ToArray()) + ")");
                pick = named.Value;
            }
            else
            {
                NodeDef? auto = AttachMath.PickChildNode(tn.Def, childNodes);
                if (!auto.HasValue)
                    throw BridgeException.BadRequest("no node of part '" + template.Name + "' fits node '" + targetNode + "'; pass node");
                pick = auto.Value;
            }
            foreach (CfgNode member in group)
            {
                AttN mn = AttNs(member).FirstOrDefault(n => n.NodeId == targetNode);
                if (mn == null)
                    throw BridgeException.BadRequest("symmetry partner " + IdOf(member) + " has no node '" + targetNode + "'");
                if (mn.PartId != 0)
                    throw BridgeException.BadRequest("node '" + targetNode + "' of part " + IdOf(member) + " is already used by part " + mn.PartId);
            }
            int stageTarget = StageForNew(stage);

            EditOutcome outcome = new EditOutcome();
            HashSet<uint> taken = TakenIds();
            string method = target.GetValue("symMethod") ?? "Radial";
            List<CfgNode> added = new List<CfgNode>();
            foreach (CfgNode member in group)
            {
                List<AttN> memberNodes = AttNs(member);
                AttN mn = memberNodes.First(n => n.NodeId == targetNode);
                Pose pose = AttachMath.NodeAttach(PoseOf(member), mn.Def, pick);
                CfgNode part = NewPart(template, NewId(taken), pose);
                part.SetValue("attm", "0");
                part.SetValue("symMethod", method);
                part.SetValue("mir", member.GetValue("mir") ?? "1,1,1");
                List<AttN> partNodes = AttNs(part);
                AttN own = partNodes.First(n => n.NodeId == pick.Id);
                own.PartName = NameOf(member);
                own.PartId = IdOf(member);
                SetAttNs(part, partNodes);
                mn.PartName = template.Name;
                mn.PartId = IdOf(part);
                SetAttNs(member, memberNodes);
                AddLink(member, part);
                Craft.AddNode(part);
                added.Add(part);
                outcome.Added.Add(IdOf(part));
            }
            if (added.Count > 1)
                SetSyms(added);
            PutInStage(added, stageTarget);
            if (stage.HasValue)
                outcome.StageRequested.UnionWith(added.Select(IdOf));
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>
        /// Surface-attaches a new part. Without symmetry partners on the target, one part per hit (n-way radial symmetry when
        /// there are several). With partners, <paramref name="hits"/> holds the hit on the target only and the part is copied
        /// to every partner through the group's own symmetry map (<paramref name="symmetry"/> must then be 1).
        /// </summary>
        public EditOutcome AttachSurface(uint targetId, PartTemplate template, IList<SurfaceHit> hits, int symmetry, double? stage)
        {
            CfgNode target = Require(targetId);
            if (symmetry < 1 || symmetry > 8)
                throw BridgeException.BadRequest("symmetry must be 1 to 8");
            if (!Rules(target).AcceptsSurface)
                throw BridgeException.BadRequest("part " + targetId + " (" + NameOf(target) + ") does not accept surface-attached parts");
            if (!template.Rules.CanSurfaceAttach || !template.SurfaceNode.HasValue)
                throw BridgeException.BadRequest("part '" + template.Name + "' cannot be surface-attached; use editor_attach_node");
            List<CfgNode> partners = Syms(target).Select(Require).ToList();
            if (partners.Count > 0 && symmetry > 1)
                throw BridgeException.BadRequest("part " + targetId + " is in a symmetry group; the new part is copied to its "
                    + partners.Count + " partner(s), so symmetry must be 1");
            int expectedHits = partners.Count > 0 ? 1 : symmetry;
            if (hits.Count != expectedHits)
                throw new ArgumentException("expected " + expectedHits + " surface hits, got " + hits.Count);
            NodeDef srf = template.SurfaceNode.Value;

            Pose targetPose = PoseOf(target);
            List<(CfgNode parent, Pose pose)> placements = new List<(CfgNode, Pose)>();
            if (partners.Count == 0)
            {
                foreach (SurfaceHit hit in hits)
                    placements.Add((target, AttachMath.SurfaceAttach(hit.Point, hit.Normal, targetPose.Rotation, srf)));
            }
            else
            {
                Pose first = AttachMath.SurfaceAttach(hits[0].Point, hits[0].Normal, targetPose.Rotation, srf);
                placements.Add((target, first));
                foreach (CfgNode partner in partners)
                {
                    CounterpartMap map = MapBetween(target, partner);
                    placements.Add((partner, new Pose(map.MapPoint(first.Position), map.MapRotation(first.Rotation))));
                }
            }
            int stageTarget = StageForNew(stage);

            EditOutcome outcome = new EditOutcome();
            HashSet<uint> taken = TakenIds();
            string method = partners.Count > 0 ? target.GetValue("symMethod") ?? "Radial" : "Radial";
            string srfRest = Geo.Format(srf.Position, "|") + "," + Geo.Format(srf.Direction, "|") + "," + Geo.Format(srf.Position, "|");
            List<CfgNode> added = new List<CfgNode>();
            foreach ((CfgNode parent, Pose pose) in placements)
            {
                CfgNode part = NewPart(template, NewId(taken), pose);
                part.SetValue("attm", "1");
                part.SetValue("symMethod", method);
                part.SetValue("mir", partners.Count > 0 ? parent.GetValue("mir") ?? "1,1,1" : "1,1,1");
                part.SetValue("srfN", new SrfN { NodeId = srf.Id, PartName = NameOf(parent), PartId = IdOf(parent), Mesh = "", Rest = srfRest }.ToString());
                AddLink(parent, part);
                Craft.AddNode(part);
                added.Add(part);
                outcome.Added.Add(IdOf(part));
            }
            if (added.Count > 1)
                SetSyms(added);
            PutInStage(added, stageTarget);
            if (stage.HasValue)
                outcome.StageRequested.UnionWith(added.Select(IdOf));
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        private PartRules Rules(CfgNode part) => rules(NameOf(part)) ?? new PartRules();

        /// <summary>Symmetry map from one group member to another, by the group's method (mirror needs a plane).</summary>
        public CounterpartMap MapBetween(CfgNode from, CfgNode to)
        {
            Pose a = PoseOf(from);
            Pose b = PoseOf(to);
            if (string.Equals(from.GetValue("symMethod"), "Mirror", StringComparison.OrdinalIgnoreCase))
            {
                Vec3 mir = from.GetValue("mir") != null ? Geo.ParseVec(from.GetValue("mir"), "mir") : new Vec3(1, 1, 1);
                Vec3? normal = AttachMath.MirrorNormal(a, b, mir);
                if (!normal.HasValue)
                    throw BridgeException.BadRequest("the mirror plane of parts " + IdOf(from) + " and " + IdOf(to) + " cannot be determined");
                return CounterpartMap.Mirror(a, b, normal.Value);
            }
            return CounterpartMap.Radial(a, b);
        }

        /// <summary>The part and its whole subtree (children, grandchildren, ...).</summary>
        public List<CfgNode> Subtree(CfgNode part)
        {
            List<CfgNode> result = new List<CfgNode> { part };
            for (int i = 0; i < result.Count; i++)
                result.AddRange(Links(result[i]).Select(Require));
            return result;
        }

        /// <summary>Removes the part, its symmetry partners and everything attached below them. The root cannot be removed.</summary>
        public EditOutcome Remove(uint id)
        {
            CfgNode part = Require(id);
            if (ParentOf(part) == null)
                throw BridgeException.BadRequest("part " + id + " is the root part and cannot be removed; use editor_new to start over");
            List<CfgNode> doomed = new[] { part }.Concat(Syms(part).Select(Require)).SelectMany(Subtree).Distinct().ToList();

            EditOutcome outcome = new EditOutcome();
            HashSet<uint> gone = new HashSet<uint>(doomed.Select(IdOf));
            foreach (CfgNode p in doomed)
                Craft.RemoveNode(p);
            foreach (CfgNode p in PartNodes)
                Detach(p, gone);
            foreach (uint g in gone)
                staged.Remove(g);
            outcome.Removed.AddRange(gone.OrderBy(x => x));
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>Drops every reference of <paramref name="part"/> to parts in <paramref name="gone"/>.</summary>
        private static void Detach(CfgNode part, HashSet<uint> gone)
        {
            part.SetValues("link", part.GetValues("link").Where(v => !gone.Contains(SplitRef(v).id)).ToList());
            part.SetValues("sym", part.GetValues("sym").Where(v => !gone.Contains(SplitRef(v).id)).ToList());
            List<AttN> nodes = AttNs(part);
            if (nodes.Any(n => gone.Contains(n.PartId)))
            {
                foreach (AttN n in nodes.Where(n => gone.Contains(n.PartId)))
                {
                    n.PartId = 0;
                    n.PartName = "Null";
                }
                SetAttNs(part, nodes);
            }
        }

        /// <summary>
        /// Replaces the part (and its partners) with a new part kind at the same place: same parent side (node of the same
        /// name, or surface), stack children moved onto the new part's node of the same name, surface children kept where
        /// they are. New parts get new ids; the old ids are reported as removed.
        /// </summary>
        public EditOutcome Replace(uint id, PartTemplate template)
        {
            CfgNode part = Require(id);
            List<CfgNode> group = new[] { part }.Concat(Syms(part).Select(Require)).ToList();
            List<NodeDef> newNodes = NodesOf(template.Node);

            // Checks first (every member has the same shape as the one asked for).
            foreach (CfgNode member in group)
            {
                uint mid = IdOf(member);
                CfgNode parent = ParentOf(member);
                if (parent == null)
                {
                    if (!template.Rules.AllowRoot)
                        throw BridgeException.BadRequest("part '" + template.Name + "' cannot be the root part");
                }
                else if (IsSurface(member))
                {
                    if (!template.Rules.CanSurfaceAttach || !template.SurfaceNode.HasValue)
                        throw BridgeException.BadRequest("part " + mid + " is surface-attached and '" + template.Name + "' cannot be surface-attached");
                }
                else
                {
                    string own = AttNs(member).FirstOrDefault(n => n.PartId == IdOf(parent))?.NodeId;
                    if (own == null || newNodes.All(n => n.Id != own))
                        throw BridgeException.BadRequest("part " + mid + " is attached by node '" + own + "' and '" + template.Name + "' has no such node");
                }
                foreach (CfgNode child in Links(member).Select(Require))
                {
                    if (IsSurface(child))
                    {
                        if (!template.Rules.AcceptsSurface)
                            throw BridgeException.BadRequest("part " + IdOf(child) + " is surface-attached to " + mid + " and '" + template.Name + "' does not accept surface-attached parts");
                    }
                    else
                    {
                        string node = AttNs(member).FirstOrDefault(n => n.PartId == IdOf(child))?.NodeId;
                        if (node == null || newNodes.All(n => n.Id != node))
                            throw BridgeException.BadRequest("part " + IdOf(child) + " is attached to node '" + node + "' of " + mid + " and '" + template.Name + "' has no such node");
                    }
                }
            }

            EditOutcome outcome = new EditOutcome();
            HashSet<uint> taken = TakenIds();
            bool oldStaged = IsStaged(id);
            int? keepStage = oldStaged ? Int(part, "istg", 0) : (int?)null;
            int? freshStage = !oldStaged ? Staging.NewStage(StageEntries()) : (int?)null;
            Dictionary<CfgNode, Pose> newPoses = NewPoses(group, newNodes, template);
            List<CfgNode> added = new List<CfgNode>();
            List<CfgNode> relinked = new List<CfgNode>();
            foreach (CfgNode member in group)
            {
                uint oldId = IdOf(member);
                Pose oldPose = PoseOf(member);
                CfgNode parent = ParentOf(member);
                List<AttN> oldNodes = AttNs(member);
                Pose pose = newPoses[member];

                CfgNode fresh = NewPart(template, NewId(taken), pose);
                uint newId = IdOf(fresh);
                foreach (string key in new[] { "attm", "symMethod", "mir" })
                {
                    if (member.GetValue(key) != null)
                        fresh.SetValue(key, member.GetValue(key));
                }
                // Parent side.
                if (parent != null)
                {
                    parent.SetValues("link", parent.GetValues("link").Select(v => SplitRef(v).id == oldId ? Ref(template.Name, newId) : v).ToList());
                    if (IsSurface(member))
                    {
                        NodeDef srf = template.SurfaceNode.Value;
                        string rest = Geo.Format(srf.Position, "|") + "," + Geo.Format(srf.Direction, "|") + "," + Geo.Format(srf.Position, "|");
                        fresh.SetValue("srfN", new SrfN { NodeId = srf.Id, PartName = NameOf(parent), PartId = IdOf(parent), Mesh = "", Rest = rest }.ToString());
                    }
                    else
                    {
                        List<AttN> pn = AttNs(parent);
                        foreach (AttN n in pn.Where(n => n.PartId == oldId))
                        {
                            n.PartId = newId;
                            n.PartName = template.Name;
                        }
                        SetAttNs(parent, pn);
                        string ownId = oldNodes.First(n => n.PartId == IdOf(parent)).NodeId;
                        LinkNode(fresh, ownId, NameOf(parent), IdOf(parent));
                    }
                }
                // Children.
                foreach (CfgNode child in Links(member).Select(Require).ToList())
                {
                    uint cid = IdOf(child);
                    relinked.Add(child);
                    fresh.AddValue("link", Ref(NameOf(child), cid));
                    if (IsSurface(child))
                    {
                        SrfN s = SrfNOf(child);
                        s.PartId = newId;
                        s.PartName = template.Name;
                        child.SetValue("srfN", s.ToString());
                    }
                    else
                    {
                        AttN at = oldNodes.First(n => n.PartId == cid);
                        NodeDef newAt = newNodes.First(n => n.Id == at.NodeId);
                        Vec3 shift = pose.Rotation * newAt.Position - oldPose.Rotation * at.Def.Position + (pose.Position - oldPose.Position);
                        foreach (CfgNode moved in Subtree(child))
                        {
                            Pose mp = PoseOf(moved);
                            SetPose(moved, new Pose(mp.Position + shift, mp.Rotation));
                        }
                        LinkNode(fresh, at.NodeId, NameOf(child), cid);
                        List<AttN> cn = AttNs(child);
                        foreach (AttN n in cn.Where(n => n.PartId == oldId))
                        {
                            n.PartId = newId;
                            n.PartName = template.Name;
                        }
                        SetAttNs(child, cn);
                    }
                }
                // Symmetry partners outside the group point at the new id later (SetSyms).
                int index = Craft.IndexOfNode(member);
                Craft.RemoveNode(member);
                Craft.InsertNode(index, fresh);
                staged.Remove(oldId);
                staged[newId] = true;
                outcome.Removed.Add(oldId);
                outcome.Added.Add(newId);
                added.Add(fresh);
            }
            if (added.Count > 1)
                SetSyms(added);
            if (keepStage.HasValue || freshStage.HasValue)
                PutInStage(added, keepStage ?? freshStage.Value);
            foreach (CfgNode child in relinked.Where(c => !added.Contains(c)))
                RebaseAttach(child);
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>
        /// World pose of each replacing part, worked out before any node changes. Stack: the new part's node of the same
        /// name sits where the old one was. Surface: the asked-for part gets the editor's formula for the new surface node
        /// at the same point and normal; its partners get that pose through the group's symmetry map, as the editor places
        /// counterparts (a mirror partner is reflected, not computed on its own).
        /// </summary>
        private Dictionary<CfgNode, Pose> NewPoses(List<CfgNode> group, List<NodeDef> newNodes, PartTemplate template)
        {
            Dictionary<CfgNode, Pose> poses = new Dictionary<CfgNode, Pose>();
            CfgNode first = group[0];
            Pose firstSurface = default(Pose);
            bool surface = ParentOf(first) != null && IsSurface(first);
            if (surface)
            {
                // The old rotation is LookRotation(normal, up) · LookRotation(oldDir, up); swap the node part of it.
                Pose oldPose = PoseOf(first);
                SrfN s = SrfNOf(first);
                string[] vectors = s?.Rest.Split(',') ?? new string[0];
                Vec3 oldSrf = vectors.Length > 0 ? Geo.ParseVec(vectors[0], "srfN position") : Vec3.Zero;
                Vec3 oldDir = vectors.Length > 1 ? Geo.ParseVec(vectors[1], "srfN orientation") : template.SurfaceNode.Value.Direction;
                NodeDef newSrf = template.SurfaceNode.Value;
                Quat rotation = (oldPose.Rotation * Quat.LookRotation(oldDir, Vec3.Up).Inverse * Quat.LookRotation(newSrf.Direction, Vec3.Up)).Normalized;
                firstSurface = new Pose(oldPose.Position + oldPose.Rotation * oldSrf - rotation * newSrf.Position, rotation);
            }
            foreach (CfgNode member in group)
            {
                Pose oldPose = PoseOf(member);
                CfgNode parent = ParentOf(member);
                if (parent == null)
                    poses[member] = oldPose;
                else if (!IsSurface(member))
                {
                    AttN own = AttNs(member).First(n => n.PartId == IdOf(parent));
                    NodeDef newOwn = newNodes.First(n => n.Id == own.NodeId);
                    poses[member] = new Pose(oldPose.Position + oldPose.Rotation * own.Def.Position - oldPose.Rotation * newOwn.Position, oldPose.Rotation);
                }
                else if (member == first)
                    poses[member] = firstSurface;
                else
                {
                    CounterpartMap map = MapBetween(first, member);
                    poses[member] = new Pose(map.MapPoint(firstSurface.Position), map.MapRotation(firstSurface.Rotation));
                }
            }
            return poses;
        }

        /// <summary>
        /// A part that kept its offset but got a new parent: the attach base moves into the new parent's space so that
        /// base + offset is still the part's local pose (attPos0 = local − attPos, attRotation0 = local · attRotation⁻¹).
        /// </summary>
        private void RebaseAttach(CfgNode part)
        {
            Pose local = LocalPose(part);
            Vec3 offset = part.GetValue("attPos") != null ? Geo.ParseVec(part.GetValue("attPos"), "attPos") : Vec3.Zero;
            Quat turn = part.GetValue("attRot") != null ? Geo.ParseQuat(part.GetValue("attRot"), "attRot") : Quat.Identity;
            part.SetValue("attPos0", Geo.Format(local.Position - offset));
            part.SetValue("attRot0", Geo.Format((local.Rotation * turn.Inverse).Normalized));
        }

        private static void LinkNode(CfgNode part, string nodeId, string otherName, uint otherId)
        {
            List<AttN> nodes = AttNs(part);
            AttN n = nodes.FirstOrDefault(x => x.NodeId == nodeId);
            if (n == null)
                return;
            n.PartId = otherId;
            n.PartName = otherName;
            SetAttNs(part, nodes);
        }

        /// <summary>
        /// Moves the part with its subtree by <paramref name="offset"/> (m, parent's local axes) and turns it by
        /// <paramref name="rotate"/> (Euler degrees, its own local axes); partners follow through the group's symmetry map.
        /// </summary>
        public EditOutcome Move(uint id, Vec3? offset, Vec3? rotate)
        {
            CfgNode part = Require(id);
            CfgNode parent = ParentOf(part);
            if (parent == null)
                throw BridgeException.BadRequest("part " + id + " is the root part; moving it would move the whole ship");
            if (!offset.HasValue && !rotate.HasValue)
                throw BridgeException.BadRequest("pass offset and/or rotate");
            List<CfgNode> partners = Syms(part).Select(Require).ToList();
            List<CounterpartMap> maps = partners.Select(p => MapBetween(part, p)).ToList();

            Pose pose = PoseOf(part);
            Vec3 localOffset = offset ?? Vec3.Zero;
            Vec3 delta = PoseOf(parent).Rotation * localOffset;
            Quat euler = rotate.HasValue ? Quat.Euler(rotate.Value.X, rotate.Value.Y, rotate.Value.Z) : Quat.Identity;
            Quat turn = (pose.Rotation * euler * pose.Rotation.Inverse).Normalized;

            EditOutcome outcome = new EditOutcome();
            MoveSubtree(part, pose.Position, turn, delta);
            SetAttachOffset(part);
            outcome.Touched.Add(id);
            for (int i = 0; i < partners.Count; i++)
            {
                CfgNode partner = partners[i];
                Vec3 d = maps[i].MapVector(delta);
                MoveSubtree(partner, PoseOf(partner).Position, maps[i].MapDelta(turn), d);
                SetAttachOffset(partner);
                outcome.Touched.Add(IdOf(partner));
            }
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        private void MoveSubtree(CfgNode top, Vec3 pivot, Quat turn, Vec3 delta)
        {
            foreach (CfgNode p in Subtree(top))
            {
                Pose old = PoseOf(p);
                SetPose(p, new Pose(turn * (old.Position - pivot) + pivot + delta, turn * old.Rotation));
            }
        }

        /// <summary>The part's pose in its parent's space (the editor's transform.localPosition/localRotation).</summary>
        private Pose LocalPose(CfgNode part)
        {
            Pose pose = PoseOf(part);
            CfgNode parent = ParentOf(part);
            if (parent == null)
                return pose;
            Pose p = PoseOf(parent);
            Quat inv = p.Rotation.Inverse;
            return new Pose(inv * (pose.Position - p.Position), (inv * pose.Rotation).Normalized);
        }

        /// <summary>
        /// A newly attached part: its local pose is the attach base (EditorLogic sets attPos0/attRotation0 to
        /// transform.localPosition/localRotation once attached) and there is no offset yet.
        /// </summary>
        private void SetAttachBase(CfgNode part)
        {
            Pose local = LocalPose(part);
            part.SetValue("attPos0", Geo.Format(local.Position));
            part.SetValue("attRot0", Geo.Format(local.Rotation));
            part.SetValue("attPos", "0,0,0");
            part.SetValue("attRot", "0,0,0,1");
        }

        /// <summary>
        /// A moved part: the offset from its attach base as the editor keeps it (attPos = localPosition - attPos0;
        /// attRotation such that reset gives attRotation0 and picking up gives base · attRotation).
        /// </summary>
        private void SetAttachOffset(CfgNode part)
        {
            Pose local = LocalPose(part);
            Vec3 basePos = part.GetValue("attPos0") != null ? Geo.ParseVec(part.GetValue("attPos0"), "attPos0") : local.Position;
            Quat baseRot = part.GetValue("attRot0") != null ? Geo.ParseQuat(part.GetValue("attRot0"), "attRot0") : local.Rotation;
            part.SetValue("attPos", Geo.Format(local.Position - basePos));
            part.SetValue("attRot", Geo.Format((baseRot.Inverse * local.Rotation).Normalized));
        }

        /// <summary>Moves a staged part (and its staged partners) to an existing stage or the new first one.</summary>
        public EditOutcome SetStage(uint id, double stage)
        {
            CfgNode part = Require(id);
            if (!IsStaged(id))
                throw BridgeException.BadRequest("part " + id + " (" + NameOf(part) + ") is not staged (no staging icon); only engines, decouplers, parachutes and the like have stages");
            List<StageEntry> entries = StageEntries();
            int target = Staging.RequireTarget(entries, stage);
            List<uint> ids = new[] { id }.Concat(Syms(part).Where(IsStaged)).ToList();
            Staging.Move(entries, ids, target);
            WriteStages(entries);
            EditOutcome outcome = new EditOutcome();
            outcome.Touched.UnionWith(ids);
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>Exchanges two existing stages.</summary>
        public EditOutcome SwapStages(double a, double b)
        {
            List<StageEntry> entries = StageEntries();
            Staging.Swap(entries, a, b);
            WriteStages(entries);
            EditOutcome outcome = new EditOutcome();
            outcome.Touched.UnionWith(entries.Where(e => e.Stage == (int)a || e.Stage == (int)b).Select(e => e.Id));
            foreach (uint newPartId in outcome.Added)
                SetAttachBase(Find(newPartId));
            NormalizeStages();
            return outcome;
        }

        /// <summary>Staged part ids by stage (for checking the loaded ship).</summary>
        public Dictionary<uint, int> StageMap() => StageEntries().ToDictionary(e => e.Id, e => e.Stage);
    }
}
