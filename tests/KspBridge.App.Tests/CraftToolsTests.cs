using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class CraftToolsTests
    {
        [TestMethod]
        public async Task ListPartsBuildsQuery()
        {
            var (client, handler) = ToolTest.Client();
            CraftTools tools = new(client);

            await tools.ListParts();
            await tools.ListParts("Fuel Tank", false, true, "flea");

            Assert.AreEqual((HttpMethod.Get, "/api/parts?unlocked=true&detail=false", (string?)null), handler.Requests[0]);
            Assert.AreEqual((HttpMethod.Get, "/api/parts?unlocked=false&detail=true&category=Fuel%20Tank&name=flea", (string?)null), handler.Requests[1]);
        }

        [TestMethod]
        public async Task BuildCraftSendsDesignAsRoot()
        {
            var (client, handler) = ToolTest.Client();
            CraftTools tools = new(client);
            JsonElement design = JsonDocument.Parse("""{"part":"mk1pod.v2","children":[{"node":"bottom","part":"solidBooster.sm.v2"}]}""").RootElement;

            await tools.BuildCraft("Test", design);

            Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
            Assert.AreEqual("/api/craft/build", handler.Requests[0].PathAndQuery);
            ToolTest.AssertJson(
                """{"name":"Test","facility":"VAB","dry_run":true,"overwrite":false,"root":{"part":"mk1pod.v2","children":[{"node":"bottom","part":"solidBooster.sm.v2"}]}}""",
                handler.Requests[0].Body);
        }

        [TestMethod]
        public async Task BuildCraftAcceptsDesignAsJsonString()
        {
            var (client, handler) = ToolTest.Client();
            CraftTools tools = new(client);
            JsonElement design = JsonDocument.Parse("\"{\\\"part\\\":\\\"solidBooster.sm.v2\\\",\\\"thrust_limit\\\":40}\"").RootElement;

            await tools.BuildCraft("Test", design);
            string error = await tools.BuildCraft("Bad", JsonDocument.Parse("\"{not json\"").RootElement);

            ToolTest.AssertJson(
                """{"name":"Test","facility":"VAB","dry_run":true,"overwrite":false,"root":{"part":"solidBooster.sm.v2","thrust_limit":40}}""",
                handler.Requests[0].Body);
            Assert.AreEqual(1, handler.Requests.Count);
            StringAssert.StartsWith(error, "error: bad_request:");
        }
    }
}
