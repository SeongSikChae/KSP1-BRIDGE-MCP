using CommandLine;
using KspBridge.Server.Bridge;
using KspBridge.Server.Contracts;
using KspBridge.Server.Core;
using KspBridge.Server.Modules;
using ModelContextProtocol.AspNetCore;
using System.Configuration;

namespace KspBridge.Server
{
    public class Program
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

            WebApplication app;
            try
            {
                app = CreateWebApplication(args, appConfiguration, cmdMain.LogDirPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (app)
            {
                ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await app.StartAsync();
                    // 테스트 서버(CreateWebApplication)는 등록하지 않는다. 실제 실행만 deploy.ps1의 배포 대상이 된다.
                    using ServerRegistration? registration = ServerRegistration.Register(
                        appConfiguration.Port!.Value, app.Services.GetRequiredService<ModuleHost>().ModulesDir, logger);
                    await app.WaitForShutdownAsync();
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "WebApp terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        /// <summary>
        /// 설정으로 서비스와 HTTP 파이프라인을 구성한 앱을 만든다.
        /// 테스트는 <paramref name="configureBuilder"/>로 <c>UseTestServer</c>를 지정한다.
        /// </summary>
        public static WebApplication CreateWebApplication(string[] args, AppConfiguration appConfiguration, string logDirPath,
            Action<WebApplicationBuilder>? configureBuilder = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = appConfiguration.ContentRootPath,
            });

            builder.Logging.AddServerSerilog(logDirPath, "KspBridge.Server.log");
            builder.WebHost.ConfigureServerKestrel(appConfiguration.Port!.Value);

            builder.Services.AddSingleton(appConfiguration);
            builder.Services.AddProblemDetails();
            builder.Services.AddHealthChecks();
            builder.Services.AddSingleton<BridgeClient>();
            builder.Services.AddSingleton<IBridgeClient>(sp => sp.GetRequiredService<BridgeClient>());
            builder.Services.AddSingleton<ModuleHost>();
            builder.Services.AddSingleton<IServerHostInfo>(sp => sp.GetRequiredService<ModuleHost>());
            builder.Services.AddHostedService<ManifestWatcher>();
            builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "kspbridge", Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0" };
            }).WithHttpTransport(transport =>
            {
                // 서버가 먼저 list_changed를 보내려면 세션을 유지해야 한다(기본값 Stateless에서는 보낼 수 없음).
                transport.SessionMode = HttpServerSessionMode.Stateful;
                // HTTP 세션마다 옵션이 따로 만들어지므로, 모든 세션이 모듈 호스트의 공유 도구 컬렉션을 쓰게 한다.
                transport.ConfigureSessionOptions = (context, options, _) =>
                {
                    options.ToolCollection = context.RequestServices.GetRequiredService<ModuleHost>().Tools;
                    options.Capabilities ??= new();
                    options.Capabilities.Tools ??= new();
                    options.Capabilities.Tools.ListChanged = true;
                    return Task.CompletedTask;
                };
            });

            configureBuilder?.Invoke(builder);
            WebApplication app = builder.Build();

            // 예외 처리를 맨 앞에 두어 이후 모든 예외를 ProblemDetails로 응답한다.
            app.UseExceptionHandler();
            app.UseStatusCodePages();

            app.MapHealthChecks("/health");
            app.MapMcp("/mcp");

            return app;
        }
    }
}
