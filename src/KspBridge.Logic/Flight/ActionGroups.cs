using System;
using System.Collections.Generic;
using System.Linq;
using KspBridge.Flight.Control;

namespace KspBridge.Flight
{
    /// <summary>get_action_groups and set_action_group (design 0043): the active vessel's action groups as the flight keys use them. Main thread only.</summary>
    public static class ActionGroups
    {
        /// <summary>Groups set_action_group changes; SAS and RCS have set_sas and set_rcs, Stage is staging.</summary>
        private static readonly KSPActionGroup[] Settable =
        {
            KSPActionGroup.Custom01, KSPActionGroup.Custom02, KSPActionGroup.Custom03, KSPActionGroup.Custom04, KSPActionGroup.Custom05,
            KSPActionGroup.Custom06, KSPActionGroup.Custom07, KSPActionGroup.Custom08, KSPActionGroup.Custom09, KSPActionGroup.Custom10,
            KSPActionGroup.Light, KSPActionGroup.Gear, KSPActionGroup.Brakes, KSPActionGroup.Abort,
        };

        /// <summary>The lock FlightInputHandler checks before the group's key.</summary>
        private static ControlTypes KeyLock(KSPActionGroup g)
        {
            switch (g)
            {
                case KSPActionGroup.Light: return ControlTypes.GROUP_LIGHTS;
                case KSPActionGroup.Gear: return ControlTypes.GROUP_GEARS;
                case KSPActionGroup.Brakes: return ControlTypes.GROUP_BRAKES;
                case KSPActionGroup.Abort: return ControlTypes.GROUP_ABORT;
                default: return ControlTypes.CUSTOM_ACTION_GROUPS;
            }
        }

        public static object List(Vessel v)
        {
            List<object> rows = new List<object>();
            for (int i = 0; i < BaseAction.ActionGroupsLength; i++)   // KSPActionGroup bits without None and REPLACEWITHDEFAULT
            {
                KSPActionGroup g = (KSPActionGroup)(1 << i);
                bool settable = Settable.Contains(g);
                rows.Add(new Dictionary<string, object>
                {
                    ["group"] = g.ToString(),
                    ["on"] = v.ActionGroups[g],
                    ["cooldown"] = v.ActionGroups.CooldownTimeRemaining(g),
                    ["settable"] = settable,
                    ["locked"] = settable ? (object)(!PartStates.Unlocked(KeyLock(g)) || ManualControl.Overridden(v, g)) : null,
                    ["actions"] = Actions(v, g),
                });
            }
            return new Dictionary<string, object>
            {
                ["vessel"] = v.vesselName,
                ["id"] = (double)v.persistentId,
                ["override"] = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0,
                ["groups"] = rows,
            };
        }

        /// <summary>The actions ActionGroupList.ToggleGroup would invoke for the group, chosen the same way.</summary>
        private static List<object> Actions(Vessel v, KSPActionGroup g)
        {
            int n = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
            bool def = n > 0 && n <= v.OverrideDefault.Length && v.OverrideDefault[n - 1];
            return BaseAction.CreateActionList(v.parts, g, n, def, true).Select(a =>
            {
                Part p = a.listParent.part;
                PartModule m = a.listParent.module;
                return (object)new Dictionary<string, object>
                {
                    ["part_id"] = (double)p.persistentId,
                    ["part"] = p.partInfo.name,
                    ["module"] = m == null ? PartStates.PartLevel : m.moduleName,
                    ["index"] = m == null ? null : (object)p.Modules.IndexOf(m),
                    ["action"] = a.name,
                    ["title"] = a.guiName,
                };
            }).ToList();
        }

        /// <summary>
        /// Like the group's key: refused while its key lock is set (the window focus lock aside); the change goes through
        /// ManualControl.SetGroupChecked (override or cooldown: 409 group_locked). An action that throws inside ToggleGroup leaves the
        /// group flipped with later actions not run: 500 action_failed with the group's state now.
        /// </summary>
        public static object Set(Vessel v, RequestBody b)
        {
            if (v.isEVA)
            {
                throw new ApiException(409, "eva_not_supported", "EVA kerbals are not supported");
            }
            KSPActionGroup g = ParseGroup(b.GetString("group"));
            string action = b.GetString("action");
            if (action != "toggle" && action != "on" && action != "off")
            {
                throw ApiException.BadRequest("action must be toggle, on or off");
            }
            if (!PartStates.Unlocked(KeyLock(g)))
            {
                // GetName: the exact flag name; ToString would also name composite flags that contain it
                throw new ApiException(409, "group_locked", g + " is input_locked (" + Enum.GetName(typeof(ControlTypes), KeyLock(g))
                    + (v.IsControllable ? "" : "; no control") + ")");
            }
            bool before = v.ActionGroups[g];
            bool want = action == "toggle" ? !before : action == "on";
            bool changed = want != before;
            List<object> actions = changed ? Actions(v, g) : new List<object>();
            uint id = v.persistentId;
            if (changed)
            {
                try
                {
                    ManualControl.SetGroupChecked(v, g, want);
                }
                catch (ApiException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string error = ex.GetType().Name + ": " + ex.Message;
                    Record(g, action, before, v.ActionGroups[g], actions, error);
                    throw new ApiException(500, "action_failed", error + " (" + g + " is now " + (v.ActionGroups[g] ? "on" : "off")
                        + "; read get_action_groups before retrying)");
                }
                Record(g, action, before, v.ActionGroups[g], actions, null);
            }
            return new Dictionary<string, object>
            {
                ["group"] = g.ToString(),
                ["action"] = action,
                ["before"] = before,
                ["on"] = v.ActionGroups[g],
                ["changed"] = changed,
                ["cooldown"] = v.ActionGroups.CooldownTimeRemaining(g),
                ["actions"] = actions,
                ["vessel_changed"] = FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.persistentId != id,
            };
        }

        private static void Record(KSPActionGroup g, string action, bool before, bool on, List<object> actions, string error) =>
            PartStates.Log("action_group", new Dictionary<string, object>
            {
                ["group"] = g.ToString(),
                ["action"] = action,
                ["before"] = before,
                ["on"] = on,
                ["changed"] = before != on,
                ["actions"] = actions,
                ["error"] = error,
            }, "action_group " + g + " " + action + " " + before + " -> " + on + " (" + actions.Count + " actions)" + (error != null ? " failed: " + error : ""));

        /// <summary>Case-insensitive; SAS, RCS and Stage point to their own tools.</summary>
        private static KSPActionGroup ParseGroup(string name)
        {
            KSPActionGroup g = Settable.FirstOrDefault(x => string.Equals(x.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (g != KSPActionGroup.None)
            {
                return g;
            }
            switch (name.ToUpperInvariant())
            {
                case "SAS": throw ApiException.BadRequest("use set_sas for the SAS group");
                case "RCS": throw ApiException.BadRequest("use set_rcs for the RCS group");
                case "STAGE": throw ApiException.BadRequest("Stage is not set here (staging)");
            }
            throw ApiException.BadRequest("group must be one of " + string.Join(", ", Settable.Select(x => x.ToString()).ToArray()));
        }
    }
}
