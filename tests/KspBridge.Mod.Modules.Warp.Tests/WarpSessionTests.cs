using KspBridge.Mod.Modules.Maneuver;
using KspBridge.Mod.Modules.Warp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace KspBridge.Mod.Modules.Warp.Tests
{
    [TestClass]
    public sealed class WarpSessionTests
    {
        private readonly FakeWarpGame game = new FakeWarpGame();
        private WarpSession session;
        private WarpProgress progress;

        private static WarpArgs Seconds(double s, double? maxRate = null, int? physics = null)
            => new WarpArgs { Goal = WarpGoal.Seconds, Value = s, MaxRate = maxRate, PhysicsRate = physics };

        /// <summary>Begins like the Coordinator: Start and the first Tick in one frame.</summary>
        private void Begin(WarpArgs args)
        {
            session = new WarpSession(game, args);
            game.Step(() =>
            {
                session.Start(game.Ut);
                progress = session.Tick(game.Ut);
            });
        }

        private void Frame() => game.Step(() => progress = session.Tick(game.Ut));

        private void Frames(int count)
        {
            for (int i = 0; i < count && progress == WarpProgress.Running; i++)
                Frame();
        }

        private void Run(Func<bool> until, int limit = 200000)
        {
            for (int i = 0; i < limit && progress == WarpProgress.Running && !until(); i++)
                Frame();
            Assert.IsTrue(until() || progress != WarpProgress.Running, "did not get there");
        }

        private void RunToEnd() => Run(() => false);

        private void ArrivedPrecisely()
        {
            Assert.AreEqual(WarpProgress.Arrived, progress);
            double error = game.Ut - session.TargetUt;
            Assert.IsTrue(error >= 0 && error <= FakeWarpGame.Frame + 1e-6, "error " + error);
            Assert.AreEqual(1.0, game.Rate);
            Assert.AreEqual(0, game.Index);
            Assert.IsFalse(game.OnRails);
        }

        [TestMethod]
        [DataRow(1.0)]
        [DataRow(2.5)]
        [DataRow(3.0)]
        [DataRow(3.01)]
        [DataRow(5.0)]
        [DataRow(13.0)]
        [DataRow(60.0)]
        [DataRow(600.0)]
        [DataRow(1500.0)]
        [DataRow(9999.0)]
        [DataRow(64800.0)]
        [DataRow(1e6)]
        [DataRow(1e8)]
        public void Arrives(double seconds)
        {
            foreach (bool bridgeFirst in new[] { false, true })
            {
                FakeWarpGame g = new FakeWarpGame { BridgeFirst = bridgeFirst };
                WarpSession s = new WarpSession(g, Seconds(seconds));
                WarpProgress p = WarpProgress.Running;
                g.Step(() =>
                {
                    s.Start(g.Ut);
                    p = s.Tick(g.Ut);
                });
                for (int i = 0; i < 200000 && p == WarpProgress.Running; i++)
                    g.Step(() => p = s.Tick(g.Ut));
                Assert.AreEqual(WarpProgress.Arrived, p);
                double error = g.Ut - s.TargetUt;
                Assert.IsTrue(error >= 0 && error <= FakeWarpGame.Frame + 1e-6, "error " + error);
                Assert.IsFalse(g.OnRails);
                Assert.AreEqual(1.0, g.Rate);
                Assert.IsTrue(s.RealSeconds <= Math.Max(4, seconds), "real " + s.RealSeconds);
                Assert.AreEqual(1.0, s.MaxPhysicsRate);
                Assert.AreEqual(0, s.Blocked.Count);
            }
        }

        [TestMethod]
        public void LongWarp_IsFast_AndReachesTheHighestRate()
        {
            Begin(Seconds(1e6));
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(100000.0, session.MaxRailsRate);
            Assert.IsTrue(session.RealSeconds < 60, "real " + session.RealSeconds);
        }

        [TestMethod]
        public void MaxRate_IsNotExceeded()
        {
            Begin(Seconds(600, maxRate: 30));
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(10.0, session.MaxRailsRate);
        }

        [TestMethod]
        public void PastTarget_ArrivesAtOnce()
        {
            Begin(new WarpArgs { Goal = WarpGoal.Ut, Value = 1 });
            Assert.AreEqual(WarpProgress.Arrived, progress);
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(0.0, session.Remaining(game.Ut));
        }

        [TestMethod]
        public void PastTarget_WithWarpOn_GoesDownFirst()
        {
            game.PlayerSets(4);
            for (int i = 0; i < 100; i++)
                game.Step(() => { });
            Assert.IsTrue(game.OnRails);
            Begin(new WarpArgs { Goal = WarpGoal.Ut, Value = 1 });
            Assert.AreEqual(WarpProgress.Running, progress);
            Assert.AreEqual(1.0, game.Rate);
            Frames(50);
            Assert.AreEqual(WarpProgress.Arrived, progress);
            Assert.IsFalse(game.OnRails);
        }

        [TestMethod]
        public void StartedWithThePlayersWarpOn_TakesOver()
        {
            game.PlayerSets(7);
            for (int i = 0; i < 100; i++)
                game.Step(() => { });
            Begin(Seconds(600));
            Assert.IsTrue(game.Rate <= 1000);
            RunToEnd();
            ArrivedPrecisely();
        }

        [TestMethod]
        public void LateOffRails_WaitsForIt()
        {
            game.OffRailsDelay = 500;
            Begin(Seconds(600));
            RunToEnd();
            Assert.AreEqual(WarpProgress.Arrived, progress);
            Assert.IsFalse(game.OnRails);
            Assert.IsTrue(game.Ut - session.TargetUt > 1);
        }

        [TestMethod]
        public void AutoWarp_IsCancelled_AtTheStartAndLater()
        {
            game.AutoWarp = true;
            Begin(Seconds(600));
            Assert.IsFalse(game.AutoWarp);
            game.AutoWarp = true;
            Frame();
            Assert.IsFalse(game.AutoWarp);
        }

        [TestMethod]
        public void PlayerLowersOrRaises_IsCorrected()
        {
            Begin(Seconds(1e6, maxRate: 50));
            Run(() => game.Rate == 50);

            game.PlayerSets(0);
            Frames(100);
            Assert.AreEqual(50.0, game.Rate, "lowered by the player, then raised again from 0 at once");

            game.PlayerSets(7);
            Frames(200);
            Assert.IsTrue(game.Rate <= 50, "rate " + game.Rate);
            Assert.AreEqual(3, game.Index);
        }

        [TestMethod]
        public void PlayerRaisesNearTheTarget_IsCorrected()
        {
            Begin(Seconds(600));
            Run(() => session.Remaining(game.Ut) < 1.5);
            game.PlayerSets(4);
            RunToEnd();
            Assert.AreEqual(WarpProgress.Arrived, progress);
            Assert.AreEqual(1.0, game.Rate);
        }

        [TestMethod]
        public void GameLowersForTheAltitude_AndTheStepRaisesAgain()
        {
            Begin(Seconds(1e6));
            Run(() => game.Rate == 100000);
            game.AltitudeLimit = 3;
            Frames(5);
            Assert.AreEqual(50.0, game.Rate);
            Assert.IsNull(session.BlockedNow);
            game.AltitudeLimit = 7;
            Run(() => game.Rate == 100000);
            RunToEnd();
            ArrivedPrecisely();
        }

        [TestMethod]
        public void Blocked_GoesOnAtOne_AndRailsComeBack()
        {
            game.BlockReason = "in_atmosphere";
            Begin(Seconds(600));
            Frames(100);
            Assert.AreEqual(1.0, game.Rate);
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual("in_atmosphere", session.BlockedNow);

            game.BlockReason = null;
            Run(() => game.Rate > 1);
            Assert.IsNull(session.BlockedNow);
            RunToEnd();
            ArrivedPrecisely();
            CollectionAssert.AreEqual(new[] { "in_atmosphere" }, session.Blocked.ToArray());
            Assert.IsTrue(session.MaxRailsRate > 1);
            Assert.AreEqual(1.0, session.MaxPhysicsRate);
        }

        [TestMethod]
        public void RailsWait_ForTheGameToSeeThrustThatJustBegan()
        {
            Begin(Seconds(300, physics: 4));
            Frames(WarpSession.FirstFrames - 1);
            Assert.AreEqual(0, game.SetRateCalls);
            game.BlockReason = "under_acceleration";
            Run(() => game.Rate == 4);
            Assert.IsFalse(game.RailsMode);
            Assert.IsFalse(game.OnRails);
            Assert.AreEqual(1.0, session.MaxRailsRate);
            CollectionAssert.AreEqual(new[] { "under_acceleration" }, session.Blocked.ToArray());
            RunToEnd();
            ArrivedPrecisely();
        }

        [TestMethod]
        public void RailsWait_OnlyTheFirstFrames()
        {
            Begin(Seconds(600));
            Frames(WarpSession.FirstFrames - 1);
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.IsNull(session.BlockedNow);
            Frame();
            Assert.AreEqual(1, game.SetRateCalls);
            Assert.IsTrue(game.Index > 0);
        }

        [TestMethod]
        public void RailsWait_NotWhenAlreadyOnRails()
        {
            game.PlayerSets(4);
            for (int i = 0; i < 100; i++)
                game.Step(() => { });
            Begin(Seconds(1e6));
            Frames(10);
            Assert.IsTrue(game.OnRails);
            Assert.IsTrue(game.Rate >= 100, "rate " + game.Rate);
        }

        [TestMethod]
        public void Blocked_ReasonsAreListedOnce_InOrder()
        {
            game.BlockReason = "under_acceleration";
            Begin(Seconds(600));
            Frames(10);
            game.BlockReason = "in_atmosphere";
            Frames(10);
            game.BlockReason = "under_acceleration";
            Frames(10);
            CollectionAssert.AreEqual(new[] { "under_acceleration", "in_atmosphere" }, session.Blocked.ToArray());
        }

        [TestMethod]
        public void Blocked_ByAltitudeZeroWithoutReason_IsNamed()
        {
            game.AltitudeLimit = 0;
            Begin(Seconds(600));
            Assert.AreEqual("about_to_crash", session.BlockedNow);
        }

        [TestMethod]
        public void BlockedOnRails_ComesDown()
        {
            Begin(Seconds(1e6, maxRate: 50));
            Run(() => game.Rate == 50);
            game.BlockReason = "in_atmosphere";
            Frames(3);
            Assert.AreEqual(1.0, game.Rate);
            Frames(20);
            Assert.IsFalse(game.OnRails);
            Assert.AreEqual("in_atmosphere", session.BlockedNow);
        }

        [TestMethod]
        public void PhysicsWarp_OnlyWhereBlocked_AndOnlyWhenAsked()
        {
            game.BlockReason = "in_atmosphere";
            Begin(Seconds(10, physics: 4));
            Run(() => game.Rate == 4);
            Assert.IsFalse(game.RailsMode);
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(4.0, session.MaxPhysicsRate);
            Assert.AreEqual(1.0, session.MaxRailsRate);
            Assert.IsTrue(session.RealSeconds < 8, "real " + session.RealSeconds);
        }

        [TestMethod]
        public void PhysicsRate_IsNotUsedWhereRailsAre()
        {
            Begin(Seconds(600, physics: 4));
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(1.0, session.MaxPhysicsRate);
            Assert.IsTrue(session.MaxRailsRate > 1);
        }

        [TestMethod]
        public void PhysicsWarp_ThenRails()
        {
            game.BlockReason = "in_atmosphere";
            Begin(Seconds(600, physics: 2));
            Run(() => game.Rate == 2);
            game.BlockReason = null;
            Run(() => game.RailsMode && game.Rate > 2);
            game.BlockReason = "in_atmosphere";
            Run(() => !game.RailsMode && game.Rate == 2);
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(2.0, session.MaxPhysicsRate);
        }

        [TestMethod]
        public void GameSettings()
        {
            game.RailsAllowed = false;
            Begin(Seconds(10, physics: 4));
            Assert.AreEqual(WarpSession.GameSetting, session.BlockedNow);
            Run(() => game.Rate == 4);
            Assert.IsFalse(game.RailsMode);

            game.PhysicsAllowed = false;
            Frames(3);
            Assert.AreEqual(1.0, game.Rate);
            RunToEnd();
            ArrivedPrecisely();
        }

        [TestMethod]
        [DataRow(500.0)]
        [DataRow(2000.0)]
        [DataRow(20000.0)]
        [DataRow(100000.0)]
        [DataRow(300000.0)]
        public void SphereOfInfluence_IsEnteredSlowly_AndOnlyOnceSlowedDown(double toSoi)
        {
            foreach (bool bridgeFirst in new[] { false, true })
            {
                FakeWarpGame g = new FakeWarpGame { BridgeFirst = bridgeFirst };
                g.SoiUt = g.Ut + toSoi;
                WarpSession s = new WarpSession(g, Seconds(toSoi + 500000));
                WarpProgress p = WarpProgress.Running;
                g.Step(() =>
                {
                    s.Start(g.Ut);
                    p = s.Tick(g.Ut);
                });
                double previous = 1;
                bool lowered = false;
                int raisedAgain = 0;
                for (int i = 0; i < 400000 && p == WarpProgress.Running; i++)
                {
                    g.Step(() => p = s.Tick(g.Ut));
                    if (!g.SoiRate.HasValue)
                    {
                        lowered |= g.Rate < previous;
                        if (lowered && g.Rate > previous)
                            raisedAgain++;
                    }
                    previous = g.Rate;
                }
                Assert.AreEqual(WarpProgress.Arrived, p);
                Assert.IsTrue(g.SoiRate.HasValue && g.SoiRate.Value <= 50, "entered at " + g.SoiRate);
                Assert.AreEqual(0, raisedAgain, "raised again before the sphere of influence");
                Assert.IsTrue(g.Ut - s.TargetUt <= FakeWarpGame.Frame + 1e-6);
            }
        }

        [TestMethod]
        public void NoVessel_FailsAtTheStart_AndLater()
        {
            game.HasVessel = false;
            Begin(Seconds(600));
            Assert.AreEqual(WarpProgress.Failed, progress);
            StringAssert.Contains(session.Failure, "no active vessel");

            game.HasVessel = true;
            Begin(Seconds(600));
            Assert.AreEqual(WarpProgress.Running, progress);
            game.HasVessel = false;
            Frame();
            Assert.AreEqual(WarpProgress.Failed, progress);
        }

        [TestMethod]
        public void BadOrbit_Fails_AndStopReleasesThePlayersWarp()
        {
            game.PlayerSets(4);
            for (int i = 0; i < 100; i++)
                game.Step(() => { });
            game.Orbit.LandedOrSplashed = true;
            Begin(new WarpArgs { Goal = WarpGoal.Apoapsis });
            Assert.AreEqual(WarpProgress.Failed, progress);
            Assert.AreEqual(100.0, session.Stop());
            Assert.AreEqual(1.0, game.Rate);
        }

        [TestMethod]
        public void Node_TargetIsTheBurnStartMinusLead_OrItsFailure()
        {
            game.Node = new NodeStartResult { NodeUt = game.Ut + 1000, BurnSeconds = 40, StartUt = game.Ut + 980 };
            Begin(new WarpArgs { Goal = WarpGoal.Node, Lead = 60 });
            Assert.AreEqual(game.Ut + 920, session.TargetUt, 0.1);
            Assert.IsNull(session.Failure);

            game.Node = new NodeStartResult { Failure = "there is no maneuver node; make one with create_node" };
            Begin(new WarpArgs { Goal = WarpGoal.Node, Lead = 60 });
            Assert.AreEqual(WarpProgress.Failed, progress);
            StringAssert.Contains(session.Failure, "no maneuver node");
        }

        [TestMethod]
        public void Apoapsis_IsDecidedOnce()
        {
            Begin(new WarpArgs { Goal = WarpGoal.Apoapsis, Lead = 30 });
            double target = session.TargetUt;
            Assert.AreEqual(game.Ut + 870, target, 0.1);
            game.Orbit.TimeToApoapsis = 5;
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(target, session.TargetUt);
        }

        [TestMethod]
        public void Stop_GoesToOne_AndTellsTheRate()
        {
            Begin(Seconds(1e6, maxRate: 50));
            Run(() => game.Rate == 50);
            game.AutoWarp = true;
            Assert.AreEqual(50.0, session.Stop());
            Assert.AreEqual(1.0, game.Rate);
            Assert.AreEqual(0, game.Index);
            Assert.IsFalse(game.AutoWarp);
        }

        [TestMethod]
        public void Stop_BeforeTheRateMoved_ReleasesSilently()
        {
            Begin(Seconds(1e6));
            Frames(WarpSession.FirstFrames);
            Assert.IsTrue(game.Index > 0);
            game.Rate = 1;
            Assert.IsNull(session.Stop());
            Assert.AreEqual(0, game.Index);
        }

        [TestMethod]
        public void Stop_WithoutWarp_ChangesNothing()
        {
            game.BlockReason = "in_atmosphere";
            Begin(Seconds(600));
            Assert.IsNull(session.Stop());
            Assert.AreEqual(0, game.SetRateCalls);
        }

        [TestMethod]
        public void Stop_AfterArrival_LeavesTheWarpAlone()
        {
            Begin(new WarpArgs { Goal = WarpGoal.Ut, Value = 1 });
            Assert.AreEqual(WarpProgress.Arrived, progress);
            game.PlayerSets(4);
            game.Rate = 100;
            Assert.IsNull(session.Stop());
            Assert.AreEqual(4, game.Index);
        }

        [TestMethod]
        public void Stop_WithoutAGame_DoesNothing()
        {
            Begin(Seconds(1e6));
            game.Available = false;
            int calls = game.SetRateCalls;
            Assert.IsNull(session.Stop());
            Assert.AreEqual(calls, game.SetRateCalls);
        }

        [TestMethod]
        public void Exception_ThenStop_GoesToOne()
        {
            Begin(Seconds(1e6, maxRate: 50));
            Run(() => game.Rate == 50);
            game.Index = 1;
            game.ThrowOnLimit = true;
            Assert.ThrowsExactly<InvalidOperationException>(() => session.Tick(game.Ut));
            Assert.AreEqual(50.0, session.Stop());
            Assert.AreEqual(1.0, game.Rate);
        }

        [TestMethod]
        public void Throttle_IsPutBackOnArrival()
        {
            game.ThrottleNow = 0.3;
            game.ZeroThrottleOnRails = true;
            Begin(Seconds(600));
            Run(() => game.OnRails);
            Assert.AreEqual(0.0, game.ThrottleNow);
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(0.3, game.ThrottleNow);
            Assert.AreEqual(1, game.ThrottleWrites);
        }

        [TestMethod]
        public void Throttle_IsNotWritten_WhenItIsTheSame()
        {
            game.ThrottleNow = 0.3;
            Begin(Seconds(600));
            RunToEnd();
            ArrivedPrecisely();
            Assert.AreEqual(0, game.ThrottleWrites);
        }

        [TestMethod]
        public void Throttle_IsNotPutBack_OnStop()
        {
            game.ThrottleNow = 0.3;
            game.ZeroThrottleOnRails = true;
            Begin(Seconds(600));
            Run(() => game.OnRails);
            session.Stop();
            Assert.AreEqual(0.0, game.ThrottleNow);
            Assert.AreEqual(0, game.ThrottleWrites);
        }

        [TestMethod]
        public void Remaining_IsNeverNegative()
        {
            Begin(Seconds(1));
            Assert.AreEqual(1.0, session.Remaining(game.Ut), 1e-9);
            Assert.AreEqual(0.0, session.Remaining(game.Ut + 100));
        }

        // --- Orbit check (R01) ---

        private void RunChanging(Action change)
        {
            Begin(Seconds(600));
            change();
            RunToEnd();
        }

        [TestMethod]
        public void Orbit_Kept_WhenNothingChanged()
        {
            RunChanging(() => { });
            ArrivedPrecisely();
            Assert.IsTrue(session.Check.Judged);
            Assert.AreEqual(0, session.Check.Reasons.Count);
            Assert.AreEqual(2863334.0, session.Check.StartOrbit.ApoapsisM.Value);
            Assert.AreEqual(2863334.0, session.Check.EndOrbit.ApoapsisM.Value);
            Assert.AreEqual(game.Ut, session.Check.EndOrbit.Ut);
        }

        [TestMethod]
        public void Orbit_StartUt_IsTheUtOfTheStartFrame()
        {
            double before = game.Ut;
            Begin(Seconds(600));
            Assert.AreEqual(before + FakeWarpGame.Frame, session.Check.StartOrbit.Ut, 1e-9);
        }

        [TestMethod]
        [DataRow(10.0, true)]
        [DataRow(-10.0, true)]
        [DataRow(11.0, false)]
        [DataRow(-11.0, false)]
        public void Orbit_Apoapsis_IsKeptWithinTenMeters(double change, bool kept)
        {
            RunChanging(() => game.ApoapsisM += change);
            Assert.AreEqual(kept ? WarpProgress.Arrived : WarpProgress.Failed, progress);
        }

        [TestMethod]
        [DataRow(10.0, true)]
        [DataRow(-10.0, true)]
        [DataRow(11.0, false)]
        [DataRow(-11.0, false)]
        public void Orbit_Periapsis_IsKeptWithinTenMeters(double change, bool kept)
        {
            RunChanging(() => game.PeriapsisM += change);
            Assert.AreEqual(kept ? WarpProgress.Arrived : WarpProgress.Failed, progress);
        }

        [TestMethod]
        public void Orbit_Changed_FailsWithTheMessageOfTheContract()
        {
            RunChanging(() => game.ApoapsisM += 11);
            Assert.AreEqual(WarpProgress.Failed, progress);
            Assert.AreEqual(
                "the orbit changed during warp without thrust: apoapsis 2863334.0 → 2863345.0 m, periapsis 2863334.0 → 2863334.0 m (limit 10 m)",
                session.Failure);
            Assert.AreEqual(1.0, game.Rate);
            Assert.IsFalse(game.OnRails);
        }

        [TestMethod]
        public void Orbit_Changed_RoundsToOneDecimal()
        {
            game.PeriapsisM = 2863288.14;
            Begin(Seconds(600));
            game.PeriapsisM = 2863300.06;
            RunToEnd();
            StringAssert.Contains(session.Failure, "periapsis 2863288.1 → 2863300.1 m (limit 10 m)");
        }

        [TestMethod]
        public void Orbit_BothOpen_ComparesPeriapsisOnly()
        {
            game.Eccentricity = 1.5;
            game.SemiMajorAxisM = -5e6;
            RunChanging(() => game.ApoapsisM += 500);
            ArrivedPrecisely();
            Assert.IsNull(session.Check.StartOrbit.ApoapsisM);
            Assert.IsNull(session.Check.EndOrbit.ApoapsisM);
        }

        [TestMethod]
        public void Orbit_BothOpen_PeriapsisChangeFails_WithApoapsisNone()
        {
            game.Eccentricity = 1.5;
            RunChanging(() => game.PeriapsisM += 11);
            Assert.AreEqual(WarpProgress.Failed, progress);
            StringAssert.Contains(session.Failure, "apoapsis none → none m");
        }

        [TestMethod]
        public void Orbit_OneOpen_IsAChange()
        {
            RunChanging(() => game.Eccentricity = 1.2);
            Assert.AreEqual(WarpProgress.Failed, progress);
            StringAssert.Contains(session.Failure, "apoapsis 2863334.0 → none m");
        }

        [TestMethod]
        [DataRow("thrust")]
        [DataRow("rcs")]
        [DataRow("atmosphere")]
        [DataRow("parts_lost")]
        [DataRow("soi_change")]
        [DataRow("landed")]
        public void Orbit_NotJudged_WhenAConditionBreaksForOneFrame_EvenWithAChange(string reason)
        {
            Begin(Seconds(600));
            Frames(50);
            Set(game, reason, broken: true);
            Frame();
            Set(game, reason, broken: false);
            game.ApoapsisM += 500;
            RunToEnd();
            Assert.AreEqual(WarpProgress.Arrived, progress);
            CollectionAssert.AreEqual(new[] { reason }, session.Check.Reasons.ToArray());
            Assert.IsFalse(session.Check.Judged);
        }

        private static void Set(FakeWarpGame g, string reason, bool broken)
        {
            switch (reason)
            {
                case "thrust": g.Thrust = broken ? 5 : 0; break;
                case "rcs": g.RcsOn = broken; break;
                case "atmosphere": g.AboveAtmosphere = !broken; break;
                case "parts_lost": g.PartCount = broken ? 11 : 12; break;
                case "soi_change": g.BodyName = broken ? "Mun" : "Kerbin"; break;
                case "landed": g.Landed = broken; break;
                default: throw new ArgumentException(reason);
            }
        }

        [TestMethod]
        public void Orbit_NotJudged_ByTheStartFrame()
        {
            game.Thrust = 3;
            Begin(Seconds(600));
            game.Thrust = 0;
            RunToEnd();
            CollectionAssert.AreEqual(new[] { "thrust" }, session.Check.Reasons.ToArray());
        }

        [TestMethod]
        public void Orbit_NotJudged_ByTheLastFrame()
        {
            Begin(Seconds(600));
            Run(() => session.Remaining(game.Ut) < 0.5);
            game.Landed = true;
            game.ApoapsisM += 500;
            RunToEnd();
            Assert.AreEqual(WarpProgress.Arrived, progress);
            CollectionAssert.AreEqual(new[] { "landed" }, session.Check.Reasons.ToArray());
        }

        [TestMethod]
        public void Orbit_Reasons_AreListedOnceInTheOrderTheyBroke()
        {
            Begin(Seconds(600));
            Frames(30);
            game.RcsOn = true;
            Frames(5);
            game.Thrust = 2;
            Frames(5);
            game.RcsOn = false;
            Frames(5);
            game.RcsOn = true;
            game.Landed = true;
            RunToEnd();
            CollectionAssert.AreEqual(new[] { "rcs", "thrust", "landed" }, session.Check.Reasons.ToArray());
        }

        [TestMethod]
        public void Orbit_NotJudged_ByAFrameOnRails()
        {
            Begin(Seconds(600));
            Run(() => game.OnRails);
            game.Thrust = 1;
            Frame();
            Assert.IsTrue(game.OnRails);
            game.Thrust = 0;
            RunToEnd();
            CollectionAssert.AreEqual(new[] { "thrust" }, session.Check.Reasons.ToArray());
        }

        // --- Rails entry (D01_08) ---

        /// <summary>The rails rate index of the single SetRate(instant) call the callback made; fails when there is not one.</summary>
        private int AppliedIndex()
        {
            string[] set = game.Calls
                .Where(c => c.StartsWith("SetRate:", StringComparison.Ordinal) && c.EndsWith(":True", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(1, set.Length);
            return int.Parse(set[0].Split(':')[1]);
        }

        /// <summary>Runs physics frames until the step has a rate waiting for the LateUpdate; render frames do not run by themselves.</summary>
        private void BeginWaiting(WarpArgs args)
        {
            game.AutoLateUpdate = false;
            Begin(args);
            Run(() => game.LateHooked > 0);
        }

        [TestMethod]
        public void RailsEntry_FromRateOne_PhysicsFrameOnlyHooksTheCallback_CallbackSetsTheRateAtOnce()
        {
            BeginWaiting(Seconds(600));
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(0, game.Index);
            Assert.AreEqual(1, game.LateHooked);
            game.RunLateUpdate();
            Assert.IsTrue(game.Index > 0);
            Assert.AreEqual(game.Index, AppliedIndex());
            Assert.AreEqual(1, game.SetRateCalls);
            Assert.AreEqual(0, game.LateHooked);
            Assert.AreEqual(1, game.InstantRaiseCalls);
        }

        [TestMethod]
        public void RailsEntry_MorePhysicsFramesBeforeTheCallback_StillNoSetRate_OneCallback()
        {
            BeginWaiting(Seconds(600));
            Frames(3);
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(1, game.LateHooked);
            Assert.AreEqual(1, game.LateAdds);
            game.RunLateUpdate();
            Assert.AreEqual(1, game.SetRateCalls);
            Assert.AreEqual(0, game.LateHooked);
        }

        [TestMethod]
        public void RailsEntry_WhileOrbitsAreOverridden_TheCallbackWaits_ThenApplies()
        {
            BeginWaiting(Seconds(600));
            game.OverrideOrbit = true;
            game.RunLateUpdate();
            game.RunLateUpdate();
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(1, game.LateHooked);
            game.OverrideOrbit = false;
            game.RunLateUpdate();
            Assert.AreEqual(1, game.SetRateCalls);
            Assert.AreEqual(0, game.LateHooked);
        }

        [TestMethod]
        public void RailsEntry_StepStopsWhileWaiting_CallbackIsRemoved_NoSetRateAfter()
        {
            BeginWaiting(Seconds(600));
            Assert.IsNull(session.Stop());
            Assert.AreEqual(0, game.LateHooked);
            game.RunLateUpdate();
            Assert.AreEqual(0, game.SetRateCalls);
        }

        [TestMethod]
        public void RailsEntry_RailsBlockedWhileWaiting_PendingIsDropped_ModeChangesInThatFrame()
        {
            BeginWaiting(Seconds(300, physics: 4));
            game.BlockReason = "under_acceleration";
            Frame();
            Assert.AreEqual(0, game.LateHooked);
            Assert.IsFalse(game.RailsMode);
            Assert.IsTrue(game.Calls.Contains("SetMode:False"));
            game.RunLateUpdate();
            Assert.IsFalse(game.Calls.Any(c => c.EndsWith(":True", StringComparison.Ordinal)), "no rails rate set");
        }

        [TestMethod]
        public void RailsEntry_WantedRateBecomesZeroWhileWaiting_PendingIsDropped()
        {
            BeginWaiting(Seconds(600));
            game.Ut = session.TargetUt - 1;
            Frame();
            Assert.AreEqual(0, game.LateHooked);
            game.RunLateUpdate();
            Assert.AreEqual(0, game.SetRateCalls);
        }

        [TestMethod]
        public void RailsEntry_WantedRateChangesWhileWaiting_OnlyTheIndexChanges_OneCallback()
        {
            BeginWaiting(Seconds(1e6));
            game.Ut = session.TargetUt - 1000;
            Frame();
            Assert.AreEqual(1, game.LateHooked);
            Assert.AreEqual(1, game.LateAdds);
            Assert.AreEqual(0, game.SetRateCalls);
            game.RunLateUpdate();
            Assert.AreEqual(5, AppliedIndex());
        }

        [TestMethod]
        public void RailsEntry_FromPhysicsMode_ModeChangesInThatFrame_RateComesInTheCallback()
        {
            game.RailsMode = false;
            game.Index = 1;
            game.Rate = 2;
            game.TargetRate = 2;
            BeginWaiting(Seconds(600));
            Assert.AreEqual(game.Calls.Count - 1, game.Calls.IndexOf("SetMode:True"), "no rate set after the mode change");
            Assert.IsTrue(game.RailsMode);
            game.RunLateUpdate();
            Assert.AreEqual("SetRate:" + game.Index + ":True", game.Calls[game.Calls.Count - 1]);
            Assert.IsTrue(game.Index > 0);
        }

        [TestMethod]
        public void RailsEntry_LoweredFromAboveZero_NoCallback_ChangesInThatFrame()
        {
            BeginWaiting(Seconds(1e6));
            game.RunLateUpdate();
            Assert.AreEqual(1, game.LateAdds);
            Frames(5);
            game.Ut = session.TargetUt - 2100; // this frame advances UT by 2000 at rate 100000
            Frame();
            Assert.AreEqual(1, game.LateAdds);
            Assert.AreEqual("SetRate:4:True", game.Calls[game.Calls.Count - 1]);
            Assert.AreEqual(4, game.Index);
        }

        [TestMethod]
        public void RailsEntry_RaiseFromIndexAboveZero_NoCallback_ThroughTheGame()
        {
            game.OnRails = true;
            game.Index = 1;
            game.Rate = 5;
            game.TargetRate = 5;
            Begin(Seconds(1e6));
            Assert.AreEqual(0, game.LateAdds);
            Assert.AreEqual("SetRate:7:False", game.Calls[0]);
        }

        [TestMethod]
        public void RailsEntry_VesselAlreadyOnRailsAtIndexZero_AlsoWaitsForTheCallback()
        {
            game.OnRails = true;
            game.AutoLateUpdate = false;
            Begin(Seconds(600));
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(1, game.LateHooked);
            game.RunLateUpdate();
            Assert.AreEqual(game.Index, AppliedIndex());
        }

        [TestMethod]
        public void RailsEntry_IndexZeroWhileTheGameStillLowersTheRate_WaitsForTheCallback()
        {
            game.OnRails = true;
            game.BridgeFirst = true;
            game.AutoLateUpdate = false;
            game.Rate = 37;
            Begin(Seconds(600));
            Assert.AreEqual(0, game.Index);
            Assert.AreEqual(0, game.SetRateCalls);
            Assert.AreEqual(1, game.LateHooked);
        }

        [TestMethod]
        public void RailsEntry_NoPhysicsFrameAdvancesUt_WithTheVesselOffRails()
        {
            foreach (bool bridgeFirst in new[] { false, true })
            {
                FakeWarpGame g = new FakeWarpGame { BridgeFirst = bridgeFirst };
                WarpSession s = new WarpSession(g, Seconds(1e6));
                WarpProgress p = WarpProgress.Running;
                g.Step(() =>
                {
                    s.Start(g.Ut);
                    p = s.Tick(g.Ut);
                });
                for (int i = 0; i < 200000 && p == WarpProgress.Running; i++)
                    g.Step(() => p = s.Tick(g.Ut));
                Assert.AreEqual(WarpProgress.Arrived, p);
                Assert.AreEqual(0.0, g.UtInPhysicsAboveRate1);
                Assert.AreEqual(0, g.LateHooked);
            }
        }

        [TestMethod]
        public void PhysicsWarp_RaiseFromZero_NoCallback_SetsTheRateInThatFrame()
        {
            game.BlockReason = "under_acceleration";
            Begin(Seconds(300, physics: 4));
            Run(() => game.Rate == 4);
            Assert.AreEqual(0, game.LateAdds);
            Assert.IsTrue(game.Calls.Contains("SetRate:3:False"));
        }
    }
}
