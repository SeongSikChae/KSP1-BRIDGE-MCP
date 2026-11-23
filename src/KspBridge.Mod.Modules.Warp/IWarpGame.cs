using KspBridge.Mod.Modules.Maneuver;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>The game's time warp at one moment.</summary>
    internal sealed class WarpReading
    {
        /// <summary>The game's rails mode (otherwise physics).</summary>
        public bool Rails;

        public int Index;

        /// <summary>Rate in effect, also while the game is changing it.</summary>
        public double Rate;

        /// <summary>The game is not changing the rate.</summary>
        public bool Settled;

        /// <summary>The active vessel is out of the physics.</summary>
        public bool OnRails;

        public bool Warping => Index > 0 || Rate > 1;
    }

    /// <summary>What a warp step needs from the game.</summary>
    internal interface IWarpGame
    {
        /// <summary>The game has a time warp to control (false while a scene is torn down).</summary>
        bool Available { get; }

        bool HasVessel { get; }

        IReadOnlyList<float> RailsRates { get; }

        IReadOnlyList<float> PhysicsRates { get; }

        /// <summary>The game's settings allow rails warp.</summary>
        bool RailsAllowed { get; }

        /// <summary>The game's settings allow physics warp.</summary>
        bool PhysicsAllowed { get; }

        double Throttle { get; }

        /// <summary>Real time (s).</summary>
        double RealTime { get; }

        WarpReading Read();

        OrbitReading ReadOrbit();

        /// <summary>The first maneuver node's start checks and burn start (maneuver module, D014).</summary>
        NodeStartResult ReadNode();

        /// <summary>Game seconds to the coming change of the sphere of influence; null when the game does not slow down for one.</summary>
        double? SoiSeconds(double ut);

        /// <summary>Highest rails rate index up to <paramref name="index"/> the game allows now; the reason says why 0.</summary>
        int RailsLimit(int index, out string reason);

        void SetRate(int index, bool instant);

        void SetMode(bool rails);

        /// <summary>Ends the game's own warp-to, running or about to start.</summary>
        void CancelAutoWarp();

        void SetThrottle(double value);
    }
}
