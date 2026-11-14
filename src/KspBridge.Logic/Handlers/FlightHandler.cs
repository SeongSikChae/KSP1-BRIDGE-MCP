using KspBridge.Flight;
using System;
using System.Collections.Generic;
using System.Globalization;
using Events = KspBridge.Flight.FlightEvents;   // KSP has a global FlightEvents type that would win over the using above

namespace KspBridge.Handlers
{
    /// <summary>Routes of the flight tools.</summary>
    public static class FlightHandler
    {
        public static IEnumerable<Route> Routes()
        {
            yield return new Route("GET", "/api/flight", (m, b, q) => Coordinator.Describe(Guard.ActiveVessel()));
            // The handler only gets the parsed body, so the App wraps the plan as {plan: {...}}; the parsed object is serialized
            // again as the raw plan for the reload handover.
            yield return new Route("POST", "/api/flight/plan", (m, b, q) =>
            {
                Dictionary<string, object> raw = b.GetObject("plan");
                Vessel v = Guard.ActiveVessel();
                return Coordinator.Submit(v, Plan.Parse(raw, Json.Json.Serialize(raw), v));
            });
            yield return new Route("POST", "/api/flight/steps", (m, b, q) =>
            {
                Vessel v = Guard.ActiveVessel();
                List<object> raw = b.GetRaw("steps") as List<object> ?? throw Plan.Invalid("steps", "must be an array");
                bool includeCurrent = b.Has("include_current") && b.GetBool("include_current");
                return Coordinator.ReplaceSteps(v, Plan.ParseSteps(raw, "steps", v.mainBody), includeCurrent);
            });
            yield return new Route("POST", "/api/flight/abort", (m, b, q) => Coordinator.UserAbort());
            yield return new Route("POST", "/api/flight/sas", (m, b, q) => Flight.Control.ManualControl.SetSas(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/rcs", (m, b, q) => Flight.Control.ManualControl.SetRcs(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/translate", (m, b, q) => Flight.Control.ManualControl.Translate(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/nodes/add", (m, b, q) => Flight.Maneuver.ManeuverNodes.Add(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/nodes/update", (m, b, q) => Flight.Maneuver.ManeuverNodes.Update(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/nodes/remove", (m, b, q) => Flight.Maneuver.ManeuverNodes.Remove(Guard.ActiveVessel(), b));
            yield return new Route("GET", "/api/vessels", (m, b, q) =>
            {
                string d = q["include_debris"];
                if (d != null && d != "true" && d != "false")
                {
                    throw ApiException.BadRequest("include_debris must be true or false");
                }
                return Flight.Targeting.List(VesselSwitch.ListScopeActive(), d == "true");   // qualified: KSP has a global Targeting type
            });
            yield return new Route("POST", "/api/flight/target", (m, b, q) => Flight.Targeting.Set(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/plan_maneuver", (m, b, q) => Flight.Maneuver.ManeuverPlanner.Plan(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/switch", (m, b, q) => VesselSwitch.Switch(b));
            yield return new Route("POST", "/api/flight/revert", (m, b, q) => FlightReset.Revert(b.GetString("target")));
            yield return new Route("POST", "/api/flight/leave", (m, b, q) => FlightReset.Leave());
            yield return new Route("POST", "/api/scene/tracking_station", (m, b, q) => FlightReset.OpenTrackingStation());
            yield return new Route("POST", "/api/vessel/recover", (m, b, q) => FlightReset.Recover(b));
            yield return new Route("POST", "/api/time_warp", (m, b, q) =>
            {
                Guard.ActiveVessel();
                double r = b.GetDouble("rate_index");
                if (r != Math.Floor(r) || r < 0 || r > 100)
                {
                    throw ApiException.BadRequest("rate_index must be an integer in [0, 100]");   // 2.5 is refused, not truncated
                }
                return TimeWarpControl.Set((int)r, b.Has("physics") && b.GetBool("physics"));
            });
            yield return new Route("GET", "/api/flight/events", (m, b, q) =>
                Events.Since(ParseLong(q["since"], 0, "since"), (int)Math.Max(1, Math.Min(Events.Capacity, ParseLong(q["limit"], 100, "limit")))));
            yield return new Route("GET", "/api/flight/controller_trace", (m, b, q) =>
            {
                Vessel v = Guard.ActiveVessel();
                double seconds = ParseDouble(q["seconds"], 10, 1, 20, "seconds");
                int? step = null;
                if (!string.IsNullOrEmpty(q["step"]))
                {
                    if (!long.TryParse(q["step"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) || s < 1 || s > 50)
                    {
                        throw ApiException.BadRequest("step must be an integer in [1, 50]");
                    }
                    step = (int)s;
                }
                return Flight.Control.ControllerTrace.Describe(v, seconds, step);
            });
            yield return new Route("GET", "/api/flight/parts", (m, b, q) => Flight.PartStates.List(Guard.ActiveVessel(), Empty(q["part_id"]), Empty(q["module"])));   // qualified: KSP has a global PartStates enum
            yield return new Route("POST", "/api/flight/part_event", (m, b, q) => Flight.PartStates.Invoke(Guard.ActiveVessel(), b));
            yield return new Route("GET", "/api/flight/commnet", (m, b, q) => CommNetState.Describe(Guard.ActiveVessel()));
            yield return new Route("POST", "/api/flight/part_field", (m, b, q) => Flight.PartStates.SetField(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/resource_flow", (m, b, q) => Flight.PartStates.SetFlow(Guard.ActiveVessel(), b));
            yield return new Route("GET", "/api/flight/action_groups", (m, b, q) => Flight.ActionGroups.List(Guard.ActiveVessel()));
            yield return new Route("POST", "/api/flight/action_group", (m, b, q) => Flight.ActionGroups.Set(Guard.ActiveVessel(), b));
            yield return new Route("POST", "/api/flight/resource_transfer", (m, b, q) => Flight.ResourceTransfer.Start(Guard.ActiveVessel(), b));
            yield return new Route("GET", "/api/flight/resource_transfer", (m, b, q) => Flight.ResourceTransfer.Status());
            yield return new Route("POST", "/api/flight/resource_transfer/stop", (m, b, q) => Flight.ResourceTransfer.StopRequest());
        }

        private static string Empty(string s) => string.IsNullOrEmpty(s) ? null : s;   // as EditorHandler.Empty: ?x= means no x

        private static long ParseLong(string text, long defaultValue, string name)
        {
            if (string.IsNullOrEmpty(text))
            {
                return defaultValue;
            }
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < 0)
            {
                throw ApiException.BadRequest(name + " must be a non-negative integer");
            }
            return value;
        }

        private static double ParseDouble(string text, double defaultValue, double min, double max, string name)
        {
            if (string.IsNullOrEmpty(text))
            {
                return defaultValue;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
            {
                throw ApiException.BadRequest(name + " must be a number in [" + min.ToString(CultureInfo.InvariantCulture) + ", "
                    + max.ToString(CultureInfo.InvariantCulture) + "]");
            }
            return value;
        }
    }
}
