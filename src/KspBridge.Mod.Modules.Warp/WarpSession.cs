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
        private readonly OrbitCheck check = new OrbitCheck();
        private double startThrottle;
        private double startReal;
        private bool arrived;
        private int frames;
        private readonly Action lateUpdate;

        /// <summary>The rails rate index decided in a physics frame and not set yet; set in the next LateUpdate (D01_08).</summary>
        private int? pending;

        public WarpSession(IWarpGame game, WarpArgs args)
        {
            this.game = game;
            this.args = args;
            lateUpdate = OnLateUpdate;
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

        /// <summary>The orbit check of the step (R01).</summary>
        public OrbitCheck Check => check;

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
            check.Begin(game.ReadCheck(ut));
            WarpTarget target = WarpTarget.From(args, ut, game.ReadOrbit, game.ReadNode);
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
            CheckReading reading = game.ReadCheck(ut);
            check.Observe(reading);
            if (now.Rails)
                MaxRailsRate = Math.Max(MaxRailsRate, now.Rate);
            else
                MaxPhysicsRate = Math.Max(MaxPhysicsRate, now.Rate);

            if (ut >= TargetUt && !now.Warping && !now.OnRails)
            {
                Drop();
                if (game.Throttle != startThrottle)
                    game.SetThrottle(startThrottle);
                arrived = true;
                string changed = check.Complete(reading);
                if (changed != null)
                {
                    Failure = changed;
                    return WarpProgress.Failed;
                }
                return WarpProgress.Arrived;
            }
            frames++;
            Steer(now, ut);
            return WarpProgress.Running;
        }

        /// <summary>The step ended; returns the rate that was released when it was above 1.</summary>
        public double? Stop()
        {
            Drop();
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
            WarpCommand command = WarpRule.Decide(now, rails, index, rails ? game.RailsRates : game.PhysicsRates);
            // A rate waiting for the LateUpdate is kept only while the rule still wants one raised from 0 (D01_08); anything else,
            // no command included, drops it and is followed in this physics frame.
            if (!(command.Change && command.AtLateUpdate))
                Drop();
            Apply(command, now);
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
            if (command.AtLateUpdate)
            {
                // D01_08: the rate is set in a later LateUpdate, not in this physics frame, so the game itself puts every vessel on
                // rails in the LateUpdate that sees it and no physics frame advances UT with a vessel still in the physics.
                if (!pending.HasValue)
                    game.AddLateUpdate(lateUpdate);
                pending = command.Index;
                return;
            }
            game.SetRate(command.Index, command.Instant);
        }

        /// <summary>The game's LateUpdate before OrbitPhysicsManager (D01_08).</summary>
        private void OnLateUpdate()
        {
            if (!pending.HasValue)
            {
                game.RemoveLateUpdate(lateUpdate);
                return;
            }
            // While the game overrides the orbits (a contract or mission makes a vessel) it puts no vessel on rails, so a rate set now
            // would run vessels in the physics at a rails rate. Ask again in the next render frame.
            if (game.OverrideOrbit)
                return;
            int index = pending.Value;
            pending = null;
            game.RemoveLateUpdate(lateUpdate);
            game.SetRate(index, instant: true);
        }

        private void Drop()
        {
            if (!pending.HasValue)
                return;
            pending = null;
            game.RemoveLateUpdate(lateUpdate);
        }
    }
}
