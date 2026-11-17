using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace KspBridge.Server.Core
{
    public static class ServerLoggingExtensions
    {
        /// <summary>
        /// Serilog로 Console과 일별 롤링 File(12개 보존)에 기록한다.
        /// </summary>
        public static ILoggingBuilder AddServerSerilog(this ILoggingBuilder logging, string logDirPath, string fileName)
        {
            logging.Services.AddSerilog(configureLogger =>
            {
                configureLogger
                    .MinimumLevel.Information()
                    .Enrich.WithCaller()
                    .WriteTo.Console(LogEventLevel.Information, CallerEnricherOutputTemplate.Default)
                    .WriteTo.File(
                        new DirectoryInfo(logDirPath).FullName,
                        fileName,
                        LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 12
                    );
            });
            return logging;
        }
    }
}
