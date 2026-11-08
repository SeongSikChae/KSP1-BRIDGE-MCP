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
            string? speechKey = null;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new YamlDotNet.Serialization.Deserializer();
                appConfiguration = deserializer.Deserialize<AppConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath));
                ConfigurationValidator.Validate(appConfiguration);
                if (appConfiguration.Broadcast is { } broadcast)
                {
                    ConfigurationValidator.Validate(broadcast);
                    if (broadcast.GameVolume < 0)
                    {
                        throw new InvalidOperationException("Broadcast.GameVolume must be 0 or more");
                    }
                    // sidechaincompress option ranges
                    if (broadcast.DuckThreshold is < 0.001 or > 1)
                    {
                        throw new InvalidOperationException("Broadcast.DuckThreshold must be between 0.001 and 1");
                    }
                    if (broadcast.DuckRatio is < 1 or > 20)
                    {
                        throw new InvalidOperationException("Broadcast.DuckRatio must be between 1 and 20");
                    }
                    if (broadcast.DuckAttack is < 0.01 or > 2000)
                    {
                        throw new InvalidOperationException("Broadcast.DuckAttack must be between 0.01 and 2000");
                    }
                    if (broadcast.DuckRelease is < 0.01 or > 9000)
                    {
                        throw new InvalidOperationException("Broadcast.DuckRelease must be between 0.01 and 9000");
                    }
                    // acompressor option ranges (threshold -60 dB ≈ its minimum 0.000976563)
                    if (broadcast.CompThreshold is < -60 or > 0)
                    {
                        throw new InvalidOperationException("Broadcast.CompThreshold must be between -60 and 0 (dB)");
                    }
                    if (broadcast.CompRatio is < 1 or > 20)
                    {
                        throw new InvalidOperationException("Broadcast.CompRatio must be between 1 and 20");
                    }
                    if (broadcast.CompAttack is < 0.01 or > 2000)
                    {
                        throw new InvalidOperationException("Broadcast.CompAttack must be between 0.01 and 2000");
                    }
                    if (broadcast.CompRelease is < 0.01 or > 9000)
                    {
                        throw new InvalidOperationException("Broadcast.CompRelease must be between 0.01 and 9000");
                    }
                    if (broadcast.Speech is { } speech)
                    {
                        ConfigurationValidator.Validate(speech);
                        if (appConfiguration.OsKms is null)
                        {
                            throw new InvalidOperationException("OsKms is required when Broadcast.Speech is set");
                        }
                        ConfigurationValidator.Validate(appConfiguration.OsKms);
                        speechKey = Broadcast.SecretBootstrap.UnprotectSpeechKey(appConfiguration, cmdMain.ConfigFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IHost host;
            try
            {
                host = CreateHostApplication(args, cmdMain, appConfiguration, speechKey);
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

        private static IHost CreateHostApplication(string[] args, CmdMain cmdMain, AppConfiguration appConfiguration, string? speechKey)
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
                client.Timeout = Timeout.InfiniteTimeSpan; // per-request timeouts in KspBridgeClient (wait_until runs up to 130s)
            });

            AddBroadcast(builder.Services, appConfiguration, speechKey);

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<FlightTools>()
                .WithTools<CraftTools>()
                .WithTools<CareerTools>()
                .WithTools<ScienceTools>()
                .WithTools<LaunchTools>()
                .WithTools<AscentTools>()
                .WithTools<DialogTools>()
                .WithTools<TechTools>()
                .WithTools<TriggerTools>()
                .WithTools<PartTestTools>()
                .WithTools<FlightLogTools>()
                .WithTools<BroadcastTools>();

            return builder.Build();
        }

        private static void AddBroadcast(IServiceCollection services, AppConfiguration cfg, string? speechKey)
        {
            Broadcast.BroadcastOptions? o = cfg.Broadcast;
            Broadcast.BroadcastSettings? settings = o is null ? null : new Broadcast.BroadcastSettings(
                o.FfmpegPath, Path.Combine(cfg.ContentRootPath, o.OutputDir), o.WindowTitle, o.FrameRate, o.Font,
                o.GameAudio, o.GameVolume, o.GameProcess,
                new Broadcast.Ducking(o.DuckThreshold, o.DuckRatio, o.DuckAttack, o.DuckRelease),
                o.GameCompress ? new Broadcast.Compressor(o.CompThreshold, o.CompRatio, o.CompAttack, o.CompRelease) : null);
            services.AddSingleton<Broadcast.IProcessRunner, Broadcast.ProcessRunner>();
            services.AddSingleton<Broadcast.IClock, Broadcast.SystemClock>();
            if (o?.Speech is { } speech && speechKey is not null)
            {
                services.AddSingleton(new Broadcast.SpeechSettings(speech.Region, speechKey, speech.Voice, speech.Style, speech.Rate));
                services.AddHttpClient<Broadcast.SpeechClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
            }
            services.AddSingleton(sp => new Broadcast.BroadcastSession(
                sp.GetRequiredService<KspBridgeClient>(),
                settings,
                speechKey is null ? null : sp.GetRequiredService<Broadcast.SpeechClient>(),
                sp.GetRequiredService<Broadcast.IProcessRunner>(),
                sp.GetRequiredService<Broadcast.IClock>(),
                sp.GetRequiredService<ILogger<Broadcast.BroadcastSession>>(),
                OperatingSystem.IsWindows() ? Broadcast.GameAudioRecorder.ForProcessLoopback(sp.GetRequiredService<Broadcast.IClock>()) : null));
        }
    }
}
