using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class LoadedGameCheckTests
    {
        private static readonly Guid A = Guid.NewGuid();
        private static readonly Guid B = Guid.NewGuid();
        private static readonly Guid C = Guid.NewGuid();

        private static LoadedGameFacts Good(params Guid[] ids) => new LoadedGameFacts
        {
            GameCompatible = true,
            FlightStateCompatible = true,
            ProtoVesselIds = new List<Guid>(ids),
        };

        private static int Pass(LoadedGameFacts f, Guid pid)
        {
            Assert.IsNull(LoadedGameCheck.Check(f, pid, out int index));
            return index;
        }

        private static string Fail(LoadedGameFacts f, Guid pid)
        {
            string reason = LoadedGameCheck.Check(f, pid, out int index);
            Assert.IsNotNull(reason);
            Assert.AreEqual(-1, index);
            return reason;
        }

        [TestMethod]
        public void FindsActiveVesselByPid_AnyPosition()
        {
            Assert.AreEqual(0, Pass(Good(A, B, C), A));
            Assert.AreEqual(1, Pass(Good(A, B, C), B));
            Assert.AreEqual(2, Pass(Good(A, B, C), C));
        }

        [TestMethod]
        public void FindsActiveVessel_WhenGameDroppedVessels()
        {
            // The file had A, X, B, C; the game dropped crewless EVA X while loading.
            Assert.AreEqual(1, Pass(Good(A, B, C), B));
        }

        [TestMethod]
        public void Refuses_EachBadResult()
        {
            Fail(null, A);
            Fail(new LoadedGameFacts { GameNull = true }, A);

            LoadedGameFacts incompatible = Good(A);
            incompatible.GameCompatible = false;
            StringAssert.Contains(Fail(incompatible, A), "incompatible");

            LoadedGameFacts noFlight = Good(A);
            noFlight.FlightStateNull = true;
            StringAssert.Contains(Fail(noFlight, A), "no flight state");

            LoadedGameFacts oldFlight = Good(A);
            oldFlight.FlightStateCompatible = false;
            StringAssert.Contains(Fail(oldFlight, A), "flight state as incompatible");
        }

        [TestMethod]
        public void Refuses_WhenActiveVesselMissing()
        {
            StringAssert.Contains(Fail(Good(A, B), C), "not among the 2 vessels");
            Fail(Good(), A);
        }
    }
}
