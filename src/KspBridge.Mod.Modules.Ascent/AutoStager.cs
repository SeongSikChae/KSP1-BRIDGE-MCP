using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Control;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>
    /// Carries out the staging rule's decision (spec C6): at most once per second of game time, again on the next frame when the
    /// game refuses, and a failure when the stage activated no part. Records stage_activated with the reason.
    /// </summary>
    internal sealed class AutoStager
    {
        public const double SpacingS = 1;

        private readonly EventLog events;
        private readonly int planId;
        private double last = double.NegativeInfinity;

        public AutoStager(EventLog events, int planId)
        {
            this.events = events;
            this.planId = planId;
        }

        /// <summary>Stages this step activated.</summary>
        public int Count { get; private set; }

        /// <summary>A failure text when staging went wrong, otherwise null.</summary>
        public string Tick(double ut, StageReason reason, Vessel vessel)
        {
            if (reason == StageReason.None || ut - last < SpacingS)
                return null;
            StageAttempt attempt = StageActivator.ActivateNext(vessel);
            if (attempt.Stage.HasValue)
            {
                last = ut;
                Count++;
                events.Add("stage_activated", planId, new Dictionary<string, object>
                {
                    ["stage"] = attempt.Stage.Value,
                    ["reason"] = StageRule.Name(reason),
                });
                return null;
            }
            return attempt.Refused ? null : "automatic staging failed: " + attempt.Failure;
        }
    }
}
