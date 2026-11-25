using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>What the rule wants done this frame.</summary>
    internal struct WarpCommand
    {
        public static readonly WarpCommand None = default;

        /// <summary>False when nothing is to be changed.</summary>
        public bool Change;

        /// <summary>Go to rate index 0 at once and change the game's mode before setting the rate.</summary>
        public bool SwitchMode;

        /// <summary>Rails (otherwise physics); only meaningful with <see cref="SwitchMode"/>.</summary>
        public bool Rails;

        /// <summary>
        /// A rails rate raised from index 0: not set in this physics frame but remembered, and set (instant) in a later render
        /// frame's LateUpdate before the game puts vessels on rails (D01_08).
        /// </summary>
        public bool AtLateUpdate;

        public int Index;
        public bool Instant;
    }

    /// <summary>
    /// Picks the warp rate from the game time left, free of the game. Rates go down at once and up through the game's own one
    /// second change, so the rate is never above the picked one; the frames kept in hand cover the frame that notices.
    /// </summary>
    internal static class WarpRule
    {
        /// <summary>Game seconds at rate 1 kept for the vessel to come off rails before the target.</summary>
        public const double RailsMargin = 2;

        public const double PhysicsMargin = 0.2;

        /// <summary>Physics frames a rate must still fit before the target.</summary>
        public const int Frames = 10;

        /// <summary>The physics frame at rate 1 (s).</summary>
        public const double FrameSeconds = 0.02;

        /// <summary>The game goes down to this rate index before a change of the sphere of influence.</summary>
        public const int SoiIndex = 3;

        private const double SoiMargin = 50;

        /// <summary>Highest rails rate index for the time left; 0 for none.</summary>
        /// <param name="soiSeconds">Game seconds to the coming change of the sphere of influence; null when the game does not slow
        /// down for one. The step goes down before the game's own limit does (D011).</param>
        public static int RailsWanted(double remaining, double? soiSeconds, IReadOnlyList<float> rates, double? maxRate)
        {
            for (int i = rates.Count - 1; i > 0; i--)
            {
                double rate = rates[i];
                if (maxRate.HasValue && rate > maxRate.Value)
                    continue;
                if (remaining < RailsMargin + Frames * FrameSeconds * rate)
                    continue;
                if (i > SoiIndex && soiSeconds.HasValue
                    && soiSeconds.Value < (rate + rates[SoiIndex]) / 2 + SoiMargin + Frames * FrameSeconds * rate)
                    continue;
                return i;
            }
            return 0;
        }

        /// <summary>Highest physics rate index for the time left; 0 for none.</summary>
        public static int PhysicsWanted(double remaining, IReadOnlyList<float> rates, int? physicsRate)
        {
            if (!physicsRate.HasValue)
                return 0;
            for (int i = rates.Count - 1; i > 0; i--)
            {
                double rate = rates[i];
                if (rate <= physicsRate.Value && remaining >= PhysicsMargin + Frames * FrameSeconds * rate)
                    return i;
            }
            return 0;
        }

        /// <param name="now">The game's warp now.</param>
        /// <param name="rails">Whether the wanted rate is a rails rate.</param>
        /// <param name="index">The wanted rate index (0 for rate 1).</param>
        /// <param name="rates">The rates of the wanted mode.</param>
        public static WarpCommand Decide(WarpReading now, bool rails, int index, IReadOnlyList<float> rates)
        {
            if (index <= 0)
            {
                return now.Warping
                    ? new WarpCommand { Change = true, Index = 0, Instant = true }
                    : WarpCommand.None;
            }
            // A rails rate index raised from 0 is set in a render frame's LateUpdate, in effect at once, so the game puts every vessel
            // on rails in that same LateUpdate and the next physics frame finds them all on rails (D01_08). It goes by the rate index
            // only, whether the vessel is on rails or the game is still lowering the rate, and is decided before the way down.
            if (now.Rails != rails)
                return new WarpCommand { Change = true, SwitchMode = true, Rails = rails, Index = index, AtLateUpdate = rails };
            if (rails && now.Index == 0)
                return new WarpCommand { Change = true, Index = index, AtLateUpdate = true };
            if (now.Index > index || now.Rate > rates[index])
                return new WarpCommand { Change = true, Index = index, Instant = true };
            if (now.Index < index && now.Settled)
                return new WarpCommand { Change = true, Index = index };
            return WarpCommand.None;
        }
    }
}
