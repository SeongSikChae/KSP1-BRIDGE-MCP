using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class PartTestToolsTests
    {
        [TestMethod]
        public async Task PartTestToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            PartTestTools tools = new(client);

            await tools.ListPartTests(default);
            await tools.RunPartTest(JsonDocument.Parse("\"HeatShield1\"").RootElement);
            await tools.RunPartTest(JsonDocument.Parse("4012345678").RootElement, force: true);
            await tools.ArmPartTest(JsonDocument.Parse("\"4012345678\"").RootElement);

            Assert.AreEqual((HttpMethod.Get, "/api/part-tests", (string?)null), h.Requests[0]);
            Assert.AreEqual((HttpMethod.Post, "/api/part-tests/run"), (h.Requests[1].Method, h.Requests[1].PathAndQuery));
            ToolTest.AssertJson("""{"part":"HeatShield1","force":false}""", h.Requests[1].Body);
            ToolTest.AssertJson("""{"part":4012345678,"force":true}""", h.Requests[2].Body);
            Assert.AreEqual((HttpMethod.Post, "/api/part-tests/arm"), (h.Requests[3].Method, h.Requests[3].PathAndQuery));
            ToolTest.AssertJson("""{"part":4012345678}""", h.Requests[3].Body);
        }
    }
}
