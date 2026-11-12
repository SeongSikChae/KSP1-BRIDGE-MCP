using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>One step's flight behaviour. Created per step (Plan.Step.CreateMode); never calls Warp or judges thrust itself.</summary>
    public interface IFlightMode
    {
        void Begin(Vessel v, Telemetry t);

        /// <summary>Every tick: attitude target, throttle and optional warp target UT.</summary>
        ModeCommand Update(Vessel v, Telemetry t);

        /// <summary>True once a self-completing mode is done (attitude never completes by itself).</summary>
        bool Done { get; }

        /// <summary>Internal state for the reload handover (Dictionary, List, numbers, strings only).</summary>
        Dictionary<string, object> Save();

        /// <summary>Called instead of Begin when a handed-over plan resumes.</summary>
        void Load(Vessel v, Dictionary<string, object> state);
    }

    /// <summary>What the Coordinator hands the Controller. The default value is Idle (throttle 0, no attitude), so reading it
    /// before the first Submit never sees a null.</summary>
    public struct ModeCommand
    {
        /// <summary>Attitude target; null = no attitude control.</summary>
        public AttitudeTarget Attitude;

        /// <summary>Final throttle 0..1 (after the no-thrust wait and the stager override).</summary>
        public double Throttle;

        /// <summary>Throttle as the mode produced it, before any override (filled by the Coordinator).</summary>
        public double ModeThrottle;

        /// <summary>Warp target UT requested by the mode; null = no warp.</summary>
        public double? WarpToUt;

        public ModeCommand(AttitudeTarget attitude, double throttle)
        {
            Attitude = attitude;
            Throttle = throttle;
            ModeThrottle = throttle;
            WarpToUt = null;
        }

        public static ModeCommand Idle => default(ModeCommand);
    }

    /// <summary>Attitude target of a mode. Filled in by the attitude controller unit (unit 4).</summary>
    public sealed class AttitudeTarget
    {
    }

    /// <summary>The mode wants thrust, no engine is operational and no staging is pending (ComputeCommand).</summary>
    public sealed class NoThrustException : Exception
    {
        public NoThrustException()
            : base("no operational engine and no staging pending")
        {
        }
    }
}
