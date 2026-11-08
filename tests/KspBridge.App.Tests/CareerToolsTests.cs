using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class CareerToolsTests
    {
        [TestMethod]
        public async Task CareerAndScienceToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            CareerTools career = new(client);
            ScienceTools science = new(client);

            await career.GetCareer(default);
            await career.ListContracts("active");
            await career.AcceptContract("42", default);
            await career.DeclineContract("-7", default);
            await science.GetScience(default);
            await science.RunExperiment("123:4", default);
            await science.DiscardScience("123:4", default);
            await career.ListFacilities(default);
            await career.UpgradeFacility("mission_control", default);

            Assert.AreEqual("/api/career", h.Requests[0].PathAndQuery);
            Assert.AreEqual("/api/contracts?state=active", h.Requests[1].PathAndQuery);
            Assert.AreEqual((HttpMethod.Post, "/api/contracts/42/accept"), (h.Requests[2].Method, h.Requests[2].PathAndQuery));
            Assert.AreEqual("/api/contracts/-7/decline", h.Requests[3].PathAndQuery);
            Assert.AreEqual((HttpMethod.Get, "/api/science"), (h.Requests[4].Method, h.Requests[4].PathAndQuery));
            ToolTest.AssertJson("""{"id":"123:4"}""", h.Requests[5].Body);
            Assert.AreEqual("/api/science/discard", h.Requests[6].PathAndQuery);
            Assert.AreEqual((HttpMethod.Get, "/api/facilities"), (h.Requests[7].Method, h.Requests[7].PathAndQuery));
            Assert.AreEqual((HttpMethod.Post, "/api/facilities/mission_control/upgrade"), (h.Requests[8].Method, h.Requests[8].PathAndQuery));
        }

        [TestMethod]
        public async Task ContractIdKeepsAllDigits()
        {
            var (client, h) = ToolTest.Client();
            CareerTools career = new(client);

            await career.AcceptContract("192925036314244835", default);
            string bad = await career.DeclineContract("1.5e3", default);

            Assert.AreEqual("/api/contracts/192925036314244835/accept", h.Requests[0].PathAndQuery);
            StringAssert.StartsWith(bad, "error: bad_request");
            Assert.HasCount(1, h.Requests);
        }

        [TestMethod]
        public async Task LaunchToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            LaunchTools launch = new(client);

            await launch.ListCraft("VAB");
            await launch.ListCrew(default);
            await launch.LaunchCraft("KB Test Flea", crew: ["Jebediah Kerman"]);
            await launch.LaunchCraft("Plane", "SPH");
            await launch.RecoverVessel(default);
            await launch.RevertToLaunch(default);

            Assert.AreEqual("/api/craft?facility=VAB", h.Requests[0].PathAndQuery);
            Assert.AreEqual("/api/crew", h.Requests[1].PathAndQuery);
            ToolTest.AssertJson("""{"craft":"KB Test Flea","facility":"VAB","site":null,"crew":["Jebediah Kerman"]}""", h.Requests[2].Body);
            ToolTest.AssertJson("""{"craft":"Plane","facility":"SPH","site":null,"crew":[]}""", h.Requests[3].Body);
            Assert.AreEqual((HttpMethod.Post, "/api/recover"), (h.Requests[4].Method, h.Requests[4].PathAndQuery));
            Assert.AreEqual("/api/revert", h.Requests[5].PathAndQuery);
        }

        [TestMethod]
        public async Task AscentToolsMapToHttp()
        {
            var (client, h) = ToolTest.Client();
            AscentTools ascent = new(client);

            await ascent.GetStages(default);
            await ascent.SetAttitude(80, 90);
            await ascent.SetAttitude(45, 90, "sas", 0);
            await ascent.ReleaseAttitude(default);
            await ascent.WaitUntil("altitude_asl", ">=", JsonDocument.Parse("1000").RootElement, 30);
            await ascent.WaitUntil("situation", "==", JsonDocument.Parse("\"LANDED\"").RootElement);

            Assert.AreEqual("/api/stages", h.Requests[0].PathAndQuery);
            ToolTest.AssertJson("""{"mode":"own","pitch":80,"heading":90,"roll":null}""", h.Requests[1].Body);
            ToolTest.AssertJson("""{"mode":"sas","pitch":45,"heading":90,"roll":0}""", h.Requests[2].Body);
            Assert.AreEqual("/api/attitude/off", h.Requests[3].PathAndQuery);
            ToolTest.AssertJson("""{"field":"altitude_asl","op":">=","value":1000,"timeout":30}""", h.Requests[4].Body);
            ToolTest.AssertJson("""{"field":"situation","op":"==","value":"LANDED","timeout":60}""", h.Requests[5].Body);
        }
    }
}
