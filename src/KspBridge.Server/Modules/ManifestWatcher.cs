using KspBridge.Shared;

namespace KspBridge.Server.Modules
{
    /// <summary>
    /// 시작할 때 모듈 세대를 읽고, 이후 <c>manifest.json</c> 변경을 감시해 500 ms 디바운스 뒤 다시 읽는다.
    /// </summary>
    public sealed class ManifestWatcher : BackgroundService
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
        private readonly ModuleHost host;
        private readonly ILogger<ManifestWatcher> logger;

        public ManifestWatcher(ModuleHost host, ILogger<ManifestWatcher> logger)
        {
            this.host = host;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Directory.CreateDirectory(host.ModulesDir);
            host.Reload();

            using SemaphoreSlim signal = new(0);
            using FileSystemWatcher watcher = new(host.ModulesDir, Protocol.ManifestFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Changed += (_, _) => signal.Release();
            watcher.Created += (_, _) => signal.Release();
            watcher.Renamed += (_, _) => signal.Release();
            watcher.EnableRaisingEvents = true;
            logger.LogInformation("Watching {Manifest}", Path.Combine(host.ModulesDir, Protocol.ManifestFileName));

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await signal.WaitAsync(stoppingToken);
                    // 복사가 끝날 때까지 이어지는 이벤트를 모은다.
                    await Task.Delay(Debounce, stoppingToken);
                    while (signal.CurrentCount > 0)
                        await signal.WaitAsync(stoppingToken);
                    host.Reload();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
