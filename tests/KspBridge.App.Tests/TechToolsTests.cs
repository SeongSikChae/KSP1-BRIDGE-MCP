namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class TechToolsTests
    {
        [TestMethod]
        public async Task TechToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            TechTools tools = new(client);

            await tools.ListTech();
            await tools.ListTech(all: true);
            await tools.ResearchTech("basicRocketry", default);

            Assert.AreEqual((HttpMethod.Get, "/api/tech"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            Assert.AreEqual("/api/tech?all=true", h.Requests[1].PathAndQuery);
            Assert.AreEqual((HttpMethod.Post, "/api/tech/basicRocketry/research"), (h.Requests[2].Method, h.Requests[2].PathAndQuery));
        }
    }
}
