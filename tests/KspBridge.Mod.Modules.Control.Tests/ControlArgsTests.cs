using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Control.Tests
{
    [TestClass]
    public sealed class ControlArgsTests
    {
        private static Dictionary<string, object> Args(string json) => Json.ParseObject(json);

        private static void BadRequest(Action action)
            => Assert.AreEqual("bad_request", Assert.ThrowsExactly<BridgeException>(action).Code);

        [TestMethod]
        [DataRow("{\"value\":0}", 0.0)]
        [DataRow("{\"value\":0.5}", 0.5)]
        [DataRow("{\"value\":1}", 1.0)]
        public void Throttle_AcceptsTheRange(string json, double expected) => Assert.AreEqual(expected, ControlArgs.Throttle(Args(json)));

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"value\":null}")]
        [DataRow("{\"value\":-0.01}")]
        [DataRow("{\"value\":1.01}")]
        [DataRow("{\"value\":\"1\"}")]
        [DataRow("{\"value\":true}")]
        public void Throttle_RejectsOthers(string json) => BadRequest(() => ControlArgs.Throttle(Args(json)));

        [TestMethod]
        public void Attitude_PitchHeading_WithDefaults()
        {
            AttitudeArgs args = ControlArgs.Attitude(Args("{\"pitch\":30,\"heading\":45}"));

            Assert.AreEqual(30, args.Target.Pitch);
            Assert.AreEqual(45, args.Target.Heading);
            Assert.IsNull(args.Target.Roll);
            Assert.IsNull(args.Target.Direction);
            Assert.AreEqual(2, args.ToleranceDeg);
            Assert.AreEqual(0.5, args.SettleRateDps);
        }

        [TestMethod]
        public void Attitude_RollAndOwnLimits()
        {
            AttitudeArgs args = ControlArgs.Attitude(Args("{\"pitch\":0,\"heading\":0,\"roll\":-180,\"tolerance_deg\":180,\"settle_rate_dps\":1000}"));

            Assert.AreEqual(-180.0, args.Target.Roll);
            Assert.AreEqual(180, args.ToleranceDeg);
            Assert.AreEqual(1000, args.SettleRateDps);
        }

        [TestMethod]
        [DataRow("{\"pitch\":-90,\"heading\":0}")]
        [DataRow("{\"pitch\":90,\"heading\":359.99}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"roll\":180}")]
        public void Attitude_AcceptsTheEdges(string json) => Assert.IsNotNull(ControlArgs.Attitude(Args(json)).Target);

        [TestMethod]
        [DataRow("prograde", (int)AttitudeDirection.Prograde)]
        [DataRow("retrograde", (int)AttitudeDirection.Retrograde)]
        [DataRow("normal", (int)AttitudeDirection.Normal)]
        [DataRow("antinormal", (int)AttitudeDirection.Antinormal)]
        [DataRow("radial_out", (int)AttitudeDirection.RadialOut)]
        [DataRow("radial_in", (int)AttitudeDirection.RadialIn)]
        [DataRow("surface_prograde", (int)AttitudeDirection.SurfacePrograde)]
        [DataRow("surface_retrograde", (int)AttitudeDirection.SurfaceRetrograde)]
        public void Attitude_Directions(string name, int expected)
        {
            AttitudeTarget target = ControlArgs.Attitude(Args("{\"direction\":\"" + name + "\"}")).Target;

            Assert.AreEqual((AttitudeDirection)expected, target.Direction);
            Assert.AreEqual(name, target.Describe()["direction"]);
        }

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"pitch\":0}")]
        [DataRow("{\"heading\":0}")]
        [DataRow("{\"roll\":0}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"direction\":\"prograde\"}")]
        [DataRow("{\"pitch\":0,\"direction\":\"prograde\"}")]
        [DataRow("{\"direction\":\"prograde\",\"roll\":0}")]
        [DataRow("{\"direction\":\"up\"}")]
        [DataRow("{\"direction\":\"Prograde\"}")]
        [DataRow("{\"direction\":1}")]
        [DataRow("{\"pitch\":90.1,\"heading\":0}")]
        [DataRow("{\"pitch\":-90.1,\"heading\":0}")]
        [DataRow("{\"pitch\":0,\"heading\":360}")]
        [DataRow("{\"pitch\":0,\"heading\":-0.1}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"roll\":180.1}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"roll\":-180.1}")]
        [DataRow("{\"pitch\":\"0\",\"heading\":0}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"tolerance_deg\":0}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"tolerance_deg\":-1}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"settle_rate_dps\":0}")]
        [DataRow("{\"pitch\":0,\"heading\":0,\"settle_rate_dps\":\"1\"}")]
        public void Attitude_RejectsOthers(string json) => BadRequest(() => ControlArgs.Attitude(Args(json)));

        [TestMethod]
        [DataRow("1", KSPActionGroup.Custom01)]
        [DataRow("2", KSPActionGroup.Custom02)]
        [DataRow("3", KSPActionGroup.Custom03)]
        [DataRow("4", KSPActionGroup.Custom04)]
        [DataRow("5", KSPActionGroup.Custom05)]
        [DataRow("6", KSPActionGroup.Custom06)]
        [DataRow("7", KSPActionGroup.Custom07)]
        [DataRow("8", KSPActionGroup.Custom08)]
        [DataRow("9", KSPActionGroup.Custom09)]
        [DataRow("10", KSPActionGroup.Custom10)]
        [DataRow("gear", KSPActionGroup.Gear)]
        [DataRow("light", KSPActionGroup.Light)]
        [DataRow("brakes", KSPActionGroup.Brakes)]
        [DataRow("abort", KSPActionGroup.Abort)]
        [DataRow("sas", KSPActionGroup.SAS)]
        [DataRow("rcs", KSPActionGroup.RCS)]
        public void ActionGroup_NamesMapToTheGameGroups(string name, KSPActionGroup expected)
        {
            ActionGroupArgs args = ControlArgs.ActionGroup(Args("{\"group\":\"" + name + "\",\"state\":\"on\"}"));

            Assert.AreEqual(expected, args.Group);
            Assert.AreEqual(name, args.Name);
        }

        [TestMethod]
        [DataRow(1, KSPActionGroup.Custom01)]
        [DataRow(10, KSPActionGroup.Custom10)]
        public void ActionGroup_CustomGroupAsNumber(int number, KSPActionGroup expected)
            => Assert.AreEqual(expected, ControlArgs.ActionGroup(Args("{\"group\":" + number + ",\"state\":\"on\"}")).Group);

        [TestMethod]
        [DataRow("on", (int)GroupChange.On)]
        [DataRow("off", (int)GroupChange.Off)]
        [DataRow("toggle", (int)GroupChange.Toggle)]
        public void ActionGroup_States(string state, int expected)
            => Assert.AreEqual((GroupChange)expected, ControlArgs.ActionGroup(Args("{\"group\":\"gear\",\"state\":\"" + state + "\"}")).Change);

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"group\":\"gear\"}")]
        [DataRow("{\"state\":\"on\"}")]
        [DataRow("{\"group\":\"stage\",\"state\":\"on\"}")]
        [DataRow("{\"group\":\"Gear\",\"state\":\"on\"}")]
        [DataRow("{\"group\":\"0\",\"state\":\"on\"}")]
        [DataRow("{\"group\":\"11\",\"state\":\"on\"}")]
        [DataRow("{\"group\":0,\"state\":\"on\"}")]
        [DataRow("{\"group\":11,\"state\":\"on\"}")]
        [DataRow("{\"group\":1.5,\"state\":\"on\"}")]
        [DataRow("{\"group\":true,\"state\":\"on\"}")]
        [DataRow("{\"group\":\"gear\",\"state\":\"ON\"}")]
        [DataRow("{\"group\":\"gear\",\"state\":true}")]
        public void ActionGroup_RejectsOthers(string json) => BadRequest(() => ControlArgs.ActionGroup(Args(json)));
    }
}
