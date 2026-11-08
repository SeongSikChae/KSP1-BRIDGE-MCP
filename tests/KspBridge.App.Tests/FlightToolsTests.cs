using System.Net;
using System.Text;
using System.Text.Json;

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

        private static void AssertBody(string? actual, string expected)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(
                JsonSerializer.Serialize(JsonDocument.Parse(expected).RootElement),
                JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement));
        }

        [TestMethod]
        public async Task ToolsMapToHttpRequests()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            CancellationToken ct = CancellationToken.None;

            await tools.GetStatus(ct);
            await tools.GetVesselState(ct);
            await tools.SetThrottle(0.5, ct: ct);
            await tools.ActivateNextStage(ct);
            await tools.SetSas(true, ct);
            await tools.SetSasMode("prograde", ct);
            await tools.SetRcs(false, ct);
            await tools.SetActionGroup("gear", "toggle", ct);
            await tools.ListManeuverNodes(ct);
            await tools.AddManeuverNode(ut: 1000, prograde: 100, ct: ct);
            await tools.AddManeuverNode(inSeconds: 300, normal: -5, radial: 2, ct: ct);
            await tools.RemoveManeuverNode(1, ct);

            var r = handler.Requests;
            Assert.HasCount(12, r);
            Assert.AreEqual((HttpMethod.Get, "/api/status", (string?)null), r[0]);
            Assert.AreEqual((HttpMethod.Get, "/api/vessel", (string?)null), r[1]);
            Assert.AreEqual((HttpMethod.Post, "/api/throttle"), (r[2].Method, r[2].Path));
            AssertBody(r[2].Body, """{"value":0.5,"hold":true}""");
            Assert.AreEqual((HttpMethod.Post, "/api/stage"), (r[3].Method, r[3].Path));
            AssertBody(r[3].Body, "{}");
            AssertBody(r[4].Body, """{"enabled":true}""");
            Assert.AreEqual("/api/sas", r[4].Path);
            AssertBody(r[5].Body, """{"mode":"prograde"}""");
            Assert.AreEqual("/api/sas/mode", r[5].Path);
            AssertBody(r[6].Body, """{"enabled":false}""");
            Assert.AreEqual("/api/rcs", r[6].Path);
            AssertBody(r[7].Body, """{"group":"gear","action":"toggle"}""");
            Assert.AreEqual("/api/action-group", r[7].Path);
            Assert.AreEqual((HttpMethod.Get, "/api/nodes", (string?)null), r[8]);
            AssertBody(r[9].Body, """{"ut":1000,"prograde":100,"normal":0,"radial":0}""");
            Assert.AreEqual((HttpMethod.Post, "/api/nodes"), (r[9].Method, r[9].Path));
            AssertBody(r[10].Body, """{"in_seconds":300,"prograde":0,"normal":-5,"radial":2}""");
            Assert.AreEqual((HttpMethod.Delete, "/api/nodes/1", (string?)null), r[11]);
        }

        [TestMethod]
        public async Task SuccessReturnsDataJson()
        {
            (FlightTools tools, _) = Create();
            Assert.AreEqual("""{"x":1}""", await tools.GetVesselState(CancellationToken.None));
        }

        [TestMethod]
        public async Task ErrorResponseReturnsCodeAndMessage()
        {
            (FlightTools tools, _) = Create(_ => Json(HttpStatusCode.Conflict,
                """{"ok":false,"error":{"code":"not_in_flight","message":"not in the flight scene"}}"""));
            Assert.AreEqual("error: not_in_flight: not in the flight scene", await tools.GetVesselState(CancellationToken.None));
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

        [TestMethod]
        public async Task WarpToolsMapToHttpRequests()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            CancellationToken ct = CancellationToken.None;

            await tools.WarpTo("pe", lead: 60, ct: ct);
            await tools.WarpTo("ut", ut: 9000, ct: ct);
            await tools.SetWarp(1000, ct: ct);
            await tools.SetWarp(4, physics: true, ct: ct);
            await tools.StopWarp(ct);

            var r = handler.Requests;
            Assert.HasCount(5, r);
            Assert.AreEqual((HttpMethod.Post, "/api/warp/to"), (r[0].Method, r[0].Path));
            AssertBody(r[0].Body, """{"target":"pe","ut":null,"lead":60}""");
            AssertBody(r[1].Body, """{"target":"ut","ut":9000,"lead":30}""");
            Assert.AreEqual((HttpMethod.Post, "/api/warp"), (r[2].Method, r[2].Path));
            AssertBody(r[2].Body, """{"rate":1000,"physics":false}""");
            AssertBody(r[3].Body, """{"rate":4,"physics":true}""");
            Assert.AreEqual((HttpMethod.Post, "/api/warp/stop"), (r[4].Method, r[4].Path));
        }

        [TestMethod]
        public async Task AddManeuverNodeRequiresExactlyOneTime()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            StringAssert.StartsWith(await tools.AddManeuverNode(prograde: 10), "error: bad_request");
            StringAssert.StartsWith(await tools.AddManeuverNode(ut: 1, inSeconds: 2), "error: bad_request");
            Assert.IsEmpty(handler.Requests);
        }
    }
}
