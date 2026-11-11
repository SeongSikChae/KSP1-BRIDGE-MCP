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

            /// <summary>The mod's response envelope returned for every request.</summary>
            public string Response { get; set; } = """{"ok":true,"data":{"accepted":true}}""";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body, request.RequestUri.Query));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Response, Encoding.UTF8, "application/json"),
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
        public async Task AddPartPostsStackAttach()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.AddPart("fuelTank", 4041040345u, new NodeSpec("bottom"), ct: CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("fuelTank", body.GetProperty("part").GetString());
            Assert.AreEqual(4041040345u, body.GetProperty("parent").GetUInt32());
            Assert.AreEqual("bottom", body.GetProperty("node").GetProperty("parent").GetString());
            Assert.AreEqual(JsonValueKind.Null, body.GetProperty("node").GetProperty("child").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, body.GetProperty("surface").ValueKind);
        }

        [TestMethod]
        public async Task AddPartPostsSurfaceAndSymmetry()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.AddPart("radialDecoupler", 1u, surface: new SurfaceSpec(90, 0.5), symmetry: new SymmetrySpec("radial", 3), ct: CancellationToken.None);

            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual(90, body.GetProperty("surface").GetProperty("azimuth").GetDouble());
            Assert.AreEqual(0.5, body.GetProperty("surface").GetProperty("height").GetDouble());
            Assert.AreEqual(0, body.GetProperty("surface").GetProperty("angle").GetDouble());
            Assert.AreEqual("radial", body.GetProperty("symmetry").GetProperty("mode").GetString());
            Assert.AreEqual(3, body.GetProperty("symmetry").GetProperty("count").GetInt32());
        }

        [TestMethod]
        public async Task RemovePartDeletesById()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.RemovePart(4041040345u, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Delete, "/api/editor/parts/4041040345", (string?)null), (handler.Requests[0].Method, handler.Requests[0].Path, handler.Requests[0].Body));
        }

        [TestMethod]
        public async Task OffsetPartPostsDistance()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.OffsetPart(7u, y: -0.5, ct: CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts/7/offset"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual(0, body.GetProperty("x").GetDouble());
            Assert.AreEqual(-0.5, body.GetProperty("y").GetDouble());
            Assert.AreEqual(0, body.GetProperty("z").GetDouble());
        }

        [TestMethod]
        public async Task RotatePartPostsAxisAndAngle()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.RotatePart(7u, "z", 10, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts/7/rotate"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("z", body.GetProperty("axis").GetString());
            Assert.AreEqual(10, body.GetProperty("angle").GetDouble());
        }

        [TestMethod]
        public async Task SetPartFieldPostsValueAsGiven()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.SetPartField(7u, "ModuleEngines", "thrustPercentage", JsonDocument.Parse("50").RootElement, CancellationToken.None);
            await tools.SetPartField(7u, "ModuleGimbal", "gimbalLock", JsonDocument.Parse("true").RootElement, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts/7/field"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("ModuleEngines", body.GetProperty("module").GetString());
            Assert.AreEqual("thrustPercentage", body.GetProperty("field").GetString());
            Assert.AreEqual(50, body.GetProperty("value").GetDouble());
            Assert.AreEqual(JsonValueKind.True, Body(handler.Requests[1].Body).GetProperty("value").ValueKind);
        }

        [TestMethod]
        public async Task SetResourcePostsFraction()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.SetResource(7u, "LiquidFuel", 0.5, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts/7/resource"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("LiquidFuel", body.GetProperty("resource").GetString());
            Assert.AreEqual(0.5, body.GetProperty("fraction").GetDouble());
        }

        [TestMethod]
        public async Task SetStagePostsStageOrNewStage()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.SetStage(7u, stage: 2, ct: CancellationToken.None);
            await tools.SetStage(7u, new_stage_at: 0, ct: CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/parts/7/stage"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement first = Body(handler.Requests[0].Body);
            Assert.AreEqual(2, first.GetProperty("stage").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, first.GetProperty("new_stage_at").ValueKind);
            JsonElement second = Body(handler.Requests[1].Body);
            Assert.AreEqual(JsonValueKind.Null, second.GetProperty("stage").ValueKind);
            Assert.AreEqual(0, second.GetProperty("new_stage_at").GetInt32());
        }

        [TestMethod]
        public async Task GetCraftGetsEditorCraft()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.GetCraft(CancellationToken.None);

            Assert.AreEqual((HttpMethod.Get, "/api/editor/craft", (string?)null), (handler.Requests[0].Method, handler.Requests[0].Path, handler.Requests[0].Body));
        }

        [TestMethod]
        public async Task CheckCraftGetsSituationQuery()
        {
            (EditorTools tools, RecordingHandler handler) = Create();

            await tools.CheckCraft(ct: CancellationToken.None);
            await tools.CheckCraft("Mun", 1500.5, vacuum: true, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Get, "/api/editor/check", "?altitude=0", (string?)null),
                (handler.Requests[0].Method, handler.Requests[0].Path, handler.Requests[0].Query, handler.Requests[0].Body));
            Assert.AreEqual(("/api/editor/check", "?altitude=1500.5&body=Mun&vacuum=true"), (handler.Requests[1].Path, handler.Requests[1].Query));
        }

        [TestMethod]
        public async Task RenderCraftReturnsViewNameAndPngPerImage()
        {
            (EditorTools tools, RecordingHandler handler) = Create();
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            string b64 = Convert.ToBase64String(png);
            handler.Response = $$$"""{"ok":true,"data":{"images":[{"view":"side","width":64,"height":64,"png":"{{{b64}}}"},{"view":"45,30","width":64,"height":64,"png":"{{{b64}}}"}]}}""";

            ModelContextProtocol.Protocol.CallToolResult result = await tools.RenderCraft(["side", "45,30"], 64, markers: true, CancellationToken.None);

            Assert.AreEqual((HttpMethod.Post, "/api/editor/render"), (handler.Requests[0].Method, handler.Requests[0].Path));
            JsonElement body = Body(handler.Requests[0].Body);
            Assert.AreEqual("45,30", body.GetProperty("views")[1].GetString());
            Assert.AreEqual(64, body.GetProperty("resolution").GetInt32());
            Assert.IsTrue(body.GetProperty("markers").GetBoolean());
            Assert.AreNotEqual(true, result.IsError);
            Assert.AreEqual(4, result.Content.Count);
            Assert.AreEqual("side", ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text);
            ModelContextProtocol.Protocol.ImageContentBlock image = (ModelContextProtocol.Protocol.ImageContentBlock)result.Content[1];
            Assert.AreEqual("image/png", image.MimeType);
            CollectionAssert.AreEqual(png, image.DecodedData.ToArray());
            Assert.AreEqual("45,30", ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[2]).Text);
        }

        [TestMethod]
        public async Task RenderCraftErrorIsToolError()
        {
            (EditorTools tools, RecordingHandler handler) = Create();
            handler.Response = """{"ok":false,"error":{"code":"bad_request","message":"resolution must be a whole number 64..1024"}}""";

            ModelContextProtocol.Protocol.CallToolResult result = await tools.RenderCraft(resolution: 2000, ct: CancellationToken.None);

            Assert.IsTrue(result.IsError);
            StringAssert.StartsWith(((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text, "error: bad_request");
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
