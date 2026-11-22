using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Control.Tests
{
    [TestClass]
    public sealed class AttitudeTargetTests
    {
        // Above the equator: up +X, north +Y, east +Z (v = ω × r keeps its usual component form).
        private static readonly Vec Up = new Vec(1, 0, 0);
        private static readonly Vec North = new Vec(0, 1, 0);
        private static readonly Vec East = new Vec(0, 0, 1);

        private static FlightFrame Frame(Vec orbit, Vec surface) => new FlightFrame
        {
            Up = Up,
            North = North,
            East = East,
            OrbitVelocity = orbit,
            SurfaceVelocity = surface,
            OrbitNormal = North * 1e9,
        };

        /// <summary>Eastward and climbing a little.</summary>
        private static readonly FlightFrame Climbing = Frame(East * 2200 + Up * 100, East * 2000 + Up * 100);

        private static void AreClose(Vec expected, Vec actual)
            => Assert.AreEqual(0, (expected.Normalized() - actual).Length, 1e-9, "expected " + expected.Normalized() + " got " + actual);

        private static Vec Nose(AttitudeDirection direction, FlightFrame frame)
        {
            TargetVectors target = AttitudeTarget.Along(direction).Resolve(frame);
            Assert.IsTrue(target.Defined);
            Assert.IsNull(target.Top);
            return target.Nose;
        }

        [TestMethod]
        public void Directions_FollowVelocityNormalAndRadial()
        {
            AreClose(East * 2200 + Up * 100, Nose(AttitudeDirection.Prograde, Climbing));
            AreClose(East * -2200 + Up * -100, Nose(AttitudeDirection.Retrograde, Climbing));
            AreClose(East * 2000 + Up * 100, Nose(AttitudeDirection.SurfacePrograde, Climbing));
            AreClose(East * -2000 + Up * -100, Nose(AttitudeDirection.SurfaceRetrograde, Climbing));
            AreClose(North, Nose(AttitudeDirection.Normal, Climbing));
            AreClose(-North, Nose(AttitudeDirection.Antinormal, Climbing));
            AreClose(Up * 2200 + East * -100, Nose(AttitudeDirection.RadialOut, Climbing));
            AreClose(Up * -2200 + East * 100, Nose(AttitudeDirection.RadialIn, Climbing));
        }

        [TestMethod]
        public void RadialOut_PointsAwayFromTheBody_AndIsSquareToTheVelocity()
        {
            foreach (double climb in new[] { -300.0, 0.0, 300.0 })
            {
                FlightFrame frame = Frame(East * 2200 + Up * climb, Vec.Zero);
                Vec radial = Nose(AttitudeDirection.RadialOut, frame);
                Assert.IsTrue(Vec.Dot(radial, Up) > 0.9);
                Assert.AreEqual(0, Vec.Dot(radial, frame.OrbitVelocity.Normalized()), 1e-9);
            }
        }

        [TestMethod]
        [DataRow(AttitudeDirectionName.SurfacePrograde)]
        [DataRow(AttitudeDirectionName.SurfaceRetrograde)]
        public void SurfaceDirections_AreUndefinedAtRest(string name)
        {
            AttitudeTarget target = ControlArgs.Attitude(Core.Json.ParseObject("{\"direction\":\"" + name + "\"}")).Target;

            Assert.IsFalse(target.Resolve(Frame(East * 175, East * 0.99)).Defined);
            Assert.IsTrue(target.Resolve(Frame(East * 175, East * 1.0)).Defined);
        }

        [TestMethod]
        public void OrbitDirections_AreUndefinedWithoutOrbitalSpeed()
        {
            FlightFrame frame = Frame(East * 0.5, East * 100);
            foreach (AttitudeDirection direction in new[]
            {
                AttitudeDirection.Prograde, AttitudeDirection.Retrograde, AttitudeDirection.Normal,
                AttitudeDirection.Antinormal, AttitudeDirection.RadialOut, AttitudeDirection.RadialIn,
            })
                Assert.IsFalse(AttitudeTarget.Along(direction).Resolve(frame).Defined, direction.ToString());
        }

        [TestMethod]
        public void Radial_IsUndefinedFlyingStraightUp_AndNormalWithoutAnOrbitPlane()
        {
            FlightFrame frame = Frame(Up * 500, Up * 500);
            frame.OrbitNormal = Vec.Zero;

            Assert.IsFalse(AttitudeTarget.Along(AttitudeDirection.RadialOut).Resolve(frame).Defined);
            Assert.IsFalse(AttitudeTarget.Along(AttitudeDirection.RadialIn).Resolve(frame).Defined);
            Assert.IsFalse(AttitudeTarget.Along(AttitudeDirection.Normal).Resolve(frame).Defined);
            Assert.IsTrue(AttitudeTarget.Along(AttitudeDirection.Prograde).Resolve(frame).Defined);
        }

        [TestMethod]
        [DataRow(0.0, 0.0, 0.0)]
        [DataRow(30.0, 45.0, 0.0)]
        [DataRow(-60.0, 200.0, 35.0)]
        [DataRow(10.0, 359.0, -170.0)]
        [DataRow(80.0, 90.0, 120.0)]
        public void PitchHeadingRoll_RoundTripsThroughTheFlightAttitude(double pitch, double heading, double roll)
        {
            TargetVectors target = AttitudeTarget.PitchHeading(pitch, heading, roll).Resolve(Climbing);
            AttitudeAngles angles = Attitude.Compute(Up, North, East, target.Nose, target.Top.Value);

            Assert.AreEqual(pitch, angles.Pitch, 1e-6);
            Assert.AreEqual(heading, angles.Heading, 1e-6);
            Assert.AreEqual(roll, angles.Roll, 1e-6);
        }

        [TestMethod]
        public void Roll_IsDefinedStraightUpAndDown()
        {
            // Heading 90, straight up: pitching up from east leaves the top facing west; rolling right by 90 turns it to the right (south).
            TargetVectors up = AttitudeTarget.PitchHeading(90, 90, 0).Resolve(Climbing);
            AreClose(Up, up.Nose);
            AreClose(-East, up.Top.Value);
            AreClose(-North, AttitudeTarget.PitchHeading(90, 90, 90).Resolve(Climbing).Top.Value);

            TargetVectors down = AttitudeTarget.PitchHeading(-90, 90, 0).Resolve(Climbing);
            AreClose(-Up, down.Nose);
            AreClose(East, down.Top.Value);
        }

        [TestMethod]
        public void WithoutRoll_ThereIsNoTop()
        {
            TargetVectors target = AttitudeTarget.PitchHeading(0, 90, null).Resolve(Climbing);
            AreClose(East, target.Nose);
            Assert.IsNull(target.Top);
        }

        [TestMethod]
        public void Describe_ShowsKindAndValues()
        {
            System.Collections.Generic.Dictionary<string, object> d = AttitudeTarget.PitchHeading(30, 45, null).Describe();
            Assert.AreEqual(30.0, d["pitch_deg"]);
            Assert.AreEqual(45.0, d["heading_deg"]);
            Assert.IsNull(d["roll_deg"]);
            Assert.AreEqual("radial_out", AttitudeTarget.Along(AttitudeDirection.RadialOut).Describe()["direction"]);
        }

        private static class AttitudeDirectionName
        {
            public const string SurfacePrograde = "surface_prograde";
            public const string SurfaceRetrograde = "surface_retrograde";
        }
    }
}
