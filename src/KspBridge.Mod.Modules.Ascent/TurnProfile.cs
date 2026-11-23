using System;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>The pitch of the gravity turn (spec C3, D002) and the ascend step's start checks on it (spec C7).</summary>
    internal sealed class TurnProfile
    {
        public const double DefaultStartWithAtmosphere = 500;
        public const double DefaultStartWithoutAtmosphere = 50;
        public const double EndShareOfAtmosphere = 0.65;
        public const double EndShareOfClimb = 0.1;
        public const double Shape = 0.6;
        public const double SagWindowS = 30;
        public const double SagMaxDeg = 20;

        private readonly double? atmosphereTop;

        public TurnProfile(double startAltitude, double targetAp, double? atmosphereTop, double? turnStartM, double? turnEndM)
        {
            this.atmosphereTop = atmosphereTop;
            TargetAp = targetAp;
            Start = startAltitude + (turnStartM ?? (atmosphereTop.HasValue ? DefaultStartWithAtmosphere : DefaultStartWithoutAtmosphere));
            End = turnEndM ?? (atmosphereTop.HasValue
                ? atmosphereTop.Value * EndShareOfAtmosphere
                : Start + (targetAp - Start) * EndShareOfClimb);
        }

        public double TargetAp { get; }

        /// <summary>Turn start altitude S (sea level, m).</summary>
        public double Start { get; }

        /// <summary>Turn end altitude E (sea level, m).</summary>
        public double End { get; }

        /// <summary>Why the step cannot start, or null.</summary>
        public string StartFailure(double bodyRadius, double soiRadius)
        {
            if (atmosphereTop.HasValue && TargetAp <= atmosphereTop.Value)
                return "the target apoapsis " + F(TargetAp) + " m is not above the atmosphere (" + F(atmosphereTop.Value) + " m)";
            if (bodyRadius + TargetAp >= soiRadius)
                return "the target apoapsis is outside the sphere of influence";
            if (End <= Start)
                return "the turn end altitude " + F(End) + " m is not above the turn start altitude " + F(Start) + " m";
            return null;
        }

        /// <summary>90 − 90 × f^0.6 with f the smaller of the altitude and apoapsis shares (altitude share only without air).</summary>
        public double Pitch(double altitude, double apoapsis)
        {
            double f = Clamp01((altitude - Start) / (End - Start));
            if (atmosphereTop.HasValue)
            {
                double span = atmosphereTop.Value - Start;
                double apShare = span > 0 ? Clamp01((apoapsis - Start) / span) : 1;
                f = Math.Min(f, apShare);
            }
            return 90 - 90 * Math.Pow(f, Shape);
        }

        /// <summary>
        /// Extra pitch so a weak upper stage does not sink: before the target is reached, once the apoapsis is above the atmosphere
        /// (or the turn end without air), up to 20° as the apoapsis comes within 30 s, and 20° once past it going down.
        /// </summary>
        public double Sag(bool reached, double apoapsis, double timeToAp, double verticalSpeed)
        {
            if (reached || !(apoapsis > (atmosphereTop ?? End)))
                return 0;
            if (verticalSpeed < 0)
                return SagMaxDeg;
            return SagMaxDeg * Clamp01((SagWindowS - timeToAp) / SagWindowS);
        }

        private static double Clamp01(double x) => double.IsNaN(x) ? 0 : x < 0 ? 0 : x > 1 ? 1 : x;

        private static string F(double x) => x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
