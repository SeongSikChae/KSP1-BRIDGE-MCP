using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>A part's pose in the editor (world position and rotation, as written to "pos"/"rot" in a craft).</summary>
    public readonly struct Pose
    {
        public Pose(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vec3 Position { get; }
        public Quat Rotation { get; }
    }

    /// <summary>An attach node of a part in the part's own space.</summary>
    public readonly struct NodeDef
    {
        public NodeDef(string id, Vec3 position, Vec3 direction)
        {
            Id = id;
            Position = position;
            Direction = direction;
        }

        public string Id { get; }
        public Vec3 Position { get; }
        public Vec3 Direction { get; }
    }

    /// <summary>How a symmetry group maps one member's pose onto another (radial: a rotation, mirror: a reflection).</summary>
    public sealed class CounterpartMap
    {
        private readonly bool mirror;
        private readonly Pose from;
        private readonly Pose to;
        private readonly Quat q;
        private readonly Vec3 normal;

        private CounterpartMap(bool mirror, Pose from, Pose to, Quat q, Vec3 normal)
        {
            this.mirror = mirror;
            this.from = from;
            this.to = to;
            this.q = q;
            this.normal = normal;
        }

        /// <summary>Radial: the counterpart is the source turned by Q = to.rot · from.rot⁻¹ about the group's axis.</summary>
        public static CounterpartMap Radial(Pose from, Pose to)
            => new CounterpartMap(false, from, to, (to.Rotation * from.Rotation.Inverse).Normalized, Vec3.Zero);

        /// <summary>Mirror: the counterpart is the source reflected across the plane with <paramref name="planeNormal"/>.</summary>
        public static CounterpartMap Mirror(Pose from, Pose to, Vec3 planeNormal)
            => new CounterpartMap(true, from, to, Quat.Identity, planeNormal.Normalized);

        /// <summary>Where a point attached to the source ends up on the counterpart.</summary>
        public Vec3 MapPoint(Vec3 p)
            => mirror ? to.Position + AttachMath.Reflect(p - from.Position, normal) : to.Position + q * (p - from.Position);

        /// <summary>Displacement of the source mapped to the counterpart.</summary>
        public Vec3 MapVector(Vec3 v) => mirror ? AttachMath.Reflect(v, normal) : q * v;

        /// <summary>
        /// Rotation of a part attached to the source mapped to the counterpart. Mirror keeps whatever constant local turn the
        /// game gives the mirrored member: r ↦ M(r · from⁻¹) · to, with M the reflection-conjugate of a rotation.
        /// </summary>
        public Quat MapRotation(Quat r)
            => mirror ? (AttachMath.MirrorRotation(r * from.Rotation.Inverse, normal) * to.Rotation).Normalized : (q * r).Normalized;

        /// <summary>A world rotation delta applied to the source, as applied to the counterpart.</summary>
        public Quat MapDelta(Quat delta)
            => mirror ? AttachMath.MirrorRotation(delta, normal) : (q * delta * q.Inverse).Normalized;
    }

    /// <summary>
    /// KSP-independent attach geometry, with the editor's own formulas (EditorLogic): stack attach turns the new part so its
    /// node faces the target node (FromToRotation(new node, −target node)); surface attach is
    /// LookRotation(normal, target up) · LookRotation(srf node direction, up) and the node point sits on the hit point.
    /// </summary>
    public static class AttachMath
    {
        /// <summary>
        /// Pose of a new part whose node <paramref name="childNode"/> attaches to <paramref name="parentNode"/> of a part at
        /// <paramref name="parent"/>. The new part starts aligned with the parent and is turned so its node direction is the
        /// opposite of the parent node's; when the two point the same way (e.g. top on top) the turn is 180° about local X.
        /// </summary>
        public static Pose NodeAttach(Pose parent, NodeDef parentNode, NodeDef childNode)
        {
            Quat local = Quat.FromTo(childNode.Direction, -parentNode.Direction, Vec3.Right);
            Quat rotation = (parent.Rotation * local).Normalized;
            Vec3 nodeWorld = parent.Position + parent.Rotation * parentNode.Position;
            return new Pose(nodeWorld - rotation * childNode.Position, rotation);
        }

        /// <summary>
        /// The free node of a new part to attach to <paramref name="parentNode"/>: the one pointing most nearly opposite the
        /// parent node (as the parts are aligned before the turn), ties broken by the top/bottom pair. Null when there is none.
        /// </summary>
        public static NodeDef? PickChildNode(NodeDef parentNode, IEnumerable<NodeDef> freeChildNodes)
        {
            Vec3 wanted = (-parentNode.Direction).Normalized;
            string pair = Pair(parentNode.Id);
            NodeDef? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (NodeDef node in freeChildNodes)
            {
                Vec3 dir = node.Direction.Normalized;
                double score = dir.Length < 0.5 ? -2 : Vec3.Dot(dir, wanted);
                if (node.Id == pair)
                    score += 1e-6;
                if (score > bestScore + 1e-9)
                {
                    best = node;
                    bestScore = score;
                }
            }
            return best;
        }

        private static string Pair(string id)
        {
            if (id == "top")
                return "bottom";
            if (id == "bottom")
                return "top";
            return null;
        }

        /// <summary>
        /// Pose of a part surface-attached at <paramref name="hitPoint"/> (surface <paramref name="hitNormal"/>) of a target
        /// with rotation <paramref name="targetRotation"/>; <paramref name="srfNode"/> is the new part's surface node.
        /// </summary>
        public static Pose SurfaceAttach(Vec3 hitPoint, Vec3 hitNormal, Quat targetRotation, NodeDef srfNode)
        {
            Quat rotation = (Quat.LookRotation(hitNormal, targetRotation * Vec3.Up) * Quat.LookRotation(srfNode.Direction, Vec3.Up)).Normalized;
            return new Pose(hitPoint - rotation * srfNode.Position, rotation);
        }

        /// <summary>Angles (degrees) of an n-way radial symmetry starting at <paramref name="angle"/>.</summary>
        public static List<double> RadialAngles(double angle, int count)
            => Enumerable.Range(0, count).Select(k => Normalize(angle + k * 360.0 / count)).ToList();

        private static double Normalize(double degrees)
        {
            double d = degrees % 360.0;
            return d < 0 ? d + 360.0 : d;
        }

        /// <summary>
        /// Surface probe for <paramref name="height"/> (m along the target's local up from its origin) and
        /// <paramref name="angle"/> (degrees about the axis; 0 = local +Z, clockwise seen from above): a ray from far outside
        /// toward the axis. Returns origin and direction in world space.
        /// </summary>
        public static (Vec3 origin, Vec3 direction) SurfaceProbe(Pose target, double height, double angle, double reach)
        {
            double rad = angle * Math.PI / 180.0;
            // Seen from above (+Y down onto the XZ plane, Unity left-handed): clockwise from +Z goes toward +X.
            Vec3 outward = new Vec3(Math.Sin(rad), 0, Math.Cos(rad));
            Vec3 axisPoint = target.Position + target.Rotation * new Vec3(0, height, 0);
            Vec3 worldOut = target.Rotation * outward;
            return (axisPoint + worldOut * reach, -worldOut);
        }

        /// <summary>Reflection of a vector across the plane through the origin with unit <paramref name="normal"/>.</summary>
        public static Vec3 Reflect(Vec3 v, Vec3 normal) => v - 2 * Vec3.Dot(v, normal) * normal;

        /// <summary>
        /// S·R·S for the reflection S across the plane with <paramref name="normal"/>: same angle, axis reflected as a
        /// pseudovector (vector part v ↦ 2(v·n)n − v).
        /// </summary>
        public static Quat MirrorRotation(Quat r, Vec3 normal)
        {
            Vec3 v = new Vec3(r.X, r.Y, r.Z);
            Vec3 m = 2 * Vec3.Dot(v, normal) * normal - v;
            return new Quat(m.X, m.Y, m.Z, r.W).Normalized;
        }

        /// <summary>
        /// Plane of a mirror pair: perpendicular to the line between the two members. When they coincide, the axis whose
        /// <paramref name="mir"/> component is −1 (in world space) is used; null when that is not unique either.
        /// </summary>
        public static Vec3? MirrorNormal(Pose a, Pose b, Vec3 mir)
        {
            Vec3 d = b.Position - a.Position;
            if (d.Length > 1e-4)
                return d.Normalized;
            int flips = (mir.X < 0 ? 1 : 0) + (mir.Y < 0 ? 1 : 0) + (mir.Z < 0 ? 1 : 0);
            if (flips != 1)
                return null;
            return mir.X < 0 ? Vec3.Right : mir.Y < 0 ? Vec3.Up : Vec3.Forward;
        }
    }
}
