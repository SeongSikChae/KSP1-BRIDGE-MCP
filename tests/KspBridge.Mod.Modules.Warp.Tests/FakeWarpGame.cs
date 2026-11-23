using KspBridge.Mod.Modules.Maneuver;
using KspBridge.Mod.Modules.Warp;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Warp.Tests
{
    /// <summary>
    /// The game's time warp as decompiled from KSP 1.12.5 TimeWarp: rates go up and down over one real second starting from the
    /// previous target rate, the game lowers a rails rate by itself, the limit before a change of the sphere of influence depends
    /// on the rate in effect, and a vessel comes off rails some frames after the rate is back to 1.
    /// </summary>
    internal sealed class FakeWarpGame : IWarpGame
    {
        public const double Frame = 0.02;

        private static readonly float[] Rails = { 1, 5, 10, 50, 100, 1000, 10000, 100000 };
        private static readonly float[] Physics = { 1, 2, 3, 4 };

        private double last = 1;
        private double lerpStart;
        private int offRailsIn;

        public double Ut = 1000;
        public bool RailsMode = true;
        public int Index;
        public double Rate = 1;
        public double TargetRate = 1;
        public bool OnRails;

        /// <summary>Frames a vessel stays on rails after the rate is back to 1.</summary>
        public int OffRailsDelay = 3;

        /// <summary>Why rails warp is not possible; null when it is.</summary>
        public string BlockReason;

        /// <summary>Highest rails index the altitude allows.</summary>
        public int AltitudeLimit = 7;

        /// <summary>UT of the coming change of the sphere of influence.</summary>
        public double? SoiUt;

        /// <summary>The bridge's FixedUpdate runs before the game's.</summary>
        public bool BridgeFirst;

        public double ThrottleNow;
        public int ThrottleWrites;
        public bool ZeroThrottleOnRails;
        public bool AutoWarp;
        public int SetRateCalls;
        public int RaiseCalls;
        public bool ThrowOnLimit;

        /// <summary>Rate with which the sphere of influence was entered.</summary>
        public double? SoiRate;

        public bool Available { get; set; } = true;

        public bool HasVessel { get; set; } = true;

        public IReadOnlyList<float> RailsRates => Rails;

        public IReadOnlyList<float> PhysicsRates => Physics;

        public bool RailsAllowed { get; set; } = true;

        public bool PhysicsAllowed { get; set; } = true;

        public double Throttle => ThrottleNow;

        public double RealTime { get; private set; }

        public OrbitReading Orbit { get; set; } = new OrbitReading
        {
            Eccentricity = 0.02, ApoapsisRadius = 720000, SphereOfInfluence = 84159286, TimeToApoapsis = 900, TimeToPeriapsis = 1800,
        };

        public WarpReading Read() => new WarpReading
        {
            Rails = RailsMode, Index = Index, Rate = Rate, Settled = Rate == Rates[Index], OnRails = OnRails,
        };

        public OrbitReading ReadOrbit() => Orbit;

        /// <summary>The node start the maneuver module would give; a failure without a node.</summary>
        public NodeStartResult Node { get; set; } = new NodeStartResult { Failure = "there is no maneuver node; make one with create_node" };

        public NodeStartResult ReadNode() => Node;

        public double? SoiSeconds(double ut) => SoiUt.HasValue ? SoiUt.Value - ut : (double?)null;

        public int RailsLimit(int index, out string reason)
        {
            if (ThrowOnLimit)
                throw new InvalidOperationException("limit");
            reason = null;
            if (BlockReason != null)
            {
                reason = BlockReason;
                return 0;
            }
            int limit = Math.Min(index, AltitudeLimit);
            if (limit < index)
                reason = "about_to_crash";
            if (SoiUt.HasValue && limit > 3)
            {
                double travel = Rate == Rails[3] ? 0 : (Rate + Rails[3]) / 2;
                if (SoiUt.Value - Ut < travel + 50)
                {
                    reason = "orbit_event_imminent";
                    limit = 3;
                }
            }
            return limit;
        }

        public void SetRate(int index, bool instant)
        {
            SetRateCalls++;
            int before = Index;
            Change(index, instant, instantIfLower: false, force: true);
            if (Index > before)
                RaiseCalls++;
        }

        public void SetMode(bool rails) => RailsMode = rails;

        public void CancelAutoWarp() => AutoWarp = false;

        public void SetThrottle(double value)
        {
            ThrottleNow = value;
            ThrottleWrites++;
        }

        private float[] Rates => RailsMode ? Rails : Physics;

        /// <summary>What the player or the game does outside the step.</summary>
        public void PlayerSets(int index) => Change(index, instant: false, instantIfLower: false, force: true);

        /// <summary>One physics frame; the bridge's part is <paramref name="bridge"/>.</summary>
        public void Step(Action bridge)
        {
            RealTime += Frame;
            Ut += Frame * Rate;
            if (SoiUt.HasValue && Ut >= SoiUt.Value)
            {
                SoiRate = Rate;
                SoiUt = null;
            }
            if (BridgeFirst)
                bridge();
            GameFixedUpdate();
            if (!BridgeFirst)
                bridge();
            LateUpdate();
        }

        private void GameFixedUpdate()
        {
            if (RailsMode && Rate > Rails[0])
                Change(Index, instant: false, instantIfLower: true, force: false);
            if (Rate != TargetRate)
            {
                double t = Math.Min(1, RealTime - lerpStart);
                Rate = t >= 1 ? TargetRate : last + (TargetRate - last) * t;
            }
        }

        private void Change(int index, bool instant, bool instantIfLower, bool force)
        {
            int to = index;
            if (RailsMode && index > 0)
            {
                to = RailsLimit(index, out string reason);
                if (to < index && instantIfLower && (reason == "in_atmosphere" || reason == "about_to_crash"))
                    instant = true;
            }
            to = Math.Max(0, Math.Min(to, Rates.Length - 1));
            if (Index == to && !force)
                return;
            Index = to;
            last = TargetRate;
            TargetRate = Rates[to];
            lerpStart = RealTime;
            if (instant)
                Rate = TargetRate;
        }

        private void LateUpdate()
        {
            bool rails = RailsMode && Rate > 1;
            if (rails)
            {
                if (!OnRails && ZeroThrottleOnRails)
                    ThrottleNow = 0;
                OnRails = true;
                offRailsIn = OffRailsDelay;
            }
            else if (OnRails && offRailsIn-- <= 0)
            {
                OnRails = false;
            }
        }
    }
}
