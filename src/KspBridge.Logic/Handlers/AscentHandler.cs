using KspBridge.Flight;

namespace KspBridge.Handlers
{
    public static class AscentHandler
    {
        /// <summary>POST /api/ascent/start {target_apoapsis, heading?, turn_start_speed?, turn_angle?, max_aoa_deg?, auto_stage?, ap_tolerance?}</summary>
        public static object Start(RequestBody body)
        {
            Vessel v = Guard.ActiveVessel();
            return AscentGuidance.Start(v, new AscentGuidance.Args
            {
                TargetApoapsis = body.GetDouble("target_apoapsis"),
                Heading = body.GetDouble("heading", 90),
                TurnStartSpeed = body.GetDouble("turn_start_speed", 60),
                TurnAngle = body.GetDouble("turn_angle", 10),
                MaxAoa = body.GetDouble("max_aoa_deg", 5),
                AutoStage = !body.Has("auto_stage") || body.GetBool("auto_stage"),
                ApTolerance = body.GetDouble("ap_tolerance", 200),
            });
        }

        /// <summary>POST /api/ascent/cancel</summary>
        public static object Cancel()
        {
            AscentGuidance.Cancel("cancelled");
            return AscentGuidance.Describe();
        }
    }
}
