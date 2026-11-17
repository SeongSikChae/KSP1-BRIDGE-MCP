using KspBridge.Server.Bridge;
using KspBridge.Server.Contracts;
using KspBridge.Shared;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KspBridge.Server.Tests;

[TestClass]
public sealed class BridgeClientTests
{
    private static BridgeClient Client(ushort port, int timeoutMs = 1000)
        => new(new HttpClient(), new Uri($"http://127.0.0.1:{port}"), TimeSpan.FromMilliseconds(timeoutMs));

    [TestMethod]
    public async Task NoListener_ReturnsModUnreachable_Quickly()
    {
        ushort port = TestServers.FreePort();
        using BridgeClient client = Client(port);

        Stopwatch watch = Stopwatch.StartNew();
        BridgeResult result = await client.CallAsync("core", "status");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(ErrorCodes.ModUnreachable, result.ErrorCode);
        Assert.IsLessThan(3000L, watch.ElapsedMilliseconds);
    }

    [TestMethod]
    public async Task SilentServer_TimesOut_WithModUnreachable()
    {
        using TcpListener silent = new(IPAddress.Loopback, 0);
        silent.Start();
        ushort port = (ushort)((IPEndPoint)silent.LocalEndpoint).Port;
        using BridgeClient client = Client(port, timeoutMs: 500);

        Stopwatch watch = Stopwatch.StartNew();
        BridgeResult result = await client.CallAsync("core", "status");

        Assert.AreEqual(ErrorCodes.ModUnreachable, result.ErrorCode);
        StringAssert.Contains(result.ErrorMessage, "did not respond");
        Assert.IsLessThan(3000L, watch.ElapsedMilliseconds);
    }

    [TestMethod]
    public async Task ProtocolMismatchHeader_ReturnsProtocolMismatch()
    {
        using FakeMod mod = new(Protocol.Version + 1, "{\"ok\":true,\"data\":{}}");
        using BridgeClient client = Client(mod.Port);

        BridgeResult result = await client.CallAsync("core", "status");

        Assert.AreEqual(ErrorCodes.ProtocolMismatch, result.ErrorCode);
    }

    [TestMethod]
    public async Task LoaderStatus_IsReadable_AcrossProtocolVersions()
    {
        using FakeMod mod = new(Protocol.Version + 1, "{\"ok\":true,\"data\":{\"protocol\":" + (Protocol.Version + 1) + "}}");
        using BridgeClient client = Client(mod.Port);

        BridgeResult result = await client.GetLoaderStatusAsync();

        Assert.IsTrue(result.Ok, result.ErrorMessage);
        Assert.AreEqual(Protocol.Version + 1, result.Data!["protocol"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task ModError_IsPassedThrough()
    {
        using FakeMod mod = new(Protocol.Version, "{\"ok\":false,\"error\":{\"code\":\"not_ready\",\"message\":\"loading\"}}");
        using BridgeClient client = Client(mod.Port);

        BridgeResult result = await client.CallAsync("core", "status");

        Assert.AreEqual(ErrorCodes.NotReady, result.ErrorCode);
        Assert.AreEqual("loading", result.ErrorMessage);
        Assert.AreEqual(Protocol.Version.ToString(), mod.LastRequestProtocol);
    }

    [TestMethod]
    public async Task Success_ReturnsData()
    {
        using FakeMod mod = new(Protocol.Version, "{\"ok\":true,\"data\":{\"scene\":\"FLIGHT\"}}");
        using BridgeClient client = Client(mod.Port);

        BridgeResult result = await client.CallAsync("core", "status");

        Assert.IsTrue(result.Ok);
        Assert.AreEqual("FLIGHT", result.Data!["scene"]!.GetValue<string>());
        Assert.AreEqual("/api/core/status", mod.LastPath);
    }

    /// <summary>한 가지 응답만 돌려주는 가짜 Mod HTTP 엔드포인트.</summary>
    private sealed class FakeMod : IDisposable
    {
        private readonly HttpListener listener = new();

        public FakeMod(int protocolVersion, string body)
        {
            Port = TestServers.FreePort();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); } catch (Exception) { return; }
                    LastPath = ctx.Request.Url!.AbsolutePath;
                    LastRequestProtocol = ctx.Request.Headers[Protocol.VersionHeader];
                    ctx.Response.Headers[Protocol.VersionHeader] = protocolVersion.ToString();
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        public ushort Port { get; }
        public string? LastPath { get; private set; }
        public string? LastRequestProtocol { get; private set; }

        public void Dispose() => listener.Close();
    }
}
