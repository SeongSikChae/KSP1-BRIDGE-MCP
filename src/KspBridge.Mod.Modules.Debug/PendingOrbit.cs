using System;

namespace KspBridge.Mod.Modules.Debug
{
    public enum OrbitState
    {
        Waiting,
        Applied,
        TimedOut,
        Cancelled,
    }

    /// <summary>
    /// The one debug_set_orbit request in progress (pure; the clock is passed in so the time-out path is unit-tested). Waiting
    /// ends in Applied (result kept), TimedOut (10 s after the start) or Cancelled (scene change, module stop).
    /// </summary>
    public sealed class PendingOrbit
    {
        public const double TimeoutSeconds = 10;

        public PendingOrbit(int id, double startTime, OrbitRequest request)
        {
            Id = id;
            StartTime = startTime;
            Request = request;
            State = OrbitState.Waiting;
        }

        public int Id { get; }
        public double StartTime { get; }
        public OrbitRequest Request { get; }
        public OrbitState State { get; private set; }
        public object Result { get; private set; }
        public string Reason { get; private set; }

        /// <summary>Moves a waiting request past its deadline to TimedOut; returns true when that just happened.</summary>
        public bool Expire(double now)
        {
            if (State != OrbitState.Waiting || now - StartTime < TimeoutSeconds)
                return false;
            State = OrbitState.TimedOut;
            Reason = "the game did not show the requested orbit (ORBITING, Ap/Pe within " + OrbitRequest.MatchTolerance +
                     " m) within " + TimeoutSeconds + " s";
            return true;
        }

        public void Apply(object result)
        {
            if (State != OrbitState.Waiting)
                throw new InvalidOperationException("orbit request " + Id + " is not waiting (" + State + ")");
            State = OrbitState.Applied;
            Result = result;
        }

        public void Cancel(string reason)
        {
            if (State != OrbitState.Waiting)
                return;
            State = OrbitState.Cancelled;
            Reason = reason;
        }

        /// <summary>Whether a new request may start: anything no longer waiting, or a waiting request past its deadline.</summary>
        public bool CanReplace(double now) => State != OrbitState.Waiting || now - StartTime >= TimeoutSeconds;
    }
}
