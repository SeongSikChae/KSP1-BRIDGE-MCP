using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Debug.Tests
{
    [TestClass]
    public sealed class SaveFileCheckTests
    {
        private const string Pid = "0123456789abcdef0123456789abcdef";

        private static SaveFileFacts Good() => new SaveFileFacts
        {
            HasGame = true,
            Version = "1.12.5",
            Scene = "7",
            HasFlightState = true,
            VesselCount = 2,
            ActiveVessel = "1",
            ActivePid = Pid,
        };

        private static string Check(SaveFileFacts f) => SaveFileCheck.Check(f, 1, 12, 5);

        private static void Damaged(SaveFileFacts f) => StringAssert.StartsWith(Check(f), "the save file is damaged");

        private static void NotFlight(SaveFileFacts f) => StringAssert.StartsWith(Check(f), "not a save made in flight");

        [TestMethod]
        public void GoodFile_Passes() => Assert.IsNull(Check(Good()));

        [TestMethod]
        public void MissingGame_IsDamaged()
        {
            SaveFileFacts f = Good();
            f.HasGame = false;
            Damaged(f);
            Damaged(null);
        }

        [TestMethod]
        public void BadVersion_IsDamaged()
        {
            foreach (string v in new[] { null, "", "1.12", "1.12.5.1", "1.x.5", "1.12.-5", " 1.12.5" })
            {
                SaveFileFacts f = Good();
                f.Version = v;
                Damaged(f);
            }
        }

        [TestMethod]
        public void BadScene_IsDamaged()
        {
            foreach (string s in new[] { null, "", "FLIGHT", "7.0" })
            {
                SaveFileFacts f = Good();
                f.Scene = s;
                Damaged(f);
            }
        }

        [TestMethod]
        public void BadActiveVessel_IsDamaged()
        {
            foreach (string a in new[] { null, "", "one" })
            {
                SaveFileFacts f = Good();
                f.ActiveVessel = a;
                Damaged(f);
            }
        }

        [TestMethod]
        public void NotFlightScene_IsNotFlight()
        {
            SaveFileFacts f = Good();
            f.Scene = "5";
            NotFlight(f);
        }

        [TestMethod]
        public void NoFlightState_IsNotFlight_NotDamaged()
        {
            SaveFileFacts f = Good();
            f.HasFlightState = false;
            f.ActiveVessel = null;
            f.VesselCount = 0;
            f.ActivePid = null;
            NotFlight(f);
        }

        [TestMethod]
        public void NoVessel_IsNotFlight_NotDamaged()
        {
            SaveFileFacts f = Good();
            f.VesselCount = 0;
            f.ActiveVessel = "0";
            f.ActivePid = null;
            NotFlight(f);
        }

        [TestMethod]
        public void ActiveOutOfRange_IsNotFlight_NotDamaged()
        {
            foreach (string a in new[] { "-1", "2" })
            {
                SaveFileFacts f = Good();
                f.ActiveVessel = a;
                f.ActivePid = null;
                NotFlight(f);
            }
        }

        [TestMethod]
        public void ActivePidMissingOrMalformed_IsDamaged()
        {
            SaveFileFacts missing = Good();
            missing.ActivePid = null;
            StringAssert.Contains(Check(missing), "no pid");

            SaveFileFacts bad = Good();
            bad.ActivePid = "not-a-guid";
            StringAssert.Contains(Check(bad), "not a GUID");
        }

        [TestMethod]
        public void OtherVersion_IsRefused()
        {
            foreach (string v in new[] { "0.12.5", "1.11.5", "1.12.4" })
            {
                SaveFileFacts f = Good();
                f.Version = v;
                string reason = Check(f);
                StringAssert.Contains(reason, v);
                StringAssert.Contains(reason, "1.12.5");
            }
        }

        [TestMethod]
        public void Order_DamagedBeforeNotFlightBeforePidBeforeVersion()
        {
            SaveFileFacts all = Good();
            all.Scene = "x";
            all.HasFlightState = false;
            all.Version = "1.11.0";
            Damaged(all);

            SaveFileFacts notFlightAndOld = Good();
            notFlightAndOld.Scene = "5";
            notFlightAndOld.Version = "1.11.0";
            NotFlight(notFlightAndOld);

            SaveFileFacts pidAndOld = Good();
            pidAndOld.ActivePid = null;
            pidAndOld.Version = "1.11.0";
            StringAssert.Contains(Check(pidAndOld), "no pid");
        }
    }
}
