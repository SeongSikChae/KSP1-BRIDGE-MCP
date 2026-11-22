using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Holds an attitude for the rest of the plan: every frame the nose is steered to the target (a direction follows its current
    /// value). While the target has no direction only the turning is stopped (D014).
    /// </summary>
    internal sealed class AttitudeHold : IPlanHold
    {
        private readonly AttitudeTarget target;

        public AttitudeHold(AttitudeTarget target) => this.target = target;

        public void Control(FlightCtrlState state, Vessel vessel)
        {
            if (vessel == null || vessel.ReferenceTransform == null)
                return;
            VesselAttitude now = VesselReader.Read(vessel);
            Vec command = AttitudeLaw.Command(Error(target, now).Rotation, now.Rate, VesselReader.Torque(vessel), now.Inertia);
            state.pitch = (float)command.X;
            state.roll = (float)command.Y;
            state.yaw = (float)command.Z;
        }

        public Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["type"] = "attitude",
            ["target"] = target.Describe(),
        };

        /// <summary>Error of <paramref name="now"/> against the target as it is now; undefined (default) without a direction.</summary>
        public static AttitudeError Error(AttitudeTarget target, VesselAttitude now)
        {
            TargetVectors wanted = target.Resolve(now.Frame);
            if (!wanted.Defined)
                return default;
            Vec? top = wanted.Top.HasValue ? AttitudeLaw.ToLocal(wanted.Top.Value, now.Right, now.Nose, now.Top) : (Vec?)null;
            return AttitudeLaw.Error(AttitudeLaw.ToLocal(wanted.Nose, now.Right, now.Nose, now.Top), top);
        }
    }
}
