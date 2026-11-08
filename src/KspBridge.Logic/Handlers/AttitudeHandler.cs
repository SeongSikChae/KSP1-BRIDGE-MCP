using System.Collections.Generic;
using KspBridge.Attitude;

namespace KspBridge.Handlers
{
    public static class AttitudeHandler
    {
        /// <summary>POST /api/attitude {mode, frame?, pitch?, heading?, roll?, pitch_offset?, yaw_offset?, max_aoa_deg?}</summary>
        public static object Set(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            string mode = body.Has("mode") ? body.GetString("mode").ToLowerInvariant() : "own";
            string frame = body.Has("frame") ? body.GetString("frame").ToLowerInvariant() : "surface";
            double? maxAoa = body.Has("max_aoa_deg") ? body.GetDouble("max_aoa_deg") : (double?)null;
            if (maxAoa.HasValue && (maxAoa.Value <= 0 || maxAoa.Value > 90))
            {
                throw ApiException.BadRequest("max_aoa_deg must be in (0, 90]");
            }

            if (frame == "surface")
            {
                if (body.Has("pitch_offset") || body.Has("yaw_offset"))
                {
                    throw ApiException.BadRequest("pitch_offset/yaw_offset apply to frames other than surface (use pitch/heading)");
                }
                double? roll = body.Has("roll") ? body.GetDouble("roll") : (double?)null;
                AttitudeController.Set(v, mode, "surface", body.GetDouble("pitch"), body.GetDouble("heading"), roll, 0, 0, maxAoa);
            }
            else
            {
                if (body.Has("pitch") || body.Has("heading") || body.Has("roll"))
                {
                    throw ApiException.BadRequest("frame '" + frame + "' takes pitch_offset/yaw_offset, not pitch/heading/roll");
                }
                AttitudeController.Set(v, mode, frame, 0, 0, null,
                    body.GetDouble("pitch_offset", 0), body.GetDouble("yaw_offset", 0), maxAoa);
            }
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
