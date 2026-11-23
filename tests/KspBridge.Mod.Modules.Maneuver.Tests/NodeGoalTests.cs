using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using static KspBridge.Mod.Modules.Maneuver.Tests.TestData;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeGoalTests
    {
        private static readonly Vec R100 = new Vec(Radius + 100000, 0, 0);

        private static void Bad(Action a, string contains)
        {
            try
            {
                a();
            }
            catch (BridgeException ex)
            {
                StringAssert.Contains(ex.Message, contains);
                return;
            }
            Assert.Fail("expected bad_request containing '" + contains + "'");
        }

        [TestMethod]
        public void ApoapsisFromACircularOrbitIsTheHohmannBurn()
        {
            Vec v = new Vec(0, CircularSpeed(100000), 0);
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Apoapsis, 300000, R100, v, Kerbin);
            var (ap, pe) = NodeGoal.Apsides(R100, v + dv, Gm);
            Assert.AreEqual(Radius + 300000, ap, 1e-3);
            Assert.AreEqual(Radius + 100000, pe, 1e-3);
            double r1 = Radius + 100000, r2 = Radius + 300000;
            Assert.AreEqual(Math.Sqrt(Gm / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1), dv.Length, 1e-6);
            Assert.AreEqual(0, NodeFrame.Cross(dv, v).Length, 1e-6, "prograde only");
        }

        [TestMethod]
        public void ApoapsisWithAFlightPathAngle()
        {
            Vec v = new Vec(300, 2300, 0);
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Apoapsis, 250000, R100, v, Kerbin);
            Assert.AreEqual(Radius + 250000, NodeGoal.Apsides(R100, v + dv, Gm).Apoapsis, 1e-3);
            Assert.AreEqual(0, NodeFrame.Cross(dv, v).Length / v.Length, 1e-6, "along the velocity");
        }

        [TestMethod]
        public void PeriapsisAtTheApoapsisOfAnEllipse()
        {
            // Ap 300 km · Pe 100 km, at the apoapsis: lower the periapsis to 80 km (a retrograde burn).
            Vec r = new Vec(Radius + 300000, 0, 0);
            double a = Radius + 200000;
            Vec v = new Vec(0, Math.Sqrt(Gm * (2 / r.Length - 1 / a)), 0);
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Periapsis, 80000, r, v, Kerbin);
            Assert.AreEqual(Radius + 80000, NodeGoal.Apsides(r, v + dv, Gm).Periapsis, 1e-3);
            Assert.IsTrue(Vec.Dot(dv, v) < 0);
        }

        [TestMethod]
        public void PeriapsisAtSeaLevel()
        {
            Vec v = new Vec(300, 2300, 0);
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Periapsis, 0, R100, v, Kerbin);
            Assert.AreEqual(Radius, NodeGoal.Apsides(R100, v + dv, Gm).Periapsis, 1e-3);
        }

        [TestMethod]
        public void CircularTakesTheHorizontalDirection()
        {
            Vec v = new Vec(200, 2400, 50);
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Circular, 0, R100, v, Kerbin);
            var (ap, pe) = NodeGoal.Apsides(R100, v + dv, Gm);
            Assert.AreEqual(R100.Length, ap, 1e-3);
            Assert.AreEqual(R100.Length, pe, 1e-3);
            Vec after = v + dv;
            Assert.AreEqual(0, after.X, 1e-9, "no radial velocity left");
        }

        [TestMethod]
        public void AltitudeAndSphereRanges()
        {
            Vec v = new Vec(0, CircularSpeed(100000), 0);
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Apoapsis, 90000, R100, v, Kerbin), "below the altitude at the node");
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Apoapsis, Soi, R100, v, Kerbin), "outside the sphere of influence");
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Periapsis, -1, R100, v, Kerbin), "must be >= 0");
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Periapsis, 110000, R100, v, Kerbin), "above the altitude at the node");
            Vec dv = NodeGoal.DeltaV(NodeGoalKind.Apoapsis, 100000, R100, v, Kerbin);
            Assert.AreEqual(0, dv.Length, 1e-3, "an apoapsis at the node altitude from a circular orbit needs nothing");
        }

        [TestMethod]
        public void UnreachableGoals()
        {
            // Steep climb: even near escape speed the periapsis stays far below 99 km.
            Vec steep = new Vec(2000, 500, 0);
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Periapsis, 99000, R100, steep, Kerbin), "cannot reach");
            Bad(() => NodeGoal.DeltaV(NodeGoalKind.Circular, 0, R100, new Vec(2000, 0, 0), Kerbin), "no horizontal part");
        }

        [TestMethod]
        public void ApsidesOfAnOpenOrbit()
        {
            var (ap, pe) = NodeGoal.Apsides(R100, new Vec(0, 4000, 0), Gm);
            Assert.IsTrue(double.IsPositiveInfinity(ap));
            Assert.AreEqual(R100.Length, pe, 1e-3);
        }
    }
}
