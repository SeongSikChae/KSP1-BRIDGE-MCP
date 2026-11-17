using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;

namespace KspBridge.Server.Core.Tests;

[TestClass]
public sealed class ServerKestrelExtensionsTests
{
    [TestMethod]
    public async Task ConfigureServerKestrel_BindsLoopbackOnly()
    {
        ushort port;
        using (TcpListener probe = new(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureServerKestrel(port);
        await using WebApplication app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync();

        List<string> addresses = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.ToList();
        CollectionAssert.AreEqual(new[] { $"http://127.0.0.1:{port}" }, addresses);

        using HttpClient http = new();
        Assert.AreEqual("ok", await http.GetStringAsync($"http://127.0.0.1:{port}/"));

        IPAddress? external = (await Dns.GetHostAddressesAsync(Dns.GetHostName()))
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        if (external is null)
            Assert.Inconclusive("No non-loopback IPv4 address to probe.");
        using TcpClient tcp = new();
        await Assert.ThrowsExactlyAsync<SocketException>(() => tcp.ConnectAsync(external, port));
    }
}
