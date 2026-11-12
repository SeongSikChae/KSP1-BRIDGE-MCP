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
            yield return new Route("GET", "/api/flight/events", (m, b, q) =>
                Events.Since(ParseLong(q["since"], 0, "since"), (int)Math.Max(1, Math.Min(Events.Capacity, ParseLong(q["limit"], 100, "limit")))));
        }

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
    }
}
