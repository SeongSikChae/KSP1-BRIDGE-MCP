using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KspBridge.App.Broadcast
{
    /// <summary>Runtime broadcast settings resolved from the config (OutputDir absolute).</summary>
    public sealed record BroadcastSettings(string FfmpegPath, string OutputDir, string WindowTitle, int FrameRate, string Font,
        bool GameAudio = true, double GameVolume = 1.0, string GameProcess = "KSP_x64", Ducking? Duck = null,
        Compressor? Comp = null);

    /// <summary>
    /// One broadcast at a time: records the KSP window with ffmpeg, runs the countdown (ignition at T-0), collects
    /// narration events from the mod while recording, and composes the final MP4 in the background after stop.
    /// </summary>
    public sealed class BroadcastSession(
        KspBridgeClient client,
        BroadcastSettings? settings,
        ISpeechSynthesizer? speech,
        IProcessRunner runner,
        IClock clock,
        ILogger<BroadcastSession> logger,
        IGameAudioRecorder? gameAudio = null) : IAsyncDisposable
    {
        /// <summary>
        /// MCP server shutdown (client closed stdio): finish an active recording so ffmpeg does not keep recording
        /// after the server is gone. raw.mkv stays playable; compose is not started.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_recorder is null || Current is not (State.Recording or State.Countdown))
            {
                return;
            }
            _pollCts?.Cancel();
            try
            {
                await client.SendAsync(HttpMethod.Post, "api/broadcast/disarm", null, CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // best effort
            }
            await _recorder.StopAsync();
            await StopGameAudio();
            SetState(State.Idle);
            logger.LogInformation("Broadcast {Dir} recording stopped at shutdown (not composed)", _dir);
        }

        public enum State { Idle, Countdown, Recording, Composing, Done, Failed }

        internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        private static readonly JsonSerializerOptions JsonOut = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly object _gate = new();
        private readonly List<Cue> _cues = [];
        private readonly List<string> _ttsFailures = [];
        private State _state = State.Idle;
        private string? _dir;
        private string? _craft;
        private double _zero;
        private long _last;
        private int _id;
        private FfmpegRecorder? _recorder;
        private CancellationTokenSource? _pollCts;
        private Task? _poll;
        private Task? _compose;
        private string? _output;
        private string? _error;
        private bool _gameAudioOn;
        private string? _gameAudioError;

        public State Current
        {
            get { lock (_gate) { return _state; } }
        }

        internal Task? Composing => _compose;

        public async Task<string> StartAsync(string? craftName, int countdownSeconds, double[]? altitudes, CancellationToken ct)
        {
            if (settings is null)
            {
                return NotConfigured;
            }
            lock (_gate)
            {
                if (_state is State.Countdown or State.Recording or State.Composing)
                {
                    return $"error: busy: a broadcast is {_state.ToString().ToLowerInvariant()} (stop_broadcast / get_broadcast_status)";
                }
                _state = State.Countdown;
                _cues.Clear();
                _ttsFailures.Clear();
                _gameAudioOn = false;
                _gameAudioError = null;
                _output = null;
                _error = null;
                _last = 0;
            }

            try
            {
                string arm = await client.SendAsync(HttpMethod.Post, "api/broadcast/arm", new { altitudes }, ct);
                if (arm.StartsWith("error:", StringComparison.Ordinal))
                {
                    SetState(State.Idle);
                    return arm;
                }
                using (JsonDocument doc = JsonDocument.Parse(arm))
                {
                    string? vessel = doc.RootElement.TryGetProperty("vessel", out JsonElement v) ? v.GetString() : null;
                    _craft = string.IsNullOrWhiteSpace(craftName) ? vessel ?? "기체" : craftName.Trim();
                }

                List<(int T, string Text, string Say, double Beep)> lines = Narration.Countdown(_craft, countdownSeconds).ToList();
                string?[] audio = await Task.WhenAll(lines.Select(l => SynthesizeOrNull(l.Say, ct)));

                _dir = Path.Combine(settings.OutputDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeName(_craft)}");
                Directory.CreateDirectory(_dir);
                _recorder = new FfmpegRecorder(runner, clock, settings.FfmpegPath);
                _zero = await _recorder.StartAsync(_dir, settings.WindowTitle, settings.FrameRate, ct);
                StartGameAudio();

                string? stage = null;
                double t0 = clock.Now + countdownSeconds;
                for (int i = 0; i < lines.Count; i++)
                {
                    await clock.DelayUntil(t0 - lines[i].T, ct);
                    AddCue(new Cue(clock.Now - _zero, lines[i].Text, audio[i], lines[i].Beep, lines[i].Say, Fixed: true));
                    if (lines[i].T == 0)
                    {
                        // Never ignite without a running recording (window minimized, ffmpeg error, ...).
                        if (!_recorder.IsRecording)
                        {
                            throw new InvalidOperationException("record_failed: ffmpeg stopped during the countdown, not ignited: " + _recorder.Tail());
                        }
                        stage = await client.SendAsync(HttpMethod.Post, "api/stage", null, CancellationToken.None);
                    }
                }

                _pollCts = new CancellationTokenSource();
                _poll = Task.Run(() => PollLoop(_pollCts.Token));
                SetState(State.Recording);
                bool ignited = stage is not null && !stage.StartsWith("error:", StringComparison.Ordinal);
                logger.LogInformation("Broadcast {Dir} started, ignited={Ignited}", _dir, ignited);
                return Json(new { dir = _dir, craft = _craft, ignited, stage = ignited ? Raw(stage!) : (object?)stage, cues = CueCount(), ttsFailures = TtsFailures(), gameAudio = _gameAudioOn, gameAudioError = _gameAudioError });
            }
            catch (Exception ex)
            {
                // Before or during the countdown: no ignition happened (or it was the last step). Clean up and report.
                if (_recorder is not null)
                {
                    await _recorder.StopAsync();
                    _recorder = null;
                    await StopGameAudio();
                }
                await client.SendAsync(HttpMethod.Post, "api/broadcast/disarm", null, CancellationToken.None);
                SetState(State.Idle);
                return ex is OperationCanceledException ? "error: cancelled: countdown aborted before ignition" : "error: " + ex.Message;
            }
        }

        public async Task<string> StopAsync()
        {
            if (settings is null)
            {
                return NotConfigured;
            }
            lock (_gate)
            {
                if (_state != State.Recording)
                {
                    return $"error: not_recording: state is {_state.ToString().ToLowerInvariant()}";
                }
                _state = State.Composing;
                _id++;
            }
            _pollCts!.Cancel();
            try
            {
                await _poll!;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Broadcast event poll ended with: {Message}", ex.Message);
            }
            catch (OperationCanceledException)
            {
            }
            await PollOnce(CancellationToken.None);   // events up to the stop
            await client.SendAsync(HttpMethod.Post, "api/broadcast/disarm", null, CancellationToken.None);
            int code = await _recorder!.StopAsync();
            await StopGameAudio();   // after the video stops: the game sound covers the whole video
            double length = clock.Now - _zero;
            logger.LogInformation("Broadcast {Dir} recording stopped (ffmpeg {Code}), composing", _dir, code);
            _compose = Task.Run(ComposeAsync);
            return Json(new { id = _id, dir = _dir, state = "composing", seconds = Math.Round(length, 1), cues = CueCount() });
        }

        public string Status()
        {
            if (settings is null)
            {
                return NotConfigured;
            }
            lock (_gate)
            {
                return Json(new
                {
                    state = _state.ToString().ToLowerInvariant(),
                    id = _id,
                    dir = _dir,
                    craft = _craft,
                    elapsed = _state is State.Recording ? Math.Round(clock.Now - _zero, 1) : (double?)null,
                    cues = _cues.Where(c => c.Text.Length > 0).Select(c => new { at = Math.Round(c.At, 2), text = c.Text }).ToList(),
                    ttsFailures = _ttsFailures.ToList(),
                    gameAudio = _gameAudioOn,
                    gameAudioError = _gameAudioError,
                    output = _output,
                    error = _error,
                });
            }
        }

        private async Task PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await PollOnce(ct);
                await Task.Delay(PollInterval, ct);
            }
        }

        internal async Task PollOnce(CancellationToken ct)
        {
            string json;
            try
            {
                json = await client.SendAsync(HttpMethod.Get, $"api/broadcast/events?since={_last}", null, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Broadcast event poll failed: {Message}", ex.Message);
                return;
            }
            double received = clock.Now - _zero;
            if (json.StartsWith("error:", StringComparison.Ordinal))
            {
                return;
            }
            try
            {
                AddEvents(json, received);
            }
            catch (Exception ex)
            {
                // A malformed response must not end the poll loop (later events would be lost).
                logger.LogWarning("Broadcast events response ignored: {Message}", ex.Message);
            }
        }

        private void AddEvents(string json, double received)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement e in events.EnumerateArray())
            {
                if (!e.TryGetProperty("seq", out JsonElement seqElement))
                {
                    continue;
                }
                long seq = seqElement.GetInt64();
                if (seq <= _last)
                {
                    continue;
                }
                _last = seq;
                string? text = Narration.ForEvent(e, _craft);
                if (text is not null)
                {
                    double age = e.TryGetProperty("ageMs", out JsonElement a) ? a.GetDouble() / 1000 : 0;
                    AddCue(new Cue(Math.Max(0, received - age), text));
                }
            }
        }

        private async Task ComposeAsync()
        {
            string dir = _dir!;
            try
            {
                List<Cue> cues;
                lock (_gate)
                {
                    cues = _cues.OrderBy(c => c.At).ToList();
                }
                // Narration audio copied into the broadcast folder: ffmpeg runs there with relative paths only.
                Directory.CreateDirectory(Path.Combine(dir, "tts"));
                List<(Cue Cue, double Duration)> timed = [];
                for (int i = 0; i < cues.Count; i++)
                {
                    Cue c = cues[i];
                    string? audio = c.Audio ?? await SynthesizeOrNull(c.Say ?? Narration.Spoken(c.Text), CancellationToken.None);
                    string? local = null;
                    double duration = 0;
                    if (audio is not null)
                    {
                        local = $"tts/{i:000}.wav";
                        File.Copy(audio, Path.Combine(dir, local), overwrite: true);
                        duration = WavInfo.Seconds(audio);
                    }
                    Cue placed = c with { Audio = local };
                    cues[i] = placed;
                    timed.Add((placed, duration));
                }
                timed = Space(timed);
                cues = timed.Select(t => t.Cue).ToList();
                await File.WriteAllTextAsync(Path.Combine(dir, FfmpegArgs.Subtitles), AssWriter.Build(timed, settings!.Font), new UTF8Encoding(false));
                GameTrack? game = _gameAudioOn && File.Exists(Path.Combine(dir, GameWav))
                    ? new GameTrack(GameWav, gameAudio!.StartedAt - _zero, settings.GameVolume, settings.Duck, settings.Comp)
                    : null;
                var (args, filter) = FfmpegArgs.Compose(cues, game);
                await File.WriteAllTextAsync(Path.Combine(dir, FfmpegArgs.FilterScript), filter, new UTF8Encoding(false));

                using IRunningProcess p = runner.Start(settings.FfmpegPath, args, dir);
                Queue<string> tail = new();
                Task drain = Task.Run(async () =>
                {
                    await foreach (string line in p.StdErr.ReadAllAsync())
                    {
                        tail.Enqueue(line);
                        if (tail.Count > 20)
                        {
                            tail.Dequeue();
                        }
                    }
                });
                int code = await p.WaitAsync(TimeSpan.FromHours(3));
                await drain;
                lock (_gate)
                {
                    if (code == 0)
                    {
                        _output = Path.Combine(dir, FfmpegArgs.Final);
                        _state = State.Done;
                    }
                    else
                    {
                        _error = $"ffmpeg exit {code}: " + string.Join("\n", tail);
                        _state = State.Failed;
                    }
                }
                logger.LogInformation("Broadcast {Dir} compose finished: {Code}", dir, code);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Broadcast compose failed");
                lock (_gate)
                {
                    _error = ex.Message;
                    _state = State.Failed;
                }
            }
        }

        internal const string GameWav = "game.wav";

        /// <summary>Starts the game sound recording right after the first video frame; a failure only leaves a warning.</summary>
        private void StartGameAudio()
        {
            if (!settings!.GameAudio || gameAudio is null)
            {
                return;
            }
            GameAudioResult r = gameAudio.Start(Path.Combine(_dir!, GameWav), settings.GameProcess);
            _gameAudioOn = r.Ok;
            _gameAudioError = r.Error;
            if (!r.Ok)
            {
                logger.LogWarning("Game sound not recorded: {Error}", r.Error);
            }
        }

        private async Task StopGameAudio()
        {
            if (_gameAudioOn && gameAudio is not null)
            {
                await gameAudio.StopAsync();
            }
        }

        /// <summary>Gap kept between one narration line and the next event line.</summary>
        internal const double LineGap = 0.2;

        /// <summary>
        /// Moves event lines that would start while the previous narration is still playing to just after it
        /// (e.g. the liftoff line arrives 0.2 s after "발사!"). Countdown lines (Fixed) keep their second.
        /// </summary>
        internal static List<(Cue Cue, double Duration)> Space(IReadOnlyList<(Cue Cue, double Duration)> timed)
        {
            List<(Cue Cue, double Duration)> result = [];
            double busyUntil = double.MinValue;
            foreach ((Cue cue, double duration) in timed.OrderBy(t => t.Cue.At))
            {
                Cue placed = !cue.Fixed && cue.At < busyUntil ? cue with { At = busyUntil } : cue;
                result.Add((placed, duration));
                if (placed.Audio is not null && duration > 0)
                {
                    busyUntil = Math.Max(busyUntil, placed.At + duration + LineGap);
                }
            }
            return result.OrderBy(t => t.Cue.At).ToList();
        }

        private async Task<string?> SynthesizeOrNull(string text, CancellationToken ct)
        {
            if (speech is null || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            try
            {
                return await speech.SynthesizeAsync(text, Path.Combine(settings!.OutputDir, "tts-cache"), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Speech failed for a line: {Message}", ex.Message);
                lock (_gate)
                {
                    _ttsFailures.Add($"{text}: {ex.Message}");
                }
                return null;
            }
        }

        private void AddCue(Cue cue)
        {
            lock (_gate)
            {
                _cues.Add(cue);
            }
        }

        private int CueCount()
        {
            lock (_gate)
            {
                return _cues.Count;
            }
        }

        private List<string> TtsFailures()
        {
            lock (_gate)
            {
                return _ttsFailures.ToList();
            }
        }

        private void SetState(State s)
        {
            lock (_gate)
            {
                _state = s;
            }
        }

        private const string NotConfigured = "error: not_configured: add the Broadcast section to the KspBridge.App config";

        private static string SafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string s = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
            return s.Length > 40 ? s[..40] : s;
        }

        private static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static string Json(object o) => JsonSerializer.Serialize(o, JsonOut);
    }
}
