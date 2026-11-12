using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>set_time_warp: time warp like the player's keys, outside any flight plan (the plan's Warp sees it as the player).</summary>
    public static class TimeWarpControl
    {
        /// <summary>0 stops (a pending or running warp-to too, like the stop key); otherwise switches rails/physics at rate 0
        /// and sets the rate. Returns the state right after; the game may cap the rate (altitude, acceleration).</summary>
        public static object Set(int rateIndex, bool physics)
        {
            TimeWarp w = TimeWarp.fetch ?? throw new ApiException(409, "not_available", "time warp is not available");
            if (rateIndex == 0)
            {
                w.setAutoWarp = false;   // a WarpTo that has not started yet (CancelAutoWarp acts only once it runs)
                w.CancelAutoWarp();
                TimeWarp.SetRate(0, true);
                return Describe();
            }
            if (Warp.Pending)
            {
                throw new ApiException(409, "warp_to_running", "a warp-to is running; stop it with rate_index 0 first");
            }
            TimeWarp.Modes mode = physics ? TimeWarp.Modes.LOW : TimeWarp.Modes.HIGH;
            float[] rates = physics ? w.physicsWarpRates : w.warpRates;
            if (rateIndex < 0 || rateIndex >= rates.Length)
            {
                throw ApiException.BadRequest("rate_index must be an integer in [0, " + (rates.Length - 1) + "]");
            }
            if (TimeWarp.WarpMode != mode)
            {
                if (!Warp.CanSetMode)
                {
                    throw new ApiException(409, "not_available", "switching rails/physics warp is not available");
                }
                bool stopped = TimeWarp.CurrentRateIndex > 0;
                if (stopped)
                {
                    TimeWarp.SetRate(0, true);   // the game switches modes only at a low rate (and may go back to rails by itself)
                }
                if (TimeWarp.WarpMode != mode && !Warp.SetMode(w, mode))
                {
                    throw new ApiException(409, "mode_switch_refused", "the game refused to switch to " + (physics ? "physics" : "rails") +
                        " warp" + (stopped ? " (the previous warp was stopped: rate_index 0)" : ""));
                }
            }
            TimeWarp.SetRate(rateIndex, true);
            return Describe();
        }

        /// <summary>get_flight.time_warp and the set_time_warp result.</summary>
        public static Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["mode"] = TimeWarp.WarpMode == TimeWarp.Modes.LOW ? "physics" : "rails",
            ["rate_index"] = TimeWarp.CurrentRateIndex,
            ["rate"] = System.Math.Round((double)TimeWarp.CurrentRate, 3),
            ["auto_warp"] = Warp.Pending,   // warp-to (ours or the player's) pending or running
        };
    }
}
