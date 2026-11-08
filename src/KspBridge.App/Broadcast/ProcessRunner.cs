using System.Diagnostics;
using System.Threading.Channels;

namespace KspBridge.App.Broadcast
{
    /// <summary>Started external process (ffmpeg). Abstracted so tests can check arguments and timing without ffmpeg.</summary>
    public interface IRunningProcess : IDisposable
    {
        /// <summary>stderr split on \r and \n (ffmpeg progress lines end with \r). Completes when the process exits.</summary>
        ChannelReader<string> StdErr { get; }

        bool HasExited { get; }

        Task WriteStdInAsync(string text);

        /// <summary>Exit code, or -1 after killing the process tree when it did not exit within the timeout.</summary>
        Task<int> WaitAsync(TimeSpan timeout);
    }

    public interface IProcessRunner
    {
        IRunningProcess Start(string exe, IReadOnlyList<string> args, string workDir);
    }

    /// <summary>Monotonic clock in seconds, injectable for tests.</summary>
    public interface IClock
    {
        double Now { get; }

        Task DelayUntil(double time, CancellationToken ct);
    }

    public sealed class SystemClock : IClock
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public double Now => _watch.Elapsed.TotalSeconds;

        public async Task DelayUntil(double time, CancellationToken ct)
        {
            double wait = time - Now;
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
        }
    }

    public sealed class ProcessRunner : IProcessRunner
    {
        public IRunningProcess Start(string exe, IReadOnlyList<string> args, string workDir)
        {
            ProcessStartInfo psi = new(exe)
            {
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }
            Process p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + exe);
            return new Running(p);
        }

        private sealed class Running : IRunningProcess
        {
            private readonly Process _p;
            private readonly Channel<string> _err = Channel.CreateUnbounded<string>();
            private readonly Task _pump;

            public Running(Process p)
            {
                _p = p;
                _pump = Task.Run(async () =>
                {
                    try
                    {
                        // stdout is unused; drain it so ffmpeg never blocks on a full pipe.
                        _ = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                        string? line;
                        while ((line = await p.StandardError.ReadLineAsync()) is not null)
                        {
                            _err.Writer.TryWrite(line);
                        }
                    }
                    finally
                    {
                        _err.Writer.TryComplete();
                    }
                });
            }

            public ChannelReader<string> StdErr => _err.Reader;

            public bool HasExited => _p.HasExited;

            public async Task WriteStdInAsync(string text)
            {
                await _p.StandardInput.WriteAsync(text);
                await _p.StandardInput.FlushAsync();
            }

            public async Task<int> WaitAsync(TimeSpan timeout)
            {
                using CancellationTokenSource cts = new(timeout);
                try
                {
                    await _p.WaitForExitAsync(cts.Token);
                    await _pump;
                    return _p.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    _p.Kill(entireProcessTree: true);
                    return -1;
                }
            }

            public void Dispose() => _p.Dispose();
        }
    }
}
