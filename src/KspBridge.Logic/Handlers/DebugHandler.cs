using KspBridge.Debugging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the debug tools (#7): POST /api/debug/&lt;name&gt; on the main thread, 409 debug_disabled with the flag off.</summary>
    public static class DebugHandler
    {
        public static IEnumerable<Route> Routes()
        {
            yield return Post("clear", b =>
            {
                DebugState.Clear();
                return DebugState.Status();
            });
            // mask is a hex string: JSON numbers are double and lose bits above 2^53
            yield return Post("input_lock", b =>
            {
                DebugState.SetInputLock(b.Has("mask") ? ParseMask(b.GetString("mask")) : (ulong?)null);
                return DebugState.Status();
            });
            yield return Post("input_locks_error", b =>
            {
                DebugState.InputLocksError = b.GetBool("on");
                return DebugState.Status();
            });
            yield return Post("stage_delay", b =>
            {
                double s = b.GetDouble("seconds");
                if (double.IsNaN(s) || double.IsInfinity(s) || s < 0)
                {
                    throw ApiException.BadRequest("seconds must be a finite number >= 0 (0 releases)");
                }
                DebugState.StageDelayS = s;
                return DebugState.Status();
            });
            yield return Post("add_part_timeout", b =>
            {
                DebugState.AddPartTimeout = b.GetBool("on");
                return DebugState.Status();
            });
        }

        private static ulong ParseMask(string s)
        {
            string hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
            if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong m))
            {
                throw ApiException.BadRequest("mask must be a hex string like 0x8");
            }
            return m;   // 0 allowed: a lock with no controls (0008 controls: [])
        }

        private static Route Post(string name, Func<RequestBody, object> f)
            => new Route("POST", "/api/debug/" + name, (m, b, q) =>
            {
                DebugState.Require();
                return f(b);
            });
    }
}
