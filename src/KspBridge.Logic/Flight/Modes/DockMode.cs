using KspBridge.Debugging;
using KspBridge.Flight.Maneuver;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// dock (design 0032 decisions 25~29, 33, 34): docks the own port (port argument, or the only free port of the target port's
    /// size) to the target docking port. Begin only checks (the trial changes nothing); the first real tick makes the own port the
    /// control point ("Control from Here") and starts the coupling watch. The attitude points the own port against the target
    /// port's axis (no roll alignment); RCS translation follows: Route (behind the port plane: around the target to the hold-off
    /// point) → BackOut → Align (lateral error) → Advance (along the axis, at most MaxSpeed, ContactSpeed near contact) → Contact.
    /// A contact that does not couple and moves away again is a failed attempt: back off and retry; more than retries →
    /// dock_failed. The Coordinator adopts the merged vessel on coupling and the step completes (docked). End restores the
    /// control part. Reasons port_incompatible, no_target, dock_failed. Events dock_attempt, docked, rcs_unavailable.
    /// </summary>
    public sealed class DockMode : IFlightMode
    {
        public static readonly string[] Args = { "port", "retries" };

        private const double HoldOffM = 10;
        private const double LateralGoM = 0.2;
        private const double LateralStopM = 0.5;
        private const double ContactM = 0.3;
        private const double MaxSpeed = 1.0;
        private const double ContactSpeed = 0.2;
        private const double LateralGain = 0.2;   // m/s per m of lateral error
        private const double AdvanceGateDeg = 2;  // no advance while the port is not aligned
        private const double ForcedStopM = 2;     // debug_dock_reject: stop this far before contact
        private const double ForcedRestS = 5;
        private const double ClearanceM = 5;

        private readonly double? _portArg;
        private readonly int _retries;
        private readonly bool? _stepRcs;

        private ModuleDockingNode _own;
        private ModuleDockingNode _tgt;
        private bool _armed;
        private bool _watchRestored;
        private bool _coupledSaved;
        private uint _controlBefore;
        private int _attempts;
        private bool _contactSeen;
        private bool _retreat;
        private bool _routed;
        private double _restSince = double.NaN;
        private readonly List<string> _reported = new List<string>();

        private DockMode(double? portArg, int retries, bool? stepRcs)
        {
            _portArg = portArg;
            _retries = retries;
            _stepRcs = stepRcs;
        }

        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double? port = o.TryGetValue("port", out object p) && p != null
                ? Plan.OptionalNumber(o, "port", path, 0, uint.MaxValue, 0)
                : (double?)null;
            int retries = (int)Plan.OptionalNumber(o, "retries", path, 0, 10, 2);
            bool? stepRcs = o.TryGetValue("rcs", out object r) ? r as bool? : null;
            return () => new DockMode(port, retries, stepRcs);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)   // checks only (the trial changes nothing)
        {
            _tgt = v.targetObject as ModuleDockingNode
                ?? throw new ModeAbortException("port_incompatible", "the target is not a docking port (set_target vessel_id + port)");
            if (!Targeting.IsFree(_tgt))
            {
                throw new ModeAbortException("port_incompatible", "the target port is already docked (" + _tgt.state + ")");
            }
            _own = ResolveOwnPort(v, _tgt.nodeType)
                ?? throw new ModeAbortException("port_incompatible", "no own docking port of size " + _tgt.nodeType
                    + (_portArg.HasValue ? " with part id " + _portArg.Value : " (or more than one: give port)"));
        }

        private ModuleDockingNode ResolveOwnPort(Vessel v, string type)
        {
            ModuleDockingNode found = null;
            int count = 0;
            foreach (Part p in v.parts)
            {
                foreach (ModuleDockingNode n in p.FindModulesImplementing<ModuleDockingNode>())
                {
                    if (n.nodeType != type || !Targeting.IsFree(n))
                    {
                        continue;
                    }
                    if (_portArg.HasValue)
                    {
                        if (p.persistentId == _portArg.Value)
                        {
                            return n;
                        }
                        continue;
                    }
                    found = n;
                    count++;
                }
            }
            return count == 1 ? found : null;
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            if (Done)
            {
                return new ModeCommand(null, 0) { Translate = Vector3.zero };
            }
            if (FlightEvents.InTrial)
            {
                return new ModeCommand(_tgt != null && _tgt.nodeTransform != null ? AttitudeTarget.Vector(-_tgt.nodeTransform.forward) : (AttitudeTarget?)null, 0);
            }
            if (!_armed)
            {
                Part before = v.GetReferenceTransformPart();
                _controlBefore = before != null ? before.persistentId : 0;
                _own.MakeReferenceTransform();   // "Control from Here"
                _armed = true;
                _watchRestored = false;
                Log("armed: own=" + _own.part.persistentId + " target=" + _tgt.part.persistentId + " control_before=" + _controlBefore);
            }
            if (!_watchRestored)
            {
                Coordinator.WatchDock(_own.part.persistentId, _coupledSaved);
                _watchRestored = true;
            }
            if (Coordinator.DockCoupled && Coordinator.TryAdoptDocked(v))   // before touching the target port (it may be gone)
            {
                return new ModeCommand(null, 0) { Translate = Vector3.zero };
            }
            if (Coordinator.DockCoupled)
            {
                return new ModeCommand(null, 0) { Translate = Vector3.zero };   // coupled, the merge is still under way: wait
            }
            if (_tgt == null || _tgt.part == null || _tgt.vessel == null || _tgt.nodeTransform == null)
            {
                throw new ModeAbortException("no_target", "the target port is gone");
            }
            double aRcs = ApproachMode.RcsAccel(v, _stepRcs, out string why);
            if (aRcs <= 0)
            {
                Report("rcs_unavailable", new Dictionary<string, object> { ["reason"] = why });
                throw new ModeAbortException("dock_failed", "no RCS translation available (" + why + ")");
            }

            Vector3d axis = _tgt.nodeTransform.forward;
            Vector3d off = (Vector3d)_own.nodeTransform.position - _tgt.nodeTransform.position;
            double along = Vector3d.Dot(off, axis);
            Vector3d lateral = off - axis * along;
            double lat = lateral.magnitude;
            Vector3d relV = v.obt_velocity - _tgt.vessel.obt_velocity;
            double ut = Planetarium.GetUniversalTime();
            Vector3d want;

            if (!_routed && along <= 0)
            {
                // Behind the port plane: around the target to its front (never straight through it).
                Vector3d side = Waypoints.Side(_tgt.vessel, axis, _own.nodeTransform.position, ClearanceM, out bool needed);
                Vector3d hold = (Vector3d)_tgt.nodeTransform.position + axis * HoldOffM;
                Vector3d goal = needed && Vector3d.Distance(_own.nodeTransform.position, side) > 2 ? side : hold;
                want = Toward(goal - (Vector3d)_own.nodeTransform.position);
            }
            else
            {
                _routed = true;
                if (_retreat)
                {
                    want = axis * 0.5 - lateral * LateralGain;   // back off to the hold-off point
                    if (along >= HoldOffM)
                    {
                        _retreat = false;
                        _contactSeen = false;
                        Log("retreat done: along=" + F(along, 2));
                    }
                }
                else if (along < HoldOffM / 2 && lat > LateralStopM)
                {
                    want = axis * 0.5 - lateral * LateralGain;   // BackOut: in front of the port, so this never crosses the target
                }
                else if (lat > LateralGoM)
                {
                    want = -lateral * LateralGain;               // Align: hold along, remove the lateral error
                }
                else
                {
                    double err = t.Num["attitude_error"];
                    bool aligned = !double.IsNaN(err) && err <= AdvanceGateDeg;
                    double speed = Math.Min(MaxSpeed, Math.Max(ContactSpeed, 0.1 * (along - 1)));
                    bool forced = DebugState.DockRejectLeft > 0;
                    if (forced && along <= ForcedStopM + 0.1)
                    {
                        speed = 0;
                        if (relV.magnitude <= 0.05)
                        {
                            if (double.IsNaN(_restSince))
                            {
                                _restSince = ut;
                            }
                            else if (ut - _restSince >= ForcedRestS)
                            {
                                DebugState.DockRejectLeft--;
                                _restSince = double.NaN;
                                Fail(true, relV.magnitude, lat);
                            }
                        }
                        else
                        {
                            _restSince = double.NaN;
                        }
                    }
                    want = axis * (aligned ? -speed : 0) - lateral * LateralGain;
                }
                // Contact and failed contacts.
                if (along > 0 && along <= ContactM && !_contactSeen)
                {
                    _contactSeen = true;
                    _attempts++;
                    AddAttempt(false, relV.magnitude, lat);
                }
                else if (_contactSeen && along > 1 && !_retreat)
                {
                    Fail(false, relV.magnitude, lat);
                }
            }
            return new ModeCommand(AttitudeTarget.Vector(-axis), 0) { Translate = ApproachMode.ToolAxes(v, (want - relV) / aRcs) };
        }

        private static Vector3d Toward(Vector3d d) => d.magnitude < 1e-3 ? Vector3d.zero : d.normalized * Math.Min(MaxSpeed, 0.2 * d.magnitude);

        /// <summary>A failed attempt (contact without coupling, or forced): back off, or dock_failed past retries.</summary>
        private void Fail(bool forced, double speed, double lat)
        {
            if (forced)
            {
                _attempts++;
                AddAttempt(true, speed, lat);
            }
            Log("attempt " + _attempts + " failed (forced " + forced + ")");
            if (_attempts > _retries)
            {
                throw new ModeAbortException("dock_failed", _attempts + " attempts without docking (retries " + _retries + ")");
            }
            _retreat = true;
            _contactSeen = false;
        }

        private void AddAttempt(bool forced, double speed, double lat)
        {
            FlightEvents.Add("dock_attempt", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["attempt"] = _attempts,
                ["forced"] = forced,
                ["contact_speed"] = OrbitMath.Num(speed),
                ["lateral_error"] = OrbitMath.Num(lat),
            });
        }

        /// <summary>Coordinator.TryAdoptDocked: the merged vessel is the plan's vessel now; the step completes.</summary>
        public void MarkDocked(Vessel merged)
        {
            Done = true;
            Log("docked: vessel=" + merged.persistentId + " parts=" + merged.parts.Count + " own_state=" + (_own != null ? _own.state : "?"));
            FlightEvents.Add("docked", Coordinator.StepIndex, new Dictionary<string, object>
            {
                ["vessel_id"] = (double)merged.persistentId,
                ["name"] = merged.vesselName,
                ["parts"] = merged.parts.Count,
                ["attempts"] = _attempts,
            });
        }

        /// <summary>Resume step 2: the coupling watch (static state) after a reload.</summary>
        public void RestoreWatch()
        {
            if (_armed && _own != null && _own.part != null)
            {
                Coordinator.WatchDock(_own.part.persistentId, _coupledSaved);
                _watchRestored = true;
            }
        }

        private void Report(string type, Dictionary<string, object> data)
        {
            if (_reported.Contains(type))
            {
                return;
            }
            _reported.Add(type);
            FlightEvents.Add(type, Coordinator.StepIndex, data);
        }

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] dock " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        public Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["own_port"] = _own != null && _own.part != null ? (object)(double)_own.part.persistentId : null,
            ["target_port"] = _tgt != null && _tgt.part != null ? (object)(double)_tgt.part.persistentId : null,
            ["armed"] = _armed,
            ["coupled"] = Coordinator.DockCoupled,
            ["control_before"] = (double)_controlBefore,
            ["attempts"] = (double)_attempts,
            ["contact_seen"] = _contactSeen,
            ["retreat"] = _retreat,
            ["routed"] = _routed,
            ["reported"] = new List<object>(_reported),
        };

        /// <summary>Instance fields only (Resume step 1 keeps static state untouched); RestoreWatch restores the watch later.</summary>
        public void Load(Vessel v, Dictionary<string, object> state)
        {
            uint own = (uint)Handover.D(state, "own_port", 0);
            uint tgt = (uint)Handover.D(state, "target_port", 0);
            _own = FindPort(v, own) ?? throw new InvalidOperationException("own docking port " + own + " not found");
            _tgt = v.targetObject as ModuleDockingNode;
            if (_tgt == null || _tgt.part == null || _tgt.part.persistentId != tgt)
            {
                throw new InvalidOperationException("target docking port " + tgt + " is not the target any more");
            }
            _armed = B(state, "armed");
            _coupledSaved = B(state, "coupled");
            _controlBefore = (uint)Handover.D(state, "control_before", 0);
            _attempts = (int)Handover.D(state, "attempts", 0);
            _contactSeen = B(state, "contact_seen");
            _retreat = B(state, "retreat");
            _routed = B(state, "routed");
            _watchRestored = false;
            _reported.Clear();
            if (state.TryGetValue("reported", out object r) && r is List<object> list)
            {
                foreach (object x in list)
                {
                    if (x is string s)
                    {
                        _reported.Add(s);
                    }
                }
            }
        }

        private static bool B(Dictionary<string, object> state, string key) => state.TryGetValue(key, out object b) && b is bool x && x;

        private static ModuleDockingNode FindPort(Vessel v, uint partId)
        {
            foreach (Part p in v.parts)
            {
                if (p.persistentId == partId)
                {
                    return p.FindModuleImplementing<ModuleDockingNode>();
                }
            }
            return null;
        }

        /// <summary>Stops the watch and puts the control point back (when that part is still on v). Safe to call twice.</summary>
        public void End(Vessel v)
        {
            Coordinator.WatchDock(0, false);
            if (_armed && v != null && _controlBefore != 0)
            {
                foreach (Part p in v.parts)
                {
                    if (p.persistentId == _controlBefore)
                    {
                        v.SetReferenceTransform(p);
                        Log("control part restored: " + _controlBefore);
                        break;
                    }
                }
            }
            _armed = false;
        }
    }
}
