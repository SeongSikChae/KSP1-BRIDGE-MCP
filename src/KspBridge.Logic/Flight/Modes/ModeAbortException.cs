using System;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// A mode ends the plan with its own reason (no_node, node_missed, no_target, target_too_far, port_incompatible, dock_failed).
    /// First step in the Submit/ReplaceSteps trial: 409 with the same code; later steps: plan_aborted with that reason.
    /// </summary>
    public sealed class ModeAbortException : Exception
    {
        public string Reason { get; }

        public ModeAbortException(string reason, string message)
            : base(message)
        {
            Reason = reason;
        }
    }
}
