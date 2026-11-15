using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// Launch clamp hold (#28, design 0064): an on_start stage request whose stage holds launch clamps lights the engines first
    /// and lets the clamps go only once the thrust reaches 90% of what the lit engines can give at this pressure and throttle.
    /// Same stage: the stage's shut-down-able liquid engines are activated directly (KSP's OnActive returns early later for an
    /// ignited engine, so the stage activation then only releases the clamps and lights the SRBs). Split: the engine stage fires,
    /// then the clamp stage. Failure (timeout, engine out, release stage refused): shut the engines down, keep the clamps, abort.
    /// Main thread only.
    /// </summary>
    public sealed class ClampHold
    {
        private enum Phase
        {
            Ignite,
            Wait,
            Release,
        }

        private const double ReleaseRatio = 0.9;
        private const double MinThrottle = 0.01;
        private const double KpaToAtm = 0.00986923;

        private Phase _phase = Phase.Ignite;
        private bool _split;
        private int _clampStage;
        private int _engineStage;
        private double _igniteUt = double.NaN;
        private double _ratio;
        private double _heldS;
        private StageRequest _request;
        private bool _failed;
        private readonly HashSet<uint> _lit = new HashSet<uint>();   // part persistentIds of the engines lit for the hold

        /// <summary>Stage activation already began (engines lit, or the split engine stage requested): a new on_start queues behind
        /// it instead of replacing it (review R1-1-B-3).</summary>
        public bool HasStarted => _phase != Phase.Ignite || (_request != null && _request.Sent);

        /// <summary>Engines lit and the clamps not let go yet: staging condition variable "clamp_hold".</summary>
        public bool Holding => _phase != Phase.Ignite;

        /// <summary>The hold for the next stage, or null when the launch goes as before (no clamp, clamps over several stages,
        /// same stage without a liquid engine, split without an engine stage).</summary>
        public static ClampHold Detect(Vessel v)
        {
            int n = v.currentStage - 1;
            int lo = int.MaxValue, hi = int.MinValue;
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].isLaunchClamp())
                {
                    lo = Math.Min(lo, parts[i].inverseStage);
                    hi = Math.Max(hi, parts[i].inverseStage);
                }
            }
            if (lo != hi)
            {
                return null;
            }
            if (lo == n)
            {
                return Liquid(v, n).Count > 0 ? new ClampHold { _split = false, _clampStage = n, _engineStage = n } : null;
            }
            return lo == n - 1 && StageHasEngine(v, n) ? new ClampHold { _split = true, _clampStage = n - 1, _engineStage = n } : null;
        }

        /// <summary>Done when the clamp stage fired; Failed only when the split engine stage could not fire (nothing lit yet: the
        /// caller's stage_failed as before). Every other failure throws ModeAbortException("clamp_hold_failed").</summary>
        /// <param name="send">False in Submit's trial: detection only, nothing is lit or fired.</param>
        public StageRequest.Result Tick(Vessel v, double ut, bool send, StagingOptions o, Action<string> staged)
        {
            if (!send)
            {
                return StageRequest.Result.Waiting;
            }
            switch (_phase)
            {
                case Phase.Ignite:
                    if (!_split)
                    {
                        List<ModuleEngines> es = Liquid(v, _engineStage);
                        for (int i = 0; i < es.Count; i++)
                        {
                            es[i].Activate();
                        }
                        Started(v, ut);
                        break;
                    }
                    _request = _request ?? new StageRequest();
                    StageRequest.Result r = _request.Tick(v, ut, true);
                    if (r == StageRequest.Result.Failed)
                    {
                        return r;
                    }
                    if (r == StageRequest.Result.Done)
                    {
                        _request = null;
                        staged("on_start");
                        Started(v, ut);
                    }
                    break;

                case Phase.Wait:
                    double ratio = Ratio(v);
                    double held = ut - _igniteUt;
                    if (!EngineModel.AnyOperational(v))
                    {
                        Fail(v, ratio, held, "engine_out");
                    }
                    if (ratio >= ReleaseRatio)
                    {
                        _phase = Phase.Release;
                        _ratio = ratio;
                        _heldS = held;
                        break;
                    }
                    if (held > EngineModel.SpoolUpS(v) + o.ClampHoldS)
                    {
                        Fail(v, ratio, held, "timeout");
                    }
                    break;

                case Phase.Release:
                    _request = _request ?? new StageRequest();
                    StageRequest.Result rr = _request.Tick(v, ut, true);
                    if (rr == StageRequest.Result.Failed)
                    {
                        Fail(v, _ratio, _heldS, "release_failed");   // lit engines, clamps held: never carry on (R1-1-B-1)
                    }
                    if (rr == StageRequest.Result.Done)
                    {
                        _request = null;
                        Event("clamp_released", v, _ratio, _heldS, null);
                        staged("on_start");
                    }
                    return rr;
            }
            return StageRequest.Result.Waiting;
        }

        private void Started(Vessel v, double ut)
        {
            _phase = Phase.Wait;
            _igniteUt = ut;
            _lit.Clear();
            int liquid = 0, solid = 0;
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                if (es[i].part.inverseStage == _engineStage && es[i].EngineIgnited)
                {
                    _lit.Add(es[i].part.persistentId);
                    if (es[i].throttleLocked)
                    {
                        solid++;
                    }
                    else
                    {
                        liquid++;
                    }
                }
            }
            FlightEvents.Add("clamp_hold_started", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["stage"] = _clampStage,
                ["layout"] = _split ? "split" : "same",
                ["engines"] = liquid,
                ["solid"] = solid,
            });
        }

        /// <summary>Thrust of the operational engines over what they can give at this pressure, limiter and throttle; 0 without
        /// throttle or without available thrust (thrust limiter 0).</summary>
        private static double Ratio(Vessel v)
        {
            double throttle = v.ctrlState.mainThrottle;
            if (!(throttle > MinThrottle))
            {
                return 0;
            }
            double max = EngineModel.MaxThrustSum(v, (float)(v.staticPressurekPa * KpaToAtm), v.atmosphericTemperature, v.atmDensity) * throttle;
            double ratio = max > 0 ? EngineModel.ThrustSum(v) / max : 0;
            return double.IsNaN(ratio) ? 0 : ratio;
        }

        private void Fail(Vessel v, double ratio, double held, string reason)
        {
            _failed = true;   // the abort that follows calls Reset: EndedWhileHolding must not shut down and report again
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                if (_lit.Contains(es[i].part.persistentId) && es[i].EngineIgnited && es[i].allowShutdown)
                {
                    es[i].Shutdown();
                }
            }
            Event("clamp_hold_failed", v, ratio, held, reason);
            throw new ModeAbortException("clamp_hold_failed",
                reason + ": thrust " + (ratio * 100).ToString("F0") + "% of available after " + held.ToString("F1") + " s");
        }

        private void Event(string type, Vessel v, double ratio, double held, string reason)
        {
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["stage"] = _clampStage,
                ["thrust_ratio"] = Math.Round(ratio, 3),
                ["hold_s"] = Math.Round(held, 3),
            };
            if (reason != null)
            {
                d["reason"] = reason;
            }
            FlightEvents.Add(type, Coordinator.StepIndex, d);
        }

        /// <summary>Engines of stage n that can be shut down again: the active mode only (MultiModeEngine sets the other module's
        /// isEnabled false), no SRB (throttleLocked), allowShutdown, with propellant.</summary>
        private static List<ModuleEngines> Liquid(Vessel v, int n)
        {
            List<ModuleEngines> list = new List<ModuleEngines>();
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                ModuleEngines e = es[i];
                if (e.part.inverseStage == n && e.isEnabled && e.allowShutdown && !e.throttleLocked && EngineModel.HasPropellantCached(e))
                {
                    list.Add(e);
                }
            }
            return list;
        }

        private static bool StageHasEngine(Vessel v, int n)
        {
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                if (es[i].part.inverseStage == n && es[i].isEnabled)
                {
                    return true;
                }
            }
            return false;
        }

        // ---- reset / handover ----

        /// <summary>The plan ended (complete, abort, user stop) while the engines are lit and the clamps still hold: shut the lit
        /// engines down so nothing burns on the pad with no one left to release the clamps (review R4-2-2). A release stage that
        /// already dropped is not a hold any more.</summary>
        public void EndedWhileHolding(Vessel v)
        {
            if (v == null || !Holding || _failed || (_request != null && _request.Sent && v.currentStage < _request.StageBefore))
            {
                return;
            }
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                if (_lit.Contains(es[i].part.persistentId) && es[i].EngineIgnited && es[i].allowShutdown)
                {
                    es[i].Shutdown();
                }
            }
            Event("clamp_hold_failed", v, Ratio(v), Planetarium.GetUniversalTime() - _igniteUt, "plan_ended");
        }

        /// <summary>A stage request already sent whose stage drop happened before its confirm tick still leaves its staged event.</summary>
        public void RecordSent(Vessel v, Action<string> staged)
        {
            if (v != null && _request != null && _request.Sent && v.currentStage < _request.StageBefore)
            {
                staged("on_start");
            }
        }

        public Dictionary<string, object> Save()
        {
            List<object> lit = new List<object>();
            foreach (uint id in _lit)
            {
                lit.Add((double)id);
            }
            return new Dictionary<string, object>
            {
                ["phase"] = (double)(int)_phase,
                ["split"] = _split,
                ["clamp_stage"] = (double)_clampStage,
                ["engine_stage"] = (double)_engineStage,
                ["ignite_ut"] = _igniteUt,
                ["ratio"] = _ratio,
                ["held_s"] = _heldS,
                ["lit"] = lit,
                ["request"] = _request?.Save(),
            };
        }

        /// <summary>Handover Load rule (null doubles: NaN). null in, null out.</summary>
        public static ClampHold Load(Dictionary<string, object> d)
        {
            if (d == null)
            {
                return null;
            }
            ClampHold h = new ClampHold
            {
                _phase = (Phase)(int)Handover.D(d, "phase", 0),
                _split = Handover.B(d, "split"),
                _clampStage = (int)Handover.D(d, "clamp_stage", -1),
                _engineStage = (int)Handover.D(d, "engine_stage", -1),
                _igniteUt = Handover.D(d, "ignite_ut"),
                _ratio = Handover.D(d, "ratio", 0),
                _heldS = Handover.D(d, "held_s", 0),
                _request = StageRequest.Load(d.TryGetValue("request", out object r) ? r as Dictionary<string, object> : null),
            };
            if (d.TryGetValue("lit", out object l) && l is List<object> lit)
            {
                foreach (object id in lit)
                {
                    h._lit.Add((uint)(double)id);
                }
            }
            return h;
        }
    }
}
