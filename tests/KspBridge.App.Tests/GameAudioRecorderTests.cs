using System.Diagnostics;
using KspBridge.App.Broadcast;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class GameAudioRecorderTests
    {
        /// <summary>Delivers a 0.5 s block of non-zero samples on the first Read, then nothing (the game goes silent).</summary>
        private sealed class OneBlockSource : IAudioCaptureSource
        {
            public readonly ManualResetEventSlim FirstRead = new();
            public bool Disposed;
            private bool _sent;

            public void Start()
            {
            }

            public void Read(Action<ReadOnlySpan<byte>> sink)
            {
                if (!_sent)
                {
                    byte[] block = new byte[ProcessLoopback.SampleRate / 2 * ProcessLoopback.BlockAlign];
                    Array.Fill(block, (byte)7);
                    sink(block);
                    _sent = true;
                }
                FirstRead.Set();
            }

            public void Dispose() => Disposed = true;
        }

        private static string CurrentProcess => Process.GetCurrentProcess().ProcessName;

        [TestMethod]
        public async Task SilentGapIsFilledToClockLength()
        {
            FakeClock clock = new() { Now = 100 };
            OneBlockSource source = new();
            GameAudioRecorder recorder = new(clock, _ => (source, null));
            string path = Path.Combine(TestWav.TempDir(), "game.wav");

            Assert.AreEqual(new GameAudioResult(true, null), recorder.Start(path, CurrentProcess));
            Assert.AreEqual(100, recorder.StartedAt);
            Assert.IsTrue(source.FirstRead.Wait(TimeSpan.FromSeconds(5)));
            clock.Now = 102;   // 2 s pass, the game makes no sound after the first 0.5 s
            await recorder.StopAsync();

            Assert.IsTrue(source.Disposed);
            Assert.AreEqual(2.0, WavInfo.Seconds(path), 0.001);
            byte[] wav = File.ReadAllBytes(path);
            int half = 44 + ProcessLoopback.SampleRate / 2 * ProcessLoopback.BlockAlign;
            Assert.AreEqual(7, wav[44]);                    // captured block
            Assert.AreEqual(7, wav[half - 1]);
            Assert.IsTrue(wav.Skip(half).All(b => b == 0)); // filled silence
        }

        [TestMethod]
        public void MissingProcessIsReportedWithoutFile()
        {
            string dir = TestWav.TempDir();
            GameAudioRecorder recorder = new(new FakeClock(), _ => throw new InvalidOperationException("must not open"));

            GameAudioResult r = recorder.Start(Path.Combine(dir, "game.wav"), "no_such_process_kspbridge");

            Assert.AreEqual(new GameAudioResult(false, "game_process_not_found: no_such_process_kspbridge"), r);
            Assert.IsFalse(File.Exists(Path.Combine(dir, "game.wav")));
        }

        [TestMethod]
        public void OpenFailureIsReported()
        {
            GameAudioRecorder recorder = new(new FakeClock(), _ => (null, "activate_failed: 0x88890008"));
            Assert.AreEqual(new GameAudioResult(false, "activate_failed: 0x88890008"),
                recorder.Start(Path.Combine(TestWav.TempDir(), "game.wav"), CurrentProcess));
        }
    }
}
