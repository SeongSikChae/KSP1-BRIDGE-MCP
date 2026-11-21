using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text.Json.Nodes;

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
    public async Task Deploy_AddsTools_WithListChangedOnListenStream()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));

        TaskCompletionSource acknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        McpClientOptions options = new();
        options.Handlers.NotificationHandlers =
        [
            new(ToolListSubscriptions.AcknowledgedMethod, (_, _) => { acknowledged.TrySetResult(); return ValueTask.CompletedTask; }),
            new(NotificationMethods.ToolListChangedNotification, (_, _) => { notified.TrySetResult(); return ValueTask.CompletedTask; }),
        ];
        await using McpClient client = await McpClient.CreateAsync(
            new HttpClientTransport(new() { Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"), TransportMode = HttpTransportMode.StreamableHttp }),
            options);

        Assert.AreEqual("2026-07-28", client.NegotiatedProtocolVersion);
        Assert.IsTrue(client.ServerCapabilities.Tools?.ListChanged);
        CollectionAssert.DoesNotContain((await client.ListToolsAsync()).Select(t => t.Name).ToList(), "fixture_echo");

        // listen 요청은 스트림이 열려 있는 동안 끝나지 않는다. 확인 응답을 받은 뒤 게시한다.
        using CancellationTokenSource listenCts = new();
        Task<JsonRpcResponse> listen = client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "subscriptions/listen",
            Params = new JsonObject { ["notifications"] = new JsonObject { ["toolsListChanged"] = true } },
        }, listenCts.Token);
        Task ackFinished = await Task.WhenAny(acknowledged.Task, listen, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(acknowledged.Task, ackFinished, "subscriptions/acknowledged was not received");
        Assert.AreEqual(1, app.Services.GetRequiredService<ToolListSubscriptions>().OpenCount);

        // 배포와 같은 순서: 모듈 복사 뒤 매니페스트 기록 → 감시가 새 세대를 읽는다.
        FixtureModule.Deploy(Path.Combine(root, "modules"));

        Task finished = await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(notified.Task, finished, "list_changed was not received");

        CollectionAssert.Contains((await client.ListToolsAsync()).Select(t => t.Name).ToList(), "fixture_echo");
        CallToolResult result = await client.CallToolAsync("fixture_echo", new Dictionary<string, object?> { ["text"] = "hi" });
        Assert.AreEqual("echo:hi", ((TextContentBlock)result.Content[0]).Text);

        listenCts.Cancel();
        try { await listen; } catch (OperationCanceledException) { }
    }

    [TestMethod]
    public async Task Deploy_NotifiesRemainingListen_WhenAnotherListenWithSameIdCloses()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        ToolListSubscriptions subscriptions = app.Services.GetRequiredService<ToolListSubscriptions>();

        // listen 요청 id는 클라이언트가 정한다. 두 클라이언트가 같은 id로 연다.
        await using ListenClient first = await ListenClient.OpenAsync(port, "listen:0");
        await using ListenClient second = await ListenClient.OpenAsync(port, "listen:0");
        Assert.AreEqual(2, subscriptions.OpenCount);

        await first.CloseAsync();
        for (int i = 0; i < 50 && subscriptions.OpenCount != 1; i++)
            await Task.Delay(20);
        Assert.AreEqual(1, subscriptions.OpenCount);

        FixtureModule.Deploy(Path.Combine(root, "modules"));

        Task finished = await Task.WhenAny(second.Notified, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(second.Notified, finished, "list_changed was not received on the remaining listen");
    }

    [TestMethod]
    public async Task Listen_WithoutToolsGranted_EndsAfterAcknowledged()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        TaskCompletionSource acknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        McpClientOptions options = new();
        options.Handlers.NotificationHandlers =
        [
            new(ToolListSubscriptions.AcknowledgedMethod, (_, _) => { acknowledged.TrySetResult(); return ValueTask.CompletedTask; }),
        ];
        await using McpClient client = await McpClient.CreateAsync(
            new HttpClientTransport(new() { Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"), TransportMode = HttpTransportMode.StreamableHttp }),
            options);

        Task<JsonRpcResponse> listen = client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "subscriptions/listen",
            Params = new JsonObject { ["notifications"] = new JsonObject { ["toolsListChanged"] = false } },
        });
        Task finished = await Task.WhenAny(listen, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(listen, finished, "a listen that grants nothing must end");
        await listen;
        // 확인 응답은 응답보다 먼저 보내지만, 클라이언트는 알림 처리기를 따로 불러 순서가 바뀌어 보일 수 있다.
        Task ack = await Task.WhenAny(acknowledged.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(acknowledged.Task, ack, "subscriptions/acknowledged was not received");
        Assert.AreEqual(0, app.Services.GetRequiredService<ToolListSubscriptions>().OpenCount);
    }

    [TestMethod]
    public async Task Listen_IsRegisteredBeforeAcknowledged()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        ToolListSubscriptions subscriptions = app.Services.GetRequiredService<ToolListSubscriptions>();
        // 확인 응답을 보낸 직후의 이음매에서 본다. 확인 응답 뒤에 등록하면 여기서 0이 되어 늘 실패한다(D086·D091).
        int? openAtAcknowledged = null;
        subscriptions.AfterAcknowledged = () =>
        {
            openAtAcknowledged = subscriptions.OpenCount;
            return Task.CompletedTask;
        };
        await using ListenClient client = await ListenClient.OpenAsync(port, "listen:order");
        for (int i = 0; i < 50 && openAtAcknowledged is null; i++)
            await Task.Delay(20);
        Assert.AreEqual(1, openAtAcknowledged);
    }

    [TestMethod]
    public async Task Listen_SendsKeepalive()
    {
        ushort port = TestServers.FreePort();
        AppConfiguration configuration = TestServers.Config(root, port);
        await using WebApplication app = Program.CreateWebApplication(["--environment", "Production"], configuration,
            Path.Combine(root, "log"), ShortKeepalive);
        await app.StartAsync();
        await using ListenClient client = await ListenClient.OpenAsync(port, "listen:keepalive");

        Task finished = await Task.WhenAny(client.KeptAlive, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(client.KeptAlive, finished, "keepalive was not received");
    }

    [TestMethod]
    public async Task ServerStop_ClosesListen()
    {
        ushort port = TestServers.FreePort();
        await using WebApplication app = await TestServers.StartAsync(TestServers.Config(root, port));
        ToolListSubscriptions subscriptions = app.Services.GetRequiredService<ToolListSubscriptions>();
        await using ListenClient client = await ListenClient.OpenAsync(port, "listen:stop");
        Assert.AreEqual(1, subscriptions.OpenCount);

        // ApplicationStopping이 기다림을 끝내므로, 서버 종료 제한 시간(기본 30초)보다 훨씬 빨리 listen이 끝난다.
        using CancellationTokenSource stopLimit = new(TimeSpan.FromSeconds(10));
        Task stop = app.StopAsync(stopLimit.Token);
        Task finished = await Task.WhenAny(client.Listen, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(client.Listen, finished, "listen did not close when the server stopped");
        await stop;
        Assert.AreEqual(0, subscriptions.OpenCount);
    }

    /// <summary>유지 신호 간격을 줄인 처리기로 바꾼다(마지막 등록이 쓰인다).</summary>
    private static void ShortKeepalive(WebApplicationBuilder builder)
        => builder.Services.AddSingleton(sp => new ToolListSubscriptions(
            sp.GetRequiredService<KspBridge.Server.Modules.ModuleHost>(),
            sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ToolListSubscriptions>>())
        {
            KeepaliveInterval = TimeSpan.FromMilliseconds(100),
        });

    /// <summary>listen 하나를 열어 둔 최신 프로토콜 클라이언트.</summary>
    private sealed class ListenClient : IAsyncDisposable
    {
        private readonly McpClient client;
        private readonly CancellationTokenSource cts;
        private readonly Task<JsonRpcResponse> listen;
        private readonly TaskCompletionSource notified;
        private readonly TaskCompletionSource keptAlive;

        private ListenClient(McpClient client, CancellationTokenSource cts, Task<JsonRpcResponse> listen, TaskCompletionSource notified,
            TaskCompletionSource keptAlive)
        {
            this.client = client;
            this.cts = cts;
            this.listen = listen;
            this.notified = notified;
            this.keptAlive = keptAlive;
        }

        public Task Notified => notified.Task;

        public Task KeptAlive => keptAlive.Task;

        public Task Listen => listen;

        public static async Task<ListenClient> OpenAsync(ushort port, string listenId)
        {
            TaskCompletionSource acknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource keptAlive = new(TaskCreationOptions.RunContinuationsAsynchronously);
            McpClientOptions options = new();
            options.Handlers.NotificationHandlers =
            [
                new(ToolListSubscriptions.AcknowledgedMethod, (_, _) => { acknowledged.TrySetResult(); return ValueTask.CompletedTask; }),
                new(NotificationMethods.ToolListChangedNotification, (_, _) => { notified.TrySetResult(); return ValueTask.CompletedTask; }),
                new(ToolListSubscriptions.KeepaliveMethod, (_, _) => { keptAlive.TrySetResult(); return ValueTask.CompletedTask; }),
            ];
            McpClient client = await McpClient.CreateAsync(
                new HttpClientTransport(new() { Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"), TransportMode = HttpTransportMode.StreamableHttp }),
                options);
            CancellationTokenSource cts = new();
            Task<JsonRpcResponse> listen = client.SendRequestAsync(new JsonRpcRequest
            {
                Id = new RequestId(listenId),
                Method = "subscriptions/listen",
                Params = new JsonObject { ["notifications"] = new JsonObject { ["toolsListChanged"] = true } },
            }, cts.Token);
            ListenClient result = new(client, cts, listen, notified, keptAlive);
            Task finished = await Task.WhenAny(acknowledged.Task, listen, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.AreSame(acknowledged.Task, finished, "subscriptions/acknowledged was not received");
            return result;
        }

        public async Task CloseAsync()
        {
            cts.Cancel();
            try { await listen; } catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAsync();
            await client.DisposeAsync();
            cts.Dispose();
        }
    }
}
