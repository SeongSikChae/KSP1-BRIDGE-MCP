using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;

namespace KspBridge.Server.Tests;

internal static class TestServers
{
    public static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "KspBridge.Server.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static ushort FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static AppConfiguration Config(string root, ushort port, string modUrl = "http://127.0.0.1:1") => new()
    {
        ContentRootPath = root,
        Port = port,
        ModUrl = modUrl,
        RequestTimeoutMs = 1000,
        ModulesDir = "modules",
    };

    /// <summary>실제 Kestrel(루프백)로 서버를 띄운다. MCP 스트리밍 알림을 그대로 시험하기 위해서다.</summary>
    public static async Task<WebApplication> StartAsync(AppConfiguration configuration)
    {
        WebApplication app = Program.CreateWebApplication(["--environment", "Production"], configuration,
            Path.Combine(configuration.ContentRootPath, "log"));
        await app.StartAsync();
        return app;
    }

    public static IReadOnlyCollection<string> Addresses(WebApplication app)
        => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToList();
}
