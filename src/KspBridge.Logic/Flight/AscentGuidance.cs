using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using KspBridge.Attitude;
using KspBridge.Craft;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Gravity-turn ascent in-game: ignite (pad) → vertical climb → kick turn_angle at turn_start_speed →
    /// follow surface prograde within max_aoa_deg → cut at the target apoapsis. Main thread only (docs/ksp-bridge/0040, 0041).
    /// </summary>
    public static class AscentGuidance
    {
        private const float MinThrottle = 0.05f;
        private const double ApproachSeconds = 5.0;
        private const double StageSettleSeconds = 1.0;
        private const float CorrectionStartThrottle = 0.1f;

        public sealed class Args
        {
            public double TargetApoapsis;
            public double Heading = 90;
            public double TurnStartSpeed = 60;
            public double TurnAngle = 10;
            public double MaxAoa = 5;
            public bool AutoStage = true;
            public double ApTolerance = 200;
        }

        // idle | launching | vertical | kick | gravity_turn | coasting | correcting | done | aborted
        public static string State { get; private set; } = "idle";
        public static string Reason { get; private set; }
        public static bool Running => State != "idle" && State != "done" && State != "aborted";

        private static Vessel _v;
        private static Args _a;
        private static float _throttle;
        private static int _partsDied0, _generation;
        private static double _lastAp, _lastUt, _apRatePerThrottle, _lastStageUt;

        public static Dictionary<string, object> Start(Vessel v, Args a)
        {
            if (NodeExecutor.Running)
            {
                throw ApiException.BadRequest("execute_node is running (cancel_node first)");
            }
            if (v.packed || TimeWarp.CurrentRate > 1.01f)
            {
                throw ApiException.BadRequest("stop time warp first");
            }
            bool pad = v.situation == Vessel.Situations.PRELAUNCH;
            if (!pad && v.situation != Vessel.Situations.LANDED && v.situation != Vessel.Situations.FLYING)
            {
                throw ApiException.BadRequest("ascent starts on the ground or in atmospheric flight (situation " + v.situation + ")");
            }
            double atmo = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
            if (a.TargetApoapsis <= atmo)
            {
                throw ApiException.BadRequest("target_apoapsis must be above the atmosphere (" + atmo.ToString("F0") + " m)");
            }
            if (a.TurnStartSpeed <= 0 || a.TurnAngle <= 0 || a.TurnAngle >= 90)
            {
                throw ApiException.BadRequest("turn_start_speed must be > 0 and turn_angle 0..90 (exclusive)");
            }
            if (a.MaxAoa <= 0 || a.MaxAoa > 90 || a.ApTolerance <= 0)
            {
                throw ApiException.BadRequest("max_aoa_deg must be in (0, 90] and ap_tolerance > 0");
            }

            Cancel("restarted");
            _v = v;
            _a = a;
            _partsDied0 = AnomalyMonitor.PartsDied(v.id);
            _apRatePerThrottle = 0;
            _lastUt = 0;
            _lastStageUt = 0;
            Reason = null;
            SetThrottle(pad ? 0f : 1f);
            if (!pad && v.situation == Vessel.Situations.FLYING && SurfacePitch(v) <= 90 - a.TurnAngle + 1)
            {
                // Taking over an ascent already past the kick: follow the surface velocity at once (no vertical command).
                SetFrame("surface_prograde");
                SetState("gravity_turn");
            }
            else
            {
                Hold(90);
                SetState(pad ? "launching" : "vertical");
            }

            return new Dictionary<string, object>
            {
                ["state"] = State,
                ["target_apoapsis"] = a.TargetApoapsis,
                ["atmosphere_height"] = atmo,
                ["heading"] = a.Heading,
                ["turn_start_speed"] = a.TurnStartSpeed,
                ["turn_angle"] = a.TurnAngle,
                ["max_aoa_deg"] = a.MaxAoa,
                ["auto_stage"] = a.AutoStage,
                ["stages"] = StagesDv(v),
            };
        }

        private static List<object> StagesDv(Vessel v)
        {
            List<object> list = new List<object>();
            VesselDeltaV d = v.VesselDeltaV;
            if (d == null || !d.IsReady)
            {
                return list;
            }
            foreach (DeltaVStageInfo s in d.OperatingStageInfo)
            {
                list.Add(new Dictionary<string, object> { ["stage"] = s.stage, ["dv_vac"] = (double)s.deltaVinVac, ["dv_asl"] = (double)s.deltaVatASL });
            }
            return list;
        }

        public static object Describe() => new Dictionary<string, object>
        {
            ["state"] = State,
            ["reason"] = Reason,
            ["target_apoapsis"] = _a?.TargetApoapsis,
        };

        public static void Cancel(string reason)
        {
            if (Running)
            {
                Abort(reason);
            }
        }

        public static void FixedUpdate()
        {
            if (!Running)
            {
                return;
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || FlightGlobals.ActiveVessel != _v || _v.state == Vessel.State.DEAD)
            {
                Abort("vessel_changed");
                return;
            }
            if (!AttitudeController.Active(_v) || AttitudeController.Generation != _generation)
            {
                Abort("attitude_overridden");
                return;
            }
            if (!OwnsThrottle())
            {
                Abort("throttle_overridden");
                return;
            }
            if (AnomalyMonitor.ControlLost)
            {
                Abort("control_lost", AnomalyMonitor.ControlLostDetail());
                return;
            }
            if (AnomalyMonitor.PartsDied(_v.id) > _partsDied0)
            {
                Abort("part_lost", (AnomalyMonitor.PartsDied(_v.id) - _partsDied0) + " part(s) destroyed");
                return;
            }

            double ap = _v.orbit.ApA;
            bool thrusting = State == "vertical" || State == "kick" || State == "gravity_turn" || State == "correcting";
            if (thrusting && Planetarium.GetUniversalTime() - _lastStageUt > StageSettleSeconds && NeedStage(out string why))
            {
                if (!_a.AutoStage)
                {
                    // Manual staging: only an ascent with no thrust at all stops; empty boosters are the user's to drop.
                    if (why == "no_thrust")
                    {
                        Abort("flameout");
                        return;
                    }
                }
                else if (!TryStage(why))
                {
                    return;
                }
            }
            switch (State)
            {
                case "launching":
                    // Engines already lit (staged by hand before the call): do not fire another stage.
                    bool lit = _v.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Any(e => e.EngineIgnited && !e.flameout);
                    if (lit || TryStage("launch"))
                    {
                        SetThrottle(1f);
                        SetState("vertical");
                    }
                    break;

                case "vertical":
                    if (_v.srfSpeed >= _a.TurnStartSpeed)
                    {
                        Hold(90 - _a.TurnAngle);
                        SetState("kick");
                    }
                    break;

                case "kick":
                    if (SurfacePitch(_v) <= 90 - _a.TurnAngle + 1)
                    {
                        SetFrame("surface_prograde");
                        SetState("gravity_turn");
                    }
                    break;

                case "gravity_turn":
                    if (ap >= _a.TargetApoapsis)
                    {
                        SetThrottle(0f);
                        SetFrame("prograde");
                        if (InAtmo())
                        {
                            SetState("coasting");
                        }
                        else
                        {
                            Finish();
                        }
                        break;
                    }
                    SetThrottle(ThrottleFor(ap, 1f));
                    break;

                case "coasting":
                    if (!InAtmo())
                    {
                        Finish();
                    }
                    else if (ap < _a.TargetApoapsis - _a.ApTolerance)
                    {
                        SetState("correcting");
                    }
                    break;

                case "correcting":
                    if (ap >= _a.TargetApoapsis)
                    {
                        SetThrottle(0f);
                        SetState("coasting");
                        break;
                    }
                    // Burn only along prograde.
                    double? err = AttitudeController.ErrorDeg(_v);
                    SetThrottle(err.HasValue && err.Value < 5 ? ThrottleFor(ap, CorrectionStartThrottle) : 0f);
                    break;
            }
        }

        private static bool InAtmo() => _v.mainBody.atmosphere && _v.altitude < _v.mainBody.atmosphereDepth;

        /// <summary>Throttle 0 and release the throttle lock; keep the prograde hold for warp_to ap and circularization.</summary>
        private static void Finish()
        {
            ReleaseThrottle();
            SetState("done");
        }

        /// <summary>Booster drop or empty stage: true when the next stage should fire now.</summary>
        private static bool NeedStage(out string why)
        {
            why = null;
            List<ModuleEngines> lit = _v.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Where(e => e.EngineIgnited).ToList();
            if (lit.All(e => e.flameout || !e.isOperational))
            {
                why = "no_thrust";
                return true;
            }
            // Ignition and clamp release in separate stages: release the clamps once the engines thrust,
            // instead of burning the first stage while held on the pad.
            if (_v.parts.Any(p => p.FindModuleImplementing<LaunchClamp>() != null))
            {
                why = "release_clamps";
                return true;
            }
            int next = _v.currentStage - 1;
            if (next < 0)
            {
                return false;
            }
            HashSet<Part> drops = new HashSet<Part>(CraftAnalysis.Drops(_v, next, new HashSet<Part>(_v.parts)));
            List<ModuleEngines> dropped = lit.Where(e => drops.Contains(e.part)).ToList();
            if (dropped.Count > 0 && dropped.All(e => e.flameout) && lit.Any(e => !drops.Contains(e.part) && !e.flameout))
            {
                why = "boosters_empty";
                return true;
            }
            return false;
        }

        /// <summary>An engine part whose propellants can still be drawn (its own tanks or through crossfeed).</summary>
        private static bool HasFuelledEngine(Part p)
        {
            foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
            {
                if (e.propellants.Count > 0 && e.propellants.All(pr =>
                {
                    p.GetConnectedResourceTotals(pr.id, pr.GetFlowMode(), out double amount, out double _);
                    return amount > 1e-3;
                }))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Pitch of the surface velocity above the horizon (deg).</summary>
        private static double SurfacePitch(Vessel v)
        {
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            return 90 - Vector3d.Angle(v.srf_velocity, up);
        }

        /// <summary>
        /// Full throttle until the apoapsis gets close, then scale by the measured apoapsis rise per unit throttle.
        /// <paramref name="unknown"/> is used while no rate has been measured yet (1 in the ascent, small for a correction).
        /// </summary>
        private static float ThrottleFor(double ap, float unknown)
        {
            double ut = Planetarium.GetUniversalTime();
            if (_lastUt <= 0)
            {
                _lastAp = ap;
                _lastUt = ut;
                return _apRatePerThrottle > 0 ? Scaled(ap) : unknown;
            }
            double dt = ut - _lastUt;
            if (dt > 0.2)
            {
                // Measure only while thrusting: a staging gap would read as a falling apoapsis.
                if (_throttle > MinThrottle && Prediction.Get(_v).ThrustKn > 0)
                {
                    double rate = (ap - _lastAp) / dt / _throttle;
                    if (rate > 0)
                    {
                        _apRatePerThrottle = _apRatePerThrottle <= 0 ? rate : 0.8 * _apRatePerThrottle + 0.2 * rate;
                    }
                }
                _lastAp = ap;
                _lastUt = ut;
            }
            return _apRatePerThrottle > 0 ? Scaled(ap) : unknown;
        }

        private static float Scaled(double ap)
        {
            double t = (_a.TargetApoapsis - ap) / (_apRatePerThrottle * ApproachSeconds);
            return (float)Math.Max(MinThrottle, Math.Min(1.0, t));
        }

        /// <summary>
        /// Fires the next stage unless it has get_stages warnings (stage_warning), would deploy a parachute, or leaves
        /// no engine with fuel attached (out_of_fuel).
        /// </summary>
        private static bool TryStage(string why)
        {
            int next = _v.currentStage - 1;
            if (next < 0)
            {
                Abort("out_of_fuel");
                return false;
            }
            HashSet<Part> attached = new HashSet<Part>(_v.parts);
            List<Part> dropped = CraftAnalysis.Drops(_v, next, attached);
            attached.ExceptWith(dropped);
            List<Part> burning = attached.Where(p => p.inverseStage >= next && p.FindModuleImplementing<ModuleEngines>() != null).ToList();
            List<string> warnings = new List<string>();
            CraftAnalysis.StageWarnings(next, dropped, burning.Count > 0, (bool)CraftAnalysis.Control(attached, burning)["steerable"], warnings);
            if (warnings.Count > 0)
            {
                Abort("stage_warning", string.Join("; ", warnings));
                return false;
            }
            if (attached.Any(p => p.inverseStage == next && p.FindModuleImplementing<ModuleParachute>() != null))
            {
                Abort("out_of_fuel", "next stage " + next + " deploys a parachute");
                return false;
            }
            if (!attached.Any(HasFuelledEngine))
            {
                Abort("out_of_fuel", "no engine with fuel left after stage " + next);
                return false;
            }
            StageManager.ActivateNextStage();
            _lastStageUt = Planetarium.GetUniversalTime();
            _apRatePerThrottle = 0;
            _lastUt = 0;
            FlightRecorder.AscentEvent(_v, new Dictionary<string, object> { ["state"] = "staged", ["stage"] = next, ["why"] = why });
            return true;
        }

        private static void Hold(double pitch)
        {
            AttitudeController.Set(_v, "own", "surface", pitch, _a.Heading, null, 0, 0, null);
            _generation = AttitudeController.Generation;
        }

        private static void SetFrame(string frame)
        {
            AttitudeController.Set(_v, "own", frame, 0, 0, null, 0, 0, _a.MaxAoa);
            _generation = AttitudeController.Generation;
        }

        private static void Abort(string reason, string detail = null)
        {
            // Not running from here on, so an exception below cannot re-enter Abort every tick.
            State = "aborted";
            Reason = reason;
            // Give back only what another tool took: a throttle takeover keeps the attitude hold, an attitude takeover
            // keeps the other tool's hold. Steering is released only when it no longer works or the vessel changed.
            bool keep = reason != "attitude_overridden" && reason != "control_lost" && reason != "vessel_changed" && _v != null;
            try
            {
                ReleaseThrottle();
                if (keep)
                {
                    SetFrame("surface_prograde");
                }
                else if (_v != null && AttitudeController.Active(_v) && AttitudeController.Generation == _generation)
                {
                    AttitudeController.Release();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] ascent abort: " + e);
                keep = false;
            }
            SetState("aborted", detail, keep);
        }

        private static void SetThrottle(float value)
        {
            _throttle = value;
            ThrottleLock.Set(_v, value);
        }

        private static bool OwnsThrottle() =>
            _v != null && ThrottleLock.Active(_v) && Math.Abs(ThrottleLock.Value - _throttle) < 1e-4f;

        /// <summary>Throttle 0 and release the lock, only when this run still owns it.</summary>
        private static void ReleaseThrottle()
        {
            if (OwnsThrottle())
            {
                ThrottleLock.Set(_v, 0f);
                FlightInputHandler.state.mainThrottle = 0f;
                ThrottleLock.Clear();
            }
        }

        private static void SetState(string state, string detail = null, bool? attitudeKept = null)
        {
            State = state;
            if (_v == null)
            {
                return;
            }
            Dictionary<string, object> e = new Dictionary<string, object>
            {
                ["state"] = state,
                ["reason"] = Reason,
                ["altitude"] = Math.Round(_v.altitude, 1),
                ["apoapsis"] = _v.orbit != null ? (object)Math.Round(_v.orbit.ApA, 1) : null,
                ["srf_speed"] = Math.Round(_v.srfSpeed, 1),
                ["stage"] = _v.currentStage,
            };
            if (detail != null)
            {
                e["detail"] = detail;
            }
            if (attitudeKept.HasValue)
            {
                e["attitude_kept"] = attitudeKept.Value;
            }
            FlightRecorder.AscentEvent(_v, e);
        }
    }
}
