using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor.Tests
{
    [TestClass]
    public sealed class AttachMathTests
    {
        private const double Tol = 1e-5;

        internal static void Near(Vec3 expected, Vec3 actual, string what = null)
            => Assert.IsTrue(expected.ApproxEquals(actual, Tol), (what ?? "vector") + ": expected " + expected + " got " + actual);

        internal static void Near(Quat expected, Quat actual, string what = null)
            => Assert.IsTrue(Quat.Angle(expected, actual) < 0.05, (what ?? "rotation") + ": expected " + expected + " got " + actual);

        private static bool Finite(Quat q) => !double.IsNaN(q.X + q.Y + q.Z + q.W) && !double.IsInfinity(q.X + q.Y + q.Z + q.W);

        [TestMethod]
        public void LookRotation_MatchesUnity()
        {
            Near(new Quat(0, Math.Sqrt(0.5), 0, Math.Sqrt(0.5)), Quat.LookRotation(Vec3.Right, Vec3.Up), "look +X");
            Near(new Quat(0, 1, 0, 0), Quat.LookRotation(-Vec3.Forward, Vec3.Up), "look -Z");
            Near(new Quat(-Math.Sqrt(0.5), 0, 0, Math.Sqrt(0.5)), Quat.LookRotation(Vec3.Up, Vec3.Up), "look up with up (parallel)");
            Quat look = Quat.LookRotation(new Vec3(1, 0, 1), Vec3.Up);
            Near(new Vec3(1, 0, 1).Normalized, look * Vec3.Forward, "forward axis");
            Near(Vec3.Up, look * Vec3.Up, "up axis");
        }

        [TestMethod]
        public void Euler_FollowsUnityOrder()
        {
            Near(Vec3.Right, Quat.Euler(0, 90, 0) * Vec3.Forward, "yaw 90");
            Near(-Vec3.Up, Quat.Euler(90, 0, 0) * Vec3.Forward, "pitch 90");
            // Z first, then X, then Y.
            Quat e = Quat.Euler(30, 40, 50);
            Quat manual = Quat.AngleAxis(40, Vec3.Up) * Quat.AngleAxis(30, Vec3.Right) * Quat.AngleAxis(50, Vec3.Forward);
            Near(manual, e);
        }

        [TestMethod]
        public void NodeAttach_BottomToTop_HangsBelow()
        {
            Pose parent = new Pose(new Vec3(0, 10, 0), Quat.Identity);
            NodeDef bottom = new NodeDef("bottom", new Vec3(0, -1, 0), new Vec3(0, -1, 0));
            NodeDef top = new NodeDef("top", new Vec3(0, 0.5, 0), new Vec3(0, 1, 0));
            Pose child = AttachMath.NodeAttach(parent, bottom, top);
            Near(Quat.Identity, child.Rotation);
            Near(new Vec3(0, 8.5, 0), child.Position);
        }

        [TestMethod]
        public void NodeAttach_FollowsParentRotation()
        {
            Quat tilt = Quat.AngleAxis(90, Vec3.Forward);
            Pose parent = new Pose(Vec3.Zero, tilt);
            NodeDef bottom = new NodeDef("bottom", new Vec3(0, -1, 0), new Vec3(0, -1, 0));
            NodeDef top = new NodeDef("top", new Vec3(0, 0.5, 0), new Vec3(0, 1, 0));
            Pose child = AttachMath.NodeAttach(parent, bottom, top);
            Vec3 parentNode = parent.Position + parent.Rotation * bottom.Position;
            Vec3 childNode = child.Position + child.Rotation * top.Position;
            Near(parentNode, childNode, "nodes meet");
            Near(-(parent.Rotation * bottom.Direction), child.Rotation * top.Direction, "nodes face each other");
        }

        [TestMethod]
        public void NodeAttach_SameDirectionNodes_TurnsHalfWithoutNaN()
        {
            Pose parent = new Pose(Vec3.Zero, Quat.Identity);
            NodeDef top = new NodeDef("top", new Vec3(0, 1, 0), new Vec3(0, 1, 0));
            Pose child = AttachMath.NodeAttach(parent, top, top);
            Assert.IsTrue(Finite(child.Rotation), "no NaN");
            Near(new Vec3(0, -1, 0), child.Rotation * Vec3.Up, "child top points down");
            Near(new Vec3(0, 1, 0), child.Position + child.Rotation * top.Position, "child top on parent top");
        }

        [TestMethod]
        public void FromTo_Opposite_UsesPerpendicularAxis()
        {
            Quat q = Quat.FromTo(Vec3.Up, -Vec3.Up, Vec3.Right);
            Assert.IsTrue(Finite(q));
            Near(-Vec3.Up, q * Vec3.Up);
            Near(Vec3.Right, q * Vec3.Right, "turned about X");
            Quat r = Quat.FromTo(Vec3.Right, -Vec3.Right, Vec3.Right);
            Assert.IsTrue(Finite(r));
            Near(-Vec3.Right, r * Vec3.Right, "fallback axis parallel to the direction is replaced");
        }

        [TestMethod]
        public void PickChildNode_PrefersOppositeThenPairName()
        {
            NodeDef bottom = new NodeDef("bottom", Vec3.Zero, -Vec3.Up);
            NodeDef top = new NodeDef("top", Vec3.Zero, Vec3.Up);
            NodeDef down = new NodeDef("bottom", Vec3.Zero, -Vec3.Up);
            Assert.AreEqual("top", AttachMath.PickChildNode(bottom, new[] { down, top }).Value.Id);

            NodeDef other = new NodeDef("connect", Vec3.Zero, Vec3.Up);
            Assert.AreEqual("top", AttachMath.PickChildNode(bottom, new[] { other, top }).Value.Id, "tie goes to the top/bottom pair");
            Assert.AreEqual("connect", AttachMath.PickChildNode(bottom, new[] { other }).Value.Id);
            Assert.IsNull(AttachMath.PickChildNode(bottom, new NodeDef[0]));
        }

        [TestMethod]
        [DataRow(1, 0, 0)]
        [DataRow(-1, 0, 0)]
        [DataRow(0, 0, 1)]
        [DataRow(0, 0, -1)]
        [DataRow(0, 1, 0)]
        [DataRow(0, -1, 0)]
        public void SurfaceAttach_GameFormula_NodeOnHitAndOutward(int dx, int dy, int dz)
        {
            Vec3 dir = new Vec3(dx, dy, dz);
            NodeDef srf = new NodeDef("srfAttach", new Vec3(0.1, 0.2, -0.3), dir);
            Quat targetRotation = Quat.AngleAxis(20, Vec3.Up);
            Vec3 normal = targetRotation * Vec3.Right;
            Vec3 hit = targetRotation * new Vec3(1.25, 0.4, 0);
            Pose pose = AttachMath.SurfaceAttach(hit, normal, targetRotation, srf);

            Quat expected = Quat.LookRotation(normal, targetRotation * Vec3.Up) * Quat.LookRotation(dir, Vec3.Up);
            Near(expected, pose.Rotation, "EditorLogic formula");
            Near(hit, pose.Position + pose.Rotation * srf.Position, "surface node on the hit point");
            // The part-local direction the game turns onto the surface normal: LookRotation(dir, up)⁻¹ · +Z.
            Vec3 outward = pose.Rotation * (Quat.LookRotation(dir, Vec3.Up).Inverse * Vec3.Forward);
            Near(normal, outward, "faces away from the surface");
        }

        [TestMethod]
        public void RadialAngles_EvenlySpaced()
        {
            CollectionAssert.AreEqual(new List<double> { 30, 150, 270 }, AttachMath.RadialAngles(30, 3));
            CollectionAssert.AreEqual(new List<double> { 350, 80, 170, 260 }, AttachMath.RadialAngles(-10, 4));
        }

        [TestMethod]
        public void SurfaceProbe_AngleZeroIsLocalForwardClockwiseFromAbove()
        {
            Pose target = new Pose(new Vec3(0, 5, 0), Quat.Identity);
            (Vec3 origin, Vec3 direction) = AttachMath.SurfaceProbe(target, 1, 0, 10);
            Near(new Vec3(0, 6, 10), origin);
            Near(-Vec3.Forward, direction);
            (Vec3 east, _) = AttachMath.SurfaceProbe(target, 0, 90, 10);
            Near(new Vec3(10, 5, 0), east, "90° is +X");
        }

        [TestMethod]
        public void RadialMap_TurnsAboutAxis()
        {
            Pose a = new Pose(new Vec3(1, 0, 0), Quat.Identity);
            Pose b = new Pose(new Vec3(-1, 0, 0), Quat.AngleAxis(180, Vec3.Up));
            CounterpartMap map = CounterpartMap.Radial(a, b);
            Near(new Vec3(-2, 0.5, 0), map.MapPoint(new Vec3(2, 0.5, 0)));
            Near(new Vec3(0, 1, 0), map.MapVector(new Vec3(0, 1, 0)));
            Near(new Vec3(-1, 0, 0), map.MapVector(new Vec3(1, 0, 0)));
            Near(b.Rotation, map.MapRotation(a.Rotation));
            Quat turn = Quat.AngleAxis(30, Vec3.Right);
            Near(Quat.AngleAxis(-30, Vec3.Right), map.MapDelta(turn), "a turn about +X becomes one about -X on the far side");
        }

        [TestMethod]
        public void MirrorMap_ReflectsAcrossPlane()
        {
            Quat ra = Quat.AngleAxis(10, Vec3.Forward);
            Pose a = new Pose(new Vec3(1, 0, 0), ra);
            Pose b = new Pose(new Vec3(-1, 0, 0), AttachMath.MirrorRotation(ra, Vec3.Right));
            Vec3? normal = AttachMath.MirrorNormal(a, b, new Vec3(-1, 1, 1));
            Near(Vec3.Right, normal.Value.X < 0 ? -normal.Value : normal.Value);
            CounterpartMap map = CounterpartMap.Mirror(a, b, normal.Value);
            Near(new Vec3(-2, 0, 0.5), map.MapPoint(new Vec3(2, 0, 0.5)));
            Near(new Vec3(-1, 1, 0), map.MapVector(new Vec3(1, 1, 0)));
            Near(new Vec3(0, 0, 1), map.MapVector(new Vec3(0, 0, 1)), "along the plane stays");
            Near(Quat.AngleAxis(-30, Vec3.Up), map.MapDelta(Quat.AngleAxis(30, Vec3.Up)), "yaw flips");
            Near(Quat.AngleAxis(30, Vec3.Right), map.MapDelta(Quat.AngleAxis(30, Vec3.Right)), "turn about the plane normal stays");
            Near(b.Rotation, map.MapRotation(a.Rotation), "member rotation maps to the partner's");
        }

        [TestMethod]
        public void MirrorNormal_SamePlace_UsesMirrorAxisOrNothing()
        {
            Pose p = new Pose(Vec3.Zero, Quat.Identity);
            Near(Vec3.Right, AttachMath.MirrorNormal(p, p, new Vec3(-1, 1, 1)).Value);
            Assert.IsNull(AttachMath.MirrorNormal(p, p, new Vec3(1, 1, 1)));
            Assert.IsNull(AttachMath.MirrorNormal(p, p, new Vec3(-1, -1, 1)));
        }

        [TestMethod]
        public void Geo_ParsesAndFormatsCraftText()
        {
            Vec3 v = Geo.ParseVec("1.5,-2,3E-05", "pos");
            Assert.AreEqual(1.5, v.X, 1e-12);
            Assert.AreEqual(3e-5, v.Z, 1e-12);
            Assert.AreEqual(-2, Geo.ParseVec("0|-2|0", "attN").Y, 1e-12);
            Assert.AreEqual("0,1,0,0", Geo.Format(new Quat(0, 1, 0, 0)));
            Assert.AreEqual("0.5|0|-1", Geo.Format(new Vec3(0.5, 0, -1), "|"));
            Assert.ThrowsExactly<KspBridge.Mod.Core.BridgeException>(() => Geo.ParseVec("1,2", "pos"));
        }
    }
}
