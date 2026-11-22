using KspBridge.Mod.Modules.Warp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Warp.Tests
{
    [TestClass]
    public sealed class WarpTargetTests
    {
        private static OrbitReading Closed() => new OrbitReading
        {
            Eccentricity = 0.02, ApoapsisRadius = 720000, SphereOfInfluence = 84159286, TimeToApoapsis = 900, TimeToPeriapsis = 1800,
        };

        private static OrbitReading Open(double timeToPe) => new OrbitReading
        {
            Eccentricity = 1.2, ApoapsisRadius = -3600000, SphereOfInfluence = 84159286,
            TimeToApoapsis = double.PositiveInfinity, TimeToPeriapsis = timeToPe,
        };

        private static WarpTarget To(WarpGoal goal, OrbitReading orbit, double lead = 0)
            => WarpTarget.From(new WarpArgs { Goal = goal, Lead = lead }, 5000, () => orbit);

        private static void Fails(WarpTarget target, string why)
        {
            Assert.IsNotNull(target.Failure);
            StringAssert.Contains(target.Failure, why);
        }

        [TestMethod]
        public void SecondsAndUt_DoNotNeedAnOrbit()
        {
            Func<OrbitReading> none = () => throw new InvalidOperationException();
            Assert.AreEqual(5600.0, WarpTarget.From(new WarpArgs { Goal = WarpGoal.Seconds, Value = 600 }, 5000, none).Ut);
            Assert.AreEqual(1.0, WarpTarget.From(new WarpArgs { Goal = WarpGoal.Ut, Value = 1 }, 5000, none).Ut);
        }

        [TestMethod]
        public void Points_WithLead()
        {
            Assert.AreEqual(5900.0, To(WarpGoal.Apoapsis, Closed()).Ut);
            Assert.AreEqual(5870.0, To(WarpGoal.Apoapsis, Closed(), 30).Ut);
            Assert.AreEqual(6800.0, To(WarpGoal.Periapsis, Closed()).Ut);
            WarpTarget past = To(WarpGoal.Apoapsis, Closed(), 5000);
            Assert.IsNull(past.Failure);
            Assert.AreEqual(900.0, past.Ut);
        }

        [TestMethod]
        public void OpenOrbit()
        {
            Fails(To(WarpGoal.Apoapsis, Open(100)), "open");
            Assert.AreEqual(5100.0, To(WarpGoal.Periapsis, Open(100)).Ut);
            Fails(To(WarpGoal.Periapsis, Open(-1)), "behind");
            OrbitReading parabola = Open(100);
            parabola.Eccentricity = 1;
            Fails(To(WarpGoal.Apoapsis, parabola), "open");
        }

        [TestMethod]
        public void ApoapsisOutsideTheSphereOfInfluence_Fails_PeriapsisDoesNot()
        {
            OrbitReading o = Closed();
            o.ApoapsisRadius = o.SphereOfInfluence;
            Fails(To(WarpGoal.Apoapsis, o), "sphere of influence");
            Assert.IsNull(To(WarpGoal.Periapsis, o).Failure);
            o.SphereOfInfluence = double.PositiveInfinity;
            Assert.IsNull(To(WarpGoal.Apoapsis, o).Failure);
        }

        [TestMethod]
        public void Landed_Fails()
        {
            OrbitReading o = Closed();
            o.LandedOrSplashed = true;
            Fails(To(WarpGoal.Apoapsis, o), "landed");
            Fails(To(WarpGoal.Periapsis, o), "landed");
        }

        [TestMethod]
        public void NoTime_Fails()
        {
            OrbitReading o = Closed();
            o.TimeToApoapsis = double.NaN;
            Fails(To(WarpGoal.Apoapsis, o), "no time");
        }
    }
}
