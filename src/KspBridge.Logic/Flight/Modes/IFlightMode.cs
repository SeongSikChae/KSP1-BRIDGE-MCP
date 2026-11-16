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

        /// <summary>Step end: completion, plan replacement, abort/release, a failed handover or a failed trial (the trial's own
        /// mode only). Called once; undoes game state the mode changed. v may be null; never throws on purpose.</summary>
        void End(Vessel v);
    }

    /// <summary>What the Coordinator hands the Controller. The default value is Idle (throttle 0, no attitude), so reading it
    /// before the first Submit never sees a null.</summary>
    public struct ModeCommand
    {
        /// <summary>Attitude target; null = no attitude control.</summary>
        public AttitudeTarget? Attitude;

        /// <summary>Final throttle 0..1 (after the no-thrust wait and the stager override).</summary>
        public double Throttle;

        /// <summary>Throttle as the mode produced it, before any override (filled by the Coordinator).</summary>
        public double ModeThrottle;

        /// <summary>Warp target UT requested by the mode; null = no warp.</summary>
        public double? WarpToUt;

        /// <summary>#22 decision 11: in the atmosphere the Coordinator may run our physics warp ×2 toward WarpToUt (land free
        /// fall with a steady attitude). Rails above the atmosphere do not need it.</summary>
        public bool PhysicsWarpOk;

        /// <summary>0070: once our physics warp runs, it is kept while this holds (a wider attitude bound than PhysicsWarpOk, so a
        /// value drifting slowly across the start bound does not start and stop it every tick). Warp also keeps it while
        /// PhysicsWarpOk holds, so a mode that leaves this false behaves as before.</summary>
        public bool PhysicsWarpKeepOk;

        /// <summary>Diagnostic only: the values PhysicsWarpOk was judged from (logged when our physics warp starts or stops).</summary>
        public string PhysicsWarpWhy;

        /// <summary>Diagnostic only (0070): the attitude error (deg) and angular rate (rad/s) judged this tick; NaN when not given.
        /// Warp logs their maximum over each of our ×2 runs.</summary>
        public double PhysicsWarpAttDeg, PhysicsWarpRate;

        /// <summary>RCS translation in the rcs_translate tool frame (x right, y top/dorsal, z nose), each [-1, 1]; null = none.</summary>
        public UnityEngine.Vector3? Translate;

        /// <summary>The mode thrusts on purpose before the axis is aligned (land after a forced start): the flight monitor
        /// pauses its attitude-error timers meanwhile.</summary>
        public bool Aligning;

        public ModeCommand(AttitudeTarget? attitude, double throttle)
        {
            Attitude = attitude;
            Throttle = throttle;
            ModeThrottle = throttle;
            WarpToUt = null;
            PhysicsWarpOk = false;
            PhysicsWarpKeepOk = false;
            PhysicsWarpWhy = null;
            PhysicsWarpAttDeg = double.NaN;
            PhysicsWarpRate = double.NaN;
            Translate = null;
            Aligning = false;
        }

        public static ModeCommand Idle => default(ModeCommand);
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
