using KspBridge.Mod.Loader;
using KspBridge.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Mod.Core
{
    /// <summary>Core routes: status, plan submit/get/abort, events, step types.</summary>
    internal sealed class CoreModule
    {
        private readonly LogicEntry logic;

        public CoreModule(LogicEntry logic) => this.logic = logic;

        public void Register(Router router)
        {
            router.Add("core/status", _ => Status());
            router.Add("core/plan.submit", args => logic.Coordinator.Submit(args.Obj("plan", required: true), args.Bool("replace")));
            router.Add("core/plan.get", _ => PlanGet());
            router.Add("core/plan.abort", args => logic.Coordinator.Abort(EndReasons.PlannerAbort, args.Str("reason", "aborted by planner"))
                                                 ?? throw BridgeException.BadRequest("no plan is running"));
            router.Add("core/events", EventsSince);
            router.Add("core/step_types", _ => logic.Steps.Types
                .Select(t => (object)new Dictionary<string, object> { ["name"] = t.Name, ["description"] = t.Describe() }).ToList());
        }

        private object PlanGet()
            => new Dictionary<string, object>
            {
                ["current"] = logic.Coordinator.CurrentSummaryWithProgress(),
                ["last"] = logic.Coordinator.LastSummary,
            };

        private object EventsSince(Dictionary<string, object> args)
        {
            long since = (long)(args.Num("since") ?? 0);
            int limit = (int)(args.Num("limit") ?? 100);
            return new Dictionary<string, object>
            {
                ["events"] = logic.Events.Since(since, limit),
                ["last_seq"] = logic.Events.LastSeq,
            };
        }

        /// <summary>Plan shown in status: the running plan, or the last one if it ran in the current flight session.</summary>
        internal object StatusPlan()
        {
            Dictionary<string, object> plan = logic.Coordinator.CurrentSummary;
            if (plan == null && logic.Coordinator.LastSummary != null && logic.Coordinator.LastSession == logic.Session.Id)
                plan = logic.Coordinator.LastSummary;
            if (plan == null)
                return null;
            return new Dictionary<string, object>
            {
                ["id"] = plan["id"],
                ["state"] = plan["state"],
                ["step_index"] = plan["step_index"],
                ["step_type"] = plan["step_type"],
                ["end_reason"] = plan["end_reason"],
            };
        }

        private object Status()
        {
            ILoaderHost host = logic.Host;
            string block = logic.Session.BlockReason();
            return new Dictionary<string, object>
            {
                ["scene"] = logic.Session.Scene,
                ["ready"] = block == null,
                ["not_ready_reason"] = block,
                ["session_id"] = logic.Session.Id,
                ["versions"] = new Dictionary<string, object>
                {
                    ["mod_loader"] = host.LoaderVersion,
                    ["mod_logic"] = typeof(LogicEntry).Assembly.GetName().Version.ToString(),
                    ["loader_api"] = LoaderApi.Version,
                    ["protocol"] = Protocol.Version,
                    ["modules"] = host.GenerationAssemblies.Select(a => (object)(a.GetName().Name + " " + a.GetName().Version)).ToList(),
                },
                ["generation"] = new Dictionary<string, object>
                {
                    ["n"] = host.Generation,
                    ["loaded_at"] = host.LoadedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["last_error"] = host.LastError,
                },
                ["vessel"] = VesselSummary(),
                ["plan"] = StatusPlan(),
                ["controller"] = new Dictionary<string, object>
                {
                    ["attached"] = logic.Controller.Attached,
                    ["vessel"] = logic.Controller.AttachedVessel,
                    ["bridge_flybywire_handlers"] = Controller.CountBridgeHandlers(),
                },
            };
        }

        private static object VesselSummary()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || FlightGlobals.fetch == null)
                return null;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
                return null;
            return new Dictionary<string, object>
            {
                ["name"] = v.GetDisplayName(),
                ["situation"] = v.situation.ToString(),
                ["body"] = v.mainBody != null ? v.mainBody.bodyName : null,
                ["altitude_m"] = Math.Round(v.altitude, 1),
            };
        }
    }
}
