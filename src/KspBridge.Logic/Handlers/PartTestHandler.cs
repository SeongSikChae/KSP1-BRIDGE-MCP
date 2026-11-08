using System.Collections.Generic;
using System.Linq;
using Contracts;

namespace KspBridge.Handlers
{
    /// <summary>
    /// Part test contracts: list test subjects on the active vessel, run a test now, or arm it to run on the
    /// first tick all contract conditions are met. Part ↔ contract link is the stock one (PartName == partInfo.title).
    /// </summary>
    public static class PartTestHandler
    {
        private sealed class Test
        {
            public Contracts.Templates.PartTest Contract;
            public Contracts.Parameters.PartTest Param;

            public bool Done => Param == null || Param.State == ParameterState.Complete;
            public bool Ready => !Contract.Hauled && Param != null && Param.State == ParameterState.Incomplete && Param.AllChildParametersComplete();
        }

        /// <summary>One test-subject part with every active contract for it (RunTest is global, one run serves all).</summary>
        private sealed class Subject
        {
            public Part Part;
            public ModuleTestSubject Module;
            public List<Test> Tests;

            public Test Primary => Tests.FirstOrDefault(t => t.Ready) ?? Tests.FirstOrDefault(t => !t.Done) ?? Tests[0];
            public bool Hauled => Tests.All(t => t.Contract.Hauled);
            public string Method => Hauled ? "haul" : Module.useEvent ? "event" : Module.useStaging ? "staging" : "none";
            public bool Done => Tests.Where(t => !t.Contract.Hauled).All(t => t.Done);
            public bool Ready => Tests.Any(t => t.Ready);
        }

        /// <summary>GET /api/part-tests</summary>
        public static object List()
        {
            Vessel v = Guard.ActiveVessel();
            return new Dictionary<string, object> { ["tests"] = Subjects(v).Select(s => (object)Describe(s)).ToList() };
        }

        /// <summary>POST /api/part-tests/run</summary>
        public static object Run(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            Subject s = Resolve(v, body.GetRaw("part"));
            CheckRunnable(s);
            bool force = body.Has("force") && body.GetBool("force");
            bool readyBefore = s.Ready;
            if (!readyBefore && !force)
            {
                throw new ApiException(409, "not_ready", "contract conditions not met: " + string.Join(", ", Unmet(s.Primary)) + " (force=true runs anyway)");
            }
            Execute(s);
            return new Dictionary<string, object>
            {
                ["part_id"] = s.Part.flightID,
                ["part"] = s.Part.partInfo.name,
                ["method"] = s.Method,
                ["ready_before"] = readyBefore,
                ["state_after"] = s.Primary.Param != null ? s.Primary.Param.State.ToString() : null,
            };
        }

        /// <summary>POST /api/part-tests/arm</summary>
        public static object Arm(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            Subject s = Resolve(v, body.GetRaw("part"));
            CheckRunnable(s);
            uint id = s.Part.flightID;
            return Flight.TriggerManager.AddInternal(
                "part test " + s.Part.partInfo.name,
                new Dictionary<string, object> { ["field"] = "part_test_ready", ["part_id"] = id },
                vessel => (object)IsReady(vessel, id),
                new Dictionary<string, object> { ["type"] = "part_test", ["part_id"] = id },
                vessel => RunArmed(vessel, id));
        }

        internal static bool IsReady(Vessel v, uint partId)
        {
            Subject s = Find(v, partId);
            return s != null && s.Ready;
        }

        /// <summary>
        /// Armed trigger action. Returns false (stay armed) when the conditions are no longer met — e.g. the
        /// window passed while a warp was being stopped — so the part is not used up outside the window.
        /// </summary>
        private static bool RunArmed(Vessel v, uint partId)
        {
            Subject s = Find(v, partId)
                ?? throw ApiException.NotFound("part " + partId + " is no longer a test subject on the vessel");
            if (s.Done)
            {
                return true;
            }
            if (!s.Ready)
            {
                return false;
            }
            Execute(s);
            return true;
        }

        private static void Execute(Subject s)
        {
            if (s.Module.useEvent)
            {
                s.Module.RunTestEvent(); // same as the stock "Run Test" button (also activates a stageable part)
                return;
            }
            // Staging-tested parts (parachutes, decouplers, engines): OnActive runs the test only when staging is
            // enabled on the part, so run it directly otherwise; then activate the part as staging would.
            if (!s.Part.stagingOn)
            {
                s.Module.RunTest();
            }
            s.Part.force_activate();
        }

