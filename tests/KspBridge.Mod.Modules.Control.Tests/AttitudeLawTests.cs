using KspBridge.Mod.Modules.Flight;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Control.Tests
{
    [TestClass]
    public sealed class AttitudeLawTests
    {
        private const double Deg = Math.PI / 180;

        private static readonly Vec One = new Vec(1, 1, 1);

        [TestMethod]
        public void ToLocal_UsesRightNoseAndMinusTop()
        {
            Vec right = new Vec(0, 0, 1);
            Vec nose = new Vec(1, 0, 0);
            Vec top = new Vec(0, -1, 0);
            Vec local = AttitudeLaw.ToLocal(new Vec(2, 3, 4), right, nose, top);

            Assert.AreEqual(4, local.X);
            Assert.AreEqual(2, local.Y);
            Assert.AreEqual(3, local.Z);
        }

        [TestMethod]
        public void Error_IsZeroOnTarget()
        {
            AttitudeError error = AttitudeLaw.Error(new Vec(0, 1, 0), new Vec(0, 0, -1));

            Assert.IsTrue(error.Defined);
            Assert.AreEqual(0, error.Rotation.Length, 1e-12);
            Assert.AreEqual(0, error.AngleDeg, 1e-12);
            Assert.AreEqual(0, error.RollDeg.Value, 1e-12);
        }

        [TestMethod]
        [DataRow(1.0, 0.0)]    // toward the right side
        [DataRow(-1.0, 0.0)]
        [DataRow(0.0, 1.0)]    // away from the top
        [DataRow(0.0, -1.0)]   // toward the top
        public void Error_TurnsTheNoseTowardTheTarget(double x, double z)
        {
            Vec target = new Vec(x, 1, z).Normalized();
            AttitudeError error = AttitudeLaw.Error(target, null);

            Assert.AreEqual(45, error.AngleDeg, 1e-9);
            Assert.IsNull(error.RollDeg);
            Assert.AreEqual(0, error.Rotation.Y);
            // v = ω × r: turning along the rotation moves the nose toward the target.
            Vec motion = AttitudeLaw.Cross(error.Rotation, new Vec(0, 1, 0));
            Assert.IsTrue(Vec.Dot(motion, target) > 0);
        }

        [TestMethod]
        public void Error_Opposite_TurnsAboutPitch()
        {
            AttitudeError error = AttitudeLaw.Error(new Vec(0, -1, 0), null);

            Assert.AreEqual(180, error.AngleDeg, 1e-9);
            Assert.AreEqual(Math.PI, error.Rotation.X, 1e-9);
            Assert.AreEqual(0, error.Rotation.Z);
        }

        [TestMethod]
        [DataRow(30.0)]
        [DataRow(-90.0)]
        [DataRow(170.0)]
        public void Error_Roll_TurnsTheTopOntoTheTarget(double degrees)
        {
            // The top (0, 0, -1) turned about the nose (+Y) by the angle, with v = ω × r.
            double a = degrees * Deg;
            Vec top = new Vec(-Math.Sin(a), 0, -Math.Cos(a));
            AttitudeError error = AttitudeLaw.Error(new Vec(0, 1, 0), top);

            Assert.AreEqual(degrees, error.RollDeg.Value, 1e-9);
            Assert.AreEqual(a, error.Rotation.Y, 1e-9);
        }

        [TestMethod]
        public void AvailableTorque_TakesTheLargerSum_AndNeverNegative()
        {
            Vec torque = AttitudeLaw.AvailableTorque(new Vec(5, -3, -1), new Vec(-5, 2, -4));

            Assert.AreEqual(5, torque.X, "opposite signs do not cancel");
            Assert.AreEqual(2, torque.Y);
            Assert.AreEqual(0, torque.Z, "negative on both sides counts as none");
        }

        [TestMethod]
        public void Command_IsOppositeToTheNeededTorque_OnEveryAxis()
        {
            Vec command = AttitudeLaw.Command(new Vec(0.5, -0.5, 0.5), Vec.Zero, One, One);

            Assert.IsTrue(command.X < 0);
            Assert.IsTrue(command.Y > 0);
            Assert.IsTrue(command.Z < 0);
        }

        [TestMethod]
        public void Command_WithoutError_StopsTheTurning()
        {
            Vec command = AttitudeLaw.Command(Vec.Zero, new Vec(0.1, -0.1, 0), One, One);

            Assert.IsTrue(command.X > 0);
            Assert.IsTrue(command.Y < 0);
            Assert.AreEqual(0, command.Z);
        }

        [TestMethod]
        public void Command_StaysInRange_AndIsZeroWithoutTorqueOrInertia()
        {
            Assert.AreEqual(-1, AttitudeLaw.Command(new Vec(3, 0, 0), Vec.Zero, One * 0.001, One * 1000).X);
            Assert.AreEqual(1, AttitudeLaw.Command(new Vec(-3, 0, 0), new Vec(5, 0, 0), One * 0.001, One * 1000).X);

            Vec none = AttitudeLaw.Command(One, One, new Vec(0, 1, 0), new Vec(1, 0, 0));
            Assert.AreEqual(0, none.X);
            Assert.AreEqual(0, none.Y);
            Assert.AreEqual(0, none.Z);
        }

        [TestMethod]
        public void Aligned_Edges()
        {
            AttitudeError error = AttitudeLaw.Error(new Vec(Math.Sin(2 * Deg), Math.Cos(2 * Deg), 0), null);
            Assert.IsTrue(AttitudeLaw.Aligned(error, 0.5, 2.0001, 0.5));
            Assert.IsFalse(AttitudeLaw.Aligned(error, 0.5, 1.9999, 0.5));
            Assert.IsFalse(AttitudeLaw.Aligned(error, 0.5001, 2.0001, 0.5));

            AttitudeError rolled = AttitudeLaw.Error(new Vec(0, 1, 0), new Vec(Math.Sin(3 * Deg), 0, -Math.Cos(3 * Deg)));
            Assert.AreEqual(-3, rolled.RollDeg.Value, 1e-9);
            Assert.IsFalse(AttitudeLaw.Aligned(rolled, 0, 2, 0.5), "the roll counts when it is asked for");
            Assert.IsTrue(AttitudeLaw.Aligned(rolled, 0, 3.0001, 0.5));

            Assert.IsFalse(AttitudeLaw.Aligned(default, 0, 180, 1000), "never aligned without a direction (D014)");
        }

        [TestMethod]
        [DataRow(30.0, 45.0, 0.0, 10.0, 20.0)]
        [DataRow(-80.0, 300.0, 120.0, 10.0, 20.0)]
        [DataRow(5.0, 270.0, -90.0, 10.0, 1.0)]       // rolls easily
        [DataRow(0.0, 90.0, 0.0, 0.5, 20.0)]           // weak: slow turn
        public void RigidBody_SettlesOnPitchHeadingRoll(double pitch, double heading, double roll, double torque, double inertia)
            => Settles(AttitudeTarget.PitchHeading(pitch, heading, roll), torque, new Vec(inertia, inertia / 10, inertia));

        [TestMethod]
        [DataRow((int)AttitudeDirection.Prograde)]
        [DataRow((int)AttitudeDirection.Retrograde)]
        [DataRow((int)AttitudeDirection.Antinormal)]
        [DataRow((int)AttitudeDirection.RadialIn)]
        public void RigidBody_SettlesOnDirections(int direction)
            => Settles(AttitudeTarget.Along((AttitudeDirection)direction), 10, new Vec(50, 5, 50));

        [TestMethod]
        public void RigidBody_SpinsDownWithoutADirection()
        {
            Body body = new Body { Rate = new Vec(0.2, -0.3, 0.1) };
            for (int i = 0; i < 1000; i++)
                body.Step(AttitudeLaw.Command(Vec.Zero, body.Rate, One * 10, One * 20), One * 10, One * 20);
            Assert.IsTrue(AttitudeLaw.RateDps(body.Rate) < 0.1);
        }

        /// <summary>From straight up with a small spin, must settle within 300 s of simulated time and stay there.</summary>
        private static void Settles(AttitudeTarget target, double torqueSize, Vec inertia)
        {
            FlightFrame frame = new FlightFrame
            {
                Up = new Vec(1, 0, 0),
                North = new Vec(0, 1, 0),
                East = new Vec(0, 0, 1),
                OrbitVelocity = new Vec(50, 0, 2200),
                SurfaceVelocity = new Vec(50, 0, 2000),
                OrbitNormal = new Vec(0, 1, 0),
            };
            Vec torque = One * torqueSize;
            Body body = new Body { Rate = new Vec(0.02, 0.05, -0.03) };
            int settledFor = 0;
            for (int i = 0; i < 15000; i++)
            {
                TargetVectors vectors = target.Resolve(frame);
                Vec? top = vectors.Top.HasValue ? body.ToLocal(vectors.Top.Value) : (Vec?)null;
                AttitudeError error = AttitudeLaw.Error(body.ToLocal(vectors.Nose), top);
                bool aligned = AttitudeLaw.Aligned(error, AttitudeLaw.RateDps(body.Rate), ControlArgs.DefaultToleranceDeg, ControlArgs.DefaultSettleRateDps);
                settledFor = aligned ? settledFor + 1 : 0;
                if (settledFor == 1000)
                    return;   // 20 s in place
                body.Step(AttitudeLaw.Command(error.Rotation, body.Rate, torque, inertia), torque, inertia);
            }
            Assert.Fail("not settled in 300 s");
        }

        /// <summary>Rigid body in 0.02 s frames: local torque = −command × torque, turning with v = ω × r.</summary>
        private sealed class Body
        {
            private const double Dt = 0.02;

            private Vec right = new Vec(0, 0, 1);
            private Vec nose = new Vec(1, 0, 0);
            private Vec top = new Vec(0, -1, 0);

            public Vec Rate;

            public Vec ToLocal(Vec v) => AttitudeLaw.ToLocal(v, right, nose, top);

            public void Step(Vec command, Vec torque, Vec inertia)
            {
                Rate = new Vec(
                    Rate.X - command.X * torque.X / inertia.X * Dt,
                    Rate.Y - command.Y * torque.Y / inertia.Y * Dt,
                    Rate.Z - command.Z * torque.Z / inertia.Z * Dt);
                Vec world = right * Rate.X + nose * Rate.Y - top * Rate.Z;
                nose = (nose + AttitudeLaw.Cross(world, nose) * Dt).Normalized();
                top = top + AttitudeLaw.Cross(world, top) * Dt;
                top = (top - nose * Vec.Dot(top, nose)).Normalized();
                right = AttitudeLaw.Cross(nose, -top).Normalized();   // X = Y × Z with Z = −top
            }
        }
    }
}
