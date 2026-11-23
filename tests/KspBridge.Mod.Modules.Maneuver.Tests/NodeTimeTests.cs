using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Maneuver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace KspBridge.Mod.Modules.Maneuver.Tests
{
    [TestClass]
    public sealed class NodeTimeTests
    {
        /// <summary>Period 2000 s, periapsis at UT 100 (apoapsis at 1100), no segment end.</summary>
        private static ReferenceOrbit Closed(SegmentEnd end = SegmentEnd.Final, double endUt = 2100)
            => new ReferenceOrbit { Eccentricity = 0.2, Period = 2000, ObtAt = ut => ut - 100, End = end, EndUt = endUt };

        private static ReferenceOrbit Open() => new ReferenceOrbit { Eccentricity = 1.2, ObtAt = ut => ut - 100, End = SegmentEnd.Escape, EndUt = 50000 };

        private static CreateNodeArgs At(NodeTimeKind kind, double value = 0) => new CreateNodeArgs { Time = kind, TimeValue = value };

        private static void Bad(Func<double> f, string contains)
        {
            try
            {
                f();
            }
            catch (BridgeException ex)
            {
                StringAssert.Contains(ex.Message, contains);
                return;
            }
            Assert.Fail("expected bad_request containing '" + contains + "'");
        }

        [TestMethod]
        public void UtAndInSeconds()
        {
            Assert.AreEqual(500, NodeTime.Resolve(At(NodeTimeKind.Ut, 500), 0, null, false, Closed()));
            Assert.AreEqual(1600, NodeTime.Resolve(At(NodeTimeKind.InSeconds, 600), 1000, null, false, Closed()));
        }

        [TestMethod]
        public void AtUsesTheLaterOfNowAndTheLastNode()
        {
            Assert.AreEqual(100, NodeTime.Resolve(At(NodeTimeKind.Periapsis), 0, null, false, Closed()), 1e-9);
            Assert.AreEqual(1100, NodeTime.Resolve(At(NodeTimeKind.Apoapsis), 0, null, false, Closed()), 1e-9);
            Assert.AreEqual(2100, NodeTime.Resolve(At(NodeTimeKind.Periapsis), 0, 1100, false, Closed()), 1e-9);
        }

        [TestMethod]
        public void TheLastNodeOnThePointGivesOnePeriodLater()
        {
            Assert.AreEqual(3100, NodeTime.Resolve(At(NodeTimeKind.Apoapsis), 0, 1100, false, Closed()), 1e-9);
        }

        [TestMethod]
        public void NotAfterNowOrTheLastNode()
        {
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Ut, 1000), 1000, null, false, Closed()), "not after now");
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Ut, 1500), 1000, 1500, false, Closed()), "not after the last node");
        }

        [TestMethod]
        public void SegmentEndsBoundOnlyEscapeAndEncounter()
        {
            Assert.AreEqual(1400, NodeTime.Resolve(At(NodeTimeKind.Ut, 1400), 0, null, false, Closed(SegmentEnd.Escape, 1500)));
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Ut, 1500), 0, null, false, Closed(SegmentEnd.Escape, 1500)), "leaves the sphere");
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Ut, 1600), 0, null, false, Closed(SegmentEnd.Encounter, 1500)), "enters another body");
            Assert.AreEqual(9000, NodeTime.Resolve(At(NodeTimeKind.Ut, 9000), 0, null, false, Closed(SegmentEnd.Final, 2100)),
                "FINAL: past one period is fine");
        }

        [TestMethod]
        public void OpenOrbit()
        {
            Assert.AreEqual(100, NodeTime.Resolve(At(NodeTimeKind.Periapsis), 0, null, false, Open()), 1e-9);
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Apoapsis), 0, null, false, Open()), "no apoapsis");
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.Periapsis), 200, null, false, Open()), "periapsis is already behind");
        }

        [TestMethod]
        public void OnTheSurface()
        {
            Bad(() => NodeTime.Resolve(At(NodeTimeKind.InSeconds, 60), 0, null, true, Closed()), "launch pad");
        }
    }
}
