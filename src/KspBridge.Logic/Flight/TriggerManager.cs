using System;
using System.Collections.Generic;
using System.Linq;
using KspBridge.Handlers;

namespace KspBridge.Flight
{
    /// <summary>
    /// Condition → actions registered from the API, evaluated every physics tick on the main thread
    /// so the actions run in the same tick the condition becomes true.
    /// </summary>
    public static class TriggerManager
    {
        private static readonly HashSet<string> ActionTypes = new HashSet<string>
        {
            "throttle", "stage", "attitude", "release_attitude", "sas", "action_group",
        };

        private sealed class Trigger
        {
            public int Id;
            public string Name;
            public Dictionary<string, object> When;
            public string Field;
            public Func<object, bool> Test;
            /// <summary>Public triggers: single or combined condition. Internal triggers use Reader + Test.</summary>
            public Condition Condition;
            public List<Dictionary<string, object>> Actions;
            public bool Repeat;
            public Guid VesselId;
            public string State = "armed";
            public bool LastTrue;
            public int FiredCount;
            public double? FiredMet;
            public object FiredValue;
            public string Error;
            public double? WarpStoppedFrom;
            public object PendingValue;
            /// <summary>Internal condition (e.g. part_test_ready); null = FlightFields.Read(Field).</summary>
            public Func<Vessel, object> Reader;
            /// <summary>Internal action (e.g. part_test); returns false to stay armed. null = the public action list.</summary>
            public Func<Vessel, bool> Custom;
            public uint? PartTestId;
        }

        /// <summary>
        /// Registers a one-shot trigger with an internal condition and action (used by arm_part_test).
        /// Same lifecycle as public triggers: listed, removable, cleared on vessel/scene change, warp rules.
        /// </summary>
        public static object AddInternal(string name, Dictionary<string, object> when, Func<Vessel, object> reader,
            Dictionary<string, object> actionDescription, Func<Vessel, bool> action)
        {
            Vessel v = Guard.ActiveVessel();
            Trigger tr = new Trigger
            {
                Id = _nextId++,
                Name = name,
                When = when,
                Field = (string)when["field"],
                Test = o => o is bool b && b,
                Actions = new List<Dictionary<string, object>> { actionDescription },
                VesselId = v.id,
                Reader = reader,
                Custom = action,
                PartTestId = when.TryGetValue("part_id", out object pid) ? (uint?)(uint)pid : null,
            };
            _triggers.Add(tr);
            return Describe(tr);
        }

        public static bool IsArmedPartTest(uint partId)
            => _triggers.Any(t => t.PartTestId == partId && (t.State == "armed" || t.State == "pending"));

        private static readonly List<Trigger> _triggers = new List<Trigger>();
        private static int _nextId = 1;

        public static object Add(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            Dictionary<string, object> when = body.GetObject("when");
            RequestBody w = new RequestBody(when);
            Condition condition = Condition.Parse(when);

            List<Dictionary<string, object>> actions = new List<Dictionary<string, object>>();
            foreach (object o in body.GetList("actions"))
            {
                Dictionary<string, object> a = o as Dictionary<string, object> ?? throw ApiException.BadRequest("each action must be an object");
                string type = a.TryGetValue("type", out object t) ? t as string : null;
                if (type == null || !ActionTypes.Contains(type))
                {
                    throw ApiException.BadRequest("action type must be one of: " + string.Join(", ", ActionTypes));
                }
                Validate(type, new RequestBody(a));
                actions.Add(a);
            }
            if (actions.Count == 0)
            {
                throw ApiException.BadRequest("actions must not be empty");
            }

            Trigger tr = new Trigger
            {
                Id = _nextId++,
                Name = body.Has("name") ? body.GetString("name") : null,
                When = when,
                Field = condition.SingleField,
                Condition = condition,
                Actions = actions,
                Repeat = body.Has("repeat") && body.GetBool("repeat"),
                VesselId = v.id,
            };
            // Already true at registration: a one-shot trigger fires on the next tick; a repeating one waits for false → true.
            tr.LastTrue = tr.Repeat && condition.Evaluate(v, new Dictionary<string, object>());
            _triggers.Add(tr);
            return Describe(tr);
        }

        public static object List()
        {
            return new Dictionary<string, object> { ["triggers"] = _triggers.Select(t => (object)Describe(t)).ToList() };
        }

        public static object Remove(string id)
        {
            if (id == null)
            {
                int n = _triggers.Count;
                _triggers.Clear();
                return new Dictionary<string, object> { ["removed"] = n };
            }
            if (!int.TryParse(id, out int i) || _triggers.RemoveAll(t => t.Id == i) == 0)
            {
                throw ApiException.NotFound("trigger " + id + " not found");
            }
            return new Dictionary<string, object> { ["removed"] = 1 };
        }

