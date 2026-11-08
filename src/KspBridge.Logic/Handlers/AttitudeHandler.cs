using System.Collections.Generic;
using KspBridge.Attitude;

namespace KspBridge.Handlers
{
    public static class AttitudeHandler
    {
        /// <summary>POST /api/attitude {mode, pitch, heading, roll?}</summary>
        public static object Set(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            string mode = body.Has("mode") ? body.GetString("mode").ToLowerInvariant() : "own";
            double? roll = body.Has("roll") ? body.GetDouble("roll") : (double?)null;
            AttitudeController.Set(v, mode, body.GetDouble("pitch"), body.GetDouble("heading"), roll);
            return AttitudeController.Describe(v);
        }

        /// <summary>POST /api/attitude/off</summary>
        public static object Off()
        {
            Vessel v = Guard.ActiveVessel();
            AttitudeController.Release();
            Dictionary<string, object> d = AttitudeController.Describe(v);
            d["sas"] = v.ActionGroups[KSPActionGroup.SAS];
            return d;
        }
    }
}
