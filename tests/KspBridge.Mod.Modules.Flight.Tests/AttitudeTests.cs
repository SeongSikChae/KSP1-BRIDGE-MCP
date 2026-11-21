using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Flight.Tests
{
    [TestClass]
    public sealed class AttitudeTests
    {
        private static readonly Vec Up = new Vec(0, 1, 0);
        private static readonly Vec North = new Vec(0, 0, 1);
        private static readonly Vec East = new Vec(1, 0, 0);

        private static double Rad(double deg) => deg * Math.PI / 180;

        private static AttitudeAngles Of(Vec nose, Vec top) => Attitude.Compute(Up, North, East, nose, top);

        [TestMethod]
        public void StraightUp_IsPitch90()
        {
            AttitudeAngles a = Of(Up, -North);
            Assert.AreEqual(90, a.Pitch, 1e-9);
            Assert.AreEqual(0, a.Heading, 1e-9, "undefined heading is 0");
            Assert.AreEqual(0, a.Roll, 1e-9, "undefined roll is 0");
        }

        [TestMethod]
        public void LevelEast_IsHeading90_Pitch0_Roll0()
        {
            AttitudeAngles a = Of(East, Up);
            Assert.AreEqual(0, a.Pitch, 1e-9);
            Assert.AreEqual(90, a.Heading, 1e-9);
            Assert.AreEqual(0, a.Roll, 1e-9);
        }

        [TestMethod]
        public void LevelNorth_IsHeading0_AndWest_Is270()
        {
            Assert.AreEqual(0, Of(North, Up).Heading, 1e-9);
            Assert.AreEqual(270, Of(-East, Up).Heading, 1e-9);
            Assert.AreEqual(180, Of(-North, Up).Heading, 1e-9);
        }

        [TestMethod]
        public void BankRight_IsPositiveRoll_BankLeft_IsNegative()
        {
            // Facing north the right wing points east; banking right tilts the top toward the right.
            Vec right30 = Up * Math.Cos(Rad(30)) + East * Math.Sin(Rad(30));
            Vec left30 = Up * Math.Cos(Rad(30)) - East * Math.Sin(Rad(30));

            Assert.AreEqual(30, Of(North, right30).Roll, 1e-9);
            Assert.AreEqual(-30, Of(North, left30).Roll, 1e-9);
        }

        [TestMethod]
        public void Climbing_KeepsHeading_AndPitch()
        {
            Vec nose = East * Math.Cos(Rad(45)) + Up * Math.Sin(Rad(45));
            Vec top = Up * Math.Cos(Rad(45)) - East * Math.Sin(Rad(45));

            AttitudeAngles a = Of(nose, top);

            Assert.AreEqual(45, a.Pitch, 1e-9);
            Assert.AreEqual(90, a.Heading, 1e-9);
            Assert.AreEqual(0, a.Roll, 1e-9);
        }

        [TestMethod]
        public void UpsideDown_IsRoll180()
        {
            Assert.AreEqual(180, Math.Abs(Of(North, -Up).Roll), 1e-9);
        }
    }
}
