using KspBridge.Flight.Control;
using KspBridge.Flight.Maneuver;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// approach (design 0032 decisions 21~24): closes on the target vessel (a docking-port target: its vessel, goal = a point
    /// distance in front of the port, reached around the target). Phases wait (warp to the closest approach, computed once at
    /// Begin, minus the kill burn, spool-up and KillLeadS) → kill (cancel the relative velocity, main engine; RCS when there is no
    /// engine) → close (target relative speed min(max_speed, Margin·√(2·a·d)); error above EngineErr: main engine, else RCS
    /// translation). Done when at the goal and the relative speed ≤ DoneSpeed for HoldS. Reasons no_target, target_too_far.
    /// Events approach_phase, approach_done, rcs_unavailable (once per step).
    /// </summary>
    public sealed class ApproachMode : IFlightMode
    {
        public static readonly string[] Args = { "distance", "max_speed" };

        private const double HoldS = 3;
        private const double DoneSpeed = 0.2;
        private const double PortPointTolM = 2;
        private const double EngineErr = 2;
        private const double MaxClosestM = 10000;
        private const double Margin = 0.6;
        private const double KillLeadS = 10;
        private const double ClearanceM = 5;

        private enum Phase { Wait, Kill, Close }

        private readonly double _distance;
        private readonly double _maxSpeed;
        private readonly bool? _stepRcs;

        private Phase _phase = Phase.Wait;
        private uint _targetId;
        private double _closestUt = double.NaN;
        private double _closestDist = double.NaN;
        private double _holdStart = double.NaN;
        private bool _waypointDone;
        private readonly List<string> _reported = new List<string>();

        private ApproachMode(double distance, double maxSpeed, bool? stepRcs)
        {
            _distance = distance;
            _maxSpeed = maxSpeed;
            _stepRcs = stepRcs;
        }

        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double distance = Plan.OptionalNumber(o, "distance", path, 1, 1000, 50);
            double maxSpeed = Plan.OptionalNumber(o, "max_speed", path, 0.5, 100, 10);
            bool? stepRcs = o.TryGetValue("rcs", out object r) ? r as bool? : null;   // applied after the trial (design 0032 decision 23)
            return () => new ApproachMode(distance, maxSpeed, stepRcs);
        }

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
            Vessel tv = TargetVessel(v) ?? throw new ModeAbortException("no_target", "no target vessel");
            if (tv.mainBody != v.mainBody)
            {
                throw new ModeAbortException("no_target", "the target orbits " + tv.mainBody.bodyName);
            }
            _targetId = tv.persistentId;
            double now = Planetarium.GetUniversalTime();
            OrbitMath.ClosestApproach(v.orbit, tv.orbit, now, OrbitMath.SafeSpan(v.orbit, now), out _closestUt, out _closestDist);
            double here = Vector3d.Distance(v.CoMD, tv.CoMD);
            if (here < _closestDist || !OrbitMath.Finite(_closestDist))
            {
                // Already closer than any future pass (inside the scan step), or no pass found but close: start from now.
                if (here <= MaxClosestM)
                {
                    _closestUt = now;
                    _closestDist = here;
                }
            }
            if (!OrbitMath.Finite(_closestDist) || _closestDist > MaxClosestM)
            {
                throw new ModeAbortException("target_too_far", "closest approach " + F(_closestDist, 0) + " m > " + F(MaxClosestM, 0) + " m: rendezvous first");
            }
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            if (Done)
            {
                return new ModeCommand(null, 0) { Translate = Vector3.zero };
            }
            Vessel tv = TargetVessel(v);
            if (tv == null || tv.persistentId != _targetId)
            {
                throw new ModeAbortException("no_target", "the target was cleared or changed");
            }
            double ut = Planetarium.GetUniversalTime();
            ModuleDockingNode port = v.targetObject as ModuleDockingNode;
            Vector3d goal = GoalPoint(v, tv, port);
            Vector3d rel = goal - v.CoMD;
            Vector3d relV = v.obt_velocity - tv.obt_velocity;
            double d = rel.magnitude;
            double aEngine = EngineModel.MaxAccel(v);
            double aRcs = RcsAccel(v, _stepRcs, out string rcsWhy);
            if (aRcs <= 0)
            {
                Report("rcs_unavailable", new Dictionary<string, object> { ["reason"] = rcsWhy });
            }

            if (_phase == Phase.Wait && !FlightEvents.InTrial && !_reported.Contains("phase_wait"))
            {
                _reported.Add("phase_wait");   // the starting phase is reported once too (wait → kill → close, design 0032 decision 31)
                Enter(Phase.Wait, d, relV.magnitude);
            }
            if (_phase == Phase.Wait)
            {
                double kill = EngineModel.BurnTimeStaged(v, relV.magnitude, out _);
                double wake = _closestUt - (OrbitMath.Finite(kill) ? kill : 0) - EngineModel.SpoolUpS(v) - KillLeadS;
                if (ut < wake && Vector3d.Dot(rel, relV) > 0)
                {
                    return new ModeCommand(AttitudeTarget.Vector(-relV.normalized), 0) { WarpToUt = wake };
                }
                Enter(Phase.Kill, d, relV.magnitude);
            }
            if (_phase == Phase.Kill)
            {
                if (relV.magnitude <= EngineErr)
                {
                    Enter(Phase.Close, d, relV.magnitude);
                }
                else if (aEngine > 0)
                {
                    return EngineBurn(-relV, aEngine, t);
                }
                else if (aRcs > 0)
                {
                    return new ModeCommand(AttitudeTarget.Vector(rel.normalized), 0) { Translate = ToolAxes(v, -relV / aRcs) };
                }
                else
                {
                    return new ModeCommand(AttitudeTarget.Vector(-relV.normalized), 1);   // no thrust at all: the Coordinator's wait / no_thrust
                }
            }

            // Close.
            double stop = port != null ? 0 : _distance;
            bool atGoal = port != null ? d <= PortPointTolM : d <= _distance;
            if (atGoal && relV.magnitude <= DoneSpeed)
            {
                if (double.IsNaN(_holdStart))
                {
                    _holdStart = ut;
                }
                if (ut - _holdStart >= HoldS)
                {
                    Done = true;
                    if (!FlightEvents.InTrial)
                    {
                        FlightEvents.Add("approach_done", Coordinator.StepIndex, new Dictionary<string, object>
                        {
                            ["distance"] = R(d),
                            ["relative_speed"] = R(relV.magnitude),
                            ["goal"] = port != null ? "port" : "vessel",
                        });
                    }
                    Log("done: d=" + F(d, 2) + " rel_v=" + F(relV.magnitude, 3));
                    return new ModeCommand(null, 0) { Translate = Vector3.zero };
                }
            }
            else
            {
                _holdStart = double.NaN;
            }
            double a = aRcs > 0 ? aRcs : aEngine;
            double vWant = d <= stop ? 0 : Math.Min(_maxSpeed, Margin * Math.Sqrt(2 * Math.Max(a, 1e-3) * (d - stop)));
            Vector3d err = (d > 1e-3 ? rel / d : Vector3d.zero) * vWant - relV;
            if ((err.magnitude > EngineErr || aRcs <= 0) && aEngine > 0)
            {
                return EngineBurn(err, aEngine, t);
            }
            if (aRcs <= 0)
            {
                return new ModeCommand(AttitudeTarget.Vector(err.normalized), 1);
            }
            return new ModeCommand(AttitudeTarget.Vector(d > 1e-3 ? rel / d : tv.CoMD - v.CoMD), 0) { Translate = ToolAxes(v, err / aRcs) };
        }

        /// <summary>Main-engine burn along dv (world): attitude to dv, throttle for dv in about 1 s, only within the 5° gate.</summary>
        private static ModeCommand EngineBurn(Vector3d dv, double aEngine, Telemetry t)
        {
            double throttle = Clamp01(dv.magnitude / aEngine);
            double err = t.Num["attitude_error"];
            if (double.IsNaN(err) || err > CircularizeMode.AttitudeGateDeg)
            {
                throttle = 0;
            }
            return new ModeCommand(AttitudeTarget.Vector(dv.normalized), throttle) { Translate = Vector3.zero };
        }

        private void Enter(Phase p, double d, double relV)
        {
            _phase = p;
            Log("phase " + p + ": d=" + F(d, 1) + " rel_v=" + F(relV, 2));
            if (!FlightEvents.InTrial)
            {
                FlightEvents.Add("approach_phase", Coordinator.StepIndex, new Dictionary<string, object>
                {
                    ["phase"] = p == Phase.Wait ? "wait" : p == Phase.Kill ? "kill" : "close",
                    ["distance"] = R(d),
                    ["relative_speed"] = R(relV),
                });
            }
        }

        private void Report(string type, Dictionary<string, object> data)
        {
            if (FlightEvents.InTrial || _reported.Contains(type))
            {
                return;
            }
            _reported.Add(type);
            FlightEvents.Add(type, Coordinator.StepIndex, data);
        }

        /// <summary>Goal point (world): the target vessel's CoM, or for a port target the point distance in front of the port,
        /// via a side waypoint (target radius + ClearanceM, perpendicular to the port axis) while still behind the target.</summary>
        private Vector3d GoalPoint(Vessel v, Vessel tv, ModuleDockingNode port)
        {
            if (port == null || port.nodeTransform == null)
            {
                return tv.CoMD;
            }
            Vector3d axis = port.nodeTransform.forward;
            Vector3d front = (Vector3d)port.nodeTransform.position + axis * _distance;
            if (!_waypointDone)
            {
                Vector3d side = Waypoints.Side(tv, axis, v.CoMD, ClearanceM, out bool needed);
                if (needed && Vector3d.Distance(v.CoMD, side) > PortPointTolM * 2)
                {
                    return side;
                }
                _waypointDone = true;
            }
            return front;
        }

        /// <summary>The target vessel: a Vessel target, or the vessel of a docking-port target.</summary>
        internal static Vessel TargetVessel(Vessel v) =>
            v.targetObject is ModuleDockingNode n ? n.vessel : v.targetObject as Vessel;

        /// <summary>Usable RCS translation acceleration (m/s²): RCS on per the step's rcs (else the group now), monopropellant
        /// left, thrusters present; about half of the summed thruster power acts along one axis. 0 with the reason otherwise.</summary>
        internal static double RcsAccel(Vessel v, bool? stepRcs, out string reason)
        {
            reason = null;
            if (!(stepRcs ?? v.ActionGroups[KSPActionGroup.RCS]))
            {
                reason = "rcs_off";
                return 0;
            }
            double power = 0;
            foreach (Part p in v.parts)
            {
                foreach (ModuleRCS m in p.FindModulesImplementing<ModuleRCS>())
                {
                    if (m.isEnabled && m.rcsEnabled)
                    {
                        power += m.thrusterPower;
                    }
                }
            }
            if (power <= 0)
            {
                reason = "no_thrusters";
                return 0;
            }
            PartResourceDefinition mono = PartResourceLibrary.Instance.GetDefinition("MonoPropellant");
            v.GetConnectedResourceTotals(mono.id, out double amount, out _);
            if (amount <= 0.01)
            {
                reason = "no_propellant";
                return 0;
            }
            return v.totalMass > 0 ? power / 2 / v.totalMass : 0;
        }

        /// <summary>World vector → rcs_translate tool frame (x = right, y = −forward (dorsal), z = up (nose)) of the control part,
        /// each clamped to [-1, 1] (ManualControl's frame and signs).</summary>
        internal static Vector3 ToolAxes(Vessel v, Vector3d w)
        {
            Transform r = v.ReferenceTransform;
            return new Vector3(Clamp1(Vector3d.Dot(w, r.right)), Clamp1(-Vector3d.Dot(w, r.forward)), Clamp1(Vector3d.Dot(w, r.up)));
        }

        private static float Clamp1(double x) => (float)(x < -1 ? -1 : x > 1 ? 1 : x);

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] approach " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        private static object R(double x) => OrbitMath.Num(x);

        public Dictionary<string, object> Save() => new Dictionary<string, object>
        {
            ["phase"] = (double)(int)_phase,
            ["target_id"] = (double)_targetId,
            ["closest_ut"] = _closestUt,
            ["closest_dist"] = _closestDist,
            ["hold_start"] = _holdStart,
            ["waypoint_done"] = _waypointDone,
            ["reported"] = new List<object>(_reported),
        };

        /// <summary>Restores Save's keys; Begin is not called again, so the start checks are not repeated.</summary>
        public void Load(Vessel v, Dictionary<string, object> state)
        {
            _phase = (Phase)(int)Handover.D(state, "phase", 0);
            _targetId = (uint)Handover.D(state, "target_id", 0);
            _closestUt = Handover.D(state, "closest_ut", double.NaN);
            _closestDist = Handover.D(state, "closest_dist", double.NaN);
            _holdStart = Handover.D(state, "hold_start", double.NaN);
            _waypointDone = state.TryGetValue("waypoint_done", out object w) && w is bool wb && wb;
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

        public void End(Vessel v)
        {
        }
    }

    /// <summary>Routing around the target vessel to the front of a docking port (shared by approach and dock).</summary>
    internal static class Waypoints
    {
        /// <summary>Side waypoint: target CoM + n·(target radius + clearance), n = the direction from the target to self
        /// perpendicular to the port axis. needed = self is behind the port plane through the target's CoM.</summary>
        public static Vector3d Side(Vessel tv, Vector3d axis, Vector3d self, double clearance, out bool needed)
        {
            Vector3d c = tv.CoMD;
            Vector3d off = self - c;
            needed = Vector3d.Dot(off, axis) < 0;
            Vector3d n = Vector3d.Exclude(axis, off);
            if (n.magnitude < 1e-3)
            {
                n = Vector3d.Exclude(axis, Vector3d.up);   // straight behind: any perpendicular
            }
            return c + n.normalized * (Radius(tv) + clearance);
        }

        /// <summary>Bounding radius of the target vessel around its CoM (m).</summary>
        public static double Radius(Vessel tv)
        {
            double r = 1;
            if (!tv.loaded)
            {
                return 10;
            }
            foreach (Part p in tv.parts)
            {
                r = Math.Max(r, Vector3d.Distance(p.transform.position, tv.CoMD) + 1);
            }
            return r;
        }
    }
}
