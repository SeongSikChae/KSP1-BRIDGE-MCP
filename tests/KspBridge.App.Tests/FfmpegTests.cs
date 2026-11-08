using System.Threading.Channels;
using KspBridge.App.Broadcast;

namespace KspBridge.App.Tests
{
    internal sealed class FakeClock : IClock
    {
        public double Now { get; set; }

        public List<double> Delays { get; } = [];

        public Action? OnDelay { get; set; }

        public Task DelayUntil(double time, CancellationToken ct)
        {
            Delays.Add(time);
            OnDelay?.Invoke();
            if (time > Now)
            {
                Now = time;
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeProcess : IRunningProcess
    {
        public Channel<string> Err { get; } = Channel.CreateUnbounded<string>();
        public List<string> StdIn { get; } = [];
        public int ExitCode { get; set; }

        public ChannelReader<string> StdErr => Err.Reader;

        public bool HasExited { get; set; }

        public Task WriteStdInAsync(string text)
        {
            StdIn.Add(text);
            Err.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public Task<int> WaitAsync(TimeSpan timeout)
        {
            Err.Writer.TryComplete();
            return Task.FromResult(ExitCode);
        }

        public void Dispose()
        {
        }
    }

    internal sealed class FakeRunner : IProcessRunner
    {
        public List<(string Exe, IReadOnlyList<string> Args, string WorkDir, FakeProcess Process)> Started { get; } = [];
        public Action<FakeProcess>? OnStart { get; set; }

        public IRunningProcess Start(string exe, IReadOnlyList<string> args, string workDir)
        {
            FakeProcess p = new();
            Started.Add((exe, args, workDir, p));
            OnStart?.Invoke(p);
            return p;
        }
    }

    [TestClass]
    public sealed class FfmpegTests
    {
        [TestMethod]
        public void RecordArgs()
        {
            CollectionAssert.AreEqual(
                new[] { "-y", "-f", "lavfi",
                        "-i", "gfxcapture=window_title=^Kerbal Space Program$:max_framerate=30:capture_cursor=0,hwdownload,format=bgra",
                        "-r", "30", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-crf", "18", "-pix_fmt", "yuv420p", "raw.mkv" },
                FfmpegArgs.Record("Kerbal Space Program", 30));
            // regex metacharacters and filter-graph separators are escaped
            StringAssert.Contains(FfmpegArgs.Record("KSP (x64): 1.12", 30)[4], @"window_title=^KSP \\(x64\\)\: 1\\.12$:");
        }

        [TestMethod]
        public void ComposeArgsAndFilter()
        {
            var (args, filter) = FfmpegArgs.Compose(
            [
                new Cue(1.0, "발사 10초 전.", "tts/a.wav"),
                new Cue(2.0, "9", "tts/b.wav", 0.15),
                new Cue(12.3456, "고도", null, 0),
            ]);

            CollectionAssert.AreEqual(
                new[] { "-y", "-i", "raw.mkv", "-i", "tts/a.wav", "-i", "tts/b.wav",
                        "-f", "lavfi", "-t", "0.15", "-i", "sine=frequency=1000:sample_rate=24000",
                        "-/filter_complex", "filter.txt", "-map", "[vout]", "-map", "[aout]", "-c:a", "aac", "-b:a", "160k", "-shortest",
                        "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "final.mp4" },
                args);
            StringAssert.Contains(filter, "[1:a]aresample=24000,aformat=channel_layouts=mono,adelay=1000|1000[a1]");
            StringAssert.Contains(filter, "[2:a]aresample=24000,aformat=channel_layouts=mono,adelay=2000|2000[a2]");
            StringAssert.Contains(filter, "[3:a]aresample=24000,aformat=channel_layouts=mono,adelay=2000|2000[a3]");
            StringAssert.Contains(filter, "[0:v]subtitles=subs.ass[vout]");
            StringAssert.Contains(filter, "[a1][a2][a3]amix=inputs=3:normalize=0:dropout_transition=0,apad[mix]");
            StringAssert.Contains(filter, "[mix]alimiter=limit=0.891:level=0[aout]");
            Assert.IsFalse(args.Any(a => a.Contains(':') && a.Contains('\\')), "relative paths only");
        }

        [TestMethod]
        public void ComposeWithGameSoundDucksUnderNarration()
        {
            var (args, filter) = FfmpegArgs.Compose([new Cue(2.0, "9", "tts/001.wav", 0.15)], new GameTrack("game.wav", 1.5, 0.8));

            Assert.AreEqual("-i", args[args.IndexOf("game.wav") - 1]);   // input #3 after raw.mkv, tts and beep
            StringAssert.Contains(filter, "[3:a]aresample=24000,aformat=channel_layouts=mono,adelay=1500|1500,volume=0.8[game]");
            StringAssert.Contains(filter, "[a1][a2]amix=inputs=2:normalize=0:dropout_transition=0,apad[nar]");
            StringAssert.Contains(filter, "[nar]asplit=2[nar1][key]");
            StringAssert.Contains(filter, "[game][key]sidechaincompress=threshold=0.02:ratio=8:attack=20:release=400[duck]");
            StringAssert.Contains(filter, "[duck][nar1]amix=inputs=2:normalize=0:dropout_transition=0[mix]");
            StringAssert.Contains(filter, "[mix]alimiter=limit=0.891:level=0[aout]");
            CollectionAssert.Contains(args, "[aout]");
        }

        [TestMethod]
        public void ComposeCompressesGameSoundBeforeVolume()
        {
            var (_, filter) = FfmpegArgs.Compose([], new GameTrack("game.wav", 0.5, 2, Comp: new Compressor()));
            // -18 dB = 0.125893 linear
            StringAssert.Contains(filter,
                "[1:a]aresample=24000,aformat=channel_layouts=mono,adelay=500|500,acompressor=threshold=0.125893:ratio=4:attack=5:release=200,volume=2[game]");
            StringAssert.Contains(filter, "[game]apad[mix]");
            StringAssert.Contains(filter, "[mix]alimiter=limit=0.891:level=0[aout]");
        }

        [TestMethod]
        public void ComposeUsesConfiguredDucking()
        {
            var (_, filter) = FfmpegArgs.Compose([new Cue(2.0, "9", "tts/001.wav", 0)],
                new GameTrack("game.wav", 0, 1, new Ducking(0.005, 1, 0.5, 1500)));
            StringAssert.Contains(filter, "[game][key]sidechaincompress=threshold=0.005:ratio=1:attack=0.5:release=1500[duck]");
        }

        [TestMethod]
        public void ComposeGameSoundStartedBeforeVideoIsTrimmed()
        {
            var (_, filter) = FfmpegArgs.Compose([], new GameTrack("game.wav", -0.3, 1));
            StringAssert.Contains(filter, "[1:a]aresample=24000,aformat=channel_layouts=mono,atrim=start=0.3,asetpts=PTS-STARTPTS,volume=1[game]");
            StringAssert.Contains(filter, "[game]apad[mix]");
        }

        [TestMethod]
        public void ComposeWithoutAudio()
        {
            var (args, filter) = FfmpegArgs.Compose([new Cue(3, "자막만")]);
            Assert.IsFalse(args.Contains("[aout]"));
            Assert.AreEqual("[0:v]subtitles=subs.ass[vout]", filter);
        }

        [TestMethod]
        [DataRow("frame=   15 fps= 30 q=0.0 size=  0kB time=00:00:00.50", 15)]
        [DataRow("frame=0 fps=0.0", 0)]
        [DataRow("Input #0, gdigrab, from 'title=Kerbal Space Program':", null)]
        public void ProgressFrame(string line, int? expected) => Assert.AreEqual(expected, FfmpegArgs.ProgressFrame(line));

        [TestMethod]
        public async Task RecorderZeroIsBackdatedByFrames()
        {
            FakeClock clock = new() { Now = 100 };
            FakeRunner runner = new()
            {
                OnStart = p =>
                {
                    p.Err.Writer.TryWrite("Input #0, gdigrab");
                    p.Err.Writer.TryWrite("frame=   15 fps= 30");
                },
            };
            FfmpegRecorder recorder = new(runner, clock, "ffmpeg.exe");

            double zero = await recorder.StartAsync("D:\\out\\b1", "Kerbal Space Program", 30, default);

            Assert.AreEqual(99.5, zero, 1e-9);
            Assert.AreEqual(("ffmpeg.exe", "D:\\out\\b1"), (runner.Started[0].Exe, runner.Started[0].WorkDir));
            Assert.AreEqual(0, await recorder.StopAsync());
            CollectionAssert.AreEqual(new[] { "q" }, runner.Started[0].Process.StdIn);
        }

        [TestMethod]
        public async Task RecorderFailsWithoutFrames()
        {
            FakeRunner runner = new() { OnStart = p => { p.Err.Writer.TryWrite("Can't find window 'x'"); p.Err.Writer.TryComplete(); } };
            FfmpegRecorder recorder = new(runner, new FakeClock(), "ffmpeg.exe");

            InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => recorder.StartAsync("d", "x", 30, default));
            StringAssert.StartsWith(ex.Message, "record_failed");
            StringAssert.Contains(ex.Message, "Can't find window 'x'");
        }
    }
}
