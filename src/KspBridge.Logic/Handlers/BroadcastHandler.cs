using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using KspBridge.Flight;

namespace KspBridge.Handlers
{
    /// <summary>Narration event feed for broadcast recordings (start_broadcast / stop_broadcast in the App).</summary>
    public static class BroadcastHandler
    {
        public static object Arm(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            double atmosphere = v.mainBody != null && v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
            List<double> altitudes = new List<double>();
            if (body.Has("altitudes"))
            {
                foreach (object o in body.GetList("altitudes"))
                {
                    if (!(o is double d) || d <= 0)
                    {
                        throw ApiException.BadRequest("'altitudes' must be positive numbers (m)");
                    }
                    altitudes.Add(d);
                }
            }
            else
            {
                altitudes.Add(10000);
            }
            if (atmosphere > 0 && !altitudes.Contains(atmosphere))
            {
                altitudes.Add(atmosphere);
            }
            double[] sorted = altitudes.Distinct().OrderBy(a => a).ToArray();
            BroadcastEvents.Arm(sorted, atmosphere);
            return new Dictionary<string, object>
            {
                ["armed"] = true,
                ["vessel"] = v.vesselName,
                ["body"] = v.mainBody != null ? v.mainBody.bodyName : "",
                ["situation"] = v.situation.ToString(),
                ["altitudes"] = sorted.Cast<object>().ToList(),
                ["atmosphere"] = atmosphere,
            };
        }

        public static object Events(NameValueCollection query)
        {
            long since = 0;
            string s = query["since"];
            if (!string.IsNullOrEmpty(s) && !long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out since))
            {
                throw ApiException.BadRequest("'since' must be an integer");
            }
            return BroadcastEvents.Since(since);
        }

        public static object Disarm()
        {
            int count = BroadcastEvents.Disarm();
            return new Dictionary<string, object> { ["armed"] = false, ["count"] = count };
        }
    }
}
