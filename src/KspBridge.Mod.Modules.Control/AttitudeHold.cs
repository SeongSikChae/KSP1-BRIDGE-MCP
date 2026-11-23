using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control
{
    /// <summary>
    /// Holds an attitude for the rest of the plan: every frame the nose is steered to the target (a direction follows its current
    /// value). While the target has no direction only the turning is stopped (D014). A step that steers along a changing target
    /// (ascend, circularize) changes <see cref="Target"/> and <see cref="Type"/> in place, so the hold is not released.
    /// </summary>
    public sealed class AttitudeHold : IPlanHold
    {
        public const string AttitudeType = "attitude";

        public AttitudeHold(AttitudeTarget target, string type = AttitudeType)
        {
            Target = target;
            Type = type;
        }

        /// <summary>The target steered to from the next frame on.</summary>
        public AttitudeTarget Target { get; set; }

        /// <summary>What get_plan's hold shows as type: attitude, or the name of the step that steers.</summary>
        public string Type { get; set; }

        public void Control(FlightCtrlState state, Vessel vessel)
        {
            if (vessel == null || vessel.ReferenceTransform == null)
                return;
            VesselAttitude now = VesselReader.Read(vessel);
            Vec command = AttitudeLaw.Command(Error(Target, now).Rotation, now.Rate, VesselReader.Torque(vessel), now.Inertia);
            state.pitch = (float)command.X;
            state.roll = (float)command.Y;
            state.yaw = (float)command.Z;
        }

        public Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["type"] = Type,
            ["target"] = Target.Describe(),
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
