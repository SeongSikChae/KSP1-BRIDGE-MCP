using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Measure.Tests
{
    /// <summary>
    /// Checks the sign rule against v = ω × r computed directly: in the reference transform's local axes the nose is +Y,
    /// the top −Z and the right wing +X (D008: nose toward top is + pitch, nose toward right is + yaw, right wing down is + roll).
    /// </summary>
    [TestClass]
    public sealed class AngularRateTests
    {
        private static readonly Vec Nose = new Vec(0, 1, 0);
        private static readonly Vec Top = new Vec(0, 0, -1);
        private static readonly Vec Right = new Vec(1, 0, 0);
        private static readonly Vec Down = -Top;

        private static Vec Cross(Vec a, Vec b) => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static void Check(Vec omega)
        {
            AngularRates rates = AngularRate.FromLocal(omega);
            double toDeg = 180 / Math.PI;
            double pitch = Vec.Dot(Cross(omega, Nose), Top) * toDeg;
            double yaw = Vec.Dot(Cross(omega, Nose), Right) * toDeg;
            double roll = Vec.Dot(Cross(omega, Right), Down) * toDeg;
            Assert.AreEqual(pitch, rates.Pitch, 1e-9, "pitch for " + omega);
            Assert.AreEqual(yaw, rates.Yaw, 1e-9, "yaw for " + omega);
            Assert.AreEqual(roll, rates.Roll, 1e-9, "roll for " + omega);
        }

        [TestMethod]
        public void EachAxis_MatchesTheCrossProduct()
        {
            Check(new Vec(1, 0, 0));
            Check(new Vec(-1, 0, 0));
            Check(new Vec(0, 1, 0));
            Check(new Vec(0, -1, 0));
            Check(new Vec(0, 0, 1));
            Check(new Vec(0, 0, -1));
            Check(new Vec(0.3, -0.7, 0.2));
        }

        [TestMethod]
        public void NoseUp_IsPositivePitch()
        {
            // ω = (−1, 0, 0): nose tip velocity (0, 0, −1) points toward the top.
            AngularRates rates = AngularRate.FromLocal(new Vec(-1, 0, 0));
            Assert.AreEqual(180 / Math.PI, rates.Pitch, 1e-9);
            Assert.AreEqual(0, rates.Yaw, 1e-12);
            Assert.AreEqual(0, rates.Roll, 1e-12);
        }

        [TestMethod]
        public void NoseRight_IsPositiveYaw()
        {
            AngularRates rates = AngularRate.FromLocal(new Vec(0, 0, -1));
            Assert.IsTrue(rates.Yaw > 0);
            Assert.AreEqual(0, rates.Pitch, 1e-12);
        }

        [TestMethod]
        public void RightWingDown_IsPositiveRoll()
        {
            AngularRates rates = AngularRate.FromLocal(new Vec(0, -1, 0));
            Assert.IsTrue(rates.Roll > 0);
            Assert.AreEqual(0, rates.Pitch, 1e-12);
        }
    }
}
