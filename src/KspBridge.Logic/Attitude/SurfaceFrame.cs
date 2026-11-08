using UnityEngine;

namespace KspBridge.Attitude
{
    /// <summary>
    /// Surface-relative attitude (MechJeb convention): pitch up from the horizon, heading clockwise from north, roll.
    /// Forward: A = Inverse(S) * R * Inverse(Euler(90,0,0)), angles from A.eulerAngles. Inverse: R = S * Euler(-pitch, heading, roll) * Euler(90,0,0).
    /// </summary>
    public static class SurfaceFrame
    {
        private static readonly Quaternion VesselToForward = Quaternion.Euler(90, 0, 0);

        /// <summary>Rotation whose forward is north and up is away from the body centre.</summary>
        public static Quaternion SurfaceRotation(Vessel v)
        {
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            Vector3d toNorthPole = v.mainBody.position + (Vector3d)v.mainBody.transform.up * v.mainBody.Radius - v.CoMD;
            Vector3d north = Vector3d.Exclude(up, toNorthPole).normalized;
            return Quaternion.LookRotation(north, up);
        }

        public static void GetAttitude(Vessel v, out double pitch, out double heading, out double roll)
        {
            Vector3 e = Quaternion.Inverse(
                VesselToForward * Quaternion.Inverse(v.GetTransform().rotation) * SurfaceRotation(v)).eulerAngles;
            pitch = e.x > 180 ? 360.0 - e.x : -(double)e.x;
            heading = e.y;
            roll = e.z > 180 ? e.z - 360.0 : (double)e.z;
        }

        /// <summary>World rotation of the vessel transform for the given surface-relative attitude.</summary>
        public static Quaternion TargetRotation(Vessel v, double pitch, double heading, double roll)
        {
            return SurfaceRotation(v) * Quaternion.Euler(-(float)pitch, (float)heading, (float)roll) * VesselToForward;
        }
    }
}
