namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class DialogToolsTests
    {
        [TestMethod]
        public async Task DialogToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            DialogTools tools = new(client);

            await tools.ListDialogs(default);
            await tools.ClickDialogButton("회수", "LaunchSiteObstructed");
            await tools.ClickDialogButton("1");
            await tools.CloseExperimentDialog(default);

            Assert.AreEqual((HttpMethod.Get, "/api/dialogs"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            ToolTest.AssertJson("""{"dialog":"LaunchSiteObstructed","button":"회수"}""", h.Requests[1].Body);
            ToolTest.AssertJson("""{"dialog":null,"button":1}""", h.Requests[2].Body);
            Assert.AreEqual("/api/dialogs/close-experiment", h.Requests[3].PathAndQuery);
        }
    }
}
