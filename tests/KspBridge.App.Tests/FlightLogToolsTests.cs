namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class FlightLogToolsTests
    {
        [TestMethod]
        public async Task FlightLogToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            FlightLogTools tools = new(client);

            await tools.ListFlights();
            await tools.ListFlights("KB Hopper", 5);
            await tools.GetFlightLog("22094-123");
            await tools.GetFlightLog("22094-123", fromMet: 10.5, toMet: 60, step: 1, types: "e");
            await tools.GetFlightLog("22094-123", around: "situation", window: 5);

            Assert.AreEqual((HttpMethod.Get, "/api/flights?limit=20"), (h.Requests[0].Method, h.Requests[0].PathAndQuery));
            Assert.AreEqual("/api/flights?limit=5&vessel=KB%20Hopper", h.Requests[1].PathAndQuery);
            Assert.AreEqual("/api/flights/22094-123/log", h.Requests[2].PathAndQuery);
            Assert.AreEqual("/api/flights/22094-123/log?from_met=10.5&to_met=60&step=1&types=e", h.Requests[3].PathAndQuery);
            Assert.AreEqual("/api/flights/22094-123/log?around=situation&window=5", h.Requests[4].PathAndQuery);
        }
    }
}
