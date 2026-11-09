using System.Net;
using System.Text;
using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class EditorToolsTests
    {
        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Path, string? Body, string Query)> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body, request.RequestUri.Query));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true,"data":{"accepted":true}}""", Encoding.UTF8, "application/json"),
                };
            }
        }

        private static (EditorTools Tools, RecordingHandler Handler) Create()
        {
            RecordingHandler handler = new();
            HttpClient http = new(handler) { BaseAddress = new Uri("http://127.0.0.1:8085/") };
            return (new EditorTools(new KspBridgeClient(http, new KspBridgeClientOptions(TimeSpan.FromSeconds(10)))), handler);
        }

        private static JsonElement Body(string? json) => JsonDocument.Parse(json!).RootElement;

        [TestMethod]
        public async Task OpenEditorPostsFacilityAndCraft()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            string result = await tools.OpenEditor("SPH", "Plane1", CancellationToken.None);

            Assert.AreEqual("""{"accepted":true}""", result);
            Assert.AreEqual((HttpMethod.Post, "/api/editor/open"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("SPH", body.GetProperty("facility").GetString());
            Assert.AreEqual("Plane1", body.GetProperty("craft").GetString());
        }

        [TestMethod]
        public async Task OpenEditorWithoutCraftSendsNull()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.OpenEditor("VAB", ct: CancellationToken.None);

            Assert.AreEqual(JsonValueKind.Null, Body(handler.Requests[0].Body).GetProperty("craft").ValueKind);
        }

        [TestMethod]
        public async Task NewCraftPostsName()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.NewCraft("Rocket1", CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/new"), (handler.Requests[0].Method, handler.Requests[0].Path));
            Assert.AreEqual("Rocket1", Body(handler.Requests[0].Body).GetProperty("name").GetString());
        }

        [TestMethod]
        public async Task ListPartsBuildsQuery()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.ListParts("Engine", "swivel tank", false, CancellationToken.None);
            await tools.ListParts(ct: CancellationToken.None);

            Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
            Assert.AreEqual("/api/parts", handler.Requests[0].Path);
            Assert.AreEqual("?category=Engine&q=swivel%20tank&unlocked=false", handler.Requests[0].Query);
            Assert.AreEqual("", handler.Requests[1].Query);
        }

        [TestMethod]
        public async Task GetCraftGetsEditorCraft()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.GetCraft(CancellationToken.None);

            Assert.AreEqual((HttpMethod.Get, "/api/editor/craft", (string?)null), (handler.Requests[0].Method, handler.Requests[0].Path, handler.Requests[0].Body));
        }

        [TestMethod]
        public async Task GetPartEscapesName()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.GetPart("mk1pod.v2", CancellationToken.None);

            Assert.AreEqual((HttpMethod.Get, "/api/parts/mk1pod.v2"), (handler.Requests[0].Method, handler.Requests[0].Path));
        }
    }
}
