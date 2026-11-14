using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight
{
    public enum StagingState
    {
        None,
        Tailoff,
        Separate,
        Settle,
        Ignite,
    }

    /// <summary>staging_options of a plan (validated at submit).</summary>
    public struct StagingOptions
    {
        public double TailoffRatio;
        public double MaxTailoffS;
        public double SettleDistance;
        public double MaxSettleS;
        public double MaxSeparateQ;        // kPa: a parallel separation waits while the dynamic pressure is above this (#16)
        public double MaxSeparateQWaitS;   // s: then it separates anyway (separation_forced)

        public static readonly StagingOptions Default = new StagingOptions
        {
            TailoffRatio = 0.05, MaxTailoffS = 5, SettleDistance = 2, MaxSettleS = 3,
            MaxSeparateQ = 25, MaxSeparateQWaitS = 30,   // TestB: boosters hit the core at 35 kPa and above, none at 30 (0027 unit 2)
        };

        public static StagingOptions Parse(object raw, string path)
        {
            if (raw == null)
            {
                return Default;
            }
            Dictionary<string, object> o = raw as Dictionary<string, object> ?? throw Plan.Invalid(path, "must be an object");
            Plan.UnknownKeys(o, new[] { "tailoff_ratio", "max_tailoff_s", "settle_distance", "max_settle_s", "max_separate_q", "max_separate_q_wait_s" }, path);
            return new StagingOptions
            {
                TailoffRatio = Plan.OptionalNumber(o, "tailoff_ratio", path, 0, 1, Default.TailoffRatio),
                MaxTailoffS = Plan.OptionalNumber(o, "max_tailoff_s", path, 0, 600, Default.MaxTailoffS),
                SettleDistance = Plan.OptionalNumber(o, "settle_distance", path, 0, 1000, Default.SettleDistance),
                MaxSettleS = Plan.OptionalNumber(o, "max_settle_s", path, 0, 600, Default.MaxSettleS),
                MaxSeparateQ = Plan.OptionalNumber(o, "max_separate_q", path, 0, 1000, Default.MaxSeparateQ),
                MaxSeparateQWaitS = Plan.OptionalNumber(o, "max_separate_q_wait_s", path, 0, 600, Default.MaxSeparateQWaitS),
            };
        }
    }

    /// <summary>
    /// Staging: on_start: ["stage"] requests and the auto_stage sequence none → tailoff → separate → settle → ignite
    /// (design "스테이징 시퀀스"). Every stage activation goes through StageRequest (request + confirm). Per-tick paths loop over
    /// cached part/engine sets without LINQ or new collections. Main thread only.
    /// </summary>
    public static class AutoStager
    {
        private const double SeparateThrottleWaitS = 2;   // throttle-down wait limit before a serial separation

        private static StagingState _state = StagingState.None;
        private static double _since;                     // UT the current state was entered
        private static double _burnoutUt, _separateUt, _settleUt;
        private static double _heldUt;                     // UT the dynamic-pressure wait began (0: none)
        private static double _heldS;                      // length of that wait once it ended (staging_completed.held_s)
        private static int _ticks;
        private static bool _parallel;
        private static double _lastUt = double.NegativeInfinity;
        private static StageRequest _request;              // sequence activation (separate / ignite)
        private static StageRequest _onStart;
        private static bool _onStartQueued;
        private static HashSet<uint> _before;              // vessels before the separation (settle candidates are the new ones)
        private static Latch _latch;

        /// <summary>Condition variable "staging".</summary>
        public static string StateName
        {
            get
            {
                switch (_state)
                {
                    case StagingState.Tailoff: return "tailoff";
                    case StagingState.Separate: return "separate";
                    case StagingState.Settle: return "settle";
                    case StagingState.Ignite: return "ignite";
                    default: return "none";
                }
            }
        }

        /// <summary>Telemetry.StagingPending: a sequence is running or an on_start stage request awaits confirmation.
        /// With auto_stage, "previous mode throttle > 0 + no lit engine + engine in the next stage" already entered Ignite in Update,
        /// so later-stage engines alone never wait.</summary>
        public static bool Pending => _state != StagingState.None || _onStart != null || _onStartQueued;

        /// <summary>Tick order (c): on_start request, then burnout detection and state transitions, before the mode runs.</summary>
        /// <param name="send">False in Submit's trial: register and transition only, never call ActivateNextStage.</param>
        public static void Update(Vessel v, Telemetry t, Plan plan, double ut, bool send)
        {
            bool newTick = ut > _lastUt;   // called twice in one physics tick (Submit then Tick): count ticks once
            _lastUt = ut;
            if (_onStart != null)          // on_start first: the sequence waits until it ends (no overlapping requests)
            {
                switch (_onStart.Tick(v, ut, send))
                {
                    case StageRequest.Result.Done:
                        Staged(v, "on_start");
                        _onStart = TakeQueued();
                        break;
                    case StageRequest.Result.Failed:
                        Event("stage_failed", v);
                        _onStart = TakeQueued();   // the step goes on
                        break;
                }
                if (_onStart != null)
                {
                    return;
                }
            }
            if (_state == StagingState.None && !plan.AutoStage)
            {
                return;   // a sequence already running finishes even when the replacing plan has auto_stage false
            }
            StagingOptions o = plan.StagingOptions;
            switch (_state)
            {
                case StagingState.None:
                    if (_latch != null && _latch.Matches(v))
                    {
                        break;   // after out_of_stages / ignite_blocked / stage_failed: nothing until the stage or lit engines change
                    }
                    _latch = null;
                    if (BurnedOut(v))
                    {
                        Enter(StagingState.Tailoff, ut);
                        _burnoutUt = ut;
                        _parallel = IsParallel(v);
                        FlightEvents.Add("stage_burnout", Coordinator.StepIndex, new Dictionary<string, object>
                        {
                            ["stage"] = v.currentStage,
                            ["thrust"] = Math.Round(EngineModel.ThrustSum(v), 3),
                        });
                    }
                    else if (t.PrevModeThrottle > 0 && !EngineModel.AnyOperational(v) && NextStageHasEngine(v))
                    {
                        Enter(StagingState.Ignite, ut);   // ignition owner: never wait with a lit-engine-less vessel and an engine stage ahead
                    }
                    break;

                case StagingState.Tailoff:
                    if ((!SpentSideSrbBurning(v, o.MaxTailoffS) && SpentThrust(v, out double max) <= o.TailoffRatio * max)
                        || ut - _since > o.MaxTailoffS)
                    {
                        Enter(StagingState.Separate, ut);
                        _separateUt = ut;
                        _parallel = IsParallel(v);   // a kept engine may also have flamed out during the tailoff wait
                        _ticks = 0;
                    }
                    break;

                case StagingState.Separate:
                    if (_request == null)   // "next stage" decisions only before the request: activation lowers currentStage at once
                    {
                        if (!NextStageDecouplesFlamedOutEngine(v) || !KeptHasEngine(v))
                        {
                            // Separate only when a decoupler drops the spent engine and the kept side has an engine to use;
                            // otherwise a capsule decoupler or chute after the last stage would fire irreversibly.
                            EndWithLatch(v, "out_of_stages", ut);
                            break;
                        }
                        if (_parallel)
                        {
                            _parallel = IsParallel(v);   // the core may run dry while the boosters wait
                            if (!_parallel)
                            {
                                _since = ut;             // serial from here: the throttle-down wait starts now
                                _ticks = 0;
                                if (_heldUt > 0)
                                {
                                    EndHold(v, ut, "serial");   // the wait ends here; held_s excludes the serial throttle-down and settle
                                }
                            }
                        }
                        if (_parallel && !HeldForQ(v, o, ut))
                        {
                            break;
                        }
                        if (!_parallel)
                        {
                            if (newTick)
                            {
                                _ticks++;   // throttle 0 comes from Override
                            }
                            bool throttleDown = _ticks >= 2
                                && v.ctrlState.mainThrottle == 0 && FlightInputHandler.state.mainThrottle == 0
                                && KeptThrottleDown(v);
                            if (!throttleDown && ut - _since < SeparateThrottleWaitS)
                            {
                                break;
                            }
                        }
                        _before = VesselIds();
                        _request = new StageRequest();
                    }
                    switch (_request.Tick(v, ut, send))
                    {
                        case StageRequest.Result.Done:
                            _request = null;
                            StagedSeparate(v);
                            if (_parallel)
                            {
                                Enter(StagingState.Ignite, ut);
                            }
                            else
                            {
                                Enter(StagingState.Settle, ut);
                                _settleUt = ut;
                            }
                            break;
                        case StageRequest.Result.Failed:
                            _request = null;
                            EndWithLatch(v, "stage_failed", ut);
                            break;
                    }
                    break;

                case StagingState.Settle:
                    if (Separated(v, o.SettleDistance) || ut - _since > o.MaxSettleS)
                    {
                        Enter(StagingState.Ignite, ut);
                    }
                    break;

                case StagingState.Ignite:
                    if (_request == null && EngineModel.AnyOperational(v))
                    {
                        Completed(v, t, ut);   // parallel core, or a decoupler and engine in the same stage: no activation needed
                        break;
                    }
                    if (_request == null)
                    {
                        if (!NextStageHasEngine(v))
                        {
                            EndWithLatch(v, "ignite_blocked", ut);   // fairing, chute...: do not fire it
                            break;
                        }
                        _request = new StageRequest();
                    }
                    switch (_request.Tick(v, ut, send))   // within 0.56 s of the separation: waits for the cooldown
                    {
                        case StageRequest.Result.Done:
                            _request = null;
                            Staged(v, "ignite");
                            Completed(v, t, ut);
                            break;
                        case StageRequest.Result.Failed:
                            _request = null;
                            EndWithLatch(v, "stage_failed", ut);
                            break;
                    }
                    break;
            }
        }

        private static void Enter(StagingState s, double ut)
        {
            _state = s;
            _since = ut;
        }

        private static void Completed(Vessel v, Telemetry t, double ut)
        {
            bool hadSequence = _burnoutUt > 0;
            FlightEvents.Add("staging_completed", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["stage"] = v.currentStage,
                ["tailoff_s"] = hadSequence ? Math.Round(_separateUt - _burnoutUt, 3) : 0.0,
                ["settle_s"] = hadSequence && !_parallel && _settleUt > 0 ? Math.Round(ut - _settleUt, 3) : 0.0,
                ["held_s"] = Math.Round(_heldS, 3),
                ["ignited"] = t.PrevModeThrottle > 0,   // the mode wanted thrust last tick (false while coasting up)
            });
            ResetSequence(ut);
        }

        private static void ResetSequence(double ut)
        {
            Enter(StagingState.None, ut);
            _ticks = 0;
            _parallel = false;
            _before = null;
            _burnoutUt = _separateUt = _settleUt = 0;
            _heldUt = _heldS = 0;
        }

        private static void EndWithLatch(Vessel v, string kind, double ut)
        {
            Event(kind, v);
            _latch = new Latch(v);   // one event per state: allocated once when the latch is set
            ResetSequence(ut);
        }

        private static void Event(string type, Vessel v) =>
            FlightEvents.Add(type, Coordinator.StepIndex, new Dictionary<string, object> { ["stage"] = v.currentStage });

        private static void Staged(Vessel v, string reason) =>
            FlightEvents.Add("staged", Coordinator.StepIndex, new Dictionary<string, object> { ["stage"] = v.currentStage, ["reason"] = reason });

        /// <summary>A separation's staged event carries the dynamic pressure it happened at (#16).</summary>
        private static void StagedSeparate(Vessel v) =>
            FlightEvents.Add("staged", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["stage"] = v.currentStage,
                ["reason"] = "separate",
                ["q_kpa"] = Math.Round(v.dynamicPressurekPa, 3),
            });

        /// <summary>Parallel separation gate (#16): above max_separate_q a booster dropped into the airflow decelerates faster than
        /// the decoupler pushes it aside and hits the core. True = separate now.</summary>
        private static bool HeldForQ(Vessel v, StagingOptions o, double ut)
        {
            double q = v.dynamicPressurekPa;
            if (!(q > o.MaxSeparateQ))
            {
                if (_heldUt > 0)
                {
                    _heldS = ut - _heldUt;   // the wait ends on the tick the request is made
                }
                return true;
            }
            if (_heldUt == 0)
            {
                _heldUt = ut;
                FlightEvents.Add("separation_held", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["stage"] = v.currentStage,
                    ["q_kpa"] = Math.Round(q, 3),
                });
            }
            if (ut - _heldUt < o.MaxSeparateQWaitS)
            {
                return false;
            }
            EndHold(v, ut, "timeout");
            return true;
        }

        /// <summary>The wait ends without the dynamic pressure dropping: timeout (separates anyway) or serial (the core ran dry).</summary>
        private static void EndHold(Vessel v, double ut, string reason)
        {
            _heldS = ut - _heldUt;
            FlightEvents.Add("separation_forced", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["stage"] = v.currentStage,
                ["q_kpa"] = Math.Round(v.dynamicPressurekPa, 3),
                ["reason"] = reason,
            });
        }

        // ---- on_start ----

        /// <summary>BeginStep's on_start: ["stage"]. A request already sent and not yet confirmed is kept until its result;
        /// the new one queues behind it. An unsent request is simply replaced.</summary>
        public static void RequestOnStart()
        {
            if (_onStart != null && _onStart.Sent)
            {
                _onStartQueued = true;
            }
            else
            {
                _onStart = new StageRequest();
            }
        }

        private static StageRequest TakeQueued()
        {
            if (!_onStartQueued)
            {
                return null;
            }
            _onStartQueued = false;
            return new StageRequest();
        }

        // ---- engine sets ----

        /// <summary>Spent = (flameout && (shut down || no propellant)) || (staged && !EngineIgnited && no propellant).
        /// A flamed-out ignited engine with propellant left is a pause KSP lifts itself (UnFlameout), never a burnout (#13).
        /// allowRestart = false engines are shut down by Flameout unless allowShutdown = false (Shutdown returns at once: SRBs stay
        /// Ignited) (ModuleEngines decompile checked).</summary>
        private static bool Spent(Vessel v, ModuleEngines e) =>
            (e.flameout && (!e.EngineIgnited || !EngineModel.HasPropellantCached(e)))
            || (e.part.inverseStage >= v.currentStage && !e.EngineIgnited && !EngineModel.HasPropellantCached(e));

        /// <summary>Burnout by fuel only (never by throttle): ≥ 1 spent engine and no lit unflamed engine, or a spent engine in J_next.</summary>
        private static bool BurnedOut(Vessel v)
        {
            ModuleEngines[] es = EngineModel.Engines(v);
            int spent = 0, burning = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (Spent(v, es[i]))
                {
                    spent++;
                }
                else if (es[i].EngineIgnited)
                {
                    burning++;
                }
            }
            return (spent > 0 && burning == 0) || NextStageDecouplesFlamedOutEngine(v);
        }

        /// <summary>Current and full thrust of the spent engines (remaining thrust while it decays).</summary>
        private static double SpentThrust(Vessel v, out double max)
        {
            ModuleEngines[] es = EngineModel.Engines(v);
            double now = 0;
            max = 0;
            for (int i = 0; i < es.Length; i++)
            {
                if (es[i].EngineIgnited && Spent(v, es[i]))
                {
                    now += es[i].finalThrust;
                    max += es[i].maxThrust * es[i].thrustPercentage / 100;
                }
            }
            return now;
        }

        /// <summary>Parallel boosters: an SRB on the side to drop is still burning and will stop within maxS.</summary>
        private static bool SpentSideSrbBurning(Vessel v, double maxS)
        {
            RefreshSets(v);
            for (int i = 0; i < _spentEngines.Count; i++)
            {
                ModuleEngines e = _spentEngines[i];
                if (EngineModel.IsSolid(e) && !e.flameout && EngineModel.Srb(e).BurnoutS <= maxS)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsParallel(Vessel v)
        {
            RefreshSets(v);
            for (int i = 0; i < _keptEngines.Count; i++)
            {
                if (EngineModel.IsOperational(_keptEngines[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NextStageDecouplesFlamedOutEngine(Vessel v)
        {
            RefreshSets(v);
            for (int i = 0; i < _spentEngines.Count; i++)
            {
                if (Spent(v, _spentEngines[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>K_next only: an operational engine (parallel core) or an unlit fuelled engine of a stage not fired yet.
        /// Later-stage engines on the side to drop (sepratrons) do not count.</summary>
        private static bool KeptHasEngine(Vessel v)
        {
            RefreshSets(v);
            for (int i = 0; i < _keptEngines.Count; i++)
            {
                ModuleEngines e = _keptEngines[i];
                if (EngineModel.IsOperational(e)
                    || (e.part.inverseStage < v.currentStage && !e.EngineIgnited && EngineModel.HasPropellantCached(e)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Serial separation wait: every lit engine not on the side to drop has currentThrottle below 0.01.</summary>
        private static bool KeptThrottleDown(Vessel v)
        {
            RefreshSets(v);
            for (int i = 0; i < _keptEngines.Count; i++)
            {
                if (_keptEngines[i].EngineIgnited && _keptEngines[i].currentThrottle >= 0.01f)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool NextStageHasEngine(Vessel v)
        {
            RefreshSets(v);
            return _nextHasEngine;
        }

        // ---- separated parts ----

        /// <summary>J_n = D_n: the side cut off from the root is thrown away; the root side stays the original Vessel.</summary>
        public static HashSet<Part> Jettison(Vessel v, int n) => Cut(v, n);

        /// <summary>D_n: parts cut off from the root when stage n fires (ModuleDecouple and ModuleAnchoredDecoupler, same rule).</summary>
        public static HashSet<Part> Cut(Vessel v, int n)
        {
            HashSet<Part> cut = new HashSet<Part>();
            foreach (Part d in v.parts)
            {
                if (d.inverseStage != n)
                {
                    continue;
                }
                ModuleDecouplerBase m = d.FindModuleImplementing<ModuleDecouplerBase>();
                if (m == null || m.isDecoupled)
                {
                    continue;
                }
                if (m.isOmniDecoupler)
                {
                    AddTree(cut, d);
                    continue;
                }
                AttachNode node = m.explosiveNodeID == "srf" ? d.srfAttachNode : d.FindAttachNode(m.explosiveNodeID);
                Part other = node?.attachedPart;
                if (other == null)
                {
                    continue;
                }
                AddTree(cut, other == d.parent ? d : other);
            }
            return cut;
        }

        private static void AddTree(HashSet<Part> set, Part p)
        {
            set.Add(p);
            for (int i = 0; i < p.children.Count; i++)
            {
                AddTree(set, p.children[i]);
            }
        }

        // Cache of J_next / K_next engines and "next stage has an engine". Key: vessel object, currentStage, part count.
        private static Vessel _setVessel;
        private static int _setStage = int.MinValue;
        private static int _setParts = -1;
        private static bool _nextHasEngine;
        private static readonly HashSet<Part> _spent = new HashSet<Part>();
        private static readonly List<ModuleEngines> _spentEngines = new List<ModuleEngines>();
        private static readonly List<ModuleEngines> _keptEngines = new List<ModuleEngines>();

        private static void RefreshSets(Vessel v)
        {
            if (ReferenceEquals(v, _setVessel) && v.currentStage == _setStage && v.parts.Count == _setParts)
            {
                return;
            }
            _setVessel = v;
            _setStage = v.currentStage;
            _setParts = v.parts.Count;
            _spent.Clear();
            _spent.UnionWith(Cut(v, v.currentStage - 1));
            _spentEngines.Clear();
            _keptEngines.Clear();
            _nextHasEngine = false;
            ModuleEngines[] es = EngineModel.Engines(v);
            for (int i = 0; i < es.Length; i++)
            {
                (_spent.Contains(es[i].part) ? _spentEngines : _keptEngines).Add(es[i]);
                if (es[i].part.inverseStage == v.currentStage - 1)
                {
                    _nextHasEngine = true;
                }
            }
            Debug.Log("[KspBridge] staging sets: stage " + v.currentStage + " parts " + v.parts.Count + " J_next " + _spent.Count
                + " spent engines " + _spentEngines.Count + " kept engines " + _keptEngines.Count + " next has engine " + _nextHasEngine);
        }

        private static void ClearSets()
        {
            _setVessel = null;
            _setStage = int.MinValue;
            _setParts = -1;
            _nextHasEngine = false;
            _spent.Clear();
            _spentEngines.Clear();
            _keptEngines.Clear();
        }

        private static HashSet<uint> VesselIds()
        {
            HashSet<uint> ids = new HashSet<uint>();
            List<Vessel> all = FlightGlobals.Vessels;
            for (int i = 0; i < all.Count; i++)
            {
                ids.Add(all[i].persistentId);
            }
            return ids;
        }

        /// <summary>Every vessel new since the separation is at least distance away (closest collider bounds) and moving apart.
        /// No candidate is not "separated": wait until max_settle_s.</summary>
        private static bool Separated(Vessel v, double distance)
        {
            int n = 0;
            List<Vessel> all = FlightGlobals.Vessels;
            for (int i = 0; i < all.Count; i++)
            {
                Vessel x = all[i];
                if (x == v || !x.loaded || _before == null || _before.Contains(x.persistentId))
                {
                    continue;
                }
                n++;
                if (MinPartDistance(x, v) < distance || Vector3d.Dot(x.obt_velocity - v.obt_velocity, x.CoMD - v.CoMD) <= 0)
                {
                    return false;
                }
            }
            return n > 0;
        }

        /// <summary>Closest collider surface distance between two vessels. The world AABB distance is only a lower bound
        /// (a tilted stack inflates its boxes), so it just skips pairs that cannot beat the current minimum.</summary>
        private static double MinPartDistance(Vessel a, Vessel b)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < a.parts.Count; i++)
            {
                Collider ca = a.parts[i].collider;
                if (ca == null)
                {
                    continue;
                }
                Bounds ba = ca.bounds;
                for (int j = 0; j < b.parts.Count; j++)
                {
                    Collider cb = b.parts[j].collider;
                    if (cb == null)
                    {
                        continue;
                    }
                    Bounds bb = cb.bounds;
                    if (BoundsDistance(ba, bb) < min)
                    {
                        min = Math.Min(min, ColliderDistance(ca, cb, ba, bb));
                    }
                }
            }
            return min;
        }

        /// <summary>Surface distance of two convex colliders by alternating closest-point projection (0 when they overlap);
        /// the box distance for collider types ClosestPoint does not support.</summary>
        private static double ColliderDistance(Collider a, Collider b, Bounds ba, Bounds bb)
        {
            if (!ClosestPointSupported(a) || !ClosestPointSupported(b))
            {
                return BoundsDistance(ba, bb);
            }
            Vector3 pa = a.ClosestPoint(bb.center);
            Vector3 pb = b.ClosestPoint(pa);
            for (int k = 0; k < 16; k++)
            {
                pa = a.ClosestPoint(pb);
                Vector3 next = b.ClosestPoint(pa);
                bool done = (next - pb).sqrMagnitude < 1e-6f;
                pb = next;
                if (done)
                {
                    break;
                }
            }
            return (pa - pb).magnitude;
        }

        private static bool ClosestPointSupported(Collider c)
        {
            MeshCollider m = c as MeshCollider;
            return m != null ? m.convex : c is BoxCollider || c is SphereCollider || c is CapsuleCollider;
        }

        private static double BoundsDistance(Bounds a, Bounds b)
        {
            double dx = Math.Max(0, Math.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            double dy = Math.Max(0, Math.Max(a.min.y - b.max.y, b.min.y - a.max.y));
            double dz = Math.Max(0, Math.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ---- latch ----

        /// <summary>Stage and lit-engine set when a sequence ended without staging; nothing is detected again until either changes.</summary>
        private sealed class Latch
        {
            public readonly int Stage;
            public readonly HashSet<uint> Ignited = new HashSet<uint>();

            public Latch(Vessel v)
            {
                Stage = v.currentStage;
                ModuleEngines[] es = EngineModel.Engines(v);
                for (int i = 0; i < es.Length; i++)
                {
                    if (es[i].EngineIgnited)
                    {
                        Ignited.Add(es[i].part.persistentId);
                    }
                }
            }

            public Latch(int stage, List<object> ids)
            {
                Stage = stage;
                foreach (object id in ids)
                {
                    Ignited.Add((uint)(double)id);
                }
            }

            /// <summary>Same stage and same lit engines (no allocation).</summary>
            public bool Matches(Vessel v)
            {
                if (v.currentStage != Stage)
                {
                    return false;
                }
                ModuleEngines[] es = EngineModel.Engines(v);
                int n = 0;
                for (int i = 0; i < es.Length; i++)
                {
                    if (es[i].EngineIgnited)
                    {
                        if (!Ignited.Contains(es[i].part.persistentId))
                        {
                            return false;
                        }
                        n++;
                    }
                }
                return n == Ignited.Count;
            }
        }

        // ---- reset / handover ----

        /// <summary>A request already sent whose stage drop happened before its confirm tick still leaves its staged event.
        /// v = plan vessel (null when the active vessel is not the plan vessel → no event).</summary>
        private static void RecordSent(Vessel v)
        {
            if (v == null)
            {
                return;
            }
            if (_request != null && _request.Sent && v.currentStage < _request.StageBefore)
            {
                if (_state == StagingState.Separate)
                {
                    StagedSeparate(v);
                }
                else
                {
                    Staged(v, "ignite");
                }
            }
            if (_onStart != null && _onStart.Sent && v.currentStage < _onStart.StageBefore)
            {
                Staged(v, "on_start");
            }
        }

        /// <summary>Release (complete, abort, failure after attach): clears the sequence, requests and latch; keeps caches and _lastUt.</summary>
        public static void Reset(Vessel v)
        {
            RecordSent(v);
            _latch = null;
            _request = null;
            _onStart = null;
            _onStartQueued = false;
            ResetSequence(_lastUt);
        }

        /// <summary>Coordinator.Stop: Reset plus caches and _lastUt (an unloading logic holds no part references).</summary>
        public static void Clear(Vessel v)
        {
            Reset(v);
            _lastUt = double.NegativeInfinity;
            ClearSets();
            EngineModel.ClearCaches();
        }

        /// <summary>Tick order (e): cold staging holds throttle 0 while separating and settling; attitude stays.</summary>
        public static ModeCommand Override(ModeCommand cmd)
        {
            if (!_parallel && (_state == StagingState.Separate || _state == StagingState.Settle))
            {
                cmd.Throttle = 0;
            }
            return cmd;
        }

        public static Dictionary<string, object> Save()
        {
            List<object> before = null;
            if (_before != null)
            {
                before = new List<object>();
                foreach (uint id in _before)
                {
                    before.Add((double)id);
                }
            }
            List<object> latchIds = null;
            if (_latch != null)
            {
                latchIds = new List<object>();
                foreach (uint id in _latch.Ignited)
                {
                    latchIds.Add((double)id);
                }
            }
            return new Dictionary<string, object>
            {
                ["state"] = (double)(int)_state,
                ["since"] = _since,
                ["burnout_ut"] = _burnoutUt,
                ["separate_ut"] = _separateUt,
                ["settle_ut"] = _settleUt,
                ["held_ut"] = _heldUt,
                ["held_s"] = _heldS,
                ["ticks"] = (double)_ticks,
                ["parallel"] = _parallel,
                ["last_ut"] = _lastUt,
                ["request"] = _request?.Save(),
                ["on_start"] = _onStart?.Save(),
                ["on_start_queued"] = _onStartQueued,
                ["before"] = before,
                ["latch_stage"] = _latch != null ? (object)(double)_latch.Stage : null,
                ["latch_ignited"] = latchIds,
            };
        }

        /// <summary>Submit's trial restore and the reload handover. Doubles follow the handover Load rule (null = NaN;
        /// last_ut: −∞, its initial value).</summary>
        public static void Load(Dictionary<string, object> d)
        {
            _state = (StagingState)(int)Handover.D(d, "state", 0);
            _since = Handover.D(d, "since");
            _burnoutUt = Handover.D(d, "burnout_ut");
            _separateUt = Handover.D(d, "separate_ut");
            _settleUt = Handover.D(d, "settle_ut");
            _heldUt = Handover.D(d, "held_ut", 0);
            _heldS = Handover.D(d, "held_s", 0);
            _ticks = (int)Handover.D(d, "ticks", 0);
            _parallel = Handover.B(d, "parallel");
            _lastUt = Handover.D(d, "last_ut", double.NegativeInfinity);
            _request = StageRequest.Load(d.TryGetValue("request", out object r) ? r as Dictionary<string, object> : null);
            _onStart = StageRequest.Load(d.TryGetValue("on_start", out object os) ? os as Dictionary<string, object> : null);
            _onStartQueued = Handover.B(d, "on_start_queued");
            _before = null;
            if (d.TryGetValue("before", out object b) && b is List<object> before)
            {
                _before = new HashSet<uint>();
                foreach (object id in before)
                {
                    _before.Add((uint)(double)id);
                }
            }
            _latch = d.TryGetValue("latch_stage", out object ls) && ls is double stage
                && d.TryGetValue("latch_ignited", out object li) && li is List<object> ids ? new Latch((int)stage, ids) : null;
        }

        /// <summary>A resume that failed before Load: the handed-over requests are not in memory, so their sent-and-confirmed check
        /// (as in RecordSent) is made from the handed-over staging alone. v null (another vessel): nothing.</summary>
        public static void RecordHandedRequests(Vessel v, Dictionary<string, object> staging)
        {
            if (v == null || staging == null)
            {
                return;
            }
            StageRequest request = StageRequest.Load(staging.TryGetValue("request", out object r) ? r as Dictionary<string, object> : null);
            StageRequest onStart = StageRequest.Load(staging.TryGetValue("on_start", out object os) ? os as Dictionary<string, object> : null);
            StagingState state = (StagingState)(int)Handover.D(staging, "state", 0);
            if (request != null && request.Sent && v.currentStage < request.StageBefore)
            {
                if (state == StagingState.Separate)
                {
                    StagedSeparate(v);
                }
                else
                {
                    Staged(v, "ignite");
                }
            }
            if (onStart != null && onStart.Sent && v.currentStage < onStart.StageBefore)
            {
                Staged(v, "on_start");
            }
        }
    }
}
