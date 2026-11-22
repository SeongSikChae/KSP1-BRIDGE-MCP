using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary><see cref="IWarpGame"/> on the running game.</summary>
    internal sealed class GameWarp : IWarpGame
    {
        /// <summary>The check SetRate itself makes, with its reason. It is private, and a copy of it could disagree with the game.</summary>
        private static readonly MethodInfo LimitMethod =
            typeof(TimeWarp).GetMethod("getMaxOnRailsRateIdx", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The active vessel of this frame.</summary>
        public Vessel Vessel { get; set; }

        public bool Available => TimeWarp.fetch != null;

        public bool HasVessel => Vessel != null;

        public IReadOnlyList<float> RailsRates => TimeWarp.fetch.warpRates;

        public IReadOnlyList<float> PhysicsRates => TimeWarp.fetch.physicsWarpRates;

        public bool RailsAllowed => HighLogic.CurrentGame.Parameters.Flight.CanTimeWarpHigh;

        public bool PhysicsAllowed => HighLogic.CurrentGame.Parameters.Flight.CanTimeWarpLow;

        public double Throttle => Vessel.ctrlState.mainThrottle;

        public double RealTime => Time.realtimeSinceStartup;

        public WarpState State => FlightSnapshot.Warp(Vessel);

        public WarpReading Read()
        {
            bool rails = TimeWarp.WarpMode == TimeWarp.Modes.HIGH;
            int index = TimeWarp.CurrentRateIndex;
            return new WarpReading
            {
                Rails = rails,
                Index = index,
                Rate = TimeWarp.CurrentRate,
                Settled = TimeWarp.CurrentRate == (rails ? RailsRates : PhysicsRates)[index],
                OnRails = Vessel != null && Vessel.packed,
            };
        }

        public OrbitReading ReadOrbit()
        {
            Orbit orbit = Vessel.orbit;
            return new OrbitReading
            {
                LandedOrSplashed = Vessel.LandedOrSplashed,
                Eccentricity = orbit.eccentricity,
                ApoapsisRadius = orbit.ApR,
                SphereOfInfluence = Vessel.mainBody.sphereOfInfluence,
                TimeToApoapsis = orbit.timeToAp,
                TimeToPeriapsis = orbit.timeToPe,
            };
        }

        public double? SoiSeconds(double ut)
        {
            // The conditions of the game's own limit (the end of getMaxOnRailsRateIdx).
            if (!GameSettings.ORBIT_WARP_DOWN_AT_SOI || !Vessel.PatchedConicsAttached
                || Vessel.orbit.patchEndTransition == Orbit.PatchTransitionType.FINAL)
                return null;
            return Vessel.orbit.EndUT - ut;
        }

        public int RailsLimit(int index, out string reason)
        {
            if (LimitMethod == null)
                throw new InvalidOperationException("this game version has no TimeWarp.getMaxOnRailsRateIdx");
            object[] arguments = { index, true, null };
            int limit = (int)LimitMethod.Invoke(TimeWarp.fetch, arguments);
            reason = ReasonName((ClearToSaveStatus)arguments[2]);
            return limit;
        }

        public void SetRate(int index, bool instant)
        {
            // The game sets its time scale with the rate. A paused game must stay paused: a plan can end while it is (D012).
            bool paused = Time.timeScale == 0;
            TimeWarp.SetRate(index, instant, postScreenMessage: false);
            if (paused)
                Time.timeScale = 0;
        }

        public void SetMode(bool rails) => TimeWarp.fetch.Mode = rails ? TimeWarp.Modes.HIGH : TimeWarp.Modes.LOW;

        public void CancelAutoWarp()
        {
            TimeWarp.fetch.setAutoWarp = false;
            TimeWarp.fetch.CancelAutoWarp();
        }

        public void SetThrottle(double value)
        {
            // FlightInputHandler.state is what the game carries from frame to frame; ctrlState is what the vessel uses until then.
            FlightInputHandler.state.mainThrottle = (float)value;
            Vessel.ctrlState.mainThrottle = (float)value;
        }

        private static string ReasonName(ClearToSaveStatus status)
        {
            if (status == ClearToSaveStatus.CLEAR)
                return null;
            string name = status.ToString();
            foreach (string prefix in new[] { "NOT_WHILE_", "NOT_" })
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return name.Substring(prefix.Length).ToLowerInvariant();
            }
            return name.ToLowerInvariant();
        }
    }
}
