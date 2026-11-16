using System;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// Out-of-atmosphere correction burn solver (#22 design decision 5): the horizontal Δv that moves the predicted impact
    /// onto the target. 2x2 Jacobian of the impact's local north/east offset by finite differences (1 m/s downrange and
    /// crossrange), one Newton step. Main thread only.
    /// </summary>
    public static class ImpactCorrection
    {
        private const double Deg = Math.PI / 180;

        /// <summary>|det J| below this (m² per (m/s)²) is singular.</summary>
        private const double MinDet = 1e-6;

        /// <summary>false: no impact for one of the three integrations, or a singular Jacobian. dvWorld is inertial, world axes.
        /// r: position relative to the body centre; vel: inertial velocity (both now).</summary>
        public static bool Solve(CelestialBody body, Vector3d r, Vector3d vel, double ut, double mass, DragModel drag,
                                 double tLat, double tLon, out Vector3d dvWorld)
        {
            dvWorld = Vector3d.zero;
            Vector3d up = r.normalized;
            Vector3d down = Vector3d.Exclude(up, vel);
            if (down.magnitude < 1e-3)
            {
                return false;   // no horizontal motion: downrange is undefined
            }
            down = down.normalized;
            Vector3d cross = Vector3d.Cross(up, down);
            // decision 12-2 2nd stage: KSP's integration; the brake start is not known yet, so ×2 in the whole atmosphere
            PhysStep step = PhysStep.Applied(double.PositiveInfinity);
            Impact i0 = ImpactPredictor.Predict(body, r, vel, ut, mass, drag, step: step);
            Impact iD = ImpactPredictor.Predict(body, r, vel + down, ut, mass, drag, step: step);
            Impact iC = ImpactPredictor.Predict(body, r, vel + cross, ut, mass, drag, step: step);
            if (i0 == null || iD == null || iC == null)
            {
                return false;
            }
            Offset(body, i0, tLat, tLon, out double n0, out double e0);
            Offset(body, iD, tLat, tLon, out double nD, out double eD);
            Offset(body, iC, tLat, tLon, out double nC, out double eC);
            // J columns: impact change per 1 m/s downrange and crossrange; dv = −J⁻¹·e
            double a = nD - n0, b = nC - n0, c = eD - e0, d = eC - e0;
            double det = a * d - b * c;
            if (Math.Abs(det) < MinDet || double.IsNaN(det))
            {
                return false;
            }
            double xD = -(d * n0 - b * e0) / det;
            double xC = -(-c * n0 + a * e0) / det;
            dvWorld = down * xD + cross * xC;
            return true;
        }

        /// <summary>Local north/east metres of the impact relative to the target (sea-level sphere, small offsets).</summary>
        private static void Offset(CelestialBody body, Impact i, double tLat, double tLon, out double north, out double east)
        {
            north = (i.Lat - tLat) * Deg * body.Radius;
            east = LandingTarget.Wrap180(i.Lon - tLon) * Deg * body.Radius * Math.Cos(tLat * Deg);
        }
    }
}
