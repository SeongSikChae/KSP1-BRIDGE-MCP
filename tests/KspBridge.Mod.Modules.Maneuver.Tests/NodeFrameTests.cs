using KspBridge.Mod.Modules.Flight;
using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeFrameTests
    {
        private static void Same(Vec expected, Vec actual)
        {
            Assert.AreEqual(expected.X, actual.X, 1e-12);
            Assert.AreEqual(expected.Y, actual.Y, 1e-12);
            Assert.AreEqual(expected.Z, actual.Z, 1e-12);
        }

        [TestMethod]
        public void AxesAsTheGameBuildsThem()
        {
            // LookRotation(forward = vel, up = Cross(-pos, vel)): right = normalize(Cross(up, forward)), up = Cross(forward, right).
            NodeFrame f = NodeFrame.At(new Vec(700000, 0, 0), new Vec(0, 2246, 0));
            Same(new Vec(0, 1, 0), f.Prograde);
            Same(new Vec(1, 0, 0), f.Radial);    // away from the body
            Same(new Vec(0, 0, -1), f.Normal);   // Cross(-r, v) normalized
        }

        [TestMethod]
        public void OrthonormalWithAFlightPathAngle()
        {
            NodeFrame f = NodeFrame.At(new Vec(500000, 300000, 100000), new Vec(-800, 1500, 900));
            Assert.AreEqual(1, f.Prograde.Length, 1e-12);
            Assert.AreEqual(1, f.Radial.Length, 1e-12);
            Assert.AreEqual(1, f.Normal.Length, 1e-12);
            Assert.AreEqual(0, Vec.Dot(f.Prograde, f.Radial), 1e-12);
            Assert.AreEqual(0, Vec.Dot(f.Prograde, f.Normal), 1e-12);
            Assert.AreEqual(0, Vec.Dot(f.Radial, f.Normal), 1e-12);
            Assert.IsTrue(Vec.Dot(f.Radial, new Vec(500000, 300000, 100000)) > 0, "radial leans away from the body");
        }

        [TestMethod]
        public void ComponentsRoundTrip()
        {
            NodeFrame f = NodeFrame.At(new Vec(500000, 300000, 100000), new Vec(-800, 1500, 900));
            Vec dv = f.ToVector(12.5, -3, 7);
            Assert.AreEqual(12.5, f.ProgradeOf(dv), 1e-9);
            Assert.AreEqual(-3, f.NormalOf(dv), 1e-9);
            Assert.AreEqual(7, f.RadialOf(dv), 1e-9);
        }

        [TestMethod]
        public void CrossIsTheUsualFormula() => Same(new Vec(0, 0, 1), NodeFrame.Cross(new Vec(1, 0, 0), new Vec(0, 1, 0)));
    }
}
