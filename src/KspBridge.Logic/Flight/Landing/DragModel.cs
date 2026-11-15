using System;
using UnityEngine;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// One drag coefficient for the impact prediction (#22 design decision 3): drag accel = K·ρ·v²/m (K m², ρ kg/m³, m kg).
    /// Initial value from the drag cubes with the vessel bottom facing the flow; in the air it is blended with KSP's own
    /// per-part drag forces (no velocity differencing, thrust model or frame correction).
    /// </summary>
    public sealed class DragModel
    {
        /// <summary>Dynamic pressure (Pa) from which a measurement is taken.</summary>
        private const double MeasureQ = 1000;

        /// <summary>Time constant (s) of the exponential average.</summary>
        private const double Tau = 3;

        public double K { get; private set; }

        /// <summary>initial | measured</summary>
        public string Source { get; private set; }

        /// <summary>Last single-tick measurement (diagnosis), NaN before any.</summary>
        public double LastSample { get; private set; } = double.NaN;

        private DragModel(double k, string source)
        {
            K = k;
            Source = source;
        }

        /// <summary>Drag cube area with the vessel bottom (engine side, vessel axis) facing the flow, whatever the current
        /// attitude. The direction follows FlightIntegrator's SetDrag (the drag direction, opposite the velocity): falling
        /// bottom first the drag points to the vessel top.</summary>
        public static DragModel Initial(Vessel v)
        {
            Vector3 dragDir = v.ReferenceTransform.up;
            double area = 0;
            foreach (Part p in v.parts)
            {
                if (p == null || p.DragCubes == null)
                {
                    continue;
                }
                area += p.DragCubes.GetCubeAreaDir(p.transform.InverseTransformDirection(dragDir));
            }
            double k = 0.5 * area * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier;
            UnityEngine.Debug.Log("[KspBridge] land drag initial: area=" + area.ToString("F3") + " k=" + k.ToString("F4"));
            return new DragModel(k, "initial");
        }

        /// <summary>One physics tick: K from the sum of the parts' drag forces (kN) over ρ·v². Blended from the first sample.</summary>
        public void Measure(Vessel v, double dt)
        {
            double rho = v.atmDensity;
            double vr = v.srfSpeed;
            if (v.dynamicPressurekPa * 1000 < MeasureQ || dt <= 0 || rho <= 0 || vr < 1)
            {
                return;
            }
            double force = 0;
            foreach (Part p in v.parts)
            {
                if (p != null)
                {
                    force += p.dragScalar;
                }
            }
            double kNow = force * 1000 / (rho * vr * vr);
            if (double.IsNaN(kNow) || double.IsInfinity(kNow) || kNow < 0)
            {
                return;
            }
            LastSample = kNow;
            K += (1 - Math.Exp(-dt / Tau)) * (kNow - K);
            Source = "measured";
        }
    }
}
