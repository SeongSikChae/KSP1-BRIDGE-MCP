using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KspBridge.Mod.Modules.Measure.Tests
{
    [TestClass]
    public sealed class RecorderTests
    {
        private static Sample At(double ut) => new Sample { Ut = ut };

        [TestMethod]
        public void StartAndStop_EndsWithStopped()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "Kerbal X", 30000);
            Assert.AreEqual(1, m.Id);
            Assert.IsTrue(r.IsRunning);
            Assert.IsTrue(m.Running);
            Assert.IsNull(m.EndUt);
            Assert.IsNull(m.EndReason);

            r.Add(At(100.02));
            r.Add(At(100.04));
            Measurement ended = r.Stop(MeasureEndReasons.Stopped, 100.04, null);

            Assert.AreSame(m, ended);
            Assert.IsFalse(r.IsRunning);
            Assert.AreSame(m, r.Current);
            Assert.AreEqual(MeasureEndReasons.Stopped, m.EndReason);
            Assert.AreEqual(100.04, m.EndUt);
            Assert.AreEqual(2, m.Samples.Count);
            Assert.IsNull(m.Error);
        }

        [TestMethod]
        public void StartWhileRunning_IsBadRequestAndKeepsTheMeasurement()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "Kerbal X", 30000);
            r.Add(At(100.02));

            BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => r.Start(200, "Other", 10));
            Assert.AreEqual("bad_request", ex.Code);
            Assert.AreSame(m, r.Running);
            Assert.AreEqual(1, m.Samples.Count);
            Assert.AreEqual(30000, m.SampleLimit);
        }

        [TestMethod]
        public void NewStart_DropsTheEndedMeasurementAndNumbersOn()
        {
            Recorder r = new Recorder();
            Measurement first = r.Start(100, "A", 10);
            r.Stop(MeasureEndReasons.Stopped, 101, null);
            Measurement second = r.Start(200, "B", 10);

            Assert.AreEqual(2, second.Id);
            Assert.IsNull(r.LastEnded);
            Assert.AreSame(second, r.Current);
            Assert.AreNotSame(first, r.Current);
        }

        [TestMethod]
        public void StopWithoutRunning_ReturnsNull()
        {
            Recorder r = new Recorder();
            Assert.IsNull(r.Stop(MeasureEndReasons.Stopped, 1, null));
            Assert.IsNull(r.Current);
        }

        [TestMethod]
        public void ReachingTheLimit_EndsWithSampleLimit()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "A", 3);
            Assert.IsFalse(r.Add(At(100.02)));
            Assert.IsFalse(r.Add(At(100.04)));
            Assert.IsTrue(r.Add(At(100.06)));

            Assert.IsFalse(r.IsRunning);
            Assert.AreEqual(MeasureEndReasons.SampleLimit, m.EndReason);
            Assert.AreEqual(100.06, m.EndUt);
            Assert.AreEqual(3, m.Samples.Count);

            Assert.IsFalse(r.Add(At(100.08)));
            Assert.AreEqual(3, m.Samples.Count);
        }

        [TestMethod]
        public void UtNotAdvancing_IsSkipped()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "A", 10);
            r.Add(At(100.02));
            r.Add(At(100.02));
            r.Add(At(100.01));
            r.Add(At(100.04));

            Assert.AreEqual(2, m.Samples.Count);
            Assert.AreEqual(100.04, m.Samples[1].Ut);
        }

        [TestMethod]
        public void FirstSample_MayHaveTheStartUt()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "A", 10);
            r.Add(At(100));
            Assert.AreEqual(1, m.Samples.Count);
        }

        [TestMethod]
        public void AddWithoutRunning_DoesNothing()
        {
            Recorder r = new Recorder();
            Assert.IsFalse(r.Add(At(1)));
            Assert.IsNull(r.Current);
        }

        [TestMethod]
        public void ErrorEnd_KeepsTheDescription()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "A", 10);
            r.Add(At(100.02));
            r.Stop(MeasureEndReasons.Error, 100.02, "NullReferenceException: boom");

            Assert.AreEqual(MeasureEndReasons.Error, m.EndReason);
            Assert.AreEqual("NullReferenceException: boom", m.Error);
            Assert.AreEqual(1, m.Samples.Count);
        }

        [TestMethod]
        public void OtherEnd_HasNoDescription()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(100, "A", 10);
            r.Stop(MeasureEndReasons.SceneChange, 100.5, "ignored");
            Assert.IsNull(m.Error);
        }
    }
}
