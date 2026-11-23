using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>The vessel values attitude control needs, read on a physics frame.</summary>
    public struct VesselAttitude
    {
        public FlightFrame Frame;
        public Vec Right;
        public Vec Nose;
        public Vec Top;

        /// <summary>Angular velocity (rad/s) on the reference transform's local axes.</summary>
        public Vec Rate;

        /// <summary>Moment of inertia (t·m²) on the same axes.</summary>
        public Vec Inertia;
    }

    public static class VesselReader
    {
        /// <summary>Attitude, velocities, rate and inertia. The nose and top are where get_flight takes them (FlightSnapshot).</summary>
        public static VesselAttitude Read(Vessel v)
        {
            Transform reference = v.ReferenceTransform;
            return new VesselAttitude
            {
                Frame = new FlightFrame
                {
                    Up = V(v.up),
                    North = V(v.north),
                    East = V(v.east),
                    OrbitVelocity = V(v.obt_velocity),
                    SurfaceVelocity = V(v.srf_velocity),
                    OrbitNormal = V(v.orbit.h.xzy),
                },
                Right = V(reference.right),
                Nose = V(reference.up),
                Top = -V(reference.forward),
                Rate = V(v.angularVelocity),
                Inertia = V(v.MOI),
            };
        }

        /// <summary>Available control torque (kN·m) per axis, summed over the parts' torque providers like the game's SAS (D018).</summary>
        public static Vec Torque(Vessel v)
        {
            Vector3 positive = Vector3.zero;
            Vector3 negative = Vector3.zero;
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null)
                    continue;
                PartModuleList modules = part.Modules;
                for (int j = 0; j < modules.Count; j++)
                {
                    if (!(modules[j] is ITorqueProvider provider))
                        continue;
                    provider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                    positive += pos;
                    negative += neg;
                }
            }
            return AttitudeLaw.AvailableTorque(V(positive), V(negative));
        }

        private static Vec V(Vector3d v) => new Vec(v.x, v.y, v.z);

        private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);
    }
}
