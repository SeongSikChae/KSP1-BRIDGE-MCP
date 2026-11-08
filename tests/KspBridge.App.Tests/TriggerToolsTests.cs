using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class TriggerToolsTests
    {
        [TestMethod]
        public async Task TriggerToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            TriggerTools tools = new(client);

            JsonElement value = JsonDocument.Parse("80000").RootElement;
            JsonElement actions = JsonDocument.Parse("""[{"type":"throttle","value":0},{"type":"stage"}]""").RootElement;
            await tools.AddTrigger("apoapsis", ">=", value, actions, "cutoff");
            await tools.ListTriggers(default);
            await tools.RemoveTrigger(3);
            await tools.RemoveTrigger();

            Assert.AreEqual((HttpMethod.Post, "/api/triggers"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            ToolTest.AssertJson(
                """{"name":"cutoff","when":{"field":"apoapsis","op":">=","value":80000},"actions":[{"type":"throttle","value":0},{"type":"stage"}],"repeat":false}""",
                h.Requests[0].Body);
            Assert.AreEqual((HttpMethod.Get, "/api/triggers"), (h.Requests[1].Method, h.Requests[1].PathAndQuery));
            Assert.AreEqual((HttpMethod.Delete, "/api/triggers/3"), (h.Requests[2].Method, h.Requests[2].PathAndQuery));
            Assert.AreEqual((HttpMethod.Delete, "/api/triggers"), (h.Requests[3].Method, h.Requests[3].PathAndQuery));
        }

        [TestMethod]
        public void FieldListIncludesGFields()
        {
            StringAssert.Contains(TriggerTools.Fields, "g_force");
            StringAssert.Contains(TriggerTools.Fields, "g_load_max");
        }

        [TestMethod]
        public async Task StringValuesAreNormalized()
        {
            var (client, h) = ToolTest.Client();
            TriggerTools tools = new(client);
            JsonElement actions = JsonDocument.Parse("""[{"type":"stage"}]""").RootElement;

            await tools.AddTrigger("met", ">=", JsonDocument.Parse("\"378.0\"").RootElement, actions);
            await tools.AddTrigger("stage_flameout", "==", JsonDocument.Parse("\"true\"").RootElement, actions);
            await tools.AddTrigger("situation", "==", JsonDocument.Parse("\"123\"").RootElement, actions);

            ToolTest.AssertJson("""{"name":null,"when":{"field":"met","op":">=","value":378},"actions":[{"type":"stage"}],"repeat":false}""", h.Requests[0].Body);
            ToolTest.AssertJson("""{"name":null,"when":{"field":"stage_flameout","op":"==","value":true},"actions":[{"type":"stage"}],"repeat":false}""", h.Requests[1].Body);
            ToolTest.AssertJson("""{"name":null,"when":{"field":"situation","op":"==","value":"123"},"actions":[{"type":"stage"}],"repeat":false}""", h.Requests[2].Body);
        }
    }
}
