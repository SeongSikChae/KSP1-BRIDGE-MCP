using System;
using System.Collections.Generic;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// gravity_turn: pitch program pitch = 90 − (90 − final_pitch)·f^shape over turn_start..turn_end altitude, limited to max_aoa off
    /// surface prograde (wider at low dynamic pressure). The throttle fades out as the predicted apoapsis (current apoapsis + the rise
    /// from engine tailoff and SRB burn that throttle 0 cannot stop) nears the target. Inside the atmosphere a reached target is kept by
    /// following surface prograde and topping up below 99%. Done above the atmosphere with the target reached and throttle 0.
    /// </summary>
    public sealed class GravityTurnMode : IFlightMode
    {
        public static readonly string[] Args =
            { "heading", "target_apoapsis", "turn_start_altitude", "turn_end_altitude", "final_pitch", "shape", "max_throttle", "max_aoa", "aoa_free_q" };

        private readonly double _heading;
        private readonly double _targetAp;
        private readonly double _start;
        private readonly double _end;
        private readonly double _finalPitch;
        private readonly double _shape;
        private readonly double _maxThrottle;
        private readonly double _maxAoa;
        private readonly double _aoaFreeQ;
        private bool _reached;

        private GravityTurnMode(double heading, double targetAp, double start, double end, double finalPitch, double shape,
            double maxThrottle, double maxAoa, double aoaFreeQ)
        {
            _heading = heading;
            _targetAp = targetAp;
            _start = start;
            _end = end;
            _finalPitch = finalPitch;
            _shape = shape;
            _maxThrottle = maxThrottle;
            _maxAoa = maxAoa;
            _aoaFreeQ = aoaFreeQ;
        }

        /// <summary>Validates the arguments at submit time against the vessel's body; returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> step, string path, CelestialBody body)
        {
            if (!step.TryGetValue("target_apoapsis", out object rawTarget) || rawTarget == null)
            {
                throw Plan.Invalid(path + ".target_apoapsis", "required for mode gravity_turn");
            }
            double targetAp = Plan.OptionalNumber(step, "target_apoapsis", path, 1, double.MaxValue, 0);
            if (body.atmosphere && targetAp <= body.atmosphereDepth)
            {
                throw Plan.Invalid(path + ".target_apoapsis", "must be above the atmosphere (" + body.atmosphereDepth + " m on " + body.bodyName + ")");
            }
            double heading = Plan.OptionalNumber(step, "heading", path, 0, 360, 90);
            double start = Plan.OptionalNumber(step, "turn_start_altitude", path, 0, double.MaxValue, 1000);
            double end = Plan.OptionalNumber(step, "turn_end_altitude", path, 0, double.MaxValue, 45000);
            if (end <= start)
            {
                throw Plan.Invalid(path + ".turn_end_altitude", "must be greater than turn_start_altitude (" + start + ")");
            }
            double finalPitch = Plan.OptionalNumber(step, "final_pitch", path, 0, 90, 0);
            double shape = Plan.OptionalNumber(step, "shape", path, 0.01, 10, 0.6);
            double maxThrottle = Plan.OptionalNumber(step, "max_throttle", path, 0.01, 1, 1);
            double maxAoa = Plan.OptionalNumber(step, "max_aoa", path, 0.1, 90, 5);
            double aoaFreeQ = Plan.OptionalNumber(step, "aoa_free_q", path, 0, double.MaxValue, 1);
            return () => new GravityTurnMode(heading, targetAp, start, end, finalPitch, shape, maxThrottle, maxAoa, aoaFreeQ);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            double alt = t.Num["altitude"];
            double q = t.Num["dynamic_pressure"];
            double f = Math.Max(0, Math.Min(1, (alt - _start) / (_end - _start)));
            double pitch = 90 - (90 - _finalPitch) * Math.Pow(f, _shape);
            AttitudeTarget target = LimitAoa(v, AttitudeTarget.PitchHeading(pitch, _heading), q);

            // Apoapsis even if the throttle went to 0 now: engine tailoff and the SRBs (which cannot be stopped) still add Δv.
            double predictedAp = t.Num["apoapsis"] + EngineModel.ApRise(v, EngineModel.TailoffDv(v) + EngineModel.SrbNetDv(v));
            double throttle = ApThrottle(predictedAp);
            bool inAtmo = v.mainBody.atmosphere && alt <= v.mainBody.atmosphereDepth;

            if (predictedAp >= _targetAp)
            {
                _reached = true;
            }
            if (_reached && inAtmo)
            {
                // Inside the atmosphere: keep going along surface prograde (AoA limit kept) and top up below 99%.
                target = LimitAoa(v, AttitudeTarget.SurfacePrograde(), q);
                throttle = predictedAp < 0.99 * _targetAp ? throttle : 0;
            }
            Done = predictedAp >= _targetAp && throttle <= 0 && !inAtmo;   // a body without air: inAtmo is always false
            return new ModeCommand(target, throttle);
        }

        /// <summary>Keeps the target within max_aoa·max(1, aoa_free_q·10 / q) of surface prograde; no limit at q ≤ aoa_free_q.</summary>
        private AttitudeTarget LimitAoa(Vessel v, AttitudeTarget want, double q)
        {
            if (!(q > _aoaFreeQ) || v.srfSpeed < 1)
            {
                return want;
            }
            double limit = _maxAoa * Math.Max(1, _aoaFreeQ * 10 / q);
            return AttitudeTarget.RotateTowards(v.srf_velocity, want.Direction(v), limit);
        }

        /// <summary>Lowest throttle while the predicted apoapsis is still below the target.</summary>
        private const double MinApproachThrottle = 0.05;

        /// <summary>
        /// Fades from full throttle at 95% of the target to 0 at the target. A purely proportional fade only approaches the target
        /// asymptotically (the throttle goes to 0 with the gap), so the target and the completion condition were never reached
        /// (measured: 79,995 m of 80,000 at throttle 0.001). Below the target the throttle never drops under MinApproachThrottle,
        /// so the apoapsis crosses the target in finite time; 0 at or above it.
        /// </summary>
        private double ApThrottle(double predictedAp)
        {
            if (predictedAp >= _targetAp)
            {
                return 0;
            }
            return Math.Max(MinApproachThrottle, Clamp01((_targetAp - predictedAp) / (0.05 * _targetAp))) * _maxThrottle;
        }

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        public Dictionary<string, object> Save() => new Dictionary<string, object> { ["reached"] = _reached };

        public void Load(Vessel v, Dictionary<string, object> state)
        {
            _reached = state.TryGetValue("reached", out object r) && r is bool b && b;
        }
    }
}
