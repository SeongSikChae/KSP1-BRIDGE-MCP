namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>
    /// The next apoapsis and periapsis of a conic after a moment, from its elements alone (spec C3 D011·D012, C2 at): whatever segment
    /// end the game gave the orbit. The time since periapsis ObT is the game's Orbit.getObtAtUT at that moment.
    /// </summary>
    internal static class ConicTimes
    {
        /// <summary>A point this close (s) after the moment counts as the moment itself, so the next one is a period later.</summary>
        public const double SameMomentS = 1e-3;

        /// <summary>The UT of the first periapsis strictly after <paramref name="ut"/>; null for an open orbit past its periapsis.</summary>
        public static double? NextPeriapsis(double ut, double eccentricity, double period, double obt)
        {
            if (eccentricity < 1)
                return ut + Ahead(Phase(obt, period), period, period);
            return obt < -SameMomentS ? ut - obt : (double?)null;
        }

        /// <summary>The UT of the first apoapsis strictly after <paramref name="ut"/>; null for an open orbit.</summary>
        public static double? NextApoapsis(double ut, double eccentricity, double period, double obt)
        {
            if (!(eccentricity < 1))
                return null;
            return ut + Ahead(Phase(obt, period), period / 2, period);
        }

        /// <summary>Time since periapsis folded into [0, period).</summary>
        private static double Phase(double obt, double period)
        {
            double o = obt % period;
            return o < 0 ? o + period : o;
        }

        /// <summary>Seconds from phase <paramref name="o"/> to the point at phase <paramref name="at"/>, more than the same-moment slack.</summary>
        private static double Ahead(double o, double at, double period)
        {
            double dt = at - o;
            if (dt < 0)
                dt += period;
            if (dt <= SameMomentS)
                dt += period;
            return dt;
        }
    }
}
