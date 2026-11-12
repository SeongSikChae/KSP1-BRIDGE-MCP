using KspBridge.Flight.Control;
using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// Runs the flight plan every physics tick: telemetry, abort conditions, staging, the step's mode, warp, step change.
    /// Abort, Complete, Release and Stop never throw. Main thread only.
    /// </summary>
    public static class Coordinator
    {
        private static Plan _plan;
        private static int _step;
        private static IFlightMode _mode;
        private static double _stepStartUt;
        private static readonly Telemetry _t = new Telemetry();
        private static readonly Telemetry _view = new Telemetry();   // get_flight: never disturbs the tick's fields

        /// <summary>What the Controller reads. A struct, so it is never null even before the first Submit.</summary>
        public static ModeCommand Command = ModeCommand.Idle;

        public static bool Running => _plan != null && _plan.State == PlanState.Running;

        public static uint TargetVesselId => _plan?.VesselId ?? 0;

        /// <summary>Current step index for events (0 without a plan).</summary>
        public static int StepIndex => _step;

        public static void Tick()
        {
            if (!Running)
            {
                return;
            }
            try
            {
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    Abort("scene", null, null);   // the scene hook was missed
                    return;
                }
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || v.persistentId != _plan.VesselId)
                {
                    Abort("vessel_changed", null, null);   // never carried on silently
                    return;
                }
                if (v.state == Vessel.State.DEAD)
                {
                    Abort("vessel_lost", null, null);   // the destroy hook was missed (an active vessel is only marked dead)
                    return;
                }
                double ut = Planetarium.GetUniversalTime();
                Capture(v, ut);                                                              // (a)

                Condition hit = FirstTrue(_plan.Abort, _plan.Steps[_step].Abort, ut);      // (b) both evaluated
                if (hit != null)
                {
                    Abort("condition", hit, null);
                    return;
                }
                if (_plan.Steps[_step].TimeoutS is double to && ut - _stepStartUt > to)
                {
                    Abort("timeout", null, null);
                    return;
                }

                Command = ComputeCommand(v, ut, _plan, _mode, true);                        // (c)(d)(e)

                if (Command.WarpToUt is double w)                                          // (f)
                {
                    Warp.Request(v, w);
                }
                else if (Warp.Requested)
                {
                    Warp.Stop();
                }
                Warp.Sync();

                Plan.Step s = _plan.Steps[_step];                                          // (g)
                if (s.Until != null ? s.Until.Eval(_t, ut) : _mode.Done)
                {
                    NextStep(v, ut);
                }
            }
            catch (NoThrustException e)
            {
                Abort("no_thrust", null, e.Message);
            }
            catch (Exception e)
            {
                Abort("error", null, e.Message);
            }
        }

        /// <summary>Evaluates every condition (timers update) and returns the first that is true.</summary>
        private static Condition FirstTrue(Condition planAbort, Condition stepAbort, double ut)
        {
            bool a = planAbort != null && planAbort.Eval(_t, ut);
            bool b = stepAbort != null && stepAbort.Eval(_t, ut);
            return a ? planAbort : b ? stepAbort : null;
        }

        private static void NextStep(Vessel v, double ut)
        {
            Warp.Stop();
            Warp.ClearLatch();
            FlightEvents.Add("step_completed", _step, StepData(_plan, _step));   // the last step too
            if (_step + 1 >= _plan.Steps.Count)
            {
                Complete();
                return;
            }
            _mode = BeginStep(v, _plan, _step + 1);
            _step++;
            _stepStartUt = ut;
            FlightEvents.Add("step_started", _step, StepData(_plan, _step));
            Capture(v, ut);
            Command = ComputeCommand(v, ut, _plan, _mode, true);   // the new step's first command in the same tick
        }

        /// <summary>New mode instance + Begin + on_start registration. No events: Submit's trial calls it too.</summary>
        private static IFlightMode BeginStep(Vessel v, Plan plan, int index)
        {
            Plan.Step step = plan.Steps[index];
            IFlightMode mode = step.CreateMode();
            mode.Begin(v, _t);
            if (step.StageOnStart)
            {
                AutoStager.RequestOnStart();
            }
            return mode;
        }

        private static void Capture(Vessel v, double ut)
        {
            _t.Capture(v, Command.Attitude, ut);
            _t.PrevCommandThrottle = Command.Throttle;
            _t.PrevModeThrottle = Command.ModeThrottle;
            _t.Num["step_time"] = ut - _stepStartUt;
        }

        /// <summary>Single command path for Tick, NextStep and Submit: stager first, then the mode, the shared no-thrust rule,
        /// then the stager's throttle override.</summary>
        private static ModeCommand ComputeCommand(Vessel v, double ut, Plan plan, IFlightMode mode, bool send)
        {
            AutoStager.Update(v, _t, plan, ut, send);                // (c)
            _t.Str["staging"] = AutoStager.StateName;
            _t.StagingPending = AutoStager.Pending;
            ModeCommand cmd = mode.Update(v, _t);                    // (d)
            cmd.ModeThrottle = cmd.Throttle;
            if (cmd.Throttle > 0 && !EngineModel.AnyOperational(v))
            {
                if (!_t.StagingPending)
                {
                    throw new NoThrustException();
                }
                cmd.Throttle = 0;                                    // keep attitude until staging brings an engine
            }
            return AutoStager.Override(cmd);                         // (e)
        }

        public static object Submit(Vessel v, Plan plan)
        {
            // 1. Before changing anything: the first command. On failure nothing changes (400/409).
            double ut = Planetarium.GetUniversalTime();
            Dictionary<string, object> stagerBefore = AutoStager.Save();
            ModeCommand first;
            IFlightMode mode;
            List<FlightEvent> trial;
            FlightEvents.BeginTrial();   // events of the trial calculation go aside
            try
            {
                mode = BeginStep(v, plan, 0);
                Capture(v, ut);
                _t.Num["step_time"] = 0;
                first = ComputeCommand(v, ut, plan, mode, false);   // requests are registered only; the first Tick sends them
                trial = FlightEvents.EndTrial();
            }
            catch (NoThrustException)
            {
                FlightEvents.EndTrial();
                AutoStager.Load(stagerBefore);
                throw new ApiException(409, "no_thrust", "no operational engine and no staging pending (use on_start: [\"stage\"] to ignite)");
            }
            catch
            {
                FlightEvents.EndTrial();   // dropped: the next Tick from the restored state makes the same events once
                AutoStager.Load(stagerBefore);
                throw;
            }

            // 2. After: failures abort, and Abort releases even before the Running switch.
            try
            {
                if (Warp.Requested)
                {
                    Warp.Stop();   // only a warp we requested; a user's warp stays
                }
                Warp.ClearLatch();
                if (Running)
                {
                    FlightEvents.Add("plan_replaced", _step, new Dictionary<string, object> { ["plan_id"] = _plan.Id, ["steps"] = _plan.Steps.Count });
                }
                _plan = plan;
                _mode = mode;
                _step = 0;
                _stepStartUt = ut;
                plan.State = PlanState.Running;
                AttitudeController.Attach(v);   // no re-attach on replacement
                FlightEvents.Add("plan_started", 0, new Dictionary<string, object> { ["plan_id"] = plan.Id, ["steps"] = plan.Steps.Count });
                FlightEvents.Add("step_started", 0, StepData(plan, 0));
                Command = first;                // no throttle-0 tick right after a replacement
                FlightEvents.Commit(ref trial);
            }
            catch (NoThrustException e)
            {
                FlightEvents.Commit(ref trial);
                Abort("no_thrust", null, e.Message);
            }
            catch (Exception e)
            {
                FlightEvents.Commit(ref trial);
                Abort("error", null, e.Message);
            }
            return PlanResult();
        }

        /// <summary>
        /// replace_flight_steps: replaces the steps after the current one, or from the current one (includeCurrent: the new first
        /// step starts now, after a Submit-style trial of its first command). The Controller is not re-attached.
        /// </summary>
        public static object ReplaceSteps(Vessel v, List<Plan.Step> steps, bool includeCurrent)
        {
            if (!Running)
            {
                throw new ApiException(409, "no_plan", "no running flight plan");
            }
            if (v.persistentId != _plan.VesselId)
            {
                throw new ApiException(409, "vessel_mismatch", "the plan belongs to another vessel");
            }
            if (steps.Count == 0 && includeCurrent)
            {
                throw Plan.Invalid("steps", "must not be empty when include_current is true");
            }
            int keep = includeCurrent ? _step : _step + 1;   // empty steps + include_current:false = drop the remaining steps
            List<FlightEvent> trial = null;
            if (includeCurrent)
            {
                // Before changing anything: the new step's first command. On failure the plan and the stager stay as they were.
                double ut = Planetarium.GetUniversalTime();
                Plan trialPlan = _plan.WithSteps(keep, steps);
                Dictionary<string, object> stagerBefore = AutoStager.Save();
                IFlightMode mode;
                ModeCommand first;
                FlightEvents.BeginTrial();
                try
                {
                    mode = BeginStep(v, trialPlan, _step);
                    Capture(v, ut);
                    _t.Num["step_time"] = 0;
                    first = ComputeCommand(v, ut, trialPlan, mode, false);
                    trial = FlightEvents.EndTrial();
                }
                catch (NoThrustException)
                {
                    FlightEvents.EndTrial();
                    AutoStager.Load(stagerBefore);
                    throw new ApiException(409, "no_thrust", "no operational engine and no staging pending (use on_start: [\"stage\"] to ignite)");
                }
                catch
                {
                    FlightEvents.EndTrial();
                    AutoStager.Load(stagerBefore);
                    throw;
                }
                try
                {
                    _plan.ReplaceSteps(keep, steps);
                    _mode = mode;
                    _stepStartUt = ut;
                    Warp.Stop();        // like NextStep
                    Warp.ClearLatch();
                    Command = first;
                }
                catch (Exception e)
                {
                    FlightEvents.Commit(ref trial);
                    Abort("error", null, e.Message);
                    throw new ApiException(409, "plan_aborted", "steps replacement failed: " + e.Message);   // no steps_replaced
                }
            }
            else
            {
                _plan.ReplaceSteps(keep, steps);
            }
            FlightEvents.Add("steps_replaced", _step, new Dictionary<string, object> { ["plan_id"] = _plan.Id, ["steps"] = _plan.Steps.Count });
            if (includeCurrent && Running)
            {
                FlightEvents.Add("step_started", _step, StepData(_plan, _step));
            }
            FlightEvents.Commit(ref trial);
            return PlanResult();
        }

        public static object UserAbort()
        {
            if (!Running)
            {
                throw new ApiException(409, "no_plan", "no running flight plan");
            }
            string id = _plan.Id;
            Abort("user", null, null);
            return new Dictionary<string, object> { ["plan_id"] = id, ["state"] = "aborted", ["event_seq"] = FlightEvents.LastSeq };
        }

        /// <summary>Never throws. Releases control whatever the plan state is (a half-done Submit included);
        /// only the state change and the event need a running plan.</summary>
        public static void Abort(string reason, Condition hit, string message)
        {
            bool running = Running;
            if (running)
            {
                _plan.State = PlanState.Aborted;
            }
            Release();
            if (running)
            {
                Safe(() => FlightEvents.Add("plan_aborted", _step, new Dictionary<string, object>
                {
                    ["reason"] = reason,
                    ["condition"] = hit?.Describe(),
                    ["message"] = message,
                }));
            }
        }

        private static void Complete()
        {
            _plan.State = PlanState.Completed;
            Release();
            Safe(() => FlightEvents.Add("plan_completed", _step, null));
        }

        private static bool _logicStopping;   // Stop(LogicContext) in progress

        /// <summary>Common release for Abort, Complete and a failed Submit. Never throws, no state check.
        /// Order: throttle 0 → warp off → SAS restore → OnFlyByWire off.</summary>
        private static void Release()
        {
            Command = ModeCommand.Idle;
            Safe(() => AttitudeController.ZeroThrottle());
            Safe(() =>
            {
                if (!_logicStopping || Warp.Requested)   // reload: only a warp we requested; a user's warp stays
                {
                    Warp.Stop();
                }
            });
            Safe(() => AttitudeController.RestoreSas());   // before Detach clears the vessel
            Safe(() => AttitudeController.Detach());
            Safe(() => AutoStager.Reset(PlanVessel()));
        }

        /// <summary>The active vessel when it is the plan's vessel, else null.</summary>
        private static Vessel PlanVessel()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            return _plan != null && v != null && v.persistentId == _plan.VesselId ? v : null;
        }

        /// <summary>Logic stop (reload_mod, KSP exit). Unit 11 hands the plan over; until then the plan is aborted. Never throws.</summary>
        public static IList<string> Stop(LogicContext context)
        {
            List<string> released = new List<string>();
            if (Running)
            {
                released.Add("flight_plan " + _plan.Id);
            }
            if (Running || AttitudeController.Target != null)
            {
                _logicStopping = true;
                try
                {
                    Abort("error", null, "logic stopped");   // without a plan the user's manual throttle and SAS are left alone
                }
                finally
                {
                    _logicStopping = false;
                }
            }
            Safe(() => AutoStager.Clear(null));
            _plan = null;
            _mode = null;
            _step = 0;
            Command = ModeCommand.Idle;
            return released;
        }

        private static Dictionary<string, object> StepData(Plan plan, int index) => new Dictionary<string, object>
        {
            ["name"] = plan.Steps[index].Name,
            ["mode"] = plan.Steps[index].Mode,
        };

        private static Dictionary<string, object> PlanResult() => new Dictionary<string, object>
        {
            ["plan_id"] = _plan.Id,
            ["state"] = StateName(_plan.State),
            ["steps"] = _plan.Steps.Count,
            ["event_seq"] = FlightEvents.LastSeq,
            ["epoch"] = FlightEvents.Epoch,
        };

        private static string StateName(PlanState s) => s == PlanState.Running ? "running" : s == PlanState.Completed ? "completed" : "aborted";

        /// <summary>get_flight: field names match the condition variables; NaN (not known) is null.</summary>
        public static object Describe(Vessel v)
        {
            _view.Capture(v, Running ? Command.Attitude : null, Planetarium.GetUniversalTime());
            _view.Str["staging"] = AutoStager.StateName;
            Dictionary<string, object> plan;
            if (_plan == null)
            {
                plan = new Dictionary<string, object> { ["state"] = "idle" };
            }
            else
            {
                Plan.Step s = _plan.Steps[_step];
                plan = new Dictionary<string, object>
                {
                    ["id"] = _plan.Id,
                    ["state"] = StateName(_plan.State),
                    ["step"] = _step,
                    ["name"] = s.Name,
                    ["mode"] = s.Mode,
                    ["step_time"] = Running ? N(Planetarium.GetUniversalTime() - _stepStartUt) : null,
                    ["attitude_error"] = N(_view.Num["attitude_error"]),
                    ["warp"] = Warp.Active,
                    ["event_seq"] = FlightEvents.LastSeq,
                    ["epoch"] = FlightEvents.Epoch,
                };
            }
            return new Dictionary<string, object>
            {
                ["vessel"] = new Dictionary<string, object>
                {
                    ["id"] = v.persistentId,
                    ["name"] = v.vesselName,
                    ["situation"] = _view.Str["situation"],
                    ["met"] = N(_view.Num["met"]),
                },
                ["flight"] = Group("altitude", "altitude_radar", "vertical_speed", "surface_speed", "orbital_speed",
                    "pitch", "heading", "roll", "dynamic_pressure"),
                ["orbit"] = With(Group("apoapsis", "periapsis", "time_to_apoapsis", "time_to_periapsis", "eccentricity", "inclination"),
                    "body", v.mainBody.bodyName),
                ["propulsion"] = With(Group("stage", "stage_dv", "total_dv", "twr", "throttle", "thrust", "ignitable_engines"),
                    "staging", _view.Str["staging"]),
                ["plan"] = plan,
                ["time_warp"] = TimeWarpControl.Describe(),
            };
        }

        private static Dictionary<string, object> Group(params string[] names)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (string n in names)
            {
                d[n] = N(_view.Num[n]);
            }
            return d;
        }

        private static Dictionary<string, object> With(Dictionary<string, object> d, string key, object value)
        {
            d[key] = value;
            return d;
        }

        /// <summary>Rounded for the response; null when not known (NaN) or not finite.</summary>
        private static object N(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)Math.Round(x, 3);

        private static void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] flight release step failed: " + e);
            }
        }
    }
}
