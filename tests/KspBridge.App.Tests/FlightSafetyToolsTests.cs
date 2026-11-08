using System.Text.Json;

namespace KspBridge.App.Tests
{
    /// <summary>Combined conditions, anomaly abort, attitude frames and node execution (docs/ksp-bridge/0038, 0039).</summary>
    [TestClass]
    public sealed class FlightSafetyToolsTests
    {
        [TestMethod]
        public async Task WaitUntilSendsConditionAndAbortOn()
        {
            var (client, h) = ToolTest.Client();
            AscentTools ascent = new(client);
            JsonElement condition = JsonDocument.Parse(
                """{"all":[{"field":"altitude_asl","op":"<=","value":"4000"},{"any":[{"field":"situation","op":"==","value":"FLYING"},{"field":"node_exec","op":"==","value":"done"}]}]}""").RootElement;

            await ascent.WaitUntil(condition: condition, timeout: 30, abortOn: ["part_lost"]);
            await ascent.WaitUntil(condition: condition, abortOn: []);

            Assert.AreEqual((HttpMethod.Post, "/api/wait"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            ToolTest.AssertJson(
                """{"condition":{"all":[{"field":"altitude_asl","op":"<=","value":4000},{"any":[{"field":"situation","op":"==","value":"FLYING"},{"field":"node_exec","op":"==","value":"done"}]}]},"timeout":30,"abort_on":["part_lost"]}""",
                h.Requests[0].Body);
            ToolTest.AssertJson(
                """{"condition":{"all":[{"field":"altitude_asl","op":"<=","value":4000},{"any":[{"field":"situation","op":"==","value":"FLYING"},{"field":"node_exec","op":"==","value":"done"}]}]},"timeout":60,"abort_on":[]}""",
                h.Requests[1].Body);
        }

        [TestMethod]
        public async Task WaitUntilNeedsExactlyOneConditionForm()
        {
            var (client, h) = ToolTest.Client();
            AscentTools ascent = new(client);
            JsonElement condition = JsonDocument.Parse("""{"field":"met","op":">=","value":1}""").RootElement;

            StringAssert.StartsWith(await ascent.WaitUntil(), "error: bad_request");
            StringAssert.StartsWith(await ascent.WaitUntil("met", ">=", JsonDocument.Parse("1").RootElement, condition: condition), "error: bad_request");
            Assert.AreEqual(0, h.Requests.Count);
        }

        [TestMethod]
        public async Task AddTriggerAcceptsCombinedCondition()
        {
            var (client, h) = ToolTest.Client();
            TriggerTools tools = new(client);
            JsonElement condition = JsonDocument.Parse("""{"all":[{"field":"altitude_asl","op":"<=","value":4000},{"field":"speed_surface","op":"<=","value":"250"}]}""").RootElement;
            JsonElement actions = JsonDocument.Parse("""[{"type":"stage"}]""").RootElement;

            await tools.AddTrigger(actions: actions, name: "chute", condition: condition);
            string missingActions = await tools.AddTrigger(condition: condition);

            ToolTest.AssertJson(
                """{"name":"chute","when":{"all":[{"field":"altitude_asl","op":"<=","value":4000},{"field":"speed_surface","op":"<=","value":250}]},"actions":[{"type":"stage"}],"repeat":false}""",
                h.Requests[0].Body);
            StringAssert.StartsWith(missingActions, "error: bad_request");
            Assert.AreEqual(1, h.Requests.Count);
        }

        [TestMethod]
        public async Task SetAttitudeSendsFrameArguments()
        {
            var (client, h) = ToolTest.Client();
            AscentTools ascent = new(client);

            await ascent.SetAttitude(frame: "surface_retrograde", maxAoaDeg: 5);
            await ascent.SetAttitude(frame: "prograde", pitchOffset: -10, yawOffset: 3, mode: "sas");
            await ascent.SetAttitude(80, 90, frame: "surface", maxAoaDeg: 4);
            await ascent.SetAttitude(roll: 0, frame: "prograde");

            ToolTest.AssertJson("""{"mode":"own","frame":"surface_retrograde","max_aoa_deg":5}""", h.Requests[0].Body);
            ToolTest.AssertJson("""{"mode":"sas","frame":"prograde","pitch_offset":-10,"yaw_offset":3}""", h.Requests[1].Body);
            ToolTest.AssertJson("""{"mode":"own","pitch":80,"heading":90,"roll":null,"frame":"surface","max_aoa_deg":4}""", h.Requests[2].Body);
            // A roll with a non-surface frame is passed on so the mod rejects it rather than dropping it silently.
            ToolTest.AssertJson("""{"mode":"own","pitch":null,"heading":null,"roll":0,"frame":"prograde"}""", h.Requests[3].Body);
        }

        [TestMethod]
        public async Task NodeExecutionToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            FlightTools tools = new(client);

            await tools.ExecuteNode();
            await tools.ExecuteNode(0.5, false, false);
            await tools.CancelNode(default);

            Assert.AreEqual((HttpMethod.Post, "/api/node/execute"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            ToolTest.AssertJson("""{"tolerance":0.1,"warp":true,"remove_when_done":true}""", h.Requests[0].Body);
            ToolTest.AssertJson("""{"tolerance":0.5,"warp":false,"remove_when_done":false}""", h.Requests[1].Body);
            Assert.AreEqual((HttpMethod.Post, "/api/node/cancel"), (h.Requests[2].Method, h.Requests[2].PathAndQuery));
        }

        [TestMethod]
        public void FieldListIncludesNewFields()
        {
            StringAssert.Contains(TriggerTools.Fields, "node_exec");
            StringAssert.Contains(TriggerTools.Fields, "node_dv_remaining");
            StringAssert.Contains(TriggerTools.Fields, "attitude_error");
        }
    }
}
