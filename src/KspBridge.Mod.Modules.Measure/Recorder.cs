using KspBridge.Mod.Core;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Measure
{
    /// <summary>End reasons of a measurement (spec C3·C9·C10).</summary>
    internal static class MeasureEndReasons
    {
        public const string Stopped = "stopped";
        public const string SampleLimit = "sample_limit";
        public const string NewFlight = "new_flight";
        public const string SceneChange = "scene_change";
        public const string VesselChange = "vessel_change";
        public const string Error = "error";
    }

    /// <summary>One measurement: the samples taken from start to end, and how it ended.</summary>
    internal sealed class Measurement
    {
        public Measurement(int id, double startUt, string vessel, int sampleLimit)
        {
            Id = id;
            StartUt = startUt;
            Vessel = vessel;
            SampleLimit = sampleLimit;
        }

        public int Id { get; }
        public double StartUt { get; }
        public string Vessel { get; }
        public int SampleLimit { get; }

        public List<Sample> Samples { get; } = new List<Sample>();

        /// <summary>Null while running.</summary>
        public double? EndUt { get; private set; }

        /// <summary>Null while running.</summary>
        public string EndReason { get; private set; }

        /// <summary>Set only when the end reason is error.</summary>
        public string Error { get; private set; }

        public bool Running => EndReason == null;

        internal void End(string reason, double endUt, string error)
        {
            EndReason = reason;
            EndUt = endUt;
            Error = reason == MeasureEndReasons.Error ? error : null;
        }
    }

    /// <summary>
    /// Measurement state machine without the game (plan.md "계측 상태 기계"): at most one running measurement and the latest
    /// ended one. A new generation makes a new Recorder, so nothing survives a reload (spec C9).
    /// </summary>
    internal sealed class Recorder
    {
        private int nextId = 1;

        public Measurement Running { get; private set; }

        public Measurement LastEnded { get; private set; }

        /// <summary>The running measurement, otherwise the latest ended one; null when there is none.</summary>
        public Measurement Current => Running ?? LastEnded;

        public bool IsRunning => Running != null;

        /// <summary>Starts a measurement; bad_request (and nothing changes) while one runs. Drops the ended one.</summary>
        public Measurement Start(double startUt, string vessel, int sampleLimit)
        {
            if (Running != null)
                throw BridgeException.BadRequest("measurement " + Running.Id + " is running; stop it with measure_stop first");
            LastEnded = null;
            Running = new Measurement(nextId++, startUt, vessel, sampleLimit);
            return Running;
        }

        /// <summary>
        /// Adds a sample to the running measurement. A sample whose UT did not advance past the previous one is skipped (C5).
        /// Returns true when this sample reached the sample limit and ended the measurement with sample_limit.
        /// </summary>
        public bool Add(Sample sample)
        {
            Measurement m = Running;
            if (m == null)
                return false;
            List<Sample> samples = m.Samples;
            if (samples.Count > 0 && !(sample.Ut > samples[samples.Count - 1].Ut))
                return false;
            samples.Add(sample);
            if (samples.Count < m.SampleLimit)
                return false;
            Stop(MeasureEndReasons.SampleLimit, sample.Ut, null);
            return true;
        }

        /// <summary>Ends the running measurement; returns it, or null when none was running (callers answer bad_request).</summary>
        public Measurement Stop(string reason, double endUt, string error)
        {
            Measurement m = Running;
            if (m == null)
                return null;
            m.End(reason, endUt, error);
            LastEnded = m;
            Running = null;
            return m;
        }
    }
}
