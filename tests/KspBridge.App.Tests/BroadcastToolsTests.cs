using System.Net;
using System.Text;
using System.Text.Json;
using KspBridge.App.Broadcast;
using Microsoft.Extensions.Logging.Abstractions;

namespace KspBridge.App.Tests
{
    internal sealed class FakeSpeech : ISpeechSynthesizer
    {
        public List<string> Texts { get; } = [];

        public Task<string> SynthesizeAsync(string text, string cacheDir, CancellationToken ct)
        {
            lock (Texts)
            {
                Texts.Add(text);
            }
            Directory.CreateDirectory(cacheDir);
            string path = Path.Combine(cacheDir, SpeechClient.CacheKey("v", text) + ".wav");
            File.WriteAllBytes(path, TestWav.Make(0.5));
            return Task.FromResult(path);
        }
    }

    [TestClass]
    public sealed class BroadcastToolsTests
    {
        private static HttpResponseMessage Ok(string data) =>
            new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"data\":" + data + "}", Encoding.UTF8, "application/json") };

        /// <summary>Records calls; writes a tiny game.wav so compose includes it.</summary>
        internal sealed class FakeGameAudio(FakeClock clock, GameAudioResult result, List<string> log) : IGameAudioRecorder
        {
            public double StartedAt { get; private set; }

            public GameAudioResult Start(string wavPath, string processName)
            {
                log.Add("game:start:" + processName);
                StartedAt = clock.Now + 0.25;
                if (result.Ok)
                {
                    File.WriteAllBytes(wavPath, TestWav.Make(0.1, 48000));
                }
                return result;
            }

            public Task StopAsync()
            {
                log.Add("game:stop");
                return Task.CompletedTask;
            }
        }

        private static (BroadcastTools Tools, BroadcastSession Session, RecordingHandler Http, FakeRunner Runner, FakeClock Clock, string Out) Create(
            bool configured = true, Func<FakeClock, IGameAudioRecorder>? game = null, bool gameAudio = true)
        {
            RecordingHandler http = new(req => req.RequestUri!.AbsolutePath switch
            {
                "/api/broadcast/arm" => Ok("""{"armed":true,"vessel":"누리호","body":"Kerbin"}"""),
                "/api/stage" => Ok("""{"stage":2}"""),
                "/api/broadcast/events" when req.RequestUri.Query == "?since=0" =>
                    Ok("""{"events":[{"seq":1,"type":"liftoff","vessel":"누리호","ageMs":500}],"last":1}"""),
                "/api/broadcast/events" => Ok("""{"events":[],"last":1}"""),
                _ => Ok("{}"),
            });
            KspBridgeClient client = new(new HttpClient(http) { BaseAddress = new Uri("http://127.0.0.1:8085/") }, new KspBridgeClientOptions(TimeSpan.FromSeconds(10)));
            FakeClock clock = new() { Now = 100 };
            FakeRunner runner = new() { OnStart = p => p.Err.Writer.TryWrite("frame=    0 fps=0.0") };
            string outDir = TestWav.TempDir();
            BroadcastSettings? settings = configured ? new BroadcastSettings("ffmpeg.exe", outDir, "Kerbal Space Program", 30, "Malgun Gothic", gameAudio, 0.8,
                Duck: new Ducking(0.05, 4, 10, 800), Comp: new Compressor(-12, 3, 5, 200)) : null;
            BroadcastSession session = new(client, settings, new FakeSpeech(), runner, clock, NullLogger<BroadcastSession>.Instance,
                game?.Invoke(clock));
            return (new BroadcastTools(session), session, http, runner, clock, outDir);
        }

        [TestMethod]
        public async Task GameSoundRecordedAlongsideVideoAndMixed()
        {
            List<string> log = [];
            var (tools, session, _, runner, _, _) = Create(game: c => new FakeGameAudio(c, new GameAudioResult(true, null), log));
            runner.OnStart = p => { log.Add("video:start"); p.Err.Writer.TryWrite("frame=    0 fps=0.0"); };

            using JsonDocument start = JsonDocument.Parse(await tools.StartBroadcast());
            Assert.IsTrue(start.RootElement.GetProperty("gameAudio").GetBoolean());
            await tools.StopBroadcast();
            await session.Composing!;

            CollectionAssert.AreEqual(new[] { "video:start", "game:start:KSP_x64", "game:stop", "video:start" }, log);   // last = compose run
            Assert.IsTrue(runner.Started[0].Process.StdIn.Contains("q"));
            string dir = runner.Started[1].WorkDir;
            CollectionAssert.Contains(runner.Started[1].Args.ToList(), "game.wav");
            string filter = File.ReadAllText(Path.Combine(dir, "filter.txt"));
            StringAssert.Contains(filter, "adelay=250|250,acompressor=");   // game started 0.25 s after the first frame
            StringAssert.Contains(filter, "sidechaincompress=threshold=0.05:ratio=4:attack=10:release=800[duck]");   // configured ducking
            StringAssert.Contains(filter, "acompressor=threshold=0.251189:ratio=3:attack=5:release=200,volume=0.8[game]");   // configured compressor
        }

        [TestMethod]
        public async Task GameSoundFailureStillIgnites()
        {
            List<string> log = [];
            var (tools, session, http, runner, _, _) = Create(game: c => new FakeGameAudio(c, new GameAudioResult(false, "game_process_not_found: KSP_x64"), log));

            using JsonDocument start = JsonDocument.Parse(await tools.StartBroadcast());
            Assert.IsTrue(start.RootElement.GetProperty("ignited").GetBoolean());
            Assert.IsFalse(start.RootElement.GetProperty("gameAudio").GetBoolean());
            StringAssert.Contains(tools.GetBroadcastStatus(), "game_process_not_found");
            await tools.StopBroadcast();
            await session.Composing!;

            CollectionAssert.DoesNotContain(log, "game:stop");
            CollectionAssert.DoesNotContain(runner.Started[1].Args.ToList(), "game.wav");
        }

        [TestMethod]
        public async Task GameSoundDisabledIsNotRecorded()
        {
            List<string> log = [];
            var (tools, _, _, _, _, _) = Create(game: c => new FakeGameAudio(c, new GameAudioResult(true, null), log), gameAudio: false);
            await tools.StartBroadcast();
            Assert.HasCount(0, log);
        }

        [TestMethod]
        public async Task CountdownIgnitesAtTZeroAndComposes()
        {
            var (tools, session, http, runner, clock, _) = Create();

            using JsonDocument start = JsonDocument.Parse(await tools.StartBroadcast(countdownSeconds: 10));
            Assert.IsTrue(start.RootElement.GetProperty("ignited").GetBoolean());
            Assert.AreEqual("누리호", start.RootElement.GetProperty("craft").GetString());
            Assert.AreEqual(110, clock.Now, 1e-9);                       // T-0 = recording zero + 10 s
            CollectionAssert.AreEqual(Enumerable.Range(0, 11).Select(i => 100.0 + i).ToArray(), clock.Delays.ToArray());
            Assert.AreEqual("/api/broadcast/arm", http.Requests[0].PathAndQuery);
            Assert.AreEqual("/api/stage", http.Requests[1].PathAndQuery);
            Assert.AreEqual("""{"altitudes":null}""", http.Requests[0].Body);

            StringAssert.StartsWith(await tools.StartBroadcast(), "error: busy");

            using JsonDocument stop = JsonDocument.Parse(await tools.StopBroadcast());
            Assert.AreEqual("composing", stop.RootElement.GetProperty("state").GetString());
            await session.Composing!;

            Assert.AreEqual("/api/broadcast/disarm", http.Requests[^1].PathAndQuery);
            Assert.IsTrue(http.Requests.Any(r => r.PathAndQuery == "/api/broadcast/events?since=0"));
            Assert.AreEqual(BroadcastSession.State.Done, session.Current);

            using JsonDocument status = JsonDocument.Parse(tools.GetBroadcastStatus());
            JsonElement root = status.RootElement;
            string dir = root.GetProperty("dir").GetString()!;
            Assert.AreEqual(Path.Combine(dir, "final.mp4"), root.GetProperty("output").GetString());
            var cues = root.GetProperty("cues").EnumerateArray().Select(c => (c.GetProperty("at").GetDouble(), c.GetProperty("text").GetString())).ToList();
            CollectionAssert.Contains(cues, (0.0, "10"));
                        CollectionAssert.Contains(cues, (9.0, "1"));
            Assert.IsFalse(cues.Any(c => c.Item2 == "발사!" || c.Item2 == ""), "T-0 has no spoken line or subtitle");
            CollectionAssert.Contains(cues, (9.5, "누리호가 발사되었습니다."));   // received at 10 s, ageMs 500

            // record process + compose process, both in the broadcast folder; compose files written there
            Assert.HasCount(2, runner.Started);
            Assert.AreEqual(dir, runner.Started[1].WorkDir);
            Assert.IsTrue(runner.Started[0].Process.StdIn.Contains("q"));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "subs.ass")));
            StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "filter.txt")), "amix=inputs=");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "tts", "000.wav")));
        }

        [TestMethod]
        public void EventLineWaitsForPlayingLine()
        {
            var spaced = BroadcastSession.Space(
            [
                (new Cue(20.18, "해머 오비터가 발사되었습니다.", "tts/012.wav"), 2.8),
                (new Cue(19.01, "1", "tts/010.wav", 0.15, "일", Fixed: true), 1.82),
                (new Cue(20.02, "발사!", "tts/011.wav", 0.6, "발사!", Fixed: true), 1.82),
                (new Cue(55.0, "고도 10킬로미터를 돌파했습니다.", "tts/013.wav"), 2.5),
            ]);

            CollectionAssert.AreEqual(new[] { "1", "발사!", "해머 오비터가 발사되었습니다.", "고도 10킬로미터를 돌파했습니다." },
                spaced.Select(s => s.Cue.Text).ToArray());
            Assert.AreEqual(20.02, spaced[1].Cue.At, 1e-9);                  // countdown keeps its second
            Assert.AreEqual(20.02 + 1.82 + 0.2, spaced[2].Cue.At, 1e-9);     // liftoff after "발사!"
            Assert.AreEqual(55.0, spaced[3].Cue.At, 1e-9);                   // no overlap: unchanged
        }

        [TestMethod]
        public async Task NotConfiguredAndNotRecording()
        {
            var (tools, _, http, _, _, _) = Create(configured: false);
            StringAssert.StartsWith(await tools.StartBroadcast(), "error: not_configured");
            StringAssert.StartsWith(tools.GetBroadcastStatus(), "error: not_configured");
            Assert.HasCount(0, http.Requests);

            var (tools2, _, _, _, _, _) = Create();
            StringAssert.StartsWith(await tools2.StopBroadcast(), "error: not_recording");
        }

        [TestMethod]
        public async Task RecorderDeathDuringCountdownBlocksIgnition()
        {
            var (tools, session, http, runner, clock, _) = Create();
            FakeClockHook(clock, () => runner.Started[0].Process.HasExited = true);

            StringAssert.StartsWith(await tools.StartBroadcast(), "error: record_failed: ffmpeg stopped during the countdown");
            Assert.IsFalse(http.Requests.Any(r => r.PathAndQuery == "/api/stage"));
            Assert.AreEqual("/api/broadcast/disarm", http.Requests[^1].PathAndQuery);
            Assert.AreEqual(BroadcastSession.State.Idle, session.Current);
        }

        [TestMethod]
        public async Task RepeatedPollAddsNoDuplicateCues()
        {
            var (tools, session, _, _, _, _) = Create();
            await tools.StartBroadcast();
            // Let the background poll take the since=0 event first (it runs on its own timer).
            for (int i = 0; i < 100 && !tools.GetBroadcastStatus().Contains("발사되었습니다"); i++)
            {
                await Task.Delay(20);
            }
            int before = JsonDocument.Parse(tools.GetBroadcastStatus()).RootElement.GetProperty("cues").GetArrayLength();
            await session.PollOnce(default);   // since=1 → empty list: no change, no throw
            Assert.AreEqual(before, JsonDocument.Parse(tools.GetBroadcastStatus()).RootElement.GetProperty("cues").GetArrayLength());
            StringAssert.Contains(await tools.StopBroadcast(), "composing");
        }

        private static void FakeClockHook(FakeClock clock, Action atFirstDelay) => clock.OnDelay = atFirstDelay;

        [TestMethod]
        public async Task RecordFailureDisarmsWithoutIgnition()
        {
            var (tools, session, http, runner, _, _) = Create();
            runner.OnStart = p => { p.Err.Writer.TryWrite("Can't find window"); p.Err.Writer.TryComplete(); };

            StringAssert.StartsWith(await tools.StartBroadcast(), "error: record_failed");
            Assert.IsFalse(http.Requests.Any(r => r.PathAndQuery == "/api/stage"));
            Assert.AreEqual("/api/broadcast/disarm", http.Requests[^1].PathAndQuery);
            Assert.AreEqual(BroadcastSession.State.Idle, session.Current);
        }
    }
}
