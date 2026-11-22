using KspBridge.Mod.Modules.Flight;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>Pitch, yaw and roll rates in °/s with the spec's signs (D008).</summary>
    internal readonly struct AngularRates
    {
        public AngularRates(double pitch, double yaw, double roll)
        {
            Pitch = pitch;
            Yaw = yaw;
            Roll = roll;
        }

        /// <summary>Nose moving toward the vessel's top is +.</summary>
        public double Pitch { get; }

        /// <summary>Nose moving toward the vessel's right is +.</summary>
        public double Yaw { get; }

        /// <summary>Right wing moving down is +.</summary>
        public double Roll { get; }
    }

    /// <summary>
    /// Converts the game's angular velocity (Vessel.angularVelocity: rad/s in the reference transform's local axes, nose +Y,
    /// top −Z, right +X) into <see cref="AngularRates"/>. With v = ω × r the nose tip (0, 1, 0) moves at (−ωz, 0, ωx) and the
    /// right wing tip (1, 0, 0) at (0, ωz, −ωy), so pitch = −ωx, yaw = −ωz, roll = −ωy (plan.md "각속도 부호 변환").
    /// </summary>
    internal static class AngularRate
    {
        private const double RadToDeg = 180 / System.Math.PI;

        public static AngularRates FromLocal(Vec omega)
            => new AngularRates(-omega.X * RadToDeg, -omega.Z * RadToDeg, -omega.Y * RadToDeg);
    }
}
