using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// coast: throttle 0, hold a reference direction (none = no attitude control); never completes by itself (until is required).
    /// With warp (default true) it reports the until condition's time target as WarpToUt; the Coordinator decides and calls Warp.
    /// </summary>
    public sealed class CoastMode : IFlightMode
    {
        public static readonly string[] Args = { "reference", "warp" };

        private readonly AttitudeTarget? _target;
        private readonly bool _warp;
        private readonly Plan.Step _step;   // its Until is parsed after the mode arguments

        private CoastMode(string reference, bool warp, Plan.Step step)
        {
            _target = reference == "none" ? (AttitudeTarget?)null : AttitudeTarget.Reference(reference, double.NaN);
            _warp = warp;
            _step = step;
        }

        /// <summary>Validates the step's mode arguments at submit time and returns a factory for the step's mode.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path, Plan.Step step)
        {
            string reference = "prograde";
            if (o.TryGetValue("reference", out object r))
            {
                reference = r as string;
                if (reference == null || (reference != "none" && Array.IndexOf(AttitudeMode.References, reference) < 0))
                {
                    throw Plan.Invalid(path + ".reference", "must be none or one of " + string.Join(", ", AttitudeMode.References));
                }
            }
            bool warp = Plan.OptionalBool(o, "warp", path, true);
            if (!o.ContainsKey("until"))
            {
                throw Plan.Invalid(path + ".until", "required for mode coast (it never completes by itself)");
            }
            return () => new CoastMode(reference, warp, step);
        }

        public bool Done => false;

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            return new ModeCommand(_target, 0)
            {
                WarpToUt = _warp && _step.Until != null ? _step.Until.WarpTargetUt(t, Planetarium.GetUniversalTime()) : null,
            };
        }

        public Dictionary<string, object> Save() => new Dictionary<string, object>();

        public void Load(Vessel v, Dictionary<string, object> state)
        {
        }

        public void End(Vessel v)
        {
        }
    }
}
