using System.Globalization;
using System.Text.RegularExpressions;

namespace KspBridge.App.Broadcast
{
    /// <summary>Recorded game sound for the final mix.</summary>
    /// <param name="File">Path relative to the broadcast folder (game.wav).</param>
    /// <param name="Offset">Recording start minus the first video frame, in seconds (may be negative).</param>
    public sealed record GameTrack(string File, double Offset, double Volume, Ducking? Duck = null, Compressor? Comp = null);

    /// <summary>acompressor on the game sound (null = off): presses loud bursts such as explosions down.</summary>
    /// <param name="ThresholdDb">Level in dBFS above which the sound is compressed (-60..0).</param>
    /// <param name="Ratio">Compression ratio (1..20).</param>
    /// <param name="Attack">ms (0.01..2000).</param>
    /// <param name="Release">ms (0.01..9000).</param>
    public sealed record Compressor(double ThresholdDb = -18, double Ratio = 4, double Attack = 5, double Release = 200)
    {
        /// <summary>acompressor takes the threshold as a linear amplitude.</summary>
        public string Filter =>
            string.Create(CultureInfo.InvariantCulture,
                $"acompressor=threshold={Math.Pow(10, ThresholdDb / 20):0.######}:ratio={Ratio:0.###}:attack={Attack:0.###}:release={Release:0.###}");
    }

    /// <summary>
    /// sidechaincompress settings that push the game sound down while narration plays.
    /// Ratio 1 turns ducking off; 8 lowered a steady game tone by about 20 dB.
    /// </summary>
    /// <param name="Threshold">Narration level that starts ducking (0.001..1).</param>
    /// <param name="Ratio">Compression ratio (1..20).</param>
    /// <param name="Attack">ms until fully ducked (0.01..2000).</param>
    /// <param name="Release">ms to come back after narration ends (0.01..9000).</param>
    public sealed record Ducking(double Threshold = 0.02, double Ratio = 8, double Attack = 20, double Release = 400)
    {
        public string Filter =>
            string.Create(CultureInfo.InvariantCulture, $"threshold={Threshold:0.######}:ratio={Ratio:0.###}:attack={Attack:0.###}:release={Release:0.###}");
    }

    public static partial class FfmpegArgs
    {
        public const string Raw = "raw.mkv";
        public const string Subtitles = "subs.ass";
        public const string FilterScript = "filter.txt";
        public const string Final = "final.mp4";

