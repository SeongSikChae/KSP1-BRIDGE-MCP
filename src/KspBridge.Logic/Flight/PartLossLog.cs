using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Flight
{
    /// <summary>
    /// Part loss and its causes on loaded vessels (debris included) as flight events and [KspBridge] log lines, with or without a plan.
    /// A collision's cause fires after part_lost: Part.HandleCollision calls explode() before onCollision/onCrash (decompile checked).
    /// Vessel.Die (recovery, debris cleanup, EVA boarding) destroys parts without Part.Die, so it raises no part_lost.
    /// Instance handlers: KSP's EvtDelegate reads evt.Target.GetType(), which throws for static methods (see FlightScene).
    /// </summary>
    public sealed class PartLossLog
    {
        private static PartLossLog _hooked;

        public static void Hook()
        {
            Unhook();
            PartLossLog s = new PartLossLog();
            GameEvents.onPartWillDie.Add(s.OnWillDie);
            GameEvents.onCollision.Add(s.OnCollision);
            GameEvents.onCrash.Add(s.OnCrash);
            GameEvents.onCrashSplashdown.Add(s.OnSplashdown);
            GameEvents.onOverheat.Add(s.OnOverheat);
            GameEvents.onOverPressure.Add(s.OnOverPressure);
            GameEvents.onOverG.Add(s.OnOverG);
            GameEvents.onPartJointBreak.Add(s.OnJointBreak);
            _hooked = s;
        }

        public static void Unhook()
        {
            PartLossLog s = _hooked;
            if (s == null)
            {
                return;
            }
            _hooked = null;
            GameEvents.onPartWillDie.Remove(s.OnWillDie);
            GameEvents.onCollision.Remove(s.OnCollision);
            GameEvents.onCrash.Remove(s.OnCrash);
            GameEvents.onCrashSplashdown.Remove(s.OnSplashdown);
            GameEvents.onOverheat.Remove(s.OnOverheat);
            GameEvents.onOverPressure.Remove(s.OnOverPressure);
            GameEvents.onOverG.Remove(s.OnOverG);
            GameEvents.onPartJointBreak.Remove(s.OnJointBreak);
        }

        /// <summary>onPartWillDie, not onPartDie: the part is still on its vessel, so the vessel's dynamic pressure and G are readable.</summary>
        private void OnWillDie(Part p) => Safe("part_lost", () =>
        {
            Vessel v = p?.vessel;
            Emit("part_lost", Base(p, new Dictionary<string, object>
            {
                ["vessel"] = v?.vesselName,
                ["q_kpa"] = R(v?.dynamicPressurekPa),
                ["g"] = R(v?.geeForce),
                ["temp"] = R(p?.temperature),
                ["max_temp"] = R(p?.maxTemp),
                ["skin_temp"] = R(p?.skinTemperature),
                ["skin_max_temp"] = R(p?.skinMaxTemp),
            }));
        });

        private void OnCollision(EventReport r) => Collision("part", r);
        private void OnCrash(EventReport r) => Collision("crash", r);
        private void OnSplashdown(EventReport r) => Collision("splashdown", r);

        /// <summary>EventReport.param is the relative speed (m/s) in Part.HandleCollision; 0 from CollisionEnhancer and Vessel.CheckKill.</summary>
        private static void Collision(string kind, EventReport r) => Safe("part_collision", () =>
            Emit("part_collision", Base(r?.origin, new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["other"] = r?.other,
                ["speed"] = R(r?.param),
                ["crash_tolerance"] = R(r?.origin?.crashTolerance),
            })));

        private void OnOverheat(EventReport r) => Limit("overheat", r);
        private void OnOverPressure(EventReport r) => Limit("pressure", r);
        private void OnOverG(EventReport r) => Limit("g", r);

        /// <summary>msg holds "current / limit" (EventReport.msg); param is explosionPotential, not a measurement.</summary>
        private static void Limit(string kind, EventReport r) => Safe("part_limit", () =>
            Emit("part_limit", Base(r?.origin, new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["msg"] = r?.msg,
            })));

        private void OnJointBreak(PartJoint j, float force)
        {
            if (force <= 0)
            {
                return;   // DestroyJoint (decouple, undock) fires 0
            }
            Safe("part_joint_break", () =>
            {
                Part parent = j?.Parent;
                Emit("part_joint_break", Base(j?.Child, new Dictionary<string, object>
                {
                    ["parent"] = parent?.partInfo?.name,
                    ["parent_id"] = parent != null ? (object)(double)parent.persistentId : null,
                    ["force"] = R(force),
                }));
            });
        }

        /// <summary>part, part_id, vessel_id, active, then the extra fields (null when p or its vessel is null).</summary>
        private static Dictionary<string, object> Base(Part p, Dictionary<string, object> extra)
        {
            Vessel v = p?.vessel;
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["part"] = p?.partInfo?.name,
                ["part_id"] = p != null ? (object)(double)p.persistentId : null,
                ["vessel_id"] = v != null ? (object)(double)v.persistentId : null,
                ["active"] = v != null && v == FlightGlobals.ActiveVessel,
            };
            foreach (KeyValuePair<string, object> kv in extra)
            {
                d[kv.Key] = kv.Value;
            }
            return d;
        }

        private static object R(double? x) => x.HasValue ? (object)Math.Round(x.Value, 3) : null;

        private static void Emit(string type, Dictionary<string, object> data)
        {
            // StepIndex keeps the last step after a plan ends, so -1 ("no step", as logic_reloaded) unless a plan is running.
            FlightEvents.AddPart(type, Coordinator.Running ? Coordinator.StepIndex : -1, data);
            UnityEngine.Debug.Log("[KspBridge] " + type + " " + Format(data));
        }

        /// <summary>k=v pairs in insertion order; null as "null".</summary>
        private static string Format(Dictionary<string, object> d) =>
            string.Join(" ", d.Select(kv => kv.Key + "=" + (kv.Value ?? "null")).ToArray());

        /// <summary>A handler never throws into KSP's event loop.</summary>
        private static void Safe(string what, Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] " + what + " record failed: " + e);
            }
        }
    }
}
