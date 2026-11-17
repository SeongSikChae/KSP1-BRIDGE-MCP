using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Core.Tests
{
    [TestClass]
    public sealed class EventLogTests
    {
        [TestMethod]
        public void Since_ReturnsNewerEvents_InOrder_WithLimit()
        {
            EventLog log = new EventLog(() => 10);
            for (int i = 0; i < 5; i++)
                log.Add("e" + i);

            List<Dictionary<string, object>> events = log.Since(2, 2);

            CollectionAssert.AreEqual(new[] { "e2", "e3" }, events.Select(e => e["type"]).ToArray());
            Assert.AreEqual(5L, log.LastSeq);
        }

        [TestMethod]
        public void Capacity_DropsOldest()
        {
            EventLog log = new EventLog(() => 0);
            for (int i = 0; i < EventLog.Capacity + 10; i++)
                log.Add("e");

            List<Dictionary<string, object>> all = log.Since(0, 10000);

            Assert.AreEqual(EventLog.Capacity, all.Count);
            Assert.AreEqual(11L, all[0]["seq"]);
        }

        [TestMethod]
        public void Snapshot_RestoresThroughJson()
        {
            EventLog log = new EventLog(() => 5);
            log.Add("plan_submitted", 3, new Dictionary<string, object> { ["steps"] = 2 });
            log.Add("plan_aborted", 3);

            EventLog next = new EventLog(() => 6);
            next.Restore(Json.ParseObject(Json.Write(log.Snapshot())));
            next.Add("logic_started");

            Assert.AreEqual(3L, next.LastSeq);
            CollectionAssert.AreEqual(new[] { "plan_submitted", "plan_aborted", "logic_started" },
                next.Since(0, 10).Select(e => e["type"]).ToArray());
        }
    }
}
