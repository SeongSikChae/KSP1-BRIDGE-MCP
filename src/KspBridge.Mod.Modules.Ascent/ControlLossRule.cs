using System.Globalization;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>
    /// Control loss (spec C7, D006, D017): inside the atmosphere at a dynamic pressure of 1 kPa or more or with the engines thrusting
    /// (0.01 kN or more), the nose more than 30° off the target for 3 s of game time. A vessel that tumbles under thrust loses speed
    /// and its dynamic pressure falls below 1 kPa, so thrust alone counts too.
    /// </summary>
    internal sealed class ControlLossRule
    {
        public const double MinQKpa = 1;
        public const double MinThrustKn = 0.01;
        public const double MaxErrorDeg = 30;
        public const double HoldS = 3;

        private double? since;

        /// <summary>The failure text once the state has lasted 3 s, otherwise null.</summary>
        public string Update(double ut, bool inAtmosphere, double qKpa, double thrustKn, double errorDeg)
        {
            if (!(inAtmosphere && (qKpa >= MinQKpa || thrustKn >= MinThrustKn) && errorDeg > MaxErrorDeg))
            {
                since = null;
                return null;
            }
            if (!since.HasValue)
                since = ut;
            if (ut - since.Value < HoldS)
                return null;
            return "control lost: the nose was " + errorDeg.ToString("0.#", CultureInfo.InvariantCulture) + "° off the target for " +
                   HoldS.ToString(CultureInfo.InvariantCulture) + " s in the atmosphere (" +
                   qKpa.ToString("0.##", CultureInfo.InvariantCulture) + " kPa, thrust " +
                   thrustKn.ToString("0.#", CultureInfo.InvariantCulture) + " kN)";
        }
    }
}
