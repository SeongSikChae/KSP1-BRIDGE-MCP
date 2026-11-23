using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>
    /// How long a delta-v takes, to center a burn on the apoapsis. From the game's delta-v calculator stages, highest first, with the
    /// rocket equation inside a stage (constant mass flow); otherwise from the running engines' acceleration.
    /// </summary>
    public static class BurnEstimate
    {
        /// <summary>Seconds, or null when it cannot be estimated (no stage data and no running engine).</summary>
        public static double? Seconds(double dv, IReadOnlyList<StageBurn> stages, double accel)
        {
            if (!(dv > 0))
                return 0;
            double left = dv;
            double time = 0;
            if (stages != null)
            {
                foreach (StageBurn s in stages)
                {
                    if (!(s.DvVac > 0 && s.BurnTime > 0 && s.StartMass > s.EndMass && s.EndMass > 0))
                        continue;
                    if (left <= s.DvVac)
                    {
                        double ve = s.DvVac / Math.Log(s.StartMass / s.EndMass);
                        double end = s.StartMass * Math.Exp(-left / ve);
                        return time + s.BurnTime * (s.StartMass - end) / (s.StartMass - s.EndMass);
                    }
                    time += s.BurnTime;
                    left -= s.DvVac;
                }
            }
            if (accel > 0)
                return time + left / accel;
            return time > 0 ? time : (double?)null;
        }
    }
}
