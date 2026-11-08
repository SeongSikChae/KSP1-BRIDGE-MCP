using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using System.Configuration;

namespace KspBridge.App
{
    internal class Program
    {
        public sealed class CmdMain
        {
            [Option("config", Required = true, HelpText = "config file path")]
            public string ConfigFilePath { get; set; } = null!;

            [Option("log", Required = true, HelpText = "log dir path")]
            public string LogDirPath { get; set; } = null!;
        }

        static async Task<int> Main(string[] args)
        {
            ParserResult<CmdMain> result = Parser.Default.ParseArguments<CmdMain>(args);
            return (int)await result.MapResult(
                cmdMain => RunAsync(args, cmdMain),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion()
                    ? ExitCode.Success
                    : ExitCode.InvalidArguments));
        }

        private static async Task<ExitCode> RunAsync(string[] args, CmdMain cmdMain)
        {
            AppConfiguration? appConfiguration;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new YamlDotNet.Serialization.Deserializer();
                appConfiguration = deserializer.Deserialize<AppConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath));
                ConfigurationValidator.Validate(appConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IHost host;
            try
            {
                host = CreateHostApplication(args, cmdMain, appConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (host)
            {
                ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await host.StartAsync();
                    await host.WaitForShutdownAsync();
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Host terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        private static IHost CreateHostApplication(string[] args, CmdMain cmdMain, AppConfiguration appConfiguration)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = appConfiguration.ContentRootPath,
            });

            // stdout is the MCP stdio channel: every console log line must go to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.Services.AddSerilog(configureLogger =>
            {
                configureLogger
                    .MinimumLevel.Information()
                    .Enrich.WithCaller()
                    .WriteTo.Console(
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose
                    )
                    .WriteTo.File(
                        new DirectoryInfo(cmdMain.LogDirPath).FullName,
                        "KspBridge.App.log",
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 12
                    );
            });

            builder.Services.AddSingleton(new KspBridgeClientOptions(TimeSpan.FromSeconds(appConfiguration.TimeoutSeconds)));
            builder.Services.AddHttpClient<KspBridgeClient>(client =>
            {
                client.BaseAddress = new Uri(appConfiguration.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // per-request timeouts in KspBridgeClient
            });

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<FlightTools>();

            return builder.Build();
        }
    }
}
