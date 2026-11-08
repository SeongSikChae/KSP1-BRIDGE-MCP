using System.Net;
using System.Text;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class FlightToolsTests
    {
        private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
                return respond(request);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static (FlightTools Tools, RecordingHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
        {
            RecordingHandler handler = new(respond ?? (_ => Json(HttpStatusCode.OK, """{"ok":true,"data":{"x":1}}""")));
            HttpClient http = new(handler) { BaseAddress = new Uri("http://127.0.0.1:8085/") };
            return (new FlightTools(new KspBridgeClient(http, new KspBridgeClientOptions(TimeSpan.FromSeconds(10)))), handler);
        }

        [TestMethod]
        public async Task PerRequestTimeoutReturnsTimeoutError()
        {
            HttpClient http = new(new DelayHandler()) { BaseAddress = new Uri("http://127.0.0.1:8085/"), Timeout = Timeout.InfiniteTimeSpan };
            KspBridgeClient client = new(http, new KspBridgeClientOptions(TimeSpan.FromSeconds(10)));
            string result = await client.SendAsync(HttpMethod.Get, "api/status", null, CancellationToken.None, TimeSpan.FromMilliseconds(100));
            Assert.AreEqual("error: timeout: no response from the KspBridge mod.", result);
        }

        private sealed class DelayHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        [TestMethod]
        public async Task ToolsMapToHttpRequests()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            CancellationToken ct = CancellationToken.None;

            await tools.GetStatus(ct);

            var r = handler.Requests;
            Assert.HasCount(1, r);
            Assert.AreEqual((HttpMethod.Get, "/api/status", (string?)null), r[0]);
        }

        [TestMethod]
        public async Task SuccessReturnsDataJson()
        {
            (FlightTools tools, _) = Create();
            Assert.AreEqual("""{"x":1}""", await tools.GetStatus(CancellationToken.None));
        }

        [TestMethod]
        public async Task ErrorResponseReturnsCodeAndMessage()
        {
            (FlightTools tools, _) = Create(_ => Json(HttpStatusCode.Conflict,
                """{"ok":false,"error":{"code":"not_in_flight","message":"not in the flight scene"}}"""));
            Assert.AreEqual("error: not_in_flight: not in the flight scene", await tools.GetStatus(CancellationToken.None));
        }

        [TestMethod]
        public async Task ConnectionFailureReturnsNotRunningMessage()
        {
            (FlightTools tools, _) = Create(_ => throw new HttpRequestException("refused"));
            Assert.AreEqual(KspBridgeClient.NotRunningMessage, await tools.GetStatus(CancellationToken.None));
        }

        [TestMethod]
        public async Task ReloadModPostsReload()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            await tools.ReloadMod(CancellationToken.None);
            Assert.AreEqual((HttpMethod.Post, "/api/reload"), (handler.Requests[0].Method, handler.Requests[0].Path));
        }

        [TestMethod]
        public async Task NonJsonResponseReturnsInvalidResponse()
        {
            (FlightTools tools, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("oops") });
            Assert.AreEqual("error: invalid_response: HTTP 502: oops", await tools.GetStatus(CancellationToken.None));
        }
    }
}