        private static void CheckRunnable(Subject s)
        {
            if (s.Hauled)
            {
                throw new ApiException(409, "haul_only", "haul contract: carry the part through the conditions, nothing to run");
            }
            if (s.Done)
            {
                throw new ApiException(409, "already_done", "the test for this part is already complete");
            }
            if (s.Method == "none")
            {
                throw new ApiException(409, "no_test", "this part has no test action");
            }
        }

        private static Subject Find(Vessel v, uint partId) => Subjects(v).FirstOrDefault(x => x.Part.flightID == partId);

        private static List<Subject> Subjects(Vessel v)
        {
            List<Subject> list = new List<Subject>();
            if (ContractSystem.Instance == null)
            {
                return list;
            }
            Contracts.Templates.PartTest[] contracts = ContractSystem.Instance.GetCurrentActiveContracts<Contracts.Templates.PartTest>();
            if (contracts.Length == 0)
            {
                return list;
            }
            foreach (Part p in v.parts)
            {
                ModuleTestSubject m = p.FindModuleImplementing<ModuleTestSubject>();
                if (m == null)
                {
                    continue;
                }
                List<Test> tests = new List<Test>();
                foreach (Contracts.Templates.PartTest c in contracts)
                {
                    if (c.PartName != p.partInfo.title)
                    {
                        continue;
                    }
                    Contracts.Parameters.PartTest param = null;
                    for (int i = 0; i < c.ParameterCount; i++)
                    {
                        if (c.GetParameter(i) is Contracts.Parameters.PartTest pt)
                        {
                            param = pt;
                            break;
                        }
                    }
                    tests.Add(new Test { Contract = c, Param = param });
                }
                if (tests.Count > 0)
                {
                    list.Add(new Subject { Part = p, Module = m, Tests = tests });
                }
            }
            return list;
        }

        private static Subject Resolve(Vessel v, object part)
        {
            List<Subject> all = Subjects(v);
            List<Subject> match;
            if (part is double d)
            {
                match = all.Where(s => s.Part.flightID == (uint)d).ToList();
            }
            else if (part is string name && name.Length > 0)
            {
                match = all.Where(s => s.Part.partInfo.name == name || s.Part.partInfo.title == name).ToList();
            }
            else
            {
                throw ApiException.BadRequest("'part' must be a part name/title or a part_id number");
            }
            if (match.Count == 0)
            {
                throw new ApiException(404, "no_test", "no active part test contract for '" + part + "' on this vessel (accept the contract before launch)");
            }
            if (match.Count > 1)
            {
                throw ApiException.BadRequest("'" + part + "' matches " + match.Count + " parts; use part_id: " + string.Join(", ", match.Select(s => s.Part.flightID)));
            }
            return match[0];
        }

        private static IEnumerable<string> Unmet(Test t)
        {
            if (t.Param == null)
            {
                yield break;
            }
            for (int i = 0; i < t.Param.ParameterCount; i++)
            {
                ContractParameter c = t.Param.GetParameter(i);
                if (c.State != ParameterState.Complete)
                {
                    yield return c.Title;
                }
            }
        }

        private static Dictionary<string, object> Describe(Subject s)
        {
            Test t = s.Primary;
            List<object> conditions = new List<object>();
            if (t.Param != null)
            {
                for (int i = 0; i < t.Param.ParameterCount; i++)
                {
                    ContractParameter c = t.Param.GetParameter(i);
                    conditions.Add(new Dictionary<string, object> { ["title"] = c.Title, ["state"] = c.State.ToString() });
                }
            }
            return new Dictionary<string, object>
            {
                ["part_id"] = s.Part.flightID,
                ["part"] = s.Part.partInfo.name,
                ["title"] = s.Part.partInfo.title,
                ["method"] = s.Method,
                ["contract_id"] = t.Contract.ContractID,
                ["contract"] = t.Contract.Title,
                ["other_contracts"] = s.Tests.Where(x => x != t).Select(x => (object)x.Contract.ContractID).ToList(),
                ["state"] = t.Param != null ? t.Param.State.ToString() : null,
                ["ready"] = s.Ready,
                ["event_active"] = s.Module.Events["RunTestEvent"]?.active ?? false,
                ["staging_on"] = s.Part.stagingOn,
                ["armed"] = Flight.TriggerManager.IsArmedPartTest(s.Part.flightID),
                ["conditions"] = conditions,
            };
        }
    }
}
