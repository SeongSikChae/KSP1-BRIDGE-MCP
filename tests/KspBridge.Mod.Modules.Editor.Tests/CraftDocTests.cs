using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KspBridge.Mod.Modules.Editor.Tests
{
    [TestClass]
    public sealed class CraftDocTests
    {
        // ---- builders ----

        private static string AttN(string id, Vec3 pos, Vec3 dir, string other = "Null", uint otherId = 0)
            => id + "," + other + "_" + otherId + "_" + Geo.Format(pos, "|") + "_" + Geo.Format(dir, "|") + "_" + Geo.Format(pos, "|") + "_" + Geo.Format(dir, "|");

        /// <summary>Stack part with top/bottom nodes at ±half (or the given nodes).</summary>
        private static CfgNode Part(string name, uint id, Vec3 pos, double half = 1, params string[] extraAttN)
        {
            CfgNode p = new CfgNode("PART");
            p.AddValue("part", name + "_" + id);
            p.AddValue("partName", "Part");
            p.AddValue("pos", Geo.Format(pos));
            p.AddValue("attPos", "0,0,0");
            p.AddValue("attPos0", Geo.Format(pos));
            p.AddValue("rot", "0,0,0,1");
            p.AddValue("attRot", "0,0,0,1");
            p.AddValue("attRot0", "0,0,0,1");
            p.AddValue("mir", "1,1,1");
            p.AddValue("symMethod", "Radial");
            p.AddValue("istg", "-1");
            p.AddValue("dstg", "0");
            p.AddValue("sidx", "-1");
            p.AddValue("attm", "0");
            if (half > 0)
            {
                p.AddValue("attN", AttN("top", new Vec3(0, half, 0), Vec3.Up));
                p.AddValue("attN", AttN("bottom", new Vec3(0, -half, 0), -Vec3.Up));
            }
            foreach (string a in extraAttN)
                p.AddValue("attN", a);
            return p;
        }

        private static void Stack(CfgNode parent, string parentNode, CfgNode child, string childNode)
        {
            uint pid = CraftDoc.IdOf(parent), cid = CraftDoc.IdOf(child);
            parent.AddValue("link", CraftDoc.NameOf(child) + "_" + cid);
            SetNode(parent, parentNode, CraftDoc.NameOf(child), cid);
            SetNode(child, childNode, CraftDoc.NameOf(parent), pid);
        }

        private static void SetNode(CfgNode part, string nodeId, string other, uint otherId)
        {
            List<CraftDoc.AttN> nodes = CraftDoc.AttNs(part);
            CraftDoc.AttN n = nodes.First(x => x.NodeId == nodeId);
            n.PartName = other;
            n.PartId = otherId;
            part.SetValues("attN", nodes.Select(x => x.ToString()));
        }

        private static void Surface(CfgNode parent, CfgNode child)
        {
            parent.AddValue("link", CraftDoc.NameOf(child) + "_" + CraftDoc.IdOf(child));
            child.SetValue("attm", "1");
            child.SetValue("srfN", "srfAttach," + CraftDoc.NameOf(parent) + "_" + CraftDoc.IdOf(parent) + ",,0|0|0,1|0|0,0|0|0");
        }

        private static void Sym(params CfgNode[] group)
        {
            foreach (CfgNode p in group)
                p.SetValues("sym", group.Where(o => o != p).Select(o => CraftDoc.NameOf(o) + "_" + CraftDoc.IdOf(o)));
        }

        private static CfgNode Craft(params CfgNode[] parts)
        {
            CfgNode c = new CfgNode("");
            c.AddValue("ship", "test");
            c.AddValue("type", "VAB");
            foreach (CfgNode p in parts)
                c.AddNode(p);
            return c;
        }

        private static readonly Dictionary<string, PartRules> Kinds = new Dictionary<string, PartRules>
        {
            ["pod"] = new PartRules { AllowRoot = true, AcceptsSurface = true },
            ["tank"] = new PartRules { AllowRoot = true, AcceptsSurface = true, CanSurfaceAttach = false },
            ["noSurface"] = new PartRules { AllowRoot = true, AcceptsSurface = false },
            ["radial"] = new PartRules { AllowRoot = false, AcceptsSurface = true, CanSurfaceAttach = true },
        };

        private static CraftDoc Doc(CfgNode craft, params uint[] stagedIds)
        {
            Dictionary<uint, bool> staged = craft.GetNodes("PART").ToDictionary(CraftDoc.IdOf, p => stagedIds.Contains(CraftDoc.IdOf(p)));
            return new CraftDoc(craft, name => Kinds.TryGetValue(name, out PartRules r) ? r : new PartRules(), staged);
        }

        private static PartTemplate Template(string name, double half = 1, PartRules rules = null, bool surfaceNode = false, Vec3? surfaceDirection = null)
        {
            CfgNode node = Part(name, 0, Vec3.Zero, half);
            node.AddNode(new CfgNode("MODULE"));
            node.GetNode("MODULE").AddValue("name", "ModuleTest");
            return new PartTemplate
            {
                Name = name,
                Node = node,
                Rules = rules ?? new PartRules { AllowRoot = true, AcceptsSurface = true, CanSurfaceAttach = surfaceNode },
                SurfaceNode = surfaceNode ? new NodeDef("srfAttach", new Vec3(-0.1, 0, 0), surfaceDirection ?? Vec3.Right) : (NodeDef?)null,
            };
        }

        private static string Text(CfgNode n)
        {
            StringBuilder b = new StringBuilder();
            Write(n, b, 0);
            return b.ToString();
        }

        private static void Write(CfgNode n, StringBuilder b, int depth)
        {
            b.Append(' ', depth).Append(n.Name).Append('{').AppendLine();
            foreach (KeyValuePair<string, string> v in n.Values)
                b.Append(' ', depth + 1).Append(v.Key).Append('=').Append(v.Value).AppendLine();
            foreach (CfgNode c in n.Nodes)
                Write(c, b, depth + 1);
            b.Append(' ', depth).Append('}').AppendLine();
        }

        private static void Refused(CfgNode craft, System.Action edit)
        {
            string before = Text(craft);
            Assert.ThrowsExactly<BridgeException>(edit);
            Assert.AreEqual(before, Text(craft), "a refused edit leaves the craft unchanged");
        }

        private static Pose PoseOf(CraftDoc doc, uint id) => CraftDoc.PoseOf(doc.Find(id));

        // ---- root ----

        [TestMethod]
        public void PlaceRoot_EmptyShipOnly()
        {
            CfgNode craft = Craft();
            CraftDoc doc = Doc(craft);
            EditOutcome o = doc.PlaceRoot(Template("pod"), new Vec3(0, 15, 0), null);
            Assert.AreEqual(1, doc.PartCount);
            Assert.AreEqual(o.Added[0], CraftDoc.IdOf(doc.Root));
            Assert.IsNull(doc.IntegrityError());
            Refused(craft, () => doc.PlaceRoot(Template("pod"), Vec3.Zero, null));
        }

        [TestMethod]
        public void PlaceRoot_RefusesPartThatCannotBeRoot()
        {
            CfgNode craft = Craft();
            Refused(craft, () => Doc(craft).PlaceRoot(Template("radial", rules: new PartRules { AllowRoot = false }), Vec3.Zero, null));
        }

        // ---- node attach ----

        [TestMethod]
        public void AttachNode_TankUnderPod()
        {
            CfgNode craft = Craft(Part("pod", 1, new Vec3(0, 10, 0), 0.5));
            CraftDoc doc = Doc(craft);
            uint tank = doc.AttachNode(1, "bottom", Template("tank", 1), null, null).Added.Single();
            PartRelation r = doc.Relations()[tank];
            Assert.AreEqual(1u, r.Parent);
            Assert.AreEqual("stack", r.Attach);
            Assert.AreEqual("bottom", r.ParentNode);
            Assert.AreEqual("top", r.Node);
            AttachMathTests.Near(new Vec3(0, 8.5, 0), PoseOf(doc, tank).Position);
            Assert.IsNull(doc.IntegrityError());
            Assert.AreEqual("ModuleTest", doc.Find(tank).GetNode("MODULE").GetValue("name"), "template modules kept");
        }

        [TestMethod]
        public void AttachNode_RefusesUsedMissingNodeOrNodelessPart()
        {
            CfgNode craft = Craft(Part("pod", 1, Vec3.Zero, 0.5));
            CraftDoc doc = Doc(craft);
            doc.AttachNode(1, "bottom", Template("tank"), null, null);
            Refused(craft, () => doc.AttachNode(1, "bottom", Template("tank"), null, null));
            Refused(craft, () => doc.AttachNode(1, "side", Template("tank"), null, null));
            Refused(craft, () => doc.AttachNode(1, "top", Template("tank", half: 0), null, null));
            Refused(craft, () => doc.AttachNode(1, "top", Template("tank"), "nope", null));
            Refused(craft, () => doc.AttachNode(99, "top", Template("tank"), null, null));
        }

        [TestMethod]
        public void AttachNode_ToSymmetryGroup_AttachesToEveryPartner()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0), 0.5);
            CfgNode b = Part("radial", 3, new Vec3(-1, 0, 0), 0.5);
            b.SetValue("rot", Geo.Format(Quat.AngleAxis(180, Vec3.Up)));
            Surface(tank, a);
            Surface(tank, b);
            Sym(a, b);
            CfgNode craft = Craft(tank, a, b);
            CraftDoc doc = Doc(craft);
            List<uint> added = doc.AttachNode(2, "bottom", Template("tank"), null, null).Added;
            Assert.AreEqual(2, added.Count);
            Dictionary<uint, PartRelation> rel = doc.Relations();
            CollectionAssert.AreEquivalent(new[] { 2u, 3u }, added.Select(x => rel[x].Parent.Value).ToArray());
            CollectionAssert.AreEqual(new[] { added[1] }, rel[added[0]].Symmetry);
            Assert.IsNull(doc.IntegrityError());
        }

        // ---- surface attach ----

        [TestMethod]
        public void AttachSurface_RadialThree_FormsSymmetryGroup()
        {
            CfgNode craft = Craft(Part("tank", 1, Vec3.Zero));
            CraftDoc doc = Doc(craft);
            List<SurfaceHit> hits = AttachMath.RadialAngles(0, 3)
                .Select(a => { (Vec3 o, Vec3 d) = AttachMath.SurfaceProbe(new Pose(Vec3.Zero, Quat.Identity), 0, a, 10); return new SurfaceHit(o + d * 9.375, -d); })
                .ToList();
            List<uint> added = doc.AttachSurface(1, Template("radial", surfaceNode: true), hits, 3, null).Added;
            Assert.AreEqual(3, added.Count);
            Dictionary<uint, PartRelation> rel = doc.Relations();
            foreach (uint id in added)
            {
                Assert.AreEqual("surface", rel[id].Attach);
                Assert.AreEqual(1u, rel[id].Parent);
                Assert.AreEqual(2, rel[id].Symmetry.Count);
            }
            Vec3[] p = added.Select(id => PoseOf(doc, id).Position).ToArray();
            double side = (p[0] - p[1]).Length;
            Assert.IsTrue(side > 0.1, "spread around the axis");
            Assert.AreEqual(side, (p[1] - p[2]).Length, 1e-4, "evenly spaced");
            Assert.AreEqual(side, (p[2] - p[0]).Length, 1e-4, "evenly spaced");
            Assert.IsNull(doc.IntegrityError());
        }

        [TestMethod]
        public void AttachSurface_RefusesWrongTargetOrPart()
        {
            CfgNode craft = Craft(Part("noSurface", 1, Vec3.Zero));
            SurfaceHit[] hit = { new SurfaceHit(new Vec3(1, 0, 0), Vec3.Right) };
            Refused(craft, () => Doc(craft).AttachSurface(1, Template("radial", surfaceNode: true), hit, 1, null));
            CfgNode craft2 = Craft(Part("tank", 1, Vec3.Zero));
            Refused(craft2, () => Doc(craft2).AttachSurface(1, Template("tank"), hit, 1, null));
            Refused(craft2, () => Doc(craft2).AttachSurface(1, Template("radial", surfaceNode: true), hit, 9, null));
        }

        [TestMethod]
        public void AttachSurface_OnSymmetryGroup_CopiesToPartnersAndRefusesMoreSymmetry()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1.5, 0, 0));
            CfgNode b = Part("radial", 3, new Vec3(-1.5, 0, 0));
            b.SetValue("rot", Geo.Format(Quat.AngleAxis(180, Vec3.Up)));
            Surface(tank, a);
            Surface(tank, b);
            Sym(a, b);
            CfgNode craft = Craft(tank, a, b);
            CraftDoc doc = Doc(craft);
            SurfaceHit hit = new SurfaceHit(new Vec3(2, 0.3, 0), Vec3.Right);
            Refused(craft, () => doc.AttachSurface(2, Template("radial", surfaceNode: true), new[] { hit }, 2, null));
            List<uint> added = doc.AttachSurface(2, Template("radial", surfaceNode: true), new[] { hit }, 1, null).Added;
            Assert.AreEqual(2, added.Count);
            Pose onA = PoseOf(doc, added[0]);
            Pose onB = PoseOf(doc, added[1]);
            AttachMathTests.Near(new Vec3(-onA.Position.X, onA.Position.Y, -onA.Position.Z), onB.Position, "partner copy turned 180° about the axis");
            Assert.AreEqual(3u, doc.Relations()[added[1]].Parent);
        }

        // ---- remove ----

        [TestMethod]
        public void Remove_TakesPartnersAndSubtrees()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0));
            CfgNode b = Part("radial", 3, new Vec3(-1, 0, 0));
            CfgNode below = Part("tank", 4, new Vec3(1, -2, 0));
            Surface(tank, a);
            Surface(tank, b);
            Sym(a, b);
            Stack(a, "bottom", below, "top");
            CfgNode craft = Craft(tank, a, b, below);
            CraftDoc doc = Doc(craft);
            Refused(craft, () => doc.Remove(1));
            EditOutcome o = doc.Remove(2);
            CollectionAssert.AreEqual(new[] { 2u, 3u, 4u }, o.Removed);
            Assert.AreEqual(1, doc.PartCount);
            Assert.AreEqual(0, doc.Find(1).GetValues("link").Count);
            Assert.IsNull(doc.IntegrityError());
        }

        // ---- replace ----

        private static (CfgNode craft, CraftDoc doc) PodTankEngine(params uint[] staged)
        {
            CfgNode pod = Part("pod", 1, new Vec3(0, 10, 0), 0.5);
            CfgNode tank = Part("tank", 2, new Vec3(0, 8.5, 0), 1);
            CfgNode engine = Part("engine", 3, new Vec3(0, 7, 0), 0.5);
            Stack(pod, "bottom", tank, "top");
            Stack(tank, "bottom", engine, "top");
            engine.SetValue("istg", "0");
            engine.SetValue("sidx", "0");
            CfgNode craft = Craft(pod, tank, engine);
            return (craft, Doc(craft, staged));
        }

        [TestMethod]
        public void Replace_LongerTank_KeepsPlaceAndMovesStackChild()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine(3);
            EditOutcome o = doc.Replace(2, Template("tank", half: 2));
            uint fresh = o.Added.Single();
            CollectionAssert.AreEqual(new[] { 2u }, o.Removed);
            Assert.AreNotEqual(2u, fresh, "a new id");
            Dictionary<uint, PartRelation> rel = doc.Relations();
            Assert.AreEqual(1u, rel[fresh].Parent);
            Assert.AreEqual("bottom", rel[fresh].ParentNode);
            Assert.AreEqual(fresh, rel[3].Parent);
            Assert.AreEqual("bottom", rel[3].ParentNode);
            AttachMathTests.Near(new Vec3(0, 7.5, 0), PoseOf(doc, fresh).Position, "top node stays under the pod");
            AttachMathTests.Near(new Vec3(0, 5, 0), PoseOf(doc, 3).Position, "engine moved down with the bottom node");
            Assert.IsNull(doc.IntegrityError());
            List<uint> changed = CraftDoc.Changed(new Dictionary<uint, PartRelation>(), rel, o);
            CollectionAssert.Contains(changed, 3u);
        }

        [TestMethod]
        public void Replace_RefusesMissingNodesSurfaceAndRoot()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine();
            Refused(craft, () => doc.Replace(2, Template("tank", half: 0)));
            Refused(craft, () => doc.Replace(1, Template("pod", rules: new PartRules { AllowRoot = false })));

            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0));
            Surface(tank, a);
            CfgNode craft2 = Craft(tank, a);
            Refused(craft2, () => Doc(craft2).Replace(1, Template("noSurface", rules: new PartRules { AllowRoot = true, AcceptsSurface = false })));
            Refused(craft2, () => Doc(craft2).Replace(2, Template("tank")));
        }

        [TestMethod]
        public void Replace_SurfacePartWithOtherNodeDirection_KeepsSurfacePointAndAttachFrame()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode craft = Craft(tank);
            CraftDoc doc = Doc(craft);
            SurfaceHit hit = new SurfaceHit(new Vec3(0.625, 0.2, 0), Vec3.Right);
            uint old = doc.AttachSurface(1, Template("radial", surfaceNode: true, surfaceDirection: Vec3.Right), new[] { hit }, 1, null).Added.Single();
            Pose before = PoseOf(doc, old);
            NodeDef oldNode = new NodeDef("srfAttach", new Vec3(-0.1, 0, 0), Vec3.Right);

            PartTemplate panel = Template("radial", surfaceNode: true, surfaceDirection: -Vec3.Forward);
            uint fresh = doc.Replace(old, panel).Added.Single();
            Pose after = PoseOf(doc, fresh);
            NodeDef newNode = panel.SurfaceNode.Value;

            AttachMathTests.Near(before.Position + before.Rotation * oldNode.Position, after.Position + after.Rotation * newNode.Position, "node on the same surface point");
            AttachMathTests.Near(before.Rotation * Quat.LookRotation(oldNode.Direction, Vec3.Up).Inverse,
                after.Rotation * Quat.LookRotation(newNode.Direction, Vec3.Up).Inverse, "same LookRotation(normal, up) frame as the editor formula");
            AttachMathTests.Near(AttachMath.SurfaceAttach(hit.Point, hit.Normal, Quat.Identity, newNode).Rotation, after.Rotation, "as if attached fresh at that point");
        }

        [TestMethod]
        public void Replace_MirrorSurfacePair_PartnerIsReflectionOfNewPart()
        {
            // The editor places a mirror counterpart by reflecting the placed part; computing each member on its own
            // with the surface formula would not give the reflection when the node direction changes.
            CfgNode body = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(0.6, 0.2, 0.1));
            CfgNode b = Part("radial", 3, new Vec3(-0.6, 0.2, 0.1));
            Quat ra = Quat.AngleAxis(30, Vec3.Up) * Quat.AngleAxis(10, Vec3.Forward);
            a.SetValue("rot", Geo.Format(ra));
            b.SetValue("rot", Geo.Format(AttachMath.MirrorRotation(ra, Vec3.Right)));
            foreach (CfgNode w in new[] { a, b })
                w.SetValue("symMethod", "Mirror");
            b.SetValue("mir", "-1,1,1");
            Surface(body, a);
            Surface(body, b);
            Sym(a, b);
            CraftDoc doc = Doc(Craft(body, a, b));

            EditOutcome o = doc.Replace(2, Template("radial", surfaceNode: true, surfaceDirection: -Vec3.Forward));
            Pose first = PoseOf(doc, o.Added[0]);
            Pose partner = PoseOf(doc, o.Added[1]);
            AttachMathTests.Near(AttachMath.Reflect(first.Position, Vec3.Right), partner.Position, "partner position reflected across x = 0");
            AttachMathTests.Near(AttachMath.MirrorRotation(first.Rotation, Vec3.Right), partner.Rotation, "partner rotation mirrored");
            Assert.IsNull(doc.IntegrityError());
        }

        [TestMethod]
        public void Replace_RelinkedChildrenKeepOffsetOnNewAttachBase()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine();
            CfgNode radial = Part("radial", 4, new Vec3(0.7, 8.5, 0));
            Surface(doc.Find(2), radial);
            craft.AddNode(radial);
            doc = Doc(craft);
            doc.Move(3, new Vec3(0.2, 0, 0), new Vec3(0, 30, 0));
            doc.Move(4, new Vec3(0.1, 0.3, 0), new Vec3(0, 0, 15));
            Dictionary<uint, (string pos, string rot)> offsets = new[] { 3u, 4u }.ToDictionary(id => id, id => (doc.Find(id).GetValue("attPos"), doc.Find(id).GetValue("attRot")));

            uint fresh = doc.Replace(2, Template("tank", half: 2)).Added.Single();
            Pose parent = PoseOf(doc, fresh);
            foreach (uint id in new[] { 3u, 4u })
            {
                CfgNode child = doc.Find(id);
                Assert.AreEqual(offsets[id].pos, child.GetValue("attPos"), "offset kept");
                Assert.AreEqual(offsets[id].rot, child.GetValue("attRot"), "offset kept");
                Pose world = PoseOf(doc, id);
                Vec3 localPos = parent.Rotation.Inverse * (world.Position - parent.Position);
                Quat localRot = parent.Rotation.Inverse * world.Rotation;
                Vec3 basePos = Geo.ParseVec(child.GetValue("attPos0"), "attPos0");
                Quat baseRot = Geo.ParseQuat(child.GetValue("attRot0"), "attRot0");
                AttachMathTests.Near(localPos, basePos + Geo.ParseVec(child.GetValue("attPos"), "attPos"), "attPos0 + attPos is the local position under the new part");
                AttachMathTests.Near(localRot, (baseRot * Geo.ParseQuat(child.GetValue("attRot"), "attRot")).Normalized, "attRot0 · attRot is the local rotation");
            }
            Assert.IsNull(doc.IntegrityError());
        }

        [TestMethod]
        public void AttachBaseIsParentLocal_MoveSetsOffsetFromIt()
        {
            CfgNode craft = Craft(Part("pod", 1, new Vec3(0, 10, 0), 0.5));
            CraftDoc doc = Doc(craft);
            uint tank = doc.AttachNode(1, "bottom", Template("tank", 1), null, null).Added.Single();
            CfgNode node = doc.Find(tank);
            AttachMathTests.Near(new Vec3(0, -1.5, 0), Geo.ParseVec(node.GetValue("attPos0"), "attPos0"), "attPos0 is the local position under the pod");
            AttachMathTests.Near(Quat.Identity, Geo.ParseQuat(node.GetValue("attRot0"), "attRot0"));

            doc.Move(tank, new Vec3(0.2, 0, 0), new Vec3(0, 30, 0));
            node = doc.Find(tank);
            AttachMathTests.Near(new Vec3(0.2, 0, 0), Geo.ParseVec(node.GetValue("attPos"), "attPos"), "attPos = local - attPos0");
            AttachMathTests.Near(Quat.Euler(0, 30, 0), Geo.ParseQuat(node.GetValue("attRot"), "attRot"), "attRot = attRot0⁻¹ · local rotation");
            AttachMathTests.Near(new Vec3(0, -1.5, 0), Geo.ParseVec(node.GetValue("attPos0"), "attPos0"), "base unchanged by a move");
        }

        [TestMethod]
        public void Replace_Root_NewPartIsFirst()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine();
            uint fresh = doc.Replace(1, Template("pod", half: 0.5)).Added.Single();
            Assert.AreEqual(fresh, CraftDoc.IdOf(doc.PartNodes[0]));
            Assert.AreEqual(fresh, doc.Relations()[2].Parent);
            Assert.IsNull(doc.IntegrityError());
        }

        [TestMethod]
        public void Replace_StageRules()
        {
            // New parts are written as possibly staged; the game decides (checked by Staging.StageMismatch after loading).
            (_, CraftDoc keep) = PodTankEngine(3);
            uint engine = keep.Replace(3, Template("engine", 0.5)).Added.Single();
            Assert.AreEqual(0, keep.StageMap()[engine], "replacing a staged part writes the new part into its stage");

            (_, CraftDoc fresh) = PodTankEngine(3);
            uint tankEngine = fresh.Replace(2, Template("engine", 1)).Added.Single();
            Assert.AreEqual(1, fresh.StageMap()[tankEngine], "replacing an unstaged part writes the new part into a new first stage");
            Assert.AreEqual(0, fresh.StageMap()[3], "other stages keep their order");
        }

        // ---- move ----

        [TestMethod]
        public void Move_RadialPartnerFollows()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0));
            CfgNode b = Part("radial", 3, new Vec3(-1, 0, 0));
            b.SetValue("rot", Geo.Format(Quat.AngleAxis(180, Vec3.Up)));
            Surface(tank, a);
            Surface(tank, b);
            Sym(a, b);
            CfgNode craft = Craft(tank, a, b);
            CraftDoc doc = Doc(craft);
            Refused(craft, () => doc.Move(1, new Vec3(0, 1, 0), null));
            EditOutcome o = doc.Move(2, new Vec3(0.5, 0.5, 0), null);
            AttachMathTests.Near(new Vec3(1.5, 0.5, 0), PoseOf(doc, 2).Position);
            AttachMathTests.Near(new Vec3(-1.5, 0.5, 0), PoseOf(doc, 3).Position, "partner moved outward and up too");
            CollectionAssert.AreEquivalent(new[] { 2u, 3u }, o.Touched.ToArray());
            Assert.AreEqual(1u, doc.Relations()[2].Parent, "attachment kept");
        }

        [TestMethod]
        public void Move_MirrorPartnerReflects()
        {
            CfgNode body = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0));
            CfgNode b = Part("radial", 3, new Vec3(-1, 0, 0));
            foreach (CfgNode w in new[] { a, b })
                w.SetValue("symMethod", "Mirror");
            b.SetValue("mir", "-1,1,1");
            Surface(body, a);
            Surface(body, b);
            Sym(a, b);
            CfgNode craft = Craft(body, a, b);
            CraftDoc doc = Doc(craft);
            doc.Move(2, new Vec3(0.5, 0, 1), new Vec3(0, 20, 0));
            AttachMathTests.Near(new Vec3(1.5, 0, 1), PoseOf(doc, 2).Position);
            AttachMathTests.Near(new Vec3(-1.5, 0, 1), PoseOf(doc, 3).Position, "reflected across the x plane");
            AttachMathTests.Near(Quat.AngleAxis(20, Vec3.Up), PoseOf(doc, 2).Rotation);
            AttachMathTests.Near(Quat.AngleAxis(-20, Vec3.Up), PoseOf(doc, 3).Rotation, "yaw mirrored");
        }

        // ---- stages ----

        [TestMethod]
        public void SetStage_OnlyStagedPartsAndValidNumbers()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine(3);
            Refused(craft, () => doc.SetStage(2, 0));
            Refused(craft, () => doc.SetStage(3, 5));
            doc.SetStage(3, 1);
            Assert.AreEqual(0, doc.StageMap()[3], "a lone stage renumbers to 0");
        }

        [TestMethod]
        public void NewStagedPart_DefaultsToNewFirstStage()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine(3);
            EditOutcome first = doc.AttachNode(1, "top", Template("chute", 0.2), null, null);
            uint chute = first.Added.Single();
            Assert.AreEqual(1, doc.StageMap()[chute], "default: a new stage that fires first");
            Assert.AreEqual(0, doc.StageMap()[3]);
            Assert.AreEqual(0, first.StageRequested.Count, "no stage requested");
            EditOutcome second = doc.AttachNode(chute, "top", Template("tank"), null, 0);
            Assert.AreEqual(0, doc.StageMap()[second.Added.Single()]);
            CollectionAssert.AreEqual(second.Added, second.StageRequested.ToList(), "explicit stage is recorded for the settled-ship check");
            Refused(craft, () => doc.AttachNode(1, "bottom", Template("tank"), null, 7));
        }

        [TestMethod]
        public void Changed_IgnoresRenumberOnly()
        {
            (CfgNode craft, CraftDoc first) = PodTankEngine();
            CfgNode chute = Part("chute", 4, new Vec3(0, 11, 0), 0.2);
            Stack(first.Find(1), "top", chute, "bottom");
            craft.AddNode(chute);
            first.Find(3).SetValue("istg", "1");
            chute.SetValue("istg", "0");
            chute.SetValue("sidx", "0");
            CraftDoc doc = Doc(craft, 3, 4);
            Assert.IsNull(doc.IntegrityError());
            Dictionary<uint, PartRelation> before = doc.Relations();
            EditOutcome o = doc.Remove(4);
            Assert.AreEqual(0, doc.StageMap()[3], "engine renumbered from 1 to 0");
            List<uint> changed = CraftDoc.Changed(before, doc.Relations(), o);
            CollectionAssert.DoesNotContain(changed, 3u, "renumbering alone is not a change");
            CollectionAssert.DoesNotContain(changed, 1u, "the pod's own parent, attachment and symmetry did not change");
            CollectionAssert.DoesNotContain(changed, 4u, "removed parts are reported as removed, not changed");
        }

        [TestMethod]
        public void Changed_ListsRelationChangesAddedAndTouchedNotRemoved()
        {
            Dictionary<uint, PartRelation> before = new Dictionary<uint, PartRelation>
            {
                [1] = new PartRelation { Name = "pod" },
                [2] = new PartRelation { Name = "tank", Parent = 1, Attach = "stack", ParentNode = "bottom", Node = "top" },
                [3] = new PartRelation { Name = "engine", Parent = 2, Attach = "stack", ParentNode = "bottom", Node = "top" },
                [4] = new PartRelation { Name = "fin", Parent = 2, Attach = "surface", Node = "srfAttach" },
            };
            Dictionary<uint, PartRelation> after = new Dictionary<uint, PartRelation>
            {
                [1] = new PartRelation { Name = "pod" },
                [5] = new PartRelation { Name = "tank", Parent = 1, Attach = "stack", ParentNode = "bottom", Node = "top" },
                [3] = new PartRelation { Name = "engine", Parent = 5, Attach = "stack", ParentNode = "bottom", Node = "top" },
                [4] = new PartRelation { Name = "fin", Parent = 5, Attach = "surface", Node = "srfAttach" },
            };
            EditOutcome o = new EditOutcome();
            o.Added.Add(5);
            o.Removed.Add(2);
            CollectionAssert.AreEqual(new[] { 3u, 4u, 5u }, CraftDoc.Changed(before, after, o));
        }

        // ---- integrity ----

        [TestMethod]
        public void IntegrityError_FindsBrokenReferences()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine();
            Assert.IsNull(doc.IntegrityError());

            doc.Find(1).AddValue("link", "ghost_77");
            Assert.IsNotNull(doc.IntegrityError(), "link to a missing part");

            (craft, doc) = PodTankEngine();
            doc.Find(2).AddValue("sym", "tank_3");
            Assert.IsNotNull(doc.IntegrityError(), "one-sided symmetry");

            (craft, doc) = PodTankEngine();
            doc.Find(2).AddValue("sym", "tank_88");
            Assert.IsNotNull(doc.IntegrityError(), "missing partner");

            (craft, doc) = PodTankEngine();
            doc.Find(3).AddValue("attN", AttN("side", Vec3.Zero, Vec3.Right, "ghost", 99));
            Assert.IsNotNull(doc.IntegrityError(), "attN to a missing part");

            (craft, doc) = PodTankEngine();
            doc.Find(3).SetValue("srfN", "srfAttach,ghost_99,,0|0|0,1|0|0,0|0|0");
            Assert.IsNotNull(doc.IntegrityError(), "srfN to a missing part");

            (craft, doc) = PodTankEngine();
            craft.AddNode(Part("pod", 9, Vec3.Zero));
            Assert.IsNotNull(doc.IntegrityError(), "two roots");

            (craft, doc) = PodTankEngine();
            CfgNode first = craft.GetNodes("PART")[0];
            craft.RemoveNode(first);
            craft.AddNode(first);
            Assert.IsNotNull(doc.IntegrityError(), "root not first");
        }

        [TestMethod]
        public void IntegrityError_StageIndexesPerSymmetryGroup()
        {
            CfgNode tank = Part("tank", 1, Vec3.Zero);
            CfgNode a = Part("radial", 2, new Vec3(1, 0, 0));
            CfgNode b = Part("radial", 3, new Vec3(-1, 0, 0));
            CfgNode c = Part("radial", 4, new Vec3(0, 0, 1));
            foreach (CfgNode p in new[] { a, b, c })
            {
                Surface(tank, p);
                p.SetValue("istg", "0");
                p.SetValue("sidx", "0");
            }
            Sym(a, b);
            CfgNode craft = Craft(tank, a, b, c);
            CraftDoc doc = Doc(craft, 2, 3, 4);
            Assert.IsNotNull(doc.IntegrityError(), "4 is not a partner of 2/3 but shares index 0");
            c.SetValue("sidx", "1");
            Assert.IsNull(doc.IntegrityError(), "partners share 0, the other part has 1");
            doc.NormalizeStages();
            Assert.IsNull(doc.IntegrityError());
            c.SetValue("istg", "2");
            Assert.IsNotNull(doc.IntegrityError(), "gap in stage numbers");
        }

        [TestMethod]
        public void UnknownValuesAndNodesAreKept()
        {
            (CfgNode craft, CraftDoc doc) = PodTankEngine();
            doc.Find(1).AddValue("custom", "keep me");
            CfgNode module = new CfgNode("MODULE");
            module.AddValue("name", "ModuleCommand");
            doc.Find(1).AddNode(module);
            craft.AddValue("description", "header kept");
            doc.AttachNode(1, "top", Template("tank"), null, null);
            Assert.AreEqual("keep me", doc.Find(1).GetValue("custom"));
            Assert.AreEqual("ModuleCommand", doc.Find(1).GetNode("MODULE").GetValue("name"));
            Assert.AreEqual("header kept", craft.GetValue("description"));
        }

        [TestMethod]
        public void RollbackFor_EmptyOriginalReopens()
        {
            Assert.AreEqual(RollbackMode.ReopenEmpty, CraftDoc.RollbackFor(0));
            Assert.AreEqual(RollbackMode.ReloadOriginal, CraftDoc.RollbackFor(3));
        }

        [TestMethod]
        public void AttN_RoundTrips()
        {
            string text = "bottom,Rockomax32.BW_4294550948_0|-1.875|0_0|-1|0_0|-1.875|0_0|-1|0";
            CraftDoc.AttN n = CraftDoc.AttN.Parse(text);
            Assert.AreEqual("bottom", n.NodeId);
            Assert.AreEqual(4294550948u, n.PartId);
            Assert.AreEqual(text, n.ToString());
            Assert.AreEqual("top,Null_0_0|1|0_0|1|0_0|1|0_0|1|0", CraftDoc.AttN.Parse("top,Null_0_0|1|0_0|1|0_0|1|0_0|1|0").ToString());
        }
    }
}
