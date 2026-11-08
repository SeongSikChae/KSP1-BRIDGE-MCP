using System.Text.Json;

namespace KspBridge.App.Tests
{
    /// <summary>Ascent guidance, read_craft and the ascent_state field (docs/ksp-bridge/0040, 0041).</summary>
    [TestClass]
    public sealed class AscentCraftToolsTests
    {
        [TestMethod]
        public async Task AscentGuidanceMapsArguments()
        {
            var (client, h) = ToolTest.Client();
            FlightTools tools = new(client);

            await tools.AscentGuidance(80000);
            await tools.AscentGuidance(100000, 45, 80, 12, 3, false, 500);

            Assert.AreEqual((HttpMethod.Post, "/api/ascent/start"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            ToolTest.AssertJson("""{"target_apoapsis":80000,"heading":90,"turn_start_speed":60,"turn_angle":10,"max_aoa_deg":5,"auto_stage":true,"ap_tolerance":200}""", h.Requests[0].Body);
            ToolTest.AssertJson("""{"target_apoapsis":100000,"heading":45,"turn_start_speed":80,"turn_angle":12,"max_aoa_deg":3,"auto_stage":false,"ap_tolerance":500}""", h.Requests[1].Body);
        }

        [TestMethod]
        public async Task CancelAscentPosts()
        {
            var (client, h) = ToolTest.Client();
            await new FlightTools(client).CancelAscent(default);
            Assert.AreEqual((HttpMethod.Post, "/api/ascent/cancel"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
        }

        [TestMethod]
        public async Task ReadCraftEscapesNameAndFacility()
        {
            var (client, h) = ToolTest.Client();
            CraftTools tools = new(client);

            await tools.ReadCraft("My Craft");
            await tools.ReadCraft("Plane&1", "SPH");

            Assert.AreEqual((HttpMethod.Get, "/api/craft/read?name=My%20Craft&facility=VAB"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            Assert.AreEqual("/api/craft/read?name=Plane%261&facility=SPH", h.Requests[1].PathAndQuery);
        }

        [TestMethod]
        public async Task AscentStateValueStaysString()
        {
            var (client, h) = ToolTest.Client();
            JsonElement condition = JsonDocument.Parse("""{"any":[{"field":"ascent_state","op":"==","value":"done"},{"field":"apoapsis","op":">=","value":"80000"}]}""").RootElement;

            await new AscentTools(client).WaitUntil(condition: condition);

            ToolTest.AssertJson(
                """{"condition":{"any":[{"field":"ascent_state","op":"==","value":"done"},{"field":"apoapsis","op":">=","value":80000}]},"timeout":60}""",
                h.Requests[0].Body);
            StringAssert.Contains(TriggerTools.Fields, "ascent_state");
        }
    }
}
