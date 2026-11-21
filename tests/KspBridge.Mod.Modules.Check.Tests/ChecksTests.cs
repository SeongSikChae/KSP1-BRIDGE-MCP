using KspBridge.Mod.Modules.Check;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Check.Tests
{
    [TestClass]
    public sealed class ChecksTests
    {
        // ---- a clean two-stage rocket (the T006 diagnostic rocket) ----
        // 1 pod (root) ─ 2 parachute (stage 0)
        //   └ 3 tank ─ 4 engine (stage 1) ─ 5 decoupler (stage 2) ─ 6 tank ─ 7 engine (stage 3)

        private static ResourceData Res(string name, double amount, double density)
            => new ResourceData { Name = name, Amount = amount, Density = density };

        private static PartData Part(uint id, uint? parent, double y, double mass, int decoupleStage, int activationStage = -1)
            => new PartData
            {
                Id = id,
                Parent = parent,
                Mass = mass,
                Com = new Vec(0, y, 0),
                HasCalc = true,
                DecoupleStage = decoupleStage,
                ActivationStage = activationStage,
            };

        private static void Stages(PartData p, params (int stage, double start, double end)[] values)
        {
            foreach ((int stage, double start, double end) v in values)
                p.StageResources[v.stage] = new StageMass(v.start, v.end);
        }

        private static ShipData Rocket()
        {
            PartData pod = Part(1, null, 15, 0.706, -1);
            pod.Command = true;
            pod.CrewCapacity = 1;
            pod.Resources.Add(Res("ElectricCharge", 50, 0));
            pod.Resources.Add(Res("MonoPropellant", 10, 0.004));
            Stages(pod, (3, 0.04, 0.04), (2, 0.04, 0.04), (1, 0.04, 0.04), (0, 0.04, 0.04));

            PartData chute = Part(2, 1, 16, 0.1, -1, 0);
            chute.Parachute = true;
            Stages(chute, (3, 0, 0), (2, 0, 0), (1, 0, 0), (0, 0, 0));

            PartData upperTank = Part(3, 1, 13, 0.25, -1);
            upperTank.Resources.Add(Res("LiquidFuel", 180, 0.005));
            upperTank.Resources.Add(Res("Oxidizer", 220, 0.005));
            Stages(upperTank, (3, 2, 2), (2, 2, 2), (1, 2, 0), (0, 0, 0));

            PartData upperEngine = Part(4, 3, 11.6, 1.25, -1, 1);
            upperEngine.IsJettison = true;
            Stages(upperEngine, (3, 0, 0), (2, 0, 0), (1, 0, 0), (0, 0, 0));

            PartData decoupler = Part(5, 4, 10.9, 0.04, 2, 2);
            decoupler.Separator = new SeparatorData { Stage = 2, DetachedRoots = { 5 } };
            Stages(decoupler, (3, 0, 0), (2, 0, 0));

            PartData lowerTank = Part(6, 5, 9.5, 0.25, 2, 2);
            lowerTank.Resources.Add(Res("LiquidFuel", 180, 0.005));
            lowerTank.Resources.Add(Res("Oxidizer", 220, 0.005));
            Stages(lowerTank, (3, 2, 0), (2, 0, 0));

            PartData lowerEngine = Part(7, 6, 8, 1.25, 2, 3);
            lowerEngine.IsJettison = true;
            Stages(lowerEngine, (3, 0, 0), (2, 0, 0));

            ShipData ship = new ShipData
            {
                Body = "Kerbin",
                Facility = "VAB",
                Parts = { pod, chute, upperTank, upperEngine, decoupler, lowerTank, lowerEngine },
                StagedNumbers = { 0, 1, 2, 3 },
                IgnoredPropellants = { "IntakeAir", "ElectricCharge" },
            };
            ship.Engines.Add(Engine(4, 1, 11.6));
            ship.Engines.Add(Engine(7, 3, 8));
            ship.Stages[3] = new StageCalc { StartMass = 7.886, EndMass = 5.886, DvAsl = 760.2, DvVac = 889.3, TwrAsl = 2.652, TwrVac = 3.102, BurnTime = 25.3 };
            ship.Stages[2] = new StageCalc { StartMass = 4.346, EndMass = 4.346 };
            ship.Stages[1] = new StageCalc { StartMass = 4.346, EndMass = 2.346, DvAsl = 1602.3, DvVac = 1874.3, TwrAsl = 4.812, TwrVac = 5.629, BurnTime = 25.3 };
            // The calculator gives a burn time to the parachute stage too (T006); the answer must show 0 (D045).
            ship.Stages[0] = new StageCalc { StartMass = 2.346, EndMass = 2.346, BurnTime = 5.0, TwrAsl = 3.1 };
            return ship;
        }

        private static EngineData Engine(uint id, int stage, double y)
            => new EngineData
            {
                PartId = id,
                Staged = true,
                Stage = stage,
                BurnStages = { stage },
                Propellants = { "LiquidFuel", "Oxidizer" },
                ThrustPoint = new Vec(0, y, 0),
                ExhaustDir = new Vec(0, -1, 0),
                ThrustVac = 240,
                ThrustAsl = 205.16,
            };

        private static PartData P(ShipData ship, uint id) => ship.Parts.First(p => p.Id == id);
        private static EngineData E(ShipData ship, uint id) => ship.Engines.First(e => e.PartId == id);

        private static List<Dictionary<string, object>> StagesOf(Dictionary<string, object> answer)
            => ((List<object>)answer["stages"]).Cast<Dictionary<string, object>>().ToList();

        private static List<Dictionary<string, object>> WarningsOf(Dictionary<string, object> answer)
            => ((List<object>)answer["warnings"]).Cast<Dictionary<string, object>>().ToList();

        private static string[] Codes(Dictionary<string, object> answer) => WarningsOf(answer).Select(w => (string)w["code"]).ToArray();

        private static uint[] Parts(Dictionary<string, object> warning) => ((List<object>)warning["parts"]).Cast<uint>().ToArray();

        private static Dictionary<string, object> Only(Dictionary<string, object> answer, string code)
        {
            CollectionAssert.AreEqual(new[] { code }, Codes(answer), "warnings: " + string.Join(",", Codes(answer)));
            return WarningsOf(answer)[0];
        }

        // ---- stage table (C2) ----

        [TestMethod]
        public void CleanRocket_NoWarnings()
        {
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(Rocket())));
        }

        [TestMethod]
        public void Stages_DescendingAndSameSetAsStaged()
        {
            Dictionary<string, object> answer = Checks.Run(Rocket());
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, StagesOf(answer).Select(s => (int)s["stage"]).ToArray());
            Assert.AreEqual("Kerbin", answer["body"]);
            Assert.AreEqual("VAB", answer["facility"]);
        }

        [TestMethod]
        public void Stages_FuelExcludesNonFuel_AndStartIsDryPlusFuel()
        {
            List<Dictionary<string, object>> stages = StagesOf(Checks.Run(Rocket()));
            Dictionary<string, object> s3 = stages[0];
            Assert.AreEqual(7.886, s3["start_mass_t"]);
            Assert.AreEqual(4.0, s3["fuel_mass_t"], "both tanks, monopropellant is not fuel (no engine burns it)");
            Assert.AreEqual(3.886, s3["dry_mass_t"]);
            Dictionary<string, object> s2 = stages[1];
            Assert.AreEqual(2.0, s2["fuel_mass_t"], "only parts with decoupleStage < 2 (calculator set)");
            Assert.AreEqual(2.346, s2["dry_mass_t"]);
            foreach (Dictionary<string, object> s in stages)
                Assert.AreEqual((double)s["start_mass_t"], (double)s["dry_mass_t"] + (double)s["fuel_mass_t"], 1e-9);
        }

        [TestMethod]
        public void NonEngineStages_AreZero_EvenWhenCalculatorGivesValues()
        {
            List<Dictionary<string, object>> stages = StagesOf(Checks.Run(Rocket()));
            Dictionary<string, object> s0 = stages[3];
            Assert.AreEqual(0.0, s0["burn_s"]);
            Assert.AreEqual(0.0, s0["twr_asl"]);
            Assert.AreEqual(0.0, s0["dv_vac_ms"]);
            Assert.IsFalse(s0.ContainsKey("com_offset_m"));
            Assert.AreEqual(0.0, stages[1]["burn_s"]);
        }

        [TestMethod]
        public void Totals_SumEngineStages()
        {
            Dictionary<string, object> total = (Dictionary<string, object>)Checks.Run(Rocket())["total"];
            Assert.AreEqual(2362.5, total["dv_asl_ms"]);
            Assert.AreEqual(2763.6, total["dv_vac_ms"]);
            Assert.AreEqual(50.6, total["burn_s"], "the parachute stage's 5 s is not counted");
        }

        [TestMethod]
        public void EngineStages_HaveComOffset()
        {
            List<Dictionary<string, object>> stages = StagesOf(Checks.Run(Rocket()));
            Assert.AreEqual(0.0, stages[0]["com_offset_m"]);
            Assert.AreEqual(0.0, stages[2]["com_offset_m"]);
        }

        [TestMethod]
        public void MissingCalculatorStage_UsesPreviousEndAndZero()
        {
            ShipData ship = Rocket();
            ship.Stages.Remove(2);
            Dictionary<string, object> s2 = StagesOf(Checks.Run(ship))[1];
            Assert.AreEqual(5.886, s2["start_mass_t"]);
            Assert.AreEqual(5.886, s2["end_mass_t"]);
            Assert.AreEqual(0.0, s2["dv_asl_ms"]);
        }

        [TestMethod]
        public void Rounding()
        {
            ShipData ship = Rocket();
            ship.Stages[3].DvAsl = 760.25;
            ship.Stages[3].TwrAsl = 2.655;
            ship.Stages[3].StartMass = 7.88649;
            Dictionary<string, object> s3 = StagesOf(Checks.Run(ship))[0];
            Assert.AreEqual(760.3, s3["dv_asl_ms"]);
            Assert.AreEqual(2.66, s3["twr_asl"]);
            Assert.AreEqual(7.886, s3["start_mass_t"]);
        }

        [TestMethod]
        public void BurnStages_DecideEngineStages()
        {
            ShipData ship = Rocket();
            E(ship, 7).BurnStages.Clear();
            Dictionary<string, object> s3 = StagesOf(Checks.Run(ship))[0];
            Assert.AreEqual(0.0, s3["dv_vac_ms"], "an engine without a burn entry does not make an engine stage");
        }

        [TestMethod]
        public void NoEngines_DirectMass_WithJettisonAndClamp()
        {
            // pod + fairing (jettisons 0.06 t when stage 1 fires) + clamp; no engines, so the calculator has no stages.
            PartData pod = Part(1, null, 2, 0.8, -1);
            pod.Command = true;
            pod.Resources.Add(Res("MonoPropellant", 10, 0.004));
            PartData fairing = Part(2, 1, 1, 0.2, -1, 1);
            fairing.IsJettison = true;
            fairing.JettisonMass = 0.06;
            PartData clamp = new PartData { Id = 3, Parent = 1, LaunchClamp = true, Mass = 0.1 };
            ShipData ship = new ShipData { Body = "Kerbin", Facility = "SPH", Parts = { pod, fairing, clamp }, StagedNumbers = { 1, 0 } };
            List<Dictionary<string, object>> stages = StagesOf(Checks.Run(ship));
            Assert.AreEqual(0.98, stages[0]["start_mass_t"], "stage 1: fairing already jettisoned, clamp left out");
            Assert.AreEqual(0.98, stages[0]["end_mass_t"]);
            Assert.AreEqual(0.0, stages[0]["fuel_mass_t"], "no engine, so nothing is fuel");
            Assert.AreEqual(0.98, stages[1]["start_mass_t"]);
            Assert.AreEqual(0.0, stages[0]["dv_vac_ms"]);
        }

        [TestMethod]
        public void NoEngines_BeforeActivation_KeepsJettisonMass()
        {
            PartData pod = Part(1, null, 2, 0.8, -1);
            pod.Command = true;
            PartData fairing = Part(2, 1, 1, 0.2, -1, 1);
            fairing.IsJettison = true;
            fairing.JettisonMass = 0.06;
            PartData chute = Part(3, 1, 3, 0.1, -1, 0);
            chute.Parachute = true;
            ShipData ship = new ShipData { Body = "Kerbin", Facility = "SPH", Parts = { pod, fairing, chute }, StagedNumbers = { 2, 1, 0 } };
            Assert.AreEqual(1.1, StagesOf(Checks.Run(ship))[0]["start_mass_t"], "stage 2 fires before the fairing");
        }

        [TestMethod]
        public void EmptyResult_NoStagesNoWarnings()
        {
            Dictionary<string, object> answer = Checks.EmptyResult("Mun", "VAB");
            Assert.AreEqual(0, ((List<object>)answer["stages"]).Count);
            Assert.AreEqual(0, ((List<object>)answer["warnings"]).Count);
            Assert.AreEqual("Mun", answer["body"]);
        }

        // ---- falls, resources (D026, D037, D046, D048) ----

        [TestMethod]
        public void NestedSeparators_FirstToFireDropsThePart()
        {
            ShipData ship = Rocket();
            // a radial booster tank on the lower tank, decoupled in stage 3 (before the main decoupler, stage 2)
            PartData radial = Part(8, 6, 9.5, 0.025, 3, 3);
            radial.Separator = new SeparatorData { Stage = 3, DetachedRoots = { 8 } };
            PartData booster = Part(9, 8, 9.5, 0.125, 3, 3);
            ship.Parts.Add(radial);
            ship.Parts.Add(booster);
            Analysis a = new Analysis(ship);
            Assert.IsTrue(a.Present(booster, 4));
            Assert.IsFalse(a.Present(booster, 3), "falls with the radial decoupler at stage 3");
            Assert.IsTrue(a.Present(P(ship, 6), 3));
            Assert.IsFalse(a.Present(P(ship, 6), 2));
            Assert.IsTrue(a.Remains(P(ship, 3)));
        }

        [TestMethod]
        public void SeparatorKeepingParent_DropsChildSide()
        {
            ShipData ship = Rocket();
            // the decoupler fires toward its child: the decoupler stays, the lower tank's subtree falls
            P(ship, 5).Separator.DetachedRoots.Clear();
            P(ship, 5).Separator.DetachedRoots.Add(6);
            Analysis a = new Analysis(ship);
            Assert.IsTrue(a.Remains(P(ship, 5)));
            Assert.IsFalse(a.Present(P(ship, 7), 2));
        }

        [TestMethod]
        public void LaunchClamp_NeverPresent()
        {
            ShipData ship = Rocket();
            PartData clamp = new PartData { Id = 10, Parent = 6, LaunchClamp = true, Mass = 0.1 };
            ship.Parts.Add(clamp);
            Analysis a = new Analysis(ship);
            Assert.IsFalse(a.Present(clamp, 3));
            Assert.IsFalse(a.Remains(clamp));
            Assert.AreEqual(7.886, StagesOf(Checks.Run(ship))[0]["start_mass_t"]);
        }

        [TestMethod]
        public void MissingResourceKey_UsesNearestEarlierEnd()
        {
            ShipData ship = Rocket();
            PartData lowerTank = P(ship, 6);
            lowerTank.StageResources.Clear();
            Stages(lowerTank, (2, 1.5, 1.2), (1, 1.2, 0.8));
            Analysis a = new Analysis(ship);
            Assert.AreEqual(0.8, a.ResourceMassAt(lowerTank, 0, true), 1e-9, "stage 0 missing: end of stage 1");
            Assert.AreEqual(0.8, a.ResourceMassAt(lowerTank, 0, false), 1e-9);
            Assert.AreEqual(1.5, a.ResourceMassAt(lowerTank, 2, true), 1e-9);
            lowerTank.StageResources.Clear();
            Assert.AreEqual(2.0, a.ResourceMassAt(lowerTank, 0, false), 1e-9, "no keys: current resource mass");
        }

        // ---- warnings (C5-C7, C8 in data form) ----

        [TestMethod]
        public void NoSeparation_EngineStaysIntoNextEngineStage()
        {
            ShipData ship = Rocket();
            P(ship, 5).Separator = null; // e.g. a docking port with staging off (D046)
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_separation");
            Assert.AreEqual(1, w["stage"]);
            CollectionAssert.AreEqual(new uint[] { 7 }, Parts(w));
        }

        [TestMethod]
        public void FuelBlocked_TankInSameUnitIsReportedWithEngine()
        {
            ShipData ship = Rocket();
            E(ship, 7).BurnStages.Clear();
            P(ship, 6).StageResources.Clear();
            Stages(P(ship, 6), (3, 2, 2), (2, 2, 2));
            Dictionary<string, object> w = Only(Checks.Run(ship), "fuel_blocked");
            Assert.AreEqual(3, w["stage"]);
            CollectionAssert.AreEquivalent(new uint[] { 7, 6 }, Parts(w));
        }

        [TestMethod]
        public void FuelBlocked_NotWhenFuelIsNowhere()
        {
            ShipData ship = Rocket();
            E(ship, 7).Propellants.Clear();
            E(ship, 7).Propellants.Add("XenonGas");
            E(ship, 7).BurnStages.Clear();
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked");
        }

        [TestMethod]
        public void NoEngineFuel_TankThatNeverDrains()
        {
            ShipData ship = Rocket();
            PartData extra = Part(8, 5, 10, 0.06, 2, 2);
            extra.Resources.Add(Res("LiquidFuel", 90, 0.005));
            Stages(extra, (3, 0.45, 0.45), (2, 0.45, 0.45));
            ship.Parts.Add(extra);
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_engine_fuel");
            Assert.AreEqual(2, w["stage"]);
            CollectionAssert.AreEqual(new uint[] { 8 }, Parts(w));
            Assert.AreEqual(0.45, ((Dictionary<string, object>)w["values"])["fuel_mass_t"]);
        }

        [TestMethod]
        public void NoEngineFuel_TankDroppedAtFirstStage()
        {
            ShipData ship = Rocket();
            // a radial decoupler fired with the launch engine (stage 3) dropping a full tank at once
            PartData radial = Part(8, 6, 9.5, 0.025, 3, 3);
            radial.Separator = new SeparatorData { Stage = 3, DetachedRoots = { 8 } };
            PartData tank = Part(9, 8, 9.5, 0.125, 3, 3);
            tank.Resources.Add(Res("LiquidFuel", 90, 0.005));
            tank.Resources.Add(Res("Oxidizer", 110, 0.005));
            Stages(tank, (3, 1, 1));
            ship.Parts.Add(radial);
            ship.Parts.Add(tank);
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_engine_fuel");
            Assert.AreEqual(3, w["stage"]);
            CollectionAssert.AreEqual(new uint[] { 9 }, Parts(w));
            Assert.AreEqual(1.0, ((Dictionary<string, object>)w["values"])["fuel_mass_t"]);
        }

        [TestMethod]
        public void FuelBlocked_TankDroppedAtFirstStage_IsReportedWithEngine()
        {
            ShipData ship = Rocket();
            PartData radial = Part(8, 6, 9.5, 0.025, 3, 3);
            radial.Separator = new SeparatorData { Stage = 3, DetachedRoots = { 8 } };
            PartData tank = Part(9, 8, 9.5, 0.125, 3, 3);
            tank.Resources.Add(Res("LiquidFuel", 90, 0.005));
            tank.Resources.Add(Res("Oxidizer", 110, 0.005));
            Stages(tank, (3, 1, 1));
            PartData booster = Part(10, 9, 9.0, 0.13, 3, 3);
            ship.Parts.AddRange(new[] { radial, tank, booster });
            EngineData blocked = Engine(10, 3, 9.0);
            blocked.BurnStages.Clear();
            ship.Engines.Add(blocked);
            Dictionary<string, object> w = Only(Checks.Run(ship), "fuel_blocked");
            CollectionAssert.AreEquivalent(new uint[] { 10, 9 }, Parts(w));
        }

        [TestMethod]
        public void ComOffset_WithoutGimbal_Warns()
        {
            ShipData ship = Rocket();
            E(ship, 7).ThrustPoint = new Vec(0.2, 8, 0);
            Dictionary<string, object> w = Only(Checks.Run(ship), "com_offset");
            Assert.AreEqual(3, w["stage"]);
            CollectionAssert.AreEqual(new uint[] { 7 }, Parts(w));
            Assert.AreEqual(0.2, ((Dictionary<string, object>)w["values"])["offset_m"]);
            Assert.AreEqual(0.2, StagesOf(Checks.Run(ship))[0]["com_offset_m"]);
        }

        [TestMethod]
        public void ComOffset_GimbalCancels_NoWarning()
        {
            ShipData ship = Rocket();
            E(ship, 7).ThrustPoint = new Vec(0.2, 8, 0);
            E(ship, 7).GimbalDeg = 10;
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(ship)));
        }

        [TestMethod]
        public void ComOffset_ZeroThrust_NoValueNoWarning()
        {
            ShipData ship = Rocket();
            E(ship, 7).ThrustVac = 0;
            E(ship, 7).ThrustAsl = 0;
            E(ship, 7).ThrustPoint = new Vec(1, 8, 0);
            Dictionary<string, object> answer = Checks.Run(ship);
            Assert.IsFalse(StagesOf(answer)[0].ContainsKey("com_offset_m"));
            CollectionAssert.DoesNotContain(Codes(answer), "com_offset");
        }

        [TestMethod]
        public void ComOffset_AirBreatherUsesSeaLevelThrust()
        {
            ShipData ship = Rocket();
            E(ship, 7).ThrustVac = 0;
            E(ship, 7).ThrustPoint = new Vec(0.2, 8, 0);
            Assert.AreEqual(0.2, StagesOf(Checks.Run(ship))[0]["com_offset_m"]);
        }

        [TestMethod]
        public void NoControl_NoCommandPart()
        {
            ShipData ship = Rocket();
            P(ship, 1).Command = false;
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_control");
            Assert.AreEqual(0, Parts(w).Length);
        }

        [TestMethod]
        public void NoControl_CommandFallsOff()
        {
            ShipData ship = Rocket();
            P(ship, 1).Command = false;
            P(ship, 6).Command = true;
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_control");
            CollectionAssert.AreEqual(new uint[] { 6 }, Parts(w));
        }

        [TestMethod]
        public void LowLaunchTwr_VabOnly()
        {
            ShipData ship = Rocket();
            ship.Stages[3].TwrAsl = 0.8;
            Dictionary<string, object> w = Only(Checks.Run(ship), "low_launch_twr");
            Assert.AreEqual(3, w["stage"]);
            ship.Facility = "SPH";
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(ship)));
        }

        [TestMethod]
        public void LeftoverFuel_PerTank()
        {
            ShipData ship = Rocket();
            P(ship, 3).StageResources.Clear();
            Stages(P(ship, 3), (3, 2, 2), (2, 2, 2), (1, 2, 0.5), (0, 0.5, 0.5));
            Dictionary<string, object> w = Only(Checks.Run(ship), "leftover_fuel");
            CollectionAssert.AreEqual(new uint[] { 3 }, Parts(w));
            Dictionary<string, object> values = (Dictionary<string, object>)w["values"];
            Assert.AreEqual(0.5, values["fuel_mass_t"]);
            CollectionAssert.AreEquivalent(new object[] { "LiquidFuel", "Oxidizer" }, (List<object>)values["resources"]);
        }

        [TestMethod]
        public void NoParachute()
        {
            ShipData ship = Rocket();
            P(ship, 2).Parachute = false;
            Dictionary<string, object> w = Only(Checks.Run(ship), "no_parachute");
            CollectionAssert.AreEqual(new uint[] { 1 }, Parts(w));
        }

        [TestMethod]
        public void WarningFields()
        {
            ShipData ship = Rocket();
            ship.Stages[3].TwrAsl = 0.8;
            Dictionary<string, object> w = WarningsOf(Checks.Run(ship))[0];
            CollectionAssert.IsSubsetOf(new[] { "code", "stage", "parts", "message", "values" }, w.Keys.ToList());
            Assert.IsInstanceOfType(w["message"], typeof(string));
        }

        // ---- stage-6 decisions (D075-D082, C2) ----

        /// <summary>The upper engine (4, stage 1) never burns; the upper tank holds no resources.</summary>
        private static ShipData UpperEngineBlocked()
        {
            ShipData ship = Rocket();
            E(ship, 4).BurnStages.Clear();
            P(ship, 3).Resources.Clear();
            P(ship, 3).StageResources.Clear();
            return ship;
        }

        [TestMethod]
        public void FuelBlocked_FuelOnPartDroppedBeforeTheStage_NotCounted()
        {
            // The lower tank still holds 1 t of LF/Ox when it falls at stage 2, before the engine's stage 1 (D075).
            ShipData ship = UpperEngineBlocked();
            P(ship, 6).StageResources.Clear();
            Stages(P(ship, 6), (3, 2, 1), (2, 1, 1));
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked");
        }

        [TestMethod]
        public void FuelBlocked_TankEmptiedByEarlierStage_NotCounted()
        {
            // The upper tank stays, holds LF/Ox now, but stage 3 drained it before stage 1 starts (D080).
            ShipData ship = UpperEngineBlocked();
            PartData tank = P(ship, 3);
            tank.Resources.Add(Res("LiquidFuel", 180, 0.005));
            tank.Resources.Add(Res("Oxidizer", 220, 0.005));
            Stages(tank, (3, 2, 0), (2, 0, 0), (1, 0, 0), (0, 0, 0));
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked");
        }

        [TestMethod]
        public void FuelBlocked_StageStartFuel_JudgedAtAnswerPrecision()
        {
            ShipData ship = UpperEngineBlocked();
            PartData tank = P(ship, 3);
            tank.Resources.Add(Res("LiquidFuel", 180, 0.005));
            tank.Resources.Add(Res("Oxidizer", 220, 0.005));
            Stages(tank, (3, 2, 0.0004), (2, 0.0004, 0.0004), (1, 0.0004, 0.0004), (0, 0.0004, 0.0004));
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked", "0.0004 t is 0 in the answer (D076)");

            tank.StageResources.Clear();
            Stages(tank, (3, 2, 0.001), (2, 0.001, 0.001), (1, 0.001, 0.001), (0, 0.001, 0.001));
            Dictionary<string, object> w = WarningsOf(Checks.Run(ship)).First(x => (string)x["code"] == "fuel_blocked");
            Assert.AreEqual(1, w["stage"]);
            CollectionAssert.Contains(Parts(w), 4u);
        }

        [TestMethod]
        public void FuelBlocked_OnlyAnotherEnginesFuelLeft_NotCounted()
        {
            // The pod's MonoPropellant is fuel because an (unstaged) engine burns it, but the blocked engine burns LF/Ox (D080).
            ShipData ship = UpperEngineBlocked();
            ship.Engines.Add(new EngineData { PartId = 1, Staged = false, Stage = -1, Propellants = { "MonoPropellant" } });
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked");
        }

        [TestMethod]
        public void FuelBlocked_IgnoredPropellantBesideAnotherEnginesFuel_NotCounted()
        {
            // An ion engine (XenonGas + ElectricCharge) never burns. The pod holds ElectricCharge (ignored, not fuel) and
            // MonoPropellant (fuel because another engine burns it), so its stage fuel mass is above 0; without D082 the pod
            // would count as carrying the ion engine's propellant.
            ShipData ship = UpperEngineBlocked();
            EngineData ion = E(ship, 4);
            ion.Propellants.Clear();
            ion.Propellants.Add("XenonGas");
            ion.Propellants.Add("ElectricCharge");
            ship.Engines.Add(new EngineData { PartId = 1, Staged = false, Stage = -1, Propellants = { "MonoPropellant" } });
            CollectionAssert.DoesNotContain(Codes(Checks.Run(ship)), "fuel_blocked");
        }

        [TestMethod]
        public void ComOffset_JudgedAtAnswerPrecision()
        {
            ShipData ship = Rocket();
            E(ship, 7).ThrustPoint = new Vec(0.0004, 8, 0);
            Dictionary<string, object> answer = Checks.Run(ship);
            CollectionAssert.DoesNotContain(Codes(answer), "com_offset", "0.0004 m is 0 in the answer (D076)");
            Assert.AreEqual(0.0, StagesOf(answer)[0]["com_offset_m"]);

            E(ship, 7).ThrustPoint = new Vec(0.001, 8, 0);
            Assert.AreEqual(0.001, ((Dictionary<string, object>)Only(Checks.Run(ship), "com_offset")["values"])["offset_m"]);
        }

        [TestMethod]
        public void LeftoverFuel_JudgedAtAnswerPrecision()
        {
            ShipData ship = Rocket();
            P(ship, 3).StageResources.Clear();
            Stages(P(ship, 3), (3, 2, 2), (2, 2, 2), (1, 2, 0.0004), (0, 0.0004, 0.0004));
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(ship)), "0.0004 t is 0 in the answer (D076)");

            P(ship, 3).StageResources.Clear();
            Stages(P(ship, 3), (3, 2, 2), (2, 2, 2), (1, 2, 0.001), (0, 0.001, 0.001));
            Assert.AreEqual(0.001, ((Dictionary<string, object>)Only(Checks.Run(ship), "leftover_fuel")["values"])["fuel_mass_t"]);
        }

        [TestMethod]
        public void NoEngineFuel_DrainBelowAnswerPrecision_StillWarns()
        {
            ShipData ship = Rocket();
            PartData extra = Part(8, 5, 10, 0.06, 2, 2);
            extra.Resources.Add(Res("LiquidFuel", 90, 0.005));
            Stages(extra, (3, 0.45, 0.4496), (2, 0.4496, 0.4496));
            ship.Parts.Add(extra);
            Only(Checks.Run(ship), "no_engine_fuel");
        }

        [TestMethod]
        public void Totals_AreSumsOfRoundedStageValues()
        {
            ShipData ship = Rocket();
            ship.Stages[3].DvAsl = 760.26;
            ship.Stages[1].DvAsl = 1602.36;
            Dictionary<string, object> answer = Checks.Run(ship);
            Assert.AreEqual(760.3, StagesOf(answer)[0]["dv_asl_ms"]);
            Assert.AreEqual(1602.4, StagesOf(answer)[2]["dv_asl_ms"]);
            Assert.AreEqual(2362.7, ((Dictionary<string, object>)answer["total"])["dv_asl_ms"], "not 2362.6, the unrounded sum (D078)");
        }

        [TestMethod]
        public void PartsWithoutStagedTargets_EmptyStageTable()
        {
            PartData pod = Part(1, null, 2, 0.8, -1);
            pod.Command = true;
            ShipData ship = new ShipData { Body = "Kerbin", Facility = "VAB", Parts = { pod } };
            Dictionary<string, object> answer = Checks.Run(ship);
            Assert.AreEqual(0, StagesOf(answer).Count);
            CollectionAssert.AreEqual(new string[0], Codes(answer));
        }

        [TestMethod]
        public void SeparatorWithEmptyNode_DropsNothing()
        {
            ShipData ship = Rocket();
            P(ship, 5).Separator.DetachedRoots.Clear();
            Analysis a = new Analysis(ship);
            Assert.IsTrue(a.Remains(P(ship, 5)));
            Assert.IsTrue(a.Remains(P(ship, 6)));
            Assert.IsTrue(a.Present(P(ship, 7), 0));
        }

        [TestMethod]
        public void SeparatorDroppingItself_FallsWithItsSubtree()
        {
            // An omni decoupler with a parent drops every child and itself (D077).
            ShipData ship = Rocket();
            P(ship, 5).Separator.DetachedRoots.Clear();
            P(ship, 5).Separator.DetachedRoots.AddRange(new uint[] { 6, 5 });
            Analysis a = new Analysis(ship);
            Assert.IsTrue(a.Present(P(ship, 5), 3));
            Assert.IsFalse(a.Present(P(ship, 5), 2));
            Assert.IsFalse(a.Present(P(ship, 6), 2));
            Assert.IsFalse(a.Present(P(ship, 7), 2));
            Assert.IsTrue(a.Remains(P(ship, 4)));
        }

        // ---- stage-7 decisions (D084-D089) ----

        [TestMethod]
        public void DetachedSide_EmptyNode_DropsNothing()
            => CollectionAssert.AreEqual(new uint[0], SeparatorData.DetachedSide(false, 5, 4, null, new uint[] { 6 }).ToArray());

        [TestMethod]
        public void DetachedSide_NodeOnParent_DropsItself()
            => CollectionAssert.AreEqual(new uint[] { 5 }, SeparatorData.DetachedSide(false, 5, 4, 4, new uint[] { 6 }).ToArray());

        [TestMethod]
        public void DetachedSide_NodeOnChild_DropsThatChild()
            => CollectionAssert.AreEqual(new uint[] { 6 }, SeparatorData.DetachedSide(false, 5, 4, 6, new uint[] { 6, 8 }).ToArray());

        [TestMethod]
        public void DetachedSide_OmniWithParent_DropsChildrenAndItself()
            => CollectionAssert.AreEquivalent(new uint[] { 6, 8, 5 }, SeparatorData.DetachedSide(true, 5, 4, 6, new uint[] { 6, 8 }).ToArray());

        [TestMethod]
        public void DetachedSide_OmniWithoutParent_DropsChildren()
            => CollectionAssert.AreEquivalent(new uint[] { 6, 8 }, SeparatorData.DetachedSide(true, 5, null, null, new uint[] { 6, 8 }).ToArray());

        [TestMethod]
        public void LowLaunchTwr_JudgedOnUnroundedTwr()
        {
            ShipData ship = Rocket();
            ship.Stages[3].TwrAsl = 0.995;
            Dictionary<string, object> w = Only(Checks.Run(ship), "low_launch_twr");
            Assert.AreEqual(0.995, ((Dictionary<string, object>)w["values"])["twr_asl"], "0.995 cannot lift off (D096)");
            StringAssert.Contains((string)w["message"], "0.995");
            ship.Stages[3].TwrAsl = 1.0;
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(ship)));
            ship.Stages[3].TwrAsl = 0.0;
            Assert.AreEqual(0.0, ((Dictionary<string, object>)Only(Checks.Run(ship), "low_launch_twr")["values"])["twr_asl"],
                "a smaller TWR is worse, so 0 warns (D089)");
        }

        [TestMethod]
        public void NoEngineFuel_StartFuelJudgedAtAnswerPrecision()
        {
            ShipData ship = Rocket();
            PartData extra = Part(8, 5, 10, 0.06, 2, 2);
            extra.Resources.Add(Res("LiquidFuel", 0.08, 0.005));
            Stages(extra, (3, 0.0004, 0.0004), (2, 0.0004, 0.0004));
            ship.Parts.Add(extra);
            CollectionAssert.AreEqual(new string[0], Codes(Checks.Run(ship)), "0.0004 t is 0 in the answer (D087)");

            extra.StageResources.Clear();
            Stages(extra, (3, 0.001, 0.001), (2, 0.001, 0.001));
            Assert.AreEqual(0.001, ((Dictionary<string, object>)Only(Checks.Run(ship), "no_engine_fuel")["values"])["fuel_mass_t"]);
        }

        [TestMethod]
        public void Normalized_ZeroStaysZero()
        {
            Vec v = new Vec(0, 0, 0).Normalized();
            Assert.AreEqual(0.0, v.Length);
        }

        [TestMethod]
        public void Normalized_TinyLengthGivesUnitVector()
        {
            // 1e-20 is below the old 1e-12 threshold and its square (1e-40) does not underflow (D088).
            Vec v = new Vec(1e-20, 0, 0).Normalized();
            Assert.AreEqual(1.0, v.Length, 1e-12);
            Assert.AreEqual(1.0, v.X, 1e-12);
        }

        [TestMethod]
        public void SameShip_SameAnswer()
        {
            string a = KspBridge.Mod.Core.Json.Write(Checks.Run(Rocket()));
            string b = KspBridge.Mod.Core.Json.Write(Checks.Run(Rocket()));
            Assert.AreEqual(a, b);
        }
    }
}
