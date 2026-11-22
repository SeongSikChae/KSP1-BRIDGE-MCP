using KspBridge.Mod.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Measure.Tests
{
    [TestClass]
    public sealed class MeasureReportTests
    {
        private const double Start = 100;
        private const double Step = 0.25;

        /// <summary>
        /// Ten samples 0.25 s apart (t_s = 0, 0.25, ... 2.25). Throttle rises 0..0.9, altitude dips to its minimum at t_s 1.0,
        /// SAS turns on at t_s 0.5 and off at 1.5, stage goes from null to 2 at t_s 1.25.
        /// </summary>
        private static Recorder Recorded(int count = 10, bool stop = true)
        {
            Recorder r = new Recorder();
            r.Start(Start, "Kerbal X", 30000);
            for (int i = 0; i < count; i++)
            {
                double t = i * Step;
                r.Add(new Sample
                {
                    Ut = Start + t,
                    Throttle = i / 10.0,
                    AltitudeM = 1000 + (i - 4) * (i - 4),
                    Sas = t >= 0.5 && t < 1.5,
                    Stage = t >= 1.25 ? 2 : (int?)null,
                    HeadingDeg = 90,
                });
            }
            if (stop)
                r.Stop(MeasureEndReasons.Stopped, Start + (count - 1) * Step, null);
            return r;
        }

        private static Dictionary<string, object> Get(Recorder r, Dictionary<string, object> args = null)
        {
            Dictionary<string, object> answer = MeasureReport.GetAnswer(r.Current, MeasureReport.ParseQuery(args ?? new Dictionary<string, object>()));
            return (Dictionary<string, object>)answer["measurement"];
        }

        private static Dictionary<string, object> Item(Dictionary<string, object> m, string name)
            => (Dictionary<string, object>)((Dictionary<string, object>)m["summary"])[name];

        private static Dictionary<string, object> Range(Dictionary<string, object> m) => (Dictionary<string, object>)m["range"];

        [TestMethod]
        public void NoMeasurement_IsNull()
        {
            Dictionary<string, object> answer = MeasureReport.GetAnswer(null, MeasureReport.ParseQuery(new Dictionary<string, object>()));
            Assert.IsTrue(answer.ContainsKey("measurement"));
            Assert.IsNull(answer["measurement"]);
        }

        [TestMethod]
        public void Header_OfAnEndedMeasurement()
        {
            Dictionary<string, object> m = Get(Recorded());
            Assert.AreEqual(1, m["id"]);
            Assert.AreEqual("ended", m["state"]);
            Assert.AreEqual("Kerbal X", m["vessel"]);
            Assert.AreEqual(Start, m["start_ut"]);
            Assert.AreEqual(Start + 9 * Step, m["end_ut"]);
            Assert.AreEqual("stopped", m["end_reason"]);
            Assert.IsNull(m["error"]);
            Assert.AreEqual(10, m["sample_count"]);
            Assert.AreEqual(30000, m["sample_limit"]);
            Assert.AreEqual(10, Range(m)["sample_count"]);
            Assert.IsNull(Range(m)["from_s"]);
            Assert.IsNull(Range(m)["to_s"]);
            Assert.IsFalse(m.ContainsKey("samples"));
        }

        [TestMethod]
        public void RunningMeasurement_HasNullEnd()
        {
            Dictionary<string, object> m = Get(Recorded(stop: false));
            Assert.AreEqual("running", m["state"]);
            Assert.IsNull(m["end_ut"]);
            Assert.IsNull(m["end_reason"]);
            Assert.IsNull(m["error"]);
            Assert.AreEqual(10, m["sample_count"]);
        }

        [TestMethod]
        public void ErrorEnd_ShowsTheDescription()
        {
            Recorder r = Recorded(stop: false);
            r.Stop(MeasureEndReasons.Error, Start + 1, "NullReferenceException: boom");
            Dictionary<string, object> m = Get(r);
            Assert.AreEqual("error", m["end_reason"]);
            Assert.AreEqual("NullReferenceException: boom", m["error"]);
        }

        [TestMethod]
        public void OtherEnds_HaveNullError()
        {
            foreach (string reason in new[] { "stopped", "sample_limit", "new_flight", "scene_change", "vessel_change" })
            {
                Recorder r = Recorded(stop: false);
                r.Stop(reason, Start + 1, "not shown");
                Assert.IsNull(Get(r)["error"], reason);
            }
        }

        [TestMethod]
        public void Summary_HasEveryNumericAndDiscreteItem()
        {
            Dictionary<string, object> summary = (Dictionary<string, object>)Get(Recorded())["summary"];
            string[] numeric =
            {
                "throttle", "pitch_input", "yaw_input", "roll_input", "mass_t", "thrust_kn", "pitch_deg", "heading_deg", "roll_deg",
                "pitch_rate_dps", "yaw_rate_dps", "roll_rate_dps", "altitude_m", "vertical_speed_mps", "surface_speed_mps", "orbital_speed_mps",
            };
            string[] discrete = { "sas", "rcs", "stage" };
            CollectionAssert.AreEquivalent(numeric.Concat(discrete).ToList(), summary.Keys.ToList());
            foreach (string name in numeric)
                CollectionAssert.AreEquivalent(new[] { "first", "last", "min", "min_t_s", "max", "max_t_s", "mean" },
                    ((Dictionary<string, object>)summary[name]).Keys.ToList(), name);
            foreach (string name in discrete)
                CollectionAssert.AreEquivalent(new[] { "first", "last", "changes" }, ((Dictionary<string, object>)summary[name]).Keys.ToList(), name);
        }

        [TestMethod]
        public void NumberSummary_OverTheWholeMeasurement()
        {
            Dictionary<string, object> m = Get(Recorded());
            Dictionary<string, object> throttle = Item(m, "throttle");
            Assert.AreEqual(0.0, throttle["first"]);
            Assert.AreEqual(0.9, throttle["last"]);
            Assert.AreEqual(0.0, throttle["min"]);
            Assert.AreEqual(0.0, throttle["min_t_s"]);
            Assert.AreEqual(0.9, throttle["max"]);
            Assert.AreEqual(9 * Step, throttle["max_t_s"]);
            Assert.AreEqual(0.45, (double)throttle["mean"], 1e-12);

            Dictionary<string, object> altitude = Item(m, "altitude_m");
            Assert.AreEqual(1000.0, altitude["min"]);
            Assert.AreEqual(1.0, altitude["min_t_s"]);
            Assert.AreEqual(1025.0, altitude["max"]);
            Assert.AreEqual(9 * Step, altitude["max_t_s"]);
        }

        [TestMethod]
        public void NumberSummary_EqualExtremesGiveTheEarliestTime()
        {
            Dictionary<string, object> heading = Item(Get(Recorded()), "heading_deg");
            Assert.AreEqual(0.0, heading["min_t_s"]);
            Assert.AreEqual(0.0, heading["max_t_s"]);
            Assert.AreEqual(90.0, heading["mean"]);
        }

        [TestMethod]
        public void DiscreteSummary_ListsTheChanges()
        {
            Dictionary<string, object> m = Get(Recorded());
            Dictionary<string, object> sas = Item(m, "sas");
            Assert.AreEqual(false, sas["first"]);
            Assert.AreEqual(false, sas["last"]);
            List<object> changes = (List<object>)sas["changes"];
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(0.5, ((Dictionary<string, object>)changes[0])["t_s"]);
            Assert.AreEqual(true, ((Dictionary<string, object>)changes[0])["value"]);
            Assert.AreEqual(1.5, ((Dictionary<string, object>)changes[1])["t_s"]);
            Assert.AreEqual(false, ((Dictionary<string, object>)changes[1])["value"]);

            Dictionary<string, object> stage = Item(m, "stage");
            Assert.IsNull(stage["first"]);
            Assert.AreEqual(2, stage["last"]);
            List<object> stageChanges = (List<object>)stage["changes"];
            Assert.AreEqual(1, stageChanges.Count);
            Assert.AreEqual(1.25, ((Dictionary<string, object>)stageChanges[0])["t_s"]);
            Assert.AreEqual(2, ((Dictionary<string, object>)stageChanges[0])["value"]);

            Assert.AreEqual(0, ((List<object>)Item(m, "rcs")["changes"]).Count);
        }

        [TestMethod]
        public void FrameInterval_MinAndMax()
        {
            Recorder r = Recorded(count: 3, stop: false);
            r.Add(new Sample { Ut = Start + 2 * Step + 0.5 });
            Dictionary<string, object> dt = (Dictionary<string, object>)Get(r)["frame_dt_s"];
            Assert.AreEqual(Step, dt["min"]);
            Assert.AreEqual(0.5, dt["max"]);
        }

        [TestMethod]
        public void Range_SummarisesOnlyItsSamples()
        {
            Dictionary<string, object> m = Get(Recorded(), new Dictionary<string, object> { ["from_s"] = 0.5, ["to_s"] = 1.0 });
            Assert.AreEqual(10, m["sample_count"]);
            Assert.AreEqual(3, Range(m)["sample_count"]);
            Assert.AreEqual(0.5, Range(m)["from_s"]);
            Assert.AreEqual(1.0, Range(m)["to_s"]);
            Dictionary<string, object> throttle = Item(m, "throttle");
            Assert.AreEqual(0.2, throttle["first"]);
            Assert.AreEqual(0.4, throttle["last"]);
            Assert.AreEqual(0.5, throttle["min_t_s"]);
            // SAS is already on at the range start: no change inside the range.
            Assert.AreEqual(0, ((List<object>)Item(m, "sas")["changes"]).Count);
            Assert.AreEqual(true, Item(m, "sas")["first"]);
        }

        [TestMethod]
        public void Range_OneSideOnly()
        {
            Assert.AreEqual(4, Range(Get(Recorded(), new Dictionary<string, object> { ["from_s"] = 1.5 }))["sample_count"]);
            Assert.AreEqual(2, Range(Get(Recorded(), new Dictionary<string, object> { ["to_s"] = 0.25 }))["sample_count"]);
        }

        [TestMethod]
        public void EmptyRange_HasZeroCountAndNullSummary()
        {
            Dictionary<string, object> m = Get(Recorded(), new Dictionary<string, object> { ["from_s"] = 0.3, ["to_s"] = 0.4, ["samples"] = true });
            Assert.AreEqual(0, Range(m)["sample_count"]);
            Assert.IsNull(m["summary"]);
            Assert.IsNull(m["frame_dt_s"]);
            Assert.AreEqual(0, ((List<object>)((Dictionary<string, object>)m["samples"])["rows"]).Count);
        }

        [TestMethod]
        public void OneSampleRange_HasSummaryButNoFrameInterval()
        {
            Dictionary<string, object> m = Get(Recorded(), new Dictionary<string, object> { ["from_s"] = 0.5, ["to_s"] = 0.5 });
            Assert.AreEqual(1, Range(m)["sample_count"]);
            Assert.IsNull(m["frame_dt_s"]);
            Dictionary<string, object> throttle = Item(m, "throttle");
            Assert.AreEqual(0.2, throttle["first"]);
            Assert.AreEqual(0.2, throttle["last"]);
            Assert.AreEqual(0.2, throttle["mean"]);
        }

        [TestMethod]
        public void Samples_AllWhenUnderTheLimit()
        {
            Dictionary<string, object> samples = (Dictionary<string, object>)Get(Recorded(), new Dictionary<string, object> { ["samples"] = true })["samples"];
            Assert.AreEqual(1, samples["stride"]);
            List<object> columns = (List<object>)samples["columns"];
            Assert.AreEqual("t_s", columns[0]);
            Assert.AreEqual("ut", columns[1]);
            Assert.AreEqual(2 + SampleItems.All.Count, columns.Count);
            List<object> rows = (List<object>)samples["rows"];
            Assert.AreEqual(10, rows.Count);
            List<object> last = (List<object>)rows[9];
            Assert.AreEqual(9 * Step, last[0]);
            Assert.AreEqual(Start + 9 * Step, last[1]);
            Assert.AreEqual(columns.Count, last.Count);
        }

        [TestMethod]
        public void Samples_ThinnedWithStride()
        {
            Dictionary<string, object> m = Get(Recorded(), new Dictionary<string, object> { ["samples"] = true, ["max_samples"] = 3.0 });
            Dictionary<string, object> samples = (Dictionary<string, object>)m["samples"];
            Assert.AreEqual(4, samples["stride"]);
            List<object> rows = (List<object>)samples["rows"];
            CollectionAssert.AreEqual(new object[] { 0.0, 4 * Step, 8 * Step }, rows.Select(row => ((List<object>)row)[0]).ToList());
            Assert.AreEqual(10, Range(m)["sample_count"]);
        }

        [TestMethod]
        public void Samples_OnlyTheChosenItems()
        {
            Dictionary<string, object> samples = (Dictionary<string, object>)Get(Recorded(),
                new Dictionary<string, object> { ["samples"] = true, ["items"] = new List<object> { "altitude_m", "stage" } })["samples"];
            CollectionAssert.AreEqual(new object[] { "t_s", "ut", "altitude_m", "stage" }, (List<object>)samples["columns"]);
            List<object> row = (List<object>)((List<object>)samples["rows"])[9];
            Assert.AreEqual(1025.0, row[2]);
            Assert.AreEqual(2, row[3]);
        }

        [TestMethod]
        public void Samples_RangeAndLimitTogether()
        {
            Dictionary<string, object> samples = (Dictionary<string, object>)Get(Recorded(),
                new Dictionary<string, object> { ["samples"] = true, ["from_s"] = 1.0, ["max_samples"] = 2.0 })["samples"];
            Assert.AreEqual(3, samples["stride"]);
            List<object> rows = (List<object>)samples["rows"];
            CollectionAssert.AreEqual(new object[] { 1.0, 1.75 }, rows.Select(row => ((List<object>)row)[0]).ToList());
        }

        [TestMethod]
        public void BadArguments_AreBadRequest()
        {
            Dictionary<string, object>[] bad =
            {
                new Dictionary<string, object> { ["items"] = new List<object> { "nope" } },
                new Dictionary<string, object> { ["items"] = new List<object> { "ut" } },
                new Dictionary<string, object> { ["items"] = new List<object> { "t_s" } },
                new Dictionary<string, object> { ["items"] = new List<object> { 3.0 } },
                new Dictionary<string, object> { ["items"] = "throttle" },
                new Dictionary<string, object> { ["max_samples"] = 0.0 },
                new Dictionary<string, object> { ["max_samples"] = -5.0 },
                new Dictionary<string, object> { ["max_samples"] = 2001.0 },
                new Dictionary<string, object> { ["max_samples"] = 2.5 },
                new Dictionary<string, object> { ["from_s"] = 2.0, ["to_s"] = 1.0 },
                new Dictionary<string, object> { ["from_s"] = "0" },
                new Dictionary<string, object> { ["samples"] = "yes" },
            };
            foreach (Dictionary<string, object> args in bad)
            {
                BridgeException ex = Assert.ThrowsExactly<BridgeException>(() => MeasureReport.ParseQuery(args), Json.Write(args));
                Assert.AreEqual("bad_request", ex.Code);
            }
        }

        [TestMethod]
        public void MaxSamples_Bounds()
        {
            Assert.AreEqual(200, MeasureReport.ParseQuery(new Dictionary<string, object>()).MaxSamples);
            Assert.AreEqual(1, MeasureReport.ParseQuery(new Dictionary<string, object> { ["max_samples"] = 1.0 }).MaxSamples);
            Assert.AreEqual(2000, MeasureReport.ParseQuery(new Dictionary<string, object> { ["max_samples"] = 2000.0 }).MaxSamples);
        }

        [TestMethod]
        public void StartAndStopAnswers()
        {
            Recorder r = new Recorder();
            Measurement m = r.Start(Start, "Kerbal X", 50);
            Dictionary<string, object> start = MeasureReport.StartAnswer(m);
            Assert.AreEqual(1, start["id"]);
            Assert.AreEqual(Start, start["start_ut"]);
            Assert.AreEqual(50, start["sample_limit"]);
            Assert.AreEqual("Kerbal X", start["vessel"]);

            r.Add(new Sample { Ut = Start });
            r.Stop(MeasureEndReasons.Stopped, Start + 1, null);
            Dictionary<string, object> stop = MeasureReport.StopAnswer(m);
            Assert.AreEqual(1, stop["id"]);
            Assert.AreEqual(Start, stop["start_ut"]);
            Assert.AreEqual(Start + 1, stop["end_ut"]);
            Assert.AreEqual(1, stop["sample_count"]);
            Assert.AreEqual("stopped", stop["end_reason"]);
        }
    }
}
