using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// attitude: hold an attitude and a fixed throttle; never completes by itself (until is required).
    /// Attitude arguments are optional: pitch + heading (+ roll), or reference (+ roll); without them only the throttle is set.
    /// </summary>
    public sealed class AttitudeMode : IFlightMode
    {
        public static readonly string[] Args = { "pitch", "heading", "roll", "reference", "throttle" };

        public static readonly string[] References =
            { "prograde", "retrograde", "surface_prograde", "normal", "antinormal", "radial_in", "radial_out" };

        private readonly double _throttle;
        private readonly AttitudeTarget _target;   // null = no attitude control

        private AttitudeMode(double pitch, double heading, double roll, string reference, double throttle)
        {
            _throttle = throttle;
            if (reference != null)
            {
                _target = AttitudeTarget.Reference(reference, roll);
            }
            else if (!double.IsNaN(pitch))
            {
                _target = AttitudeTarget.PitchHeading(pitch, heading, roll);
            }
        }

        /// <summary>Validates the step's mode arguments at submit time and returns a factory for the step's mode.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> step, string path)
        {
            double pitch = Plan.OptionalNumber(step, "pitch", path, -90, 90, double.NaN);
            double heading = Plan.OptionalNumber(step, "heading", path, 0, 360, double.NaN);
            double roll = Plan.OptionalNumber(step, "roll", path, -180, 180, double.NaN);
            double throttle = Plan.OptionalNumber(step, "throttle", path, 0, 1, 0);
            string reference = null;
            if (step.TryGetValue("reference", out object r))
            {
                reference = r as string;
                if (reference == null || Array.IndexOf(References, reference) < 0)
                {
                    throw Plan.Invalid(path + ".reference", "must be one of " + string.Join(", ", References));
                }
                if (!double.IsNaN(pitch) || !double.IsNaN(heading))
                {
                    throw Plan.Invalid(path + ".reference", "give either reference or pitch/heading, not both");
                }
            }
            if (double.IsNaN(pitch) != double.IsNaN(heading))
            {
                throw Plan.Invalid(path + (double.IsNaN(pitch) ? ".pitch" : ".heading"), "pitch and heading are given together");
            }
            if (!double.IsNaN(roll) && double.IsNaN(pitch) && reference == null)
            {
                throw Plan.Invalid(path + ".roll", "roll needs pitch/heading or reference");
            }
            if (!step.ContainsKey("until"))
            {
                throw Plan.Invalid(path + ".until", "required for mode attitude (it never completes by itself)");
            }
            return () => new AttitudeMode(pitch, heading, roll, reference, throttle);
        }

        public bool Done => false;

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t) => new ModeCommand(_target, _throttle);

        public Dictionary<string, object> Save() => new Dictionary<string, object>();

        public void Load(Vessel v, Dictionary<string, object> state)
        {
        }
    }
}
