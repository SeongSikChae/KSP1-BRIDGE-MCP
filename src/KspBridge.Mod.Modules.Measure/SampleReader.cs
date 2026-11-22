using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>
    /// Reads one <see cref="Sample"/> from the active vessel (KSP side), every physics frame. Throttle, SAS/RCS, mass, thrust,
    /// attitude, altitude and speeds are read where get_flight reads them (FlightSnapshot.Take); control inputs from
    /// Vessel.ctrlState and angular velocity from Vessel.angularVelocity (plan.md "표본 항목").
    /// The part loop walks each part's module list directly and reuses one staged-part list, so a frame allocates nothing per part.
    /// </summary>
    internal sealed class SampleReader
    {
        private readonly List<StagedPart> staged = new List<StagedPart>();

        public Sample Read(Vessel v)
        {
            Transform reference = v.ReferenceTransform;
            Vec nose = reference != null ? V(reference.up) : Vec.Zero;
            Vec top = reference != null ? -V(reference.forward) : Vec.Zero;
            AttitudeAngles attitude = Attitude.Compute(V(v.up), V(v.north), V(v.east), nose, top);
            AngularRates rates = AngularRate.FromLocal(V(v.angularVelocity));
            FlightCtrlState c = v.ctrlState;

            double thrust = 0;
            staged.Clear();
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (p == null)
                    continue;
                PartModuleList modules = p.Modules;
                for (int j = 0; j < modules.Count; j++)
                {
                    if (modules[j] is ModuleEngines engine)
                        thrust += engine.finalThrust;
                }
                if (IsStaged(p))
                    staged.Add(new StagedPart(p.inverseStage, p.State == PartStates.ACTIVE || p.State == PartStates.DEACTIVATED));
            }

            return new Sample
            {
                Ut = Planetarium.GetUniversalTime(),
                Throttle = c != null ? c.mainThrottle : 0,
                Sas = v.ActionGroups[KSPActionGroup.SAS],
                Rcs = v.ActionGroups[KSPActionGroup.RCS],
                PitchInput = c != null ? c.pitch : 0,
                YawInput = c != null ? c.yaw : 0,
                RollInput = c != null ? c.roll : 0,
                MassT = v.GetTotalMass(),
                ThrustKn = thrust,
                PitchDeg = attitude.Pitch,
                HeadingDeg = attitude.Heading,
                RollDeg = attitude.Roll,
                PitchRateDps = rates.Pitch,
                YawRateDps = rates.Yaw,
                RollRateDps = rates.Roll,
                Stage = StageRef.Judge(v.currentStage, staged).LastActivated,
                AltitudeM = v.altitude,
                VerticalSpeedMps = v.verticalSpeed,
                SurfaceSpeedMps = v.srfSpeed,
                OrbitalSpeedMps = v.obt_speed,
            };
        }

        /// <summary>
        /// Same rule as the flight module's FlightSnapshot.IsStaged (src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs), which is
        /// internal there (D010): the part has a staging icon and staging is on, so a fired separator is not counted.
        /// </summary>
        private static bool IsStaged(Part part)
            => (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;

        private static Vec V(Vector3d v) => new Vec(v.x, v.y, v.z);

        private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);
    }
}
