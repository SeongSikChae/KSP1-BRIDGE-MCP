using System;
using UnityEngine;

namespace KspBridge.Flight.Landing
{
    /// <summary>
    /// Drag model for the impact prediction (#22 design decision 3, U5b): drag force = K·ρ·v² (K m², ρ kg/m³) with
    /// K(M, ρv) = 0.5 · DragCubeMultiplier · DragMultiplier · DragCurvePseudoReynolds(ρv) · Σ AreaDrag(flow, M) · Scale,
    /// the per-part formula KSP's FlightIntegrator uses (DragCubeList.AddSurfaceDragDirection, re-implemented read-only:
    /// SetDrag changes the parts' physics state). Σ AreaDrag is tabulated once per mach step with the vessel bottom (engine
    /// side) facing the flow. In the air KSP's own per-part drag forces correct it through Scale (measured / model).
    /// </summary>
    public sealed class DragModel
    {
        /// <summary>Dynamic pressure (Pa) from which a measurement is taken.</summary>
        private const double MeasureQ = 1000;

        /// <summary>Time constant (s) of the exponential average.</summary>
        private const double Tau = 3;

        private const double MachStep = 0.1;
        private const int MachSteps = 250;   // M = 0 .. 25

        /// <summary>DragCubeList.faceDirections order: right, left, up, down, forward, back.</summary>
        private static readonly Vector3[] Faces =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        private readonly double[] _area;   // Σ AreaDrag per mach step

        /// <summary>Measured / model, 1 before any measurement.</summary>
        public double Scale { get; private set; } = 1;

        /// <summary>initial (model only) | measured (Scale measured)</summary>
        public string Source { get; private set; }

        /// <summary>Last single-tick K measurement (diagnosis), NaN before any.</summary>
        public double LastSample { get; private set; } = double.NaN;

        private DragModel(double[] area, double scale, string source)
        {
            _area = area;
            Scale = scale;
            Source = source;
        }

        /// <summary>Builds the mach table from the vessel's drag cubes (any attitude: the flow is fixed to the vessel axis).</summary>
        public static DragModel Initial(Vessel v) => new DragModel(Table(v), 1, "initial");

        /// <summary>reload handover: the table is built again from the parts, the measured scale is kept.</summary>
        internal static DragModel Restore(Vessel v, double scale, string source) =>
            new DragModel(Table(v), double.IsNaN(scale) || scale <= 0 ? 1 : scale, source == "measured" ? "measured" : "initial");

        private static double[] Table(Vessel v)
        {
            var area = new double[MachSteps + 1];
            Vector3 flow = -v.ReferenceTransform.up;   // falling bottom first the velocity points to the vessel bottom
            foreach (Part p in v.parts)
            {
                DragCubeList c = p?.DragCubes;
                if (c == null || c.None)
                {
                    continue;
                }
                Vector3 d = p.transform.InverseTransformDirection(flow);   // = AddSurfaceDragDirection(-dragVectorDirLocal)
                float[] occluded = c.AreaOccluded;
                float[] weighted = c.WeightedDrag;
                for (int m = 0; m <= MachSteps; m++)
                {
                    float mach = (float)(m * MachStep);
                    double sum = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        float dot = Vector3.Dot(d, Faces[i]);
                        float curve = PhysicsGlobals.DragCurveValue(c.SurfaceCurves, (dot + 1f) * 0.5f, mach);
                        float wd = weighted[i];
                        float cd = wd < 1f ? Mathf.Pow(c.DragCurveCd.Evaluate(wd), c.DragCurveCdPower.Evaluate(mach)) : wd;
                        sum += occluded[i] * curve * cd;
                    }
                    area[m] += sum;
                }
            }
            UnityEngine.Debug.Log("[KspBridge] land drag table: area(M0)=" + area[0].ToString("F3") + " area(M1)=" + area[10].ToString("F3") +
                " area(M7)=" + area[70].ToString("F3"));
            return area;
        }

        /// <summary>K with Scale (m²); 0 for a non-finite input.</summary>
        public double K(double mach, double rhoV) => Model(mach, rhoV) * Scale;

        /// <summary>K at the vessel's current mach number and ρv (get_flight drag_k).</summary>
        public double KNow(Vessel v) => K(v.mach, v.atmDensity * v.srfSpeed);

        private double Model(double mach, double rhoV)
        {
            if (double.IsNaN(mach) || double.IsNaN(rhoV) || double.IsInfinity(mach) || double.IsInfinity(rhoV))
            {
                return 0;
            }
            double x = Math.Max(0, Math.Min(MachSteps, mach / MachStep));
            int i = Math.Min(MachSteps - 1, (int)x);
            double area = _area[i] + (_area[i + 1] - _area[i]) * (x - i);
            double re = PhysicsGlobals.DragCurvePseudoReynolds.Evaluate((float)Math.Max(0, rhoV));
            return 0.5 * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier * re * area;
        }

        /// <summary>One physics tick: Scale from the sum of the parts' drag forces (kN) over ρ·v², divided by the model at the
        /// same mach and ρv. Blended from the first sample.</summary>
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
            double model = Model(v.mach, rho * vr);
            if (double.IsNaN(kNow) || double.IsInfinity(kNow) || kNow < 0 || !(model > 0))
            {
                return;
            }
            LastSample = kNow;
            Scale += (1 - Math.Exp(-dt / Tau)) * (kNow / model - Scale);
            Source = "measured";
        }
    }
}
