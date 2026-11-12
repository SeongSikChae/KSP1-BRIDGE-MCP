using KSP.UI.Screens;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// Stage activation = request + confirm. StageManager.ActivateStage silently does nothing while CanSeparate is false and
    /// starts a StagingCooldownTimer (0.5625 s) cooldown on success (decompile checked), so a request is confirmed by
    /// currentStage dropping on a later tick, re-sent once, then reported failed.
    /// </summary>
    public sealed class StageRequest
    {
        public enum Result
        {
            Waiting,
            Done,
            Failed,
        }

        private const int MaxTries = 2;   // one request + one re-request

        private static double ConfirmS => PhysicsGlobals.StagingCooldownTimer + 1;

        private int _stageBefore = -1;
        private double _firstUt = double.NaN;
        private double _lastSendUt = double.NaN;
        private int _tries;

        /// <summary>True once ActivateNextStage was sent for this request.</summary>
        public bool Sent => _tries > 0;

        public int StageBefore => _stageBefore;

        /// <param name="send">False in Submit's trial: register only, never call ActivateNextStage.</param>
        public Result Tick(Vessel v, double ut, bool send)
        {
            if (double.IsNaN(_firstUt))
            {
                _firstUt = ut;
                _stageBefore = v.currentStage;
            }
            if (v.currentStage < _stageBefore)
            {
                UnityEngine.Debug.Log("[KspBridge] stage request confirmed: stage " + _stageBefore + " -> " + v.currentStage + " ut " + ut + " (sent ut " + _lastSendUt + ")");
                return Result.Done;
            }
            if (!send)
            {
                return Result.Waiting;
            }
            bool expired = _tries >= MaxTries
                ? ut - _lastSendUt > ConfirmS              // not confirmed even after the re-request
                : ut - _firstUt > ConfirmS * MaxTries;      // cooldown or a lock kept us from even sending
            if (expired)
            {
                return Result.Failed;
            }
            bool due = _tries == 0 || ut - _lastSendUt > ConfirmS;   // re-request only after the confirm time; never twice a tick
            if (due && StageManager.CanSeparate)                      // during the cooldown: wait, do not send
            {
                StageManager.ActivateNextStage();
                _lastSendUt = ut;
                _tries++;
                UnityEngine.Debug.Log("[KspBridge] stage request sent: stage " + v.currentStage + " ut " + ut + " try " + _tries);
            }
            return Result.Waiting;
        }

        public Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["stage_before"] = (double)_stageBefore,
            ["first_ut"] = _firstUt,
            ["last_send_ut"] = _lastSendUt,
            ["tries"] = (double)_tries,
        };

        /// <summary>Handover Load rule: null doubles come back as NaN (a request not started yet counts from its first try again).</summary>
        public static StageRequest Load(Dictionary<string, object> d) => d == null ? null : new StageRequest
        {
            _stageBefore = (int)Handover.D(d, "stage_before", -1),
            _firstUt = Handover.D(d, "first_ut"),
            _lastSendUt = Handover.D(d, "last_send_ut"),
            _tries = (int)Handover.D(d, "tries", 0),
        };
    }
}
