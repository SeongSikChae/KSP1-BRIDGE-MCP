using KspBridge.Flight.Modes;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Flight
{
    /// <summary>
    /// reload_mod handover: {format, epoch, events, seq, plan?} built from Dictionary, List, numbers, strings and bools only
    /// (Json.Serialize writes unknown objects with ToString and NaN/Infinity as null). Lives only in LogicContext.Handover.
    /// </summary>
    public static class Handover
    {
        public const int Format = 1;

        /// <summary>Load rule for every handed-over double: null (NaN/∞ written by Json.Serialize) becomes the field default.</summary>
        public static double D(Dictionary<string, object> o, string key, double def = double.NaN) =>
            o != null && o.TryGetValue(key, out object x) && x != null ? Convert.ToDouble(x, CultureInfo.InvariantCulture) : def;

        public static bool B(Dictionary<string, object> o, string key, bool def = false) =>
            o != null && o.TryGetValue(key, out object x) && x is bool b ? b : def;

        public static Dictionary<string, object> Capture()
        {
            Dictionary<string, object> h = CaptureEvents();
            Dictionary<string, object> plan = Coordinator.CapturePlan();
            if (plan != null)
            {
                h["plan"] = plan;
            }
            return h;
        }

        /// <summary>Without the plan: events and seq only (fallback when the plan cannot be serialized).</summary>
        public static Dictionary<string, object> CaptureEvents() => new Dictionary<string, object>
        {
            ["format"] = (double)Format,
            ["epoch"] = FlightEvents.Epoch,
            ["events"] = FlightEvents.Save(),
            ["seq"] = (double)FlightEvents.LastSeq,
        };

        /// <summary>Never throws. A wrong format or unreadable plan gives Incompatible (with whatever plan facts could be read).</summary>
        public static HandoverState Parse(string json)
        {
            HandoverState s = new HandoverState();
            if (string.IsNullOrEmpty(json))
            {
                return s;
            }
            Dictionary<string, object> h;
            try
            {
                h = Json.Json.Parse(json) as Dictionary<string, object>;
            }
            catch (Exception e)
            {
                s.Error = "handover parse failed: " + e.Message;
                return s;   // no epoch: a fresh event session; no plan to take over
            }
            if (h == null)
            {
                return s;
            }
            bool formatOk;
            try
            {
                formatOk = D(h, "format") == Format;
                if (formatOk)
                {
                    s.Epoch = h.TryGetValue("epoch", out object ep) ? ep as string : null;
                    s.Events = h.TryGetValue("events", out object ev) ? ev as List<object> : null;
                    s.Seq = (long)D(h, "seq", 0);
                }
            }
            catch (Exception e)   // a non-number format or seq: treated as another format (events not restored)
            {
                formatOk = false;
                s.Epoch = null;
                s.Events = null;
                s.Seq = 0;
                s.Error = "handover header unreadable: " + e.Message;
            }
            if (!h.TryGetValue("plan", out object p) || !(p is Dictionary<string, object> plan))
            {
                return s;
            }
            HandedPlan hp = new HandedPlan { Source = plan };
            s.Plan = hp;
            try
            {
                // Facts the incompatible path needs are read first and one by one.
                hp.VesselId = (uint)D(plan, "vessel_id", 0);
                hp.SasBefore = plan.TryGetValue("sas_before", out object sb) && sb is bool sasB ? sasB : (bool?)null;
                hp.Staging = plan.TryGetValue("staging", out object st) ? st as Dictionary<string, object> : null;
                Dictionary<string, object> cmd = plan.TryGetValue("command", out object c) ? c as Dictionary<string, object> : null;
                hp.CommandThrottle = D(cmd, "throttle", 0);
                if (!formatOk)
                {
                    throw new InvalidOperationException(s.Error ?? "handover format " + D(h, "format") + " (expected " + Format + ")");
                }
                hp.PlanId = (string)plan["plan_id"];
                hp.Raw = (string)plan["raw"];
                hp.Step = (int)D(plan, "step");
                hp.StepStartUt = D(plan, "step_start_ut");
                hp.Timers = plan["timers"] as List<object> ?? throw new InvalidOperationException("timers missing");
                hp.ModeState = plan["mode_state"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                hp.ControllerIntegral = plan.TryGetValue("controller_integral", out object ci) ? ci as List<object> : null;
                hp.WarpCancelled = B(plan, "warp_cancelled");
                ModeCommand command = new ModeCommand(null, hp.CommandThrottle) { ModeThrottle = D(cmd, "mode_throttle", 0) };
                if (cmd != null && cmd.TryGetValue("attitude", out object a) && a is List<object> dir && dir.Count == 3)
                {
                    command.Attitude = AttitudeTarget.Vector(new Vector3d(
                        Convert.ToDouble(dir[0], CultureInfo.InvariantCulture),
                        Convert.ToDouble(dir[1], CultureInfo.InvariantCulture),
                        Convert.ToDouble(dir[2], CultureInfo.InvariantCulture)));
                }
                hp.Command = command;
            }
            catch (Exception e)
            {
                s.Incompatible = true;
                s.Error = e.Message;
            }
            return s;
        }
    }

    public sealed class HandoverState
    {
        public string Epoch;
        public List<object> Events;
        public long Seq;
        public HandedPlan Plan;
        public bool Incompatible;
        public string Error;
    }

    public sealed class HandedPlan
    {
        public Dictionary<string, object> Source;
        public uint VesselId;
        public bool? SasBefore;
        public Dictionary<string, object> Staging;
        public double CommandThrottle;
        public string PlanId;
        public string Raw;
        public int Step;
        public double StepStartUt;
        public List<object> Timers;
        public Dictionary<string, object> ModeState;
        public List<object> ControllerIntegral;
        public bool WarpCancelled;
        public ModeCommand Command = ModeCommand.Idle;
    }
}