        /// <summary>Called from the addon's FixedUpdate.</summary>
        public static void FixedUpdate()
        {
            if (_triggers.Count == 0)
            {
                return;
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                _triggers.Clear();
                return;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                return;
            }
            foreach (Trigger t in _triggers.ToList())
            {
                // An earlier action may have cleared the triggers or changed/destroyed the active vessel.
                if (!_triggers.Contains(t) || FlightGlobals.ActiveVessel != v)
                {
                    continue;
                }
                if (t.VesselId != v.id)
                {
                    continue;
                }
                if (t.State == "pending")
                {
                    // Fired during rails warp: run once the vessel is back off rails at 1x.
                    if (!v.packed && TimeWarp.CurrentRate <= 1.01f)
                    {
                        t.State = "armed";
                        Run(t, v, t.PendingValue);
                        Prediction.Invalidate();
                    }
                    continue;
                }
                if (t.State != "armed")
                {
                    continue;
                }
                object value;
                bool now;
                try
                {
                    if (t.Condition != null)
                    {
                        Dictionary<string, object> values = new Dictionary<string, object>();
                        now = t.Condition.Evaluate(v, values);
                        value = t.Condition.Report(values);
                    }
                    else
                    {
                        value = t.Reader != null ? t.Reader(v) : FlightFields.Read(v, t.Field);
                        now = t.Test(value);
                    }
                }
                catch (Exception e)
                {
                    t.State = "failed";
                    t.Error = "condition: " + e.GetType().Name + ": " + e.Message;
                    UnityEngine.Debug.LogError("[KspBridge] trigger " + t.Id + " " + t.Error);
                    continue;
                }
                bool fire = now && (!t.Repeat || !t.LastTrue);
                t.LastTrue = now;
                // Rate index drops to 0 at once on a gradual stop while the vessel is still on rails: check the real rate and packed.
                if (fire && (v.packed || TimeWarp.CurrentRate > 1.01f))
                {
                    t.WarpStoppedFrom = TimeWarp.CurrentRate > 1.01f ? (double?)TimeWarp.CurrentRate : null;
                    bool rails = v.packed;
                    Handlers.WarpHandler.StopNow();
                    if (rails)
                    {
                        t.State = "pending";
                        t.PendingValue = value;
                        continue;
                    }
                }
                if (fire)
                {
                    Run(t, v, value);
                    Prediction.Invalidate(); // actions (stage, throttle) change thrust/mass for later triggers in this tick
                }
            }
        }

        private static void Run(Trigger t, Vessel v, object value)
        {
            t.FiredCount++;
            t.FiredMet = v.missionTime;
            t.FiredValue = value;
            FlightRecorder.TriggerFired(v, t.Id, t.Name, t.Field, value, t.Actions);
            if (!t.Repeat)
            {
                t.State = "fired";
            }
            if (t.Custom != null)
            {
                try
                {
                    if (!t.Custom(v))
                    {
                        // Conditions no longer met (e.g. after a warp stop): back to armed, not counted as fired.
                        t.State = "armed";
                        t.FiredCount--;
                        t.FiredMet = null;
                        t.FiredValue = null;
                        t.LastTrue = false;
                    }
                }
                catch (Exception e)
                {
                    t.State = "failed";
                    t.Error = (string)t.Actions[0]["type"] + ": " + (e is ApiException ae ? ae.Message : e.ToString());
                    UnityEngine.Debug.LogError("[KspBridge] trigger " + t.Id + " " + t.Error);
                }
                return;
            }
            foreach (Dictionary<string, object> a in t.Actions)
            {
                try
                {
                    Execute((string)a["type"], new RequestBody(a));
                }
                catch (Exception e)
                {
                    t.State = "failed";
                    t.Error = (string)a["type"] + ": " + (e is ApiException ae ? ae.Message : e.ToString());
                    UnityEngine.Debug.LogError("[KspBridge] trigger " + t.Id + " " + t.Error);
                    return;
                }
            }
        }

        private static void Validate(string type, RequestBody a)
        {
            switch (type)
            {
                case "throttle":
                    double value = a.GetDouble("value");
                    if (value < 0 || value > 1)
                    {
                        throw ApiException.BadRequest("throttle value must be 0..1");
                    }
                    break;
                case "attitude":
                    a.GetDouble("pitch");
                    a.GetDouble("heading");
                    break;
                case "sas":
                    a.GetBool("on");
                    break;
                case "action_group":
                    a.GetString("group");
                    break;
            }
        }

        /// <summary>Same code paths as the HTTP control endpoints.</summary>
        private static void Execute(string type, RequestBody a)
        {
            switch (type)
            {
                case "throttle":
                    ControlHandler.SetThrottle(new RequestBody(new Dictionary<string, object> { ["value"] = a.GetDouble("value") }));
                    break;
                case "stage":
                    ControlHandler.ActivateNextStage();
                    break;
                case "attitude":
                    AttitudeHandler.Set(a);
                    break;
                case "release_attitude":
                    AttitudeHandler.Off();
                    break;
                case "sas":
                    ControlHandler.SetSas(new RequestBody(new Dictionary<string, object> { ["enabled"] = a.GetBool("on") }));
                    break;
                case "action_group":
                    string action = a.Has("on") ? (a.GetBool("on") ? "on" : "off") : "toggle";
                    ControlHandler.SetActionGroup(new RequestBody(new Dictionary<string, object> { ["group"] = a.GetString("group"), ["action"] = action }));
                    break;
            }
        }

        private static Dictionary<string, object> Describe(Trigger t) => new Dictionary<string, object>
        {
            ["id"] = t.Id,
            ["name"] = t.Name,
            ["when"] = t.When,
            ["actions"] = t.Actions.Cast<object>().ToList(),
            ["repeat"] = t.Repeat,
            ["state"] = t.State,
            ["fired_count"] = t.FiredCount,
            ["fired_met"] = t.FiredMet,
            ["fired_value"] = t.FiredValue,
            ["error"] = t.Error,
            ["warp_stopped_from"] = t.WarpStoppedFrom,
        };

        /// <summary>Removes all triggers; returns how many there were.</summary>
        public static int Clear()
        {
            int n = _triggers.Count;
            _triggers.Clear();
            return n;
        }
    }
}
