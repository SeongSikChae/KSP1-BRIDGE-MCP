using KspBridge.App.Broadcast;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace KspBridge.App
{
    [McpServerToolType]
    public sealed class BroadcastTools(BroadcastSession session)
    {
        [McpServerTool(Name = "start_broadcast", Destructive = true)]
        [Description("Record the KSP window as a launch broadcast and ignite at T-0. Starts ffmpeg (gfxcapture of the KSP window), counts down with Korean subtitles and Azure Speech narration (T-20 announcement when countdown >= 20, then 십..일 with beeps and a long beep at T-0) and activates the next stage at T-0 (same as activate_next_stage). Check get_stages delta-v on the pad BEFORE calling this (launch preflight); do not stage separately. While recording, liftoff, stage separations, altitude marks (default 10 km and the atmosphere edge), orbit/landing/splashdown and SOI changes are narrated. Returns {dir, ignited, stage}. Stop with stop_broadcast at the destination.")]
        public Task<string> StartBroadcast(
            [Description("Name spoken in the narration (default: the vessel name)")] string? craftName = null,
            [Description("Countdown seconds, 3..30 (default 10)")] int countdownSeconds = 10,
            [Description("Altitude marks in m to announce (default [10000]; the atmosphere edge is always added)")] double[]? altitudes = null,
            CancellationToken ct = default)
            => session.StartAsync(craftName, Math.Clamp(countdownSeconds, 3, 30), altitudes, ct);

        [McpServerTool(Name = "stop_broadcast", Destructive = false)]
        [Description("Stop the broadcast recording and start composing the final MP4 (burned-in subtitles + narration) in the background. Returns {id, dir, state: composing}; poll get_broadcast_status until done.")]
        public Task<string> StopBroadcast() => session.StopAsync();

        [McpServerTool(Name = "get_broadcast_status", ReadOnly = true)]
        [Description("Broadcast state (idle, countdown, recording, composing, done, failed), folder, elapsed seconds while recording, cues (time and subtitle), ttsFailures, output (final MP4 path when done) and error.")]
        public string GetBroadcastStatus() => session.Status();
    }
}
