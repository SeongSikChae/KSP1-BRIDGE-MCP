using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// SAS and RCS outside a flight plan (set_sas, set_rcs, rcs_translate) and their state for get_flight/get_status.
    /// Main thread only. Refused while a flight plan runs: the plan owns SAS (off) and RCS (step key rcs).
    /// </summary>
    public static class ManualControl
    {
        private static readonly (string Name, VesselAutopilot.AutopilotMode Mode)[] Modes =
        {
            ("stability", VesselAutopilot.AutopilotMode.StabilityAssist),
            ("prograde", VesselAutopilot.AutopilotMode.Prograde),
            ("retrograde", VesselAutopilot.AutopilotMode.Retrograde),
            ("normal", VesselAutopilot.AutopilotMode.Normal),
            ("antinormal", VesselAutopilot.AutopilotMode.Antinormal),
            ("radial_in", VesselAutopilot.AutopilotMode.RadialIn),
            ("radial_out", VesselAutopilot.AutopilotMode.RadialOut),
            ("target", VesselAutopilot.AutopilotMode.Target),
            ("anti_target", VesselAutopilot.AutopilotMode.AntiTarget),
            ("maneuver", VesselAutopilot.AutopilotMode.Maneuver),
        };

        /// <summary>Tool name of a mode; null for a value this table does not know (never throws: the handover uses it).</summary>
        public static string ModeName(VesselAutopilot.AutopilotMode mode)
        {
            foreach ((string name, VesselAutopilot.AutopilotMode m) in Modes)
            {
                if (m == mode)
                {
                    return name;
                }
            }
            return null;
        }

        /// <summary>Mode of a tool name; null for null or an unknown name.</summary>
        public static VesselAutopilot.AutopilotMode? ParseModeOrNull(string name)
        {
            foreach ((string n, VesselAutopilot.AutopilotMode m) in Modes)
            {
                if (n == name)
                {
                    return m;
                }
            }
            return null;
        }

        private static VesselAutopilot.AutopilotMode ParseMode(string name) => ParseModeOrNull(name)
            ?? throw ApiException.BadRequest("mode must be one of " + string.Join(", ", Modes.Select(x => x.Name)));

        private static string SpeedModeName(FlightGlobals.SpeedDisplayModes m) =>
            m == FlightGlobals.SpeedDisplayModes.Orbit ? "orbit" : m == FlightGlobals.SpeedDisplayModes.Surface ? "surface" : "target";

        private static FlightGlobals.SpeedDisplayModes ParseReference(Vessel v, string reference)
        {
            FlightGlobals.SpeedDisplayModes m = reference == "orbit" ? FlightGlobals.SpeedDisplayModes.Orbit
                : reference == "surface" ? FlightGlobals.SpeedDisplayModes.Surface
                : reference == "target" ? FlightGlobals.SpeedDisplayModes.Target
                : throw ApiException.BadRequest("reference must be orbit, surface or target");
            if (m == FlightGlobals.SpeedDisplayModes.Target && v.targetObject == null)   // a destroyed target is not caught (stock TargetLockInvalid alike)
            {
                throw new ApiException(409, "sas_mode_unavailable", "reference target needs a target");
            }
            return m;
        }

        /// <summary>Modes CanSetMode allows now (it depends on the navball speed display mode).</summary>
        private static List<string> Available(Vessel v)
        {
            VesselAutopilot ap = v.Autopilot;
            return Modes.Where(x => ap.CanSetMode(x.Mode)).Select(x => x.Name).ToList();
        }

        /// <summary>The checks every manual control tool shares.</summary>
        private static void RequireManual(Vessel v)
        {
            FlightReset.RequireNoPendingRecovery();
            FlightReset.RequireSceneReady();
            if (Coordinator.Running)
            {
                throw new ApiException(409, "plan_running", "a flight plan is running (abort_flight first)");
            }
            if (v.isEVA)
            {
                throw new ApiException(409, "eva_not_supported", "EVA kerbals are not supported");
            }
            if (!v.IsControllable)
            {
                throw new ApiException(409, "not_controllable", "the vessel cannot be controlled (no pilot, probe core or signal)");
            }
        }

        /// <summary>
        /// set_sas. Atomic: the display mode, the SAS group and the autopilot state are recorded first and put back on any failure.
        /// Turning on calls Autopilot.Enable(mode) directly: SetMode on a disabled autopilot only records the mode, and the next
        /// VesselAutopilot.Update turns it on with Enable() = stability (VesselAutopilot.cs 2295~2323, 2499~2534).
        /// </summary>
        public static object SetSas(Vessel v, RequestBody b)
        {
            RequireManual(v);
            bool on = b.GetBool("on");
            string name = b.Has("mode") ? b.GetString("mode") : null;
            string reference = b.Has("reference") ? b.GetString("reference") : null;
            if (!on && (name != null || reference != null))
            {
                throw ApiException.BadRequest("mode and reference need on: true");
            }
            VesselAutopilot.AutopilotMode? m = name == null ? (VesselAutopilot.AutopilotMode?)null : ParseMode(name);
            FlightGlobals.SpeedDisplayModes? speed = reference == null ? (FlightGlobals.SpeedDisplayModes?)null : ParseReference(v, reference);

            VesselAutopilot ap = v.Autopilot;
            SasSnapshot before = SasSnapshot.Take(v);
            try
            {
                if (!on)
                {
                    SetGroupChecked(v, KSPActionGroup.SAS, false);
                    return DescribeSas(v);
                }
                bool running = before.Enabled && before.Group;
                VesselAutopilot.AutopilotMode want = m ?? (running ? before.Mode : VesselAutopilot.AutopilotMode.StabilityAssist);
                if (speed.HasValue && speed.Value != FlightGlobals.speedDisplayMode)
                {
                    FlightGlobals.SetSpeedMode(speed.Value);
                }
                if (!ap.CanSetMode(want))
                {
                    throw Unavailable(v, want);   // judged once, right after the display mode change
                }
                if (!running)
                {
                    SetGroupChecked(v, KSPActionGroup.SAS, true);
                    if (!ap.Enable(want))
                    {
                        throw Unavailable(v, want);
                    }
                }
                else if (want != ap.Mode && !ap.SetMode(want))
                {
                    throw Unavailable(v, want);
                }
                return DescribeSas(v);
            }
            catch
            {
                before.Restore(v);
                throw;
            }
        }

        /// <summary>Error code by the mode actually judged: stability means SAS itself is unavailable.</summary>
        private static ApiException Unavailable(Vessel v, VesselAutopilot.AutopilotMode judged) =>
            judged == VesselAutopilot.AutopilotMode.StabilityAssist
                ? new ApiException(409, "sas_unavailable", "SAS is not available on this vessel")
                : new ApiException(409, "sas_mode_unavailable", ModeName(judged) + " is not available now (speed display "
                    + SpeedModeName(FlightGlobals.speedDisplayMode) + "; available: " + string.Join(", ", Available(v)) + ")");

        /// <summary>
        /// SetGroup goes through ToggleGroup. An override group (ADDITIONAL_ACTION_GROUPS) toggles only its own list and leaves the
        /// group itself unchanged (ActionGroupList.cs 106~148), which cannot be undone afterwards: refused before touching it.
        /// A cooldown changes nothing, so it is found by reading the group back.
        /// </summary>
        internal static void SetGroupChecked(Vessel v, KSPActionGroup g, bool on)
        {
            if (v.ActionGroups[g] == on)
            {
                return;
            }
            if (Overridden(v, g))
            {
                throw new ApiException(409, "group_locked", g + " is held by action group override " + v.GroupOverride);
            }
            v.ActionGroups.SetGroup(g, on);
            if (v.ActionGroups[g] != on)
            {
                throw new ApiException(409, "group_locked", g + " did not change (cooldown)");
            }
        }

        internal static bool Overridden(Vessel v, KSPActionGroup g)
        {
            int n = v.GroupOverride;
            return GameSettings.ADDITIONAL_ACTION_GROUPS && n > 0 && v.OverrideDefault != null && n <= v.OverrideDefault.Length
                && (v.OverrideDefault[n - 1] || (v.OverrideActionControl[n - 1] & g) != 0);
        }

        /// <summary>The state set_sas puts back on failure. Restore only touches what differs: Enable on a running autopilot
        /// re-initializes it and re-locks stability to the current attitude.</summary>
        private sealed class SasSnapshot
        {
            public FlightGlobals.SpeedDisplayModes Speed;
            public bool Group;
            public bool Enabled;
            public VesselAutopilot.AutopilotMode Mode;

            public static SasSnapshot Take(Vessel v) => new SasSnapshot
            {
                Speed = FlightGlobals.speedDisplayMode,
                Group = v.ActionGroups[KSPActionGroup.SAS],
                Enabled = v.Autopilot.Enabled,
                Mode = v.Autopilot.Mode,
            };

            public void Restore(Vessel v)
            {
                Safe(() =>
                {
                    if (FlightGlobals.speedDisplayMode != Speed)
                    {
                        FlightGlobals.SetSpeedMode(Speed);
                    }
                });
                Safe(() =>
                {
                    if (v.ActionGroups[KSPActionGroup.SAS] != Group && !Overridden(v, KSPActionGroup.SAS))
                    {
                        v.ActionGroups.SetGroup(KSPActionGroup.SAS, Group);
                    }
                });
                Safe(() =>
                {
                    VesselAutopilot ap = v.Autopilot;
                    if (Enabled && (!ap.Enabled || ap.Mode != Mode))
                    {
                        ap.Enable(Mode);
                    }
                });
            }
        }

        public static Dictionary<string, object> DescribeSas(Vessel v)
        {
            VesselAutopilot ap = v.Autopilot;
            return new Dictionary<string, object>
            {
                ["on"] = ap.Enabled && v.ActionGroups[KSPActionGroup.SAS],
                ["mode"] = ModeName(ap.Mode),
                ["speed_mode"] = SpeedModeName(FlightGlobals.speedDisplayMode),
                ["available_modes"] = Available(v),
            };
        }

        /// <summary>set_rcs: the RCS action group. A refused change leaves the group as it was.</summary>
        public static object SetRcs(Vessel v, RequestBody b)
        {
            RequireManual(v);
            SetGroupChecked(v, KSPActionGroup.RCS, b.GetBool("on"));
            return DescribeRcs(v);
        }

        public static Dictionary<string, object> DescribeRcs(Vessel v)
        {
            int parts = 0;
            foreach (Part p in v.parts)
            {
                if (p.FindModuleImplementing<ModuleRCS>() != null)
                {
                    parts++;
                }
            }
            object mono = null;
            PartResourceDefinition mp = PartResourceLibrary.Instance?.GetDefinition("MonoPropellant");   // a mod may remove it
            if (mp != null)
            {
                v.GetConnectedResourceTotals(mp.id, out double amount, out double max);
                mono = max > 0 ? new Dictionary<string, object> { ["amount"] = amount, ["max"] = max } : null;
            }
            return new Dictionary<string, object>
            {
                ["on"] = v.ActionGroups[KSPActionGroup.RCS],
                ["parts"] = parts,
                ["monopropellant"] = mono,
            };
        }

        /// <summary>get_flight controls group.</summary>
        public static Dictionary<string, object> Describe(Vessel v) => new Dictionary<string, object>
        {
            ["sas"] = DescribeSas(v),
            ["rcs"] = DescribeRcs(v),
        };

        /// <summary>Fields added to get_status.flight.</summary>
        public static void AddSummary(Vessel v, Dictionary<string, object> d)
        {
            d["sas"] = v.Autopilot.Enabled && v.ActionGroups[KSPActionGroup.SAS];
            d["rcs"] = v.ActionGroups[KSPActionGroup.RCS];
        }

        private static void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] manual control: " + e);
            }
        }
    }
}
