using System.Diagnostics;

namespace KspBridge.App.Broadcast
{
    /// <summary>Result of starting the game sound recording; Error is a short reason when it was not started.</summary>
    public sealed record GameAudioResult(bool Ok, string? Error);

    /// <summary>Records the game process sound into a WAV aligned with the broadcast clock.</summary>
    public interface IGameAudioRecorder
    {
        /// <summary>Clock time (IClock) of the first sample.</summary>
        double StartedAt { get; }

        GameAudioResult Start(string wavPath, string processName);

        Task StopAsync();
    }

    /// <summary>
    /// Process loopback recording of the game. The WAV always covers the elapsed time: when the source delivers less
    /// than the clock says (the game is silent, or capture stalls) the gap is filled with silence, keeping sync.
    /// </summary>
    public sealed class GameAudioRecorder(IClock clock, Func<int, (IAudioCaptureSource? Source, string? Error)> open) : IGameAudioRecorder
    {
        /// <summary>Gap tolerated before silence is inserted (0.1 s).</summary>
        internal const int Tolerance = ProcessLoopback.SampleRate / 10;

        private IAudioCaptureSource? _source;
        private WavWriter? _wav;
        private Thread? _thread;
        private volatile bool _stop;

        public double StartedAt { get; private set; }

        public static GameAudioRecorder ForProcessLoopback(IClock clock) =>
            new(clock, pid => ProcessLoopback.Open(pid, TimeSpan.FromSeconds(5)));

        public GameAudioResult Start(string wavPath, string processName)
        {
            Process? process = Process.GetProcessesByName(processName).FirstOrDefault();
            if (process is null)
            {
                return new GameAudioResult(false, $"game_process_not_found: {processName}");
            }
            var (source, error) = open(process.Id);
            if (source is null)
            {
                return new GameAudioResult(false, error ?? "capture_failed");
            }
            try
            {
                _wav = new WavWriter(wavPath, ProcessLoopback.SampleRate, ProcessLoopback.Channels);
                StartedAt = clock.Now;
                source.Start();
            }
            catch (Exception ex)
            {
                source.Dispose();
                _wav?.Dispose();
                _wav = null;
                return new GameAudioResult(false, "capture_start_failed: " + ex.Message);
            }
            _source = source;
            _stop = false;
            _thread = new Thread(Pump) { IsBackground = true, Name = "GameAudioRecorder" };   // MTA (default for new threads)
            _thread.Start();
            return new GameAudioResult(true, null);
        }

        public async Task StopAsync()
        {
            if (_thread is null)
            {
                return;
            }
            _stop = true;
            await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(5)));
            _thread = null;
        }

        /// <summary>One pump step: drain the source, then fill the gap behind the clock. Internal for tests.</summary>
        internal void Step()
        {
            _source!.Read(data => _wav!.Write(data));
            long expected = (long)((clock.Now - StartedAt) * ProcessLoopback.SampleRate);
            if (expected - _wav!.Frames > Tolerance)
            {
                _wav.WriteSilence(expected - _wav.Frames);
            }
        }

        /// <summary>Final step: fill up to the clock and close the file. Internal for tests.</summary>
        internal void Finish()
        {
            try
            {
                _source!.Read(data => _wav!.Write(data));
                long expected = (long)((clock.Now - StartedAt) * ProcessLoopback.SampleRate);
                if (expected > _wav!.Frames)
                {
                    _wav.WriteSilence(expected - _wav.Frames);
                }
            }
            finally
            {
                _source!.Dispose();
                _wav!.Dispose();
                _source = null;
                _wav = null;
            }
        }

        private void Pump()
        {
            try
            {
                while (!_stop)
                {
                    Step();
                    Thread.Sleep(10);
                }
            }
            catch (Exception)
            {
                // capture lost (device removed, game exited): keep what was recorded
            }
            finally
            {
                Finish();
            }
        }
    }

    /// <summary>16-bit PCM WAV writer; sizes are written on Dispose.</summary>
    public sealed class WavWriter : IDisposable
    {
        private readonly FileStream _file;
        private readonly int _blockAlign;
        private long _dataBytes;
        private byte[] _zeros = new byte[4096];

        public WavWriter(string path, int sampleRate, int channels)
        {
            _blockAlign = channels * 2;
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using BinaryWriter w = new(_file, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(sampleRate); w.Write(sampleRate * _blockAlign); w.Write((short)_blockAlign); w.Write((short)16);
            w.Write("data"u8); w.Write(0);
        }

        public long Frames => _dataBytes / _blockAlign;

        public void Write(ReadOnlySpan<byte> data)
        {
            _file.Write(data);
            _dataBytes += data.Length;
        }

        public void WriteSilence(long frames)
        {
            long bytes = frames * _blockAlign;
            while (bytes > 0)
            {
                int n = (int)Math.Min(bytes, _zeros.Length);
                _file.Write(_zeros, 0, n);
                _dataBytes += n;
                bytes -= n;
            }
        }

        public void Dispose()
        {
            using (BinaryWriter w = new(_file, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                _file.Seek(4, SeekOrigin.Begin);
                w.Write((int)Math.Min(int.MaxValue, 36 + _dataBytes));
                _file.Seek(40, SeekOrigin.Begin);
                w.Write((int)Math.Min(int.MaxValue, _dataBytes));
            }
            _file.Dispose();
        }
    }
}
