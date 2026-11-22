using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>Game values the measure_start readiness judgement looks at; read by <see cref="MeasureModule"/>, judged here.</summary>
    internal struct MeasureReadinessInput
    {
        public bool GameLoaded;

        /// <summary>The core's block reason (FlightSession.BlockReason()): scene loading, flight scene not ready.</summary>
        public string CoreBlockReason;

        public string SceneName;
        public bool InFlight;
        public bool HasActiveVessel;
    }

    /// <summary>Game events that end a running measurement.</summary>
    internal enum MeasureEvent
    {
        SceneLoadRequested,
        VesselChange,
    }

    /// <summary>Argument checks, readiness and the event-to-end-reason rule, kept free of the game so they are unit-tested.</summary>
    internal static class MeasureArgs
    {
        public const string SampleLimitKey = "sample_limit";
        public const int DefaultSampleLimit = 30000;
        public const int MaxSampleLimit = 180000;

        /// <summary>The sample limit in <paramref name="args"/> (default 30,000); bad_request unless an integer in 1..180,000 (C2·C10).</summary>
        public static int SampleLimit(Dictionary<string, object> args)
        {
            double? raw = args.Num(SampleLimitKey);
            if (!raw.HasValue)
                return DefaultSampleLimit;
            double v = raw.Value;
            if (v != Math.Floor(v) || v < 1 || v > MaxSampleLimit)
                throw BridgeException.BadRequest("'" + SampleLimitKey + "' must be an integer from 1 to "
                    + MaxSampleLimit.ToString(CultureInfo.InvariantCulture) + " (got " + v.ToString(CultureInfo.InvariantCulture) + ")");
            return (int)v;
        }

        /// <summary>Null when the tool measure_start may start now, otherwise the not_ready reason (C10). Input locks are ignored.</summary>
        public static string Readiness(MeasureReadinessInput input)
        {
            if (!input.GameLoaded)
                return "no game is loaded (now " + input.SceneName + "); load a save first";
            if (input.CoreBlockReason != null)
                return input.CoreBlockReason;
            if (!input.InFlight)
                return "measure_start needs the flight scene (now " + input.SceneName + ")";
            if (!input.HasActiveVessel)
                return "there is no active vessel";
            return null;
        }

        /// <summary>
        /// End reason for a game event (C9): a scene load requested from the flight scene into the flight scene is new_flight,
        /// any other scene load is scene_change (the core's FlightSession.OnLoadRequested rule); an active-vessel change is vessel_change.
        /// </summary>
        public static string EndReason(MeasureEvent evt, bool inFlight, bool requestedFlight)
        {
            switch (evt)
            {
                case MeasureEvent.SceneLoadRequested:
                    return inFlight && requestedFlight ? MeasureEndReasons.NewFlight : MeasureEndReasons.SceneChange;
                case MeasureEvent.VesselChange:
                    return MeasureEndReasons.VesselChange;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evt), evt, null);
            }
        }
    }
}
