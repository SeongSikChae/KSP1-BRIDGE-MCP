using KspBridge.Mod.Modules.Flight;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>
    /// The three directions of a node's delta-v components at one moment, built exactly as the game builds them (decompiled
    /// PatchedConicSolver.CheckNextManeuver: QuaternionD.LookRotation(vel, Cross(-pos, vel)) * DeltaV, with pos and vel the xzy state
    /// vectors; QuaternionD.LookRotation: right = normalize(Cross(up, forward)), up = Cross(forward, right)). DeltaV.x is along
    /// <see cref="Radial"/>, DeltaV.y along <see cref="Normal"/> and DeltaV.z along <see cref="Prograde"/>.
    /// </summary>
    internal readonly struct NodeFrame
    {
        private NodeFrame(Vec radial, Vec normal, Vec prograde)
        {
            Radial = radial;
            Normal = normal;
            Prograde = prograde;
        }

        /// <summary>DeltaV.x: in the orbit plane, perpendicular to the velocity (away from the body for the game's left-handed frame).</summary>
        public Vec Radial { get; }

        /// <summary>DeltaV.y: the orbit normal.</summary>
        public Vec Normal { get; }

        /// <summary>DeltaV.z: along the velocity.</summary>
        public Vec Prograde { get; }

        /// <param name="position">Position relative to the body (xzy, as the game passes it).</param>
        /// <param name="velocity">Orbital velocity (xzy).</param>
        public static NodeFrame At(Vec position, Vec velocity)
        {
            Vec forward = velocity.Normalized();
            Vec up = Cross(-position, velocity);
            Vec right = Cross(up, forward).Normalized();
            return new NodeFrame(right, Cross(forward, right), forward);
        }

        /// <summary>The delta-v vector of components (m/s).</summary>
        public Vec ToVector(double prograde, double normal, double radial) => Prograde * prograde + Normal * normal + Radial * radial;

        public double ProgradeOf(Vec dv) => Vec.Dot(dv, Prograde);

        public double NormalOf(Vec dv) => Vec.Dot(dv, Normal);

        public double RadialOf(Vec dv) => Vec.Dot(dv, Radial);

        /// <summary>The cross product with the usual component formula (Vector3d.Cross).</summary>
        public static Vec Cross(Vec a, Vec b)
            => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
