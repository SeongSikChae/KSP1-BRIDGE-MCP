using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class OrbitRequestTests
    {
        private static readonly List<OrbitBody> Bodies = new List<OrbitBody>
        {
            new OrbitBody { Name = "Sun", Index = 0, Radius = 261600000, TerrainMax = 1, SphereOfInfluence = double.PositiveInfinity },
            new OrbitBody { Name = "Kerbin", Index = 1, Radius = 600000, AtmosphereTop = 70000, TerrainMax = 6767, SphereOfInfluence = 84159286 },
            new OrbitBody { Name = "Mun", Index = 2, Radius = 200000, TerrainMax = 7061, SphereOfInfluence = 2429559 },
            new OrbitBody { Name = "Minmus", Index = 3, Radius = 60000, TerrainMax = null, SphereOfInfluence = 2247428 },
        };

        private static Dictionary<string, object> Args(double ap, double pe, string body = null, double? inc = null)
        {
            Dictionary<string, object> args = new Dictionary<string, object> { ["apoapsis_m"] = ap, ["periapsis_m"] = pe };
            if (body != null)
                args["body"] = body;
            if (inc.HasValue)
                args["inclination_deg"] = inc.Value;
            return args;
        }

        private static string BadRequest(Dictionary<string, object> args)
        {
            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => OrbitRequest.Parse(args, Bodies, "Kerbin"));
            Assert.AreEqual("bad_request", ex.Code);
            return ex.Message;
        }

        [TestMethod]
        public void Converts_ApPe_ToSemiMajorAxisAndEccentricity()
        {
            OrbitRequest r = OrbitRequest.Parse(Args(120000, 90000, "kerbin", 30), Bodies, "Mun");

            Assert.AreEqual("Kerbin", r.Body.Name, "body names are case-insensitive");
            Assert.AreEqual(1, r.Body.Index);
            Assert.AreEqual(705000.0, r.SemiMajorAxis, 1e-9);
            Assert.AreEqual(30000.0 / 1410000.0, r.Eccentricity, 1e-12);
            Assert.AreEqual(30.0, r.Inclination);
        }

        [TestMethod]
        public void Defaults_CurrentBody_AndZeroInclination()
        {
            OrbitRequest r = OrbitRequest.Parse(Args(100000, 100000), Bodies, "Kerbin");
            Assert.AreEqual("Kerbin", r.Body.Name);
            Assert.AreEqual(0.0, r.Inclination);
            Assert.AreEqual(0.0, r.Eccentricity, 1e-12);
        }

        [TestMethod]
        public void ApoapsisBelowPeriapsis_IsBadRequest()
        {
            StringAssert.Contains(BadRequest(Args(90000, 120000)), "below periapsis");
        }

        [TestMethod]
        public void PeriapsisInTheAtmosphere_IsBadRequest()
        {
            StringAssert.Contains(BadRequest(Args(120000, 60000)), "atmosphere");
            StringAssert.Contains(BadRequest(Args(120000, 70000)), "atmosphere", "the atmosphere top itself is inside");
        }

        [TestMethod]
        public void AirlessBody_PeriapsisNotAboveTheHighestTerrain_IsBadRequest()
        {
            StringAssert.Contains(BadRequest(Args(50000, 1000, "Mun")), "highest terrain");
            OrbitRequest ok = OrbitRequest.Parse(Args(50000, 8000, "Mun"), Bodies, "Kerbin");
            Assert.AreEqual("Mun", ok.Body.Name);
        }

        [TestMethod]
        public void AirlessBody_UnknownTerrainHeight_IsBadRequest()
        {
            StringAssert.Contains(BadRequest(Args(50000, 20000, "Minmus")), "not known");
        }

        [TestMethod]
        public void ApoapsisOutsideTheSphereOfInfluence_IsBadRequest()
        {
            // rA = R + Ap counts from the body's centre.
            StringAssert.Contains(BadRequest(Args(84159286 - 600000, 100000)), "sphere of influence");
            StringAssert.Contains(BadRequest(Args(84159286 - 599999 + 1000, 100000)), "sphere of influence");
        }

        [TestMethod]
        public void SemiMajorAxisTheGameWouldCut_IsBadRequest()
        {
            // rA just inside the SOI but the semi-major axis above 0.99 of it.
            double soi = 84159286;
            StringAssert.Contains(BadRequest(Args(soi - 600000 - 1000, soi * 0.995 - 600000)), "cut");
        }

        [TestMethod]
        public void InfiniteSphereOfInfluence_SkipsTheUpperChecks()
        {
            OrbitRequest r = OrbitRequest.Parse(Args(1e13, 1e12, "Sun"), Bodies, "Kerbin");
            Assert.AreEqual("Sun", r.Body.Name);
        }

        [TestMethod]
        public void UnknownBody_AndBadArguments_AreBadRequest()
        {
            StringAssert.Contains(BadRequest(Args(120000, 90000, "Pluto")), "Pluto");
            Dictionary<string, object> notText = Args(120000, 90000);
            notText["body"] = 3.0;
            BadRequest(notText);
            BadRequest(new Dictionary<string, object> { ["apoapsis_m"] = 120000.0 });
            BadRequest(Args(double.NaN, 90000));
        }

        [TestMethod]
        public void IsApplied_NeedsBodySituationAndApPeWithin1Km()
        {
            OrbitRequest r = OrbitRequest.Parse(Args(120000, 90000), Bodies, "Kerbin");

            Assert.IsTrue(r.IsApplied("Kerbin", "ORBITING", 120900, 89100));
            Assert.IsFalse(r.IsApplied("Kerbin", "PRELAUNCH", 120000, 90000));
            Assert.IsFalse(r.IsApplied("Mun", "ORBITING", 120000, 90000));
            Assert.IsFalse(r.IsApplied("Kerbin", "ORBITING", 121500, 90000));
        }
    }
}
