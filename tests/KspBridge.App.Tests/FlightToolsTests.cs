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
                Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
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
            await tools.GetFlight(ct);
            using JsonDocument plan = JsonDocument.Parse("""{"steps":[{"mode":"attitude","throttle":1}]}""");
            await tools.SubmitFlightPlan(plan.RootElement, ct);
            await tools.AbortFlight(ct);
            await tools.GetFlightEvents(5, 10, ct);
            using JsonDocument steps = JsonDocument.Parse("""[{"mode":"attitude","throttle":0}]""");
            await tools.ReplaceFlightSteps(steps.RootElement, true, ct);
            await tools.RevertFlight("launch", ct);
            await tools.LeaveFlight(ct);
            await tools.RecoverVessel("LaunchPad", ct);
            await tools.RecoverVessel(ct: ct);
            await tools.SetSas(true, "prograde", "orbit", ct);
            await tools.SetSas(false, ct: ct);
            await tools.SetRcs(true, ct);
            await tools.RcsTranslate(2, z: 1, ct: ct);

            var r = handler.Requests;
            Assert.HasCount(14, r);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/translate", (string?)"""{"seconds":2,"x":0,"y":0,"z":1}"""), r[13]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/rcs", (string?)"""{"on":true}"""), r[12]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/sas", (string?)"""{"on":true,"mode":"prograde","reference":"orbit"}"""), r[10]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/sas", (string?)"""{"on":false,"mode":null,"reference":null}"""), r[11]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/revert", (string?)"""{"target":"launch"}"""), r[6]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/leave", (string?)"{}"), r[7]);
            Assert.AreEqual((HttpMethod.Post, "/api/vessel/recover", (string?)"""{"site":"LaunchPad"}"""), r[8]);
            Assert.AreEqual((HttpMethod.Post, "/api/vessel/recover", (string?)"""{"site":null}"""), r[9]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/steps", (string?)"""{"steps":[{"mode":"attitude","throttle":0}],"include_current":true}"""), r[5]);
            Assert.AreEqual((HttpMethod.Get, "/api/status", (string?)null), r[0]);
            Assert.AreEqual((HttpMethod.Get, "/api/flight", (string?)null), r[1]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/plan", (string?)"""{"plan":{"steps":[{"mode":"attitude","throttle":1}]}}"""), r[2]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/abort", (string?)"{}"), r[3]);
            Assert.AreEqual((HttpMethod.Get, "/api/flight/events?since=5&limit=10", (string?)null), r[4]);
        }

        [TestMethod]
        public async Task ManeuverNodeToolsMapToHttpRequests()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            CancellationToken ct = CancellationToken.None;

            await tools.AddManeuverNode("apoapsis", prograde: 100, ct: ct);
            await tools.AddManeuverNode(ut: 1234.5, normal: -20, radial: 3, ct: ct);
            await tools.UpdateManeuverNode(0, normal: 50, ct: ct);
            await tools.RemoveManeuverNode(1, ct: ct);
            await tools.RemoveManeuverNode(all: true, ct: ct);
            await tools.PlanManeuver("hohmann", altitude: 120000, ct: ct);
            await tools.PlanManeuver("change_inclination", inclination: 5, at: "auto", append: true, ct: ct);

            var r = handler.Requests;
            Assert.HasCount(7, r);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/plan_maneuver", (string?)"""{"kind":"hohmann","altitude":120000,"inclination":null,"at":null,"ut":null,"append":null,"periapsis":null}"""), r[5]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/plan_maneuver", (string?)"""{"kind":"change_inclination","altitude":null,"inclination":5,"at":"auto","ut":null,"append":true,"periapsis":null}"""), r[6]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/nodes/add", (string?)"""{"at":"apoapsis","ut":null,"prograde":100,"normal":0,"radial":0}"""), r[0]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/nodes/add", (string?)"""{"at":null,"ut":1234.5,"prograde":0,"normal":-20,"radial":3}"""), r[1]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/nodes/update", (string?)"""{"index":0,"ut":null,"prograde":null,"normal":50,"radial":null}"""), r[2]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/nodes/remove", (string?)"""{"index":1,"all":null}"""), r[3]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/nodes/remove", (string?)"""{"index":null,"all":true}"""), r[4]);
        }

        [TestMethod]
        public async Task TargetToolsMapToHttpRequests()
        {
            (FlightTools tools, RecordingHandler handler) = Create();
            CancellationToken ct = CancellationToken.None;

            await tools.ListVessels(ct: ct);
            await tools.ListVessels(true, ct);
            await tools.SetTarget(body: "Mun", ct: ct);
            await tools.SetTarget(vessel_id: 12345, port: 678, ct: ct);
            await tools.SetTarget(clear: true, ct: ct);
            await tools.PlanManeuver("transfer_body", periapsis: 30000, ct: ct);

            var r = handler.Requests;
            Assert.HasCount(6, r);
            Assert.AreEqual((HttpMethod.Get, "/api/vessels?include_debris=false", (string?)null), r[0]);
            Assert.AreEqual((HttpMethod.Get, "/api/vessels?include_debris=true", (string?)null), r[1]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/target", (string?)"""{"body":"Mun","vessel_id":null,"port":null,"clear":null}"""), r[2]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/target", (string?)"""{"body":null,"vessel_id":12345,"port":678,"clear":null}"""), r[3]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/target", (string?)"""{"body":null,"vessel_id":null,"port":null,"clear":true}"""), r[4]);
            Assert.AreEqual((HttpMethod.Post, "/api/flight/plan_maneuver", (string?)"""{"kind":"transfer_body","altitude":null,"inclination":null,"at":null,"ut":null,"append":null,"periapsis":30000}"""), r[5]);
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
