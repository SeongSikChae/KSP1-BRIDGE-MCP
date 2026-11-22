using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Warp
{
    internal enum WarpProgress
    {
        Running,
        Arrived,
        Failed,
    }

    /// <summary>
    /// One warp step from start to end: keeps the rate where <see cref="WarpRule"/> wants it every physics frame (whoever changed
    /// it, D003), arrives at rate 1 off rails, and goes back to rate 1 when it ends otherwise.
    /// </summary>
    internal sealed class WarpSession
    {
        public const string GameSetting = "game_setting";
        public const string UnknownReason = "unknown";

        /// <summary>
        /// Physics frames from the start of the step in which rails are not entered (D017). The game judges acceleration by its mean
        /// over 20 frames (decompiled Vessel.accelSmoothingLength), so thrust that began just before the step does not show yet, and
        /// on rails it would be gone unnoticed.
        /// </summary>
        public const int FirstFrames = 25;

        private readonly IWarpGame game;
        private readonly WarpArgs args;
        private readonly List<string> blocked = new List<string>();
        private double startThrottle;
        private double startReal;
        private bool arrived;
        private int frames;

        public WarpSession(IWarpGame game, WarpArgs args)
        {
            this.game = game;
            this.args = args;
        }

        public double TargetUt { get; private set; }

        /// <summary>Why the step failed; null when it did not.</summary>
        public string Failure { get; private set; }

        public double MaxRailsRate { get; private set; } = 1;

        public double MaxPhysicsRate { get; private set; } = 1;

        /// <summary>Every reason rails warp was not possible while the step ran, in the order they came.</summary>
        public IReadOnlyList<string> Blocked => blocked;

        /// <summary>Why rails warp is not possible now; null when it is.</summary>
        public string BlockedNow { get; private set; }

        public double RealSeconds => game.RealTime - startReal;

        public double Remaining(double ut) => Math.Max(0, TargetUt - ut);

        public void Start(double ut)
        {
            startReal = game.RealTime;
            if (!game.HasVessel)
            {
                Failure = NoVessel;
                return;
            }
            startThrottle = game.Throttle;
            WarpTarget target = WarpTarget.From(args, ut, game.ReadOrbit);
            TargetUt = target.Ut;
            Failure = target.Failure;
        }

        /// <summary>One physics frame.</summary>
        public WarpProgress Tick(double ut)
        {
            if (Failure == null && !game.HasVessel)
                Failure = NoVessel;
            if (Failure != null)
                return WarpProgress.Failed;

            game.CancelAutoWarp();
            WarpReading now = game.Read();
            if (now.Rails)
                MaxRailsRate = Math.Max(MaxRailsRate, now.Rate);
            else
                MaxPhysicsRate = Math.Max(MaxPhysicsRate, now.Rate);

            if (ut >= TargetUt && !now.Warping && !now.OnRails)
            {
                if (game.Throttle != startThrottle)
                    game.SetThrottle(startThrottle);
                arrived = true;
                return WarpProgress.Arrived;
            }
            frames++;
            Steer(now, ut);
            return WarpProgress.Running;
        }

        /// <summary>The step ended; returns the rate that was released when it was above 1.</summary>
        public double? Stop()
        {
            if (arrived || !game.Available)
                return null;
            game.CancelAutoWarp();
            WarpReading now = game.Read();
            if (!now.Warping)
                return null;
            game.SetRate(0, instant: true);
            return now.Rate > 1 ? now.Rate : (double?)null;
        }

        private static string NoVessel => "there is no active vessel";

        private void Steer(WarpReading now, double ut)
        {
            double remaining = TargetUt - ut;
            int wanted = WarpRule.RailsWanted(remaining, game.SoiSeconds(ut), game.RailsRates, args.MaxRate);
            int limit = wanted;
            string reason = null;
            if (!game.RailsAllowed)
            {
                limit = 0;
                reason = GameSetting;
            }
            else if (!(now.Rails && now.Index > 0 && now.Index >= wanted))
            {
                // Asked also when no rate is wanted: near the target a vessel in the air is still blocked, not merely slow.
                limit = game.RailsLimit(Math.Max(1, wanted), out reason);
                reason = limit > 0 ? null : reason ?? UnknownReason;
            }

            BlockedNow = reason;
            if (reason != null && !blocked.Contains(reason))
                blocked.Add(reason);

            bool rails = reason == null;
            bool waits = frames <= FirstFrames && !now.OnRails;
            int index = rails
                ? waits ? 0 : Math.Min(wanted, limit)
                : game.PhysicsAllowed ? WarpRule.PhysicsWanted(remaining, game.PhysicsRates, args.PhysicsRate) : 0;
            Apply(WarpRule.Decide(now, rails, index, rails ? game.RailsRates : game.PhysicsRates), now);
        }

        private void Apply(WarpCommand command, WarpReading now)
        {
            if (!command.Change)
                return;
            if (command.SwitchMode)
            {
                if (now.Warping)
                    game.SetRate(0, instant: true);
                game.SetMode(command.Rails);
            }
            game.SetRate(command.Index, command.Instant);
        }
    }
}
