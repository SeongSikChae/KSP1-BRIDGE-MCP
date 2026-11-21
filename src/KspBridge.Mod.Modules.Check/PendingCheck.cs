using System;

namespace KspBridge.Mod.Modules.Check
{
    public enum CheckState
    {
        Waiting,
        Done,
        TimedOut,
        Cancelled,
    }

    /// <summary>
    /// The one check in progress (pure; the clock is passed in so the time-out path is unit-tested). Waiting ends in Done (result
    /// kept until taken), TimedOut (10 s after the start) or Cancelled (scene change, game exception).
    /// </summary>
    public sealed class PendingCheck
    {
        public const double TimeoutSeconds = 10;

        public PendingCheck(int id, double startTime)
        {
            Id = id;
            StartTime = startTime;
            State = CheckState.Waiting;
        }

        public int Id { get; }
        public double StartTime { get; }
        public CheckState State { get; private set; }
        public object Result { get; private set; }
        public string Reason { get; private set; }

        /// <summary>Moves a waiting check past its deadline to TimedOut; returns true when that just happened.</summary>
        public bool Expire(double now)
        {
            if (State != CheckState.Waiting || now - StartTime < TimeoutSeconds)
                return false;
            State = CheckState.TimedOut;
            Reason = "the game's delta-v calculator did not finish within " + TimeoutSeconds + " s";
            return true;
        }

        public void Complete(object result)
        {
            if (State != CheckState.Waiting)
                throw new InvalidOperationException("check " + Id + " is not waiting (" + State + ")");
            State = CheckState.Done;
            Result = result;
        }

        public void Cancel(string reason)
        {
            if (State != CheckState.Waiting)
                return;
            State = CheckState.Cancelled;
            Reason = reason;
        }

        /// <summary>
        /// Whether a new check may replace this one: anything that is no longer waiting (a result nobody took, a time-out, a
        /// cancel) or a waiting check past its deadline. A waiting check within its 10 s blocks a new one.
        /// </summary>
        public bool CanReplace(double now) => State != CheckState.Waiting || now - StartTime >= TimeoutSeconds;
    }
}
