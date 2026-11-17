using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net;

namespace KspBridge.Server.Tests;

[TestClass]
public sealed class McpEndpointTests
{
    private string root = null!;

    [TestInitialize]
    public void Init() => root = TestServers.NewTempDir();

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public async Task Health_Returns200()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        using HttpClient http = new();
        HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{port}/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        using HttpClient http = new();
        HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{port}/does-not-exist");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Deploy_AddsToolsToOpenSession_WithListChangedNotification()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));

        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        McpClientOptions options = new();
        options.Handlers.NotificationHandlers =
        [
            new(NotificationMethods.ToolListChangedNotification, (_, _) => { notified.TrySetResult(); return ValueTask.CompletedTask; }),
        ];
        await using McpClient client = await McpClient.CreateAsync(
            new HttpClientTransport(new() { Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"), TransportMode = HttpTransportMode.StreamableHttp }),
            options);

        Assert.IsTrue(client.ServerCapabilities.Tools?.ListChanged);
        CollectionAssert.DoesNotContain((await client.ListToolsAsync()).Select(t => t.Name).ToList(), "fixture_echo");

        // 배포와 같은 순서: 모듈 복사 뒤 매니페스트 기록 → 감시가 새 세대를 읽는다.
        FixtureModule.Deploy(Path.Combine(root, "modules"));

        Task finished = await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(notified.Task, finished, "list_changed was not received");

        CollectionAssert.Contains((await client.ListToolsAsync()).Select(t => t.Name).ToList(), "fixture_echo");
        CallToolResult result = await client.CallToolAsync("fixture_echo", new Dictionary<string, object?> { ["text"] = "hi" });
        Assert.AreEqual("echo:hi", ((TextContentBlock)result.Content[0]).Text);
    }
}
