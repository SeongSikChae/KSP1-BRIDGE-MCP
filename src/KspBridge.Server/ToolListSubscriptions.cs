using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KspBridge.Server.Modules;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KspBridge.Server
{
    /// <summary>
    /// 최신 프로토콜(2026-07-28)의 <c>subscriptions/listen</c> 처리기.
    /// 도구 목록 변경을 구독한 listen 스트림을 모아 두고, 모듈 호스트 공유 도구 컬렉션이 바뀔 때마다
    /// 열린 스트림 모두에 <c>notifications/tools/list_changed</c>를, 스트림마다 일정 간격으로 유지 신호를 보낸다.
    /// 스트림은 요청이 취소되거나(연결 끊김 포함) 서버가 멈출 때 목록에서 빠진다.
    /// </summary>
    public sealed class ToolListSubscriptions : IDisposable
    {
        public const string AcknowledgedMethod = "notifications/subscriptions/acknowledged";
        public const string ToolsListChangedMethod = "notifications/tools/list_changed";
        public const string KeepaliveMethod = "notifications/kspbridge/keepalive";
        private const string SubscriptionIdKey = "io.modelcontextprotocol/subscriptionId";

        private readonly ModuleHost moduleHost;
        private readonly IHostApplicationLifetime lifetime;
        private readonly ILogger<ToolListSubscriptions> logger;
        // listen 요청 id는 클라이언트가 정하므로 여러 스트림이 같은 id를 쓸 수 있다. 목록은 스트림마다 따로 만든 키로 둔다.
        private readonly ConcurrentDictionary<Guid, ListenStream> streams = new();

        public ToolListSubscriptions(ModuleHost moduleHost, IHostApplicationLifetime lifetime, ILogger<ToolListSubscriptions> logger)
        {
            this.moduleHost = moduleHost;
            this.lifetime = lifetime;
            this.logger = logger;
            moduleHost.Tools.Changed += OnToolsChanged;
        }

        /// <summary>유지 신호 간격. 클라이언트 쪽 유휴 끊김을 막으려고 보낸다.</summary>
        public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>지금 열려 있는 listen 스트림 수.</summary>
        public int OpenCount => streams.Count;

        /// <summary>테스트용 이음매: 도구를 허가한 listen의 확인 응답을 보낸 직후 기다린다(D091). 비어 있으면 동작이 그대로다.</summary>
        internal Func<Task>? AfterAcknowledged { get; set; }

        public async ValueTask<EmptyResult> HandleAsync(RequestContext<SubscriptionsListenRequestParams> request, CancellationToken cancellationToken)
        {
            JsonRpcRequest jsonRpcRequest = request.JsonRpcRequest;
            bool toolsGranted = request.Params?.Notifications?.ToolsListChanged == true;
            SubscriptionsListenNotifications granted = new() { ToolsListChanged = toolsGranted ? true : null };
            // request.Server는 이 listen 요청의 응답 스트림에 묶인 서버라, 이것으로 보내면 그 스트림으로 간다.
            ListenStream stream = new(Guid.NewGuid(), jsonRpcRequest.Id, request.Server);

            // 확인 응답은 SDK 기본 처리기처럼 처리기 토큰으로 보낸다. 실패하면 요청이 오류로 끝난다.
            JsonNode? ackParams = JsonSerializer.SerializeToNode(
                new SubscriptionsAcknowledgedNotificationParams { Notifications = granted }, McpJsonUtilities.DefaultOptions);
            if (!toolsGranted)
            {
                await SendAsync(stream, AcknowledgedMethod, ackParams, cancellationToken).ConfigureAwait(false);
                return new EmptyResult();
            }

            // 확인 응답보다 먼저 목록에 넣어, 확인 응답을 받은 클라이언트가 그 뒤의 list_changed를 놓치지 않게 한다(D086).
            // 그 사이 바뀐 목록의 list_changed가 확인 응답보다 먼저 갈 수 있지만 SDK 기본 처리기도 같은 순서이고
            // 클라이언트는 확인 응답과 상관없이 처리한다(D090).
            using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
            streams[stream.Key] = stream;
            bool acknowledged = false;
            try
            {
                await SendAsync(stream, AcknowledgedMethod, ackParams, cancellationToken).ConfigureAwait(false);
                acknowledged = true;
                logger.LogInformation("subscriptions/listen opened {SubscriptionId} (granted toolsListChanged)", stream.Id);
                if (AfterAcknowledged is { } afterAcknowledged)
                    await afterAcknowledged().ConfigureAwait(false);
                using PeriodicTimer timer = new(KeepaliveInterval);
                while (await timer.WaitForNextTickAsync(wait.Token).ConfigureAwait(false))
                    await TrySendAsync(stream, KeepaliveMethod).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (acknowledged && wait.IsCancellationRequested)
            {
            }
            finally
            {
                streams.TryRemove(stream.Key, out _);
                if (acknowledged)
                {
                    string reason = lifetime.ApplicationStopping.IsCancellationRequested ? "server stopping" : "request cancelled";
                    logger.LogInformation("subscriptions/listen closed {SubscriptionId} ({Reason})", stream.Id, reason);
                }
            }
            return new EmptyResult();
        }

        public void Dispose() => moduleHost.Tools.Changed -= OnToolsChanged;

        private void OnToolsChanged(object? sender, EventArgs e) => _ = NotifyToolsChangedAsync();

        private async Task NotifyToolsChangedAsync()
        {
            foreach (ListenStream stream in streams.Values)
                await TrySendAsync(stream, ToolsListChangedMethod).ConfigureAwait(false);
        }

        /// <summary>
        /// SDK 기본 구현(<c>McpServerImpl.SendListChangedNotificationAsync</c>)처럼 <see cref="CancellationToken.None"/>으로 보내고
        /// 스트림마다 예외를 잡아, 한 스트림의 실패가 다른 스트림이나 서버 프로세스에 번지지 않게 한다.
        /// </summary>
        private async Task TrySendAsync(ListenStream stream, string method)
        {
            try
            {
                await SendAsync(stream, method, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send {Method} on subscriptions/listen {SubscriptionId}", method, stream.Id);
            }
        }

        /// <summary>listen 요청의 응답 스트림으로, 구독 id를 <c>_meta</c>에 붙인 알림을 보낸다.</summary>
        private static Task SendAsync(ListenStream stream, string method, JsonNode? paramsNode, CancellationToken cancellationToken)
        {
            JsonObject parameters = paramsNode as JsonObject ?? new JsonObject();
            if (parameters["_meta"] is not JsonObject meta)
                parameters["_meta"] = meta = new JsonObject();
            meta[SubscriptionIdKey] = stream.Id.Id switch
            {
                string s => JsonValue.Create(s),
                long l => JsonValue.Create(l),
                _ => null,
            };
            JsonRpcNotification notification = new() { Method = method, Params = parameters };
            return stream.Server.SendMessageAsync(notification, cancellationToken);
        }

        private sealed record ListenStream(Guid Key, RequestId Id, McpServer Server);
    }
}