        /// <summary>
        /// Records the KSP window into the broadcast folder (run with that folder as working directory).
        /// gfxcapture (Windows Graphics Capture) captures the window itself, also when other windows cover it
        /// (gdigrab copies the screen area and recorded the window on top). -r makes a constant frame rate.
        /// -tune zerolatency: without it x264 frame threading holds back ~1 s of frames, so the "frame=" progress
        /// count lags and the first-frame time (zero) is estimated about 1 s late.
        /// </summary>
        public static List<string> Record(string windowTitle, int frameRate)
        {
            string fps = frameRate.ToString(CultureInfo.InvariantCulture);
            // window_title is an ECMAScript regex: match the whole title. Filter-graph special characters are escaped.
            string regex = string.Concat(windowTitle.Select(c => @".^$|?*+()[]{}\".Contains(c) ? "\\" + c : c.ToString()));
            string title = "^" + regex.Replace("\\", "\\\\").Replace(":", "\\:").Replace(",", "\\,").Replace("'", "\\'") + "$";
            return
            [
                "-y", "-f", "lavfi",
                "-i", $"gfxcapture=window_title={title}:max_framerate={fps}:capture_cursor=0,hwdownload,format=bgra",
                "-r", fps, "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-crf", "18", "-pix_fmt", "yuv420p", Raw,
            ];
        }

        /// <summary>
        /// Burns subs.ass into raw.mkv and mixes narration/beeps at their cue times into final.mp4.
        /// All paths are relative: run in the broadcast folder (no filter path escaping).
        /// </summary>
        public static (List<string> Args, string Filter) Compose(IReadOnlyList<Cue> cues, GameTrack? game = null)
        {
            List<string> args = ["-y", "-i", Raw];
            List<string> filters = [];
            List<string> labels = [];
            int input = 1;
            void Delay(long ms)
            {
                filters.Add($"[{input}:a]aresample=24000,aformat=channel_layouts=mono,adelay={ms}|{ms}[a{input}]");
                labels.Add($"[a{input}]");
                input++;
            }
            foreach (Cue c in cues)
            {
                long ms = (long)Math.Round(Math.Max(0, c.At) * 1000);
                if (c.Audio is not null)
                {
                    args.AddRange(["-i", c.Audio]);
                    Delay(ms);
                }
                if (c.Beep > 0)
                {
                    args.AddRange(["-f", "lavfi", "-t", c.Beep.ToString(CultureInfo.InvariantCulture), "-i", "sine=frequency=1000:sample_rate=24000"]);
                    Delay(ms);
                }
            }
            if (game is not null)
            {
                // Game sound placed at its recording start: delayed when it started after the first frame, trimmed when before.
                args.AddRange(["-i", game.File]);
                string align = game.Offset >= 0
                    ? $"adelay={Ms(game.Offset)}|{Ms(game.Offset)}"
                    : $"atrim=start={Num(-game.Offset)},asetpts=PTS-STARTPTS";
                // Compressor before the volume: loud bursts (explosions) are pressed down, then the whole track is raised.
                string comp = game.Comp is null ? "" : "," + game.Comp.Filter;
                filters.Add($"[{input}:a]aresample=24000,aformat=channel_layouts=mono,{align}{comp},volume={Num(game.Volume)}[game]");
                input++;
            }
            filters.Add($"[0:v]subtitles={Subtitles}[vout]");
            string mix = $"{string.Concat(labels)}amix=inputs={labels.Count}:normalize=0:dropout_transition=0,apad";
            if (labels.Count > 0 && game is not null)
            {
                // Ducking: the narration (key) pushes the game sound down only while it plays.
                filters.Add(mix + "[nar]");
                filters.Add("[nar]asplit=2[nar1][key]");
                filters.Add($"[game][key]sidechaincompress={(game.Duck ?? new Ducking()).Filter}[duck]");
                filters.Add("[duck][nar1]amix=inputs=2:normalize=0:dropout_transition=0[mix]");
            }
            else if (game is not null)
            {
                filters.Add("[game]apad[mix]");
            }
            else if (labels.Count > 0)
            {
                filters.Add(mix + "[mix]");
            }
            if (labels.Count > 0 || game is not null)
            {
                // Final limiter: peaks above -1 dBFS are held down instead of clipping (level=0: no auto gain).
                filters.Add($"[mix]{Limiter}[aout]");
            }
            bool audio = labels.Count > 0 || game is not null;
            // "-/option file" reads the option value from a file (ffmpeg 7.0+; -filter_complex_script was removed).
            args.AddRange(["-/filter_complex", FilterScript, "-map", "[vout]"]);
            if (audio)
            {
                // apad makes the mix endless; -shortest then ends it with the video (full video length kept).
                args.AddRange(["-map", "[aout]", "-c:a", "aac", "-b:a", "160k", "-shortest"]);
            }
            args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", Final]);
            return (args, string.Join(";\n", filters));
        }

        /// <summary>alimiter on the final mix: limit 0.891 = -1 dBFS.</summary>
        public const string Limiter = "alimiter=limit=0.891:level=0";

        private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Frame count of an ffmpeg progress line ("frame=  15 fps=..."), or null.</summary>
        public static int? ProgressFrame(string line)
        {
            Match m = FrameRegex().Match(line);
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }

        [GeneratedRegex(@"^frame=\s*(\d+)")]
        private static partial Regex FrameRegex();
    }

    /// <summary>gfxcapture recording of the KSP window.</summary>
    public sealed class FfmpegRecorder(IProcessRunner runner, IClock clock, string ffmpegPath)
    {
        public static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(3);

        private IRunningProcess? _process;
        private readonly Queue<string> _tail = new();
        private Task? _drain;

        /// <summary>
        /// Starts recording and returns the clock time of the first recorded frame. ffmpeg prints progress about every
        /// 0.5 s, so the first line "frame=N" arrives after N frames: zero = received − N / fps.
        /// </summary>
        public async Task<double> StartAsync(string dir, string windowTitle, int frameRate, CancellationToken ct)
        {
            _process = runner.Start(ffmpegPath, FfmpegArgs.Record(windowTitle, frameRate), dir);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(FirstFrameTimeout);
            try
            {
                while (await _process.StdErr.WaitToReadAsync(timeout.Token))
                {
                    while (_process.StdErr.TryRead(out string? line))
                    {
                        Remember(line);
                        if (FfmpegArgs.ProgressFrame(line) is int frames)
                        {
                            double zero = clock.Now - (double)frames / frameRate;
                            _drain = Task.Run(() => DrainAsync(_process));
                            return zero;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // fall through: no frame within the timeout
            }
            await _process.WaitAsync(TimeSpan.Zero);
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("record_failed: ffmpeg recorded no frame within " + FirstFrameTimeout.TotalSeconds
                + " s (is KSP running windowed with the title in Broadcast.WindowTitle?): " + Tail());
        }

        /// <summary>True while the recording process runs.</summary>
        public bool IsRecording => _process is { HasExited: false };

        /// <summary>Stops gracefully with 'q' (finalizes the file); kills after 10 s.</summary>
        public async Task<int> StopAsync()
        {
            if (_process is null)
            {
                return 0;
            }
            try
            {
                await _process.WriteStdInAsync("q");
            }
            catch (IOException)
            {
                // already exited
            }
            int code = await _process.WaitAsync(TimeSpan.FromSeconds(10));
            if (_drain is not null)
            {
                await _drain;
            }
            _process.Dispose();
            _process = null;
            return code;
        }

        public string Tail()
        {
            lock (_tail)
            {
                return string.Join("\n", _tail);
            }
        }

        private async Task DrainAsync(IRunningProcess p)
        {
            await foreach (string line in p.StdErr.ReadAllAsync())
            {
                Remember(line);
            }
        }

        private void Remember(string line)
        {
            lock (_tail)
            {
                _tail.Enqueue(line);
                while (_tail.Count > 20)
                {
                    _tail.Dequeue();
                }
            }
        }
    }
}
