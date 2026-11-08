using System.Text.Json;
using KspBridge.App.Broadcast;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class NarrationTests
    {
        [TestMethod]
        [DataRow("누리호", "누리호가")]
        [DataRow("탐사선", "탐사선이")]
        [DataRow("나로", "나로가")]
        [DataRow("KB Hopper", "KB Hopper가")]
        [DataRow("Kerbal X", "Kerbal X가")]
        [DataRow("Probe L", "Probe L이")]
        [DataRow("Kerbin", "Kerbin이")]
        [DataRow("Eagle", "Eagle이")]
        [DataRow("SSTO", "SSTO가")]
        [DataRow("Hopper 2", "Hopper 2가")]
        [DataRow("Hopper 3", "Hopper 3이")]
        public void SubjectParticle(string name, string expected) => Assert.AreEqual(expected, Narration.Subject(name));

        [TestMethod]
        public void CountdownTen()
        {
            var lines = Narration.Countdown("누리호", 10).ToList();
            CollectionAssert.AreEqual(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 }, lines.Select(l => l.T).ToArray());
            Assert.AreEqual(("10", "십", 0.15), (lines[0].Text, lines[0].Say, lines[0].Beep));
            Assert.AreEqual(("9", "구", 0.15), (lines[1].Text, lines[1].Say, lines[1].Beep));
            Assert.AreEqual(("", "", 0.6), (lines[^1].Text, lines[^1].Say, lines[^1].Beep));   // T-0: beep only
        }

        [TestMethod]
        public void CountdownTwentyAndFive()
        {
            var twenty = Narration.Countdown("누리호", 20).ToList();
            Assert.AreEqual((20, "누리호, 발사 20초 전입니다.", "누리호, 발사 이십 초 전입니다."), (twenty[0].T, twenty[0].Text, twenty[0].Say));
            Assert.AreEqual(10, twenty[1].T);
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0 }, Narration.Countdown("x", 5).Select(l => l.T).ToArray());
        }

        [TestMethod]
        [DataRow("""{"type":"liftoff","vessel":"누리호"}""", "누리호가 발사되었습니다.")]
        [DataRow("""{"type":"separation","index":1}""", "1단이 분리되었습니다.")]
        [DataRow("""{"type":"altitude","meters":10000,"atmosphere":false}""", "고도 10킬로미터를 돌파했습니다.")]
        [DataRow("""{"type":"altitude","meters":2500,"atmosphere":false}""", "고도 2.5킬로미터를 돌파했습니다.")]
        [DataRow("""{"type":"altitude","meters":70000,"atmosphere":true}""", "대기권을 벗어났습니다.")]
        [DataRow("""{"type":"situation","situation":"ORBITING","body":"Kerbin"}""", "Kerbin 궤도에 진입했습니다.")]
        [DataRow("""{"type":"situation","situation":"LANDED","body":"Mun"}""", "Mun에 착륙했습니다.")]
        [DataRow("""{"type":"situation","situation":"SPLASHED","body":"Kerbin"}""", "Kerbin에 착수했습니다.")]
        [DataRow("""{"type":"soi","from":"Kerbin","to":"Mun"}""", "Mun 영향권에 진입했습니다.")]
        public void EventLines(string json, string expected) =>
            Assert.AreEqual(expected, Narration.ForEvent(JsonDocument.Parse(json).RootElement));

        [TestMethod]
        [DataRow(1, "일")]
        [DataRow(10, "십")]
        [DataRow(16, "십육")]
        [DataRow(20, "이십")]
        [DataRow(99, "구십구")]
        public void SinoKoreanNumbers(int n, string expected) => Assert.AreEqual(expected, Narration.Sino(n));

        [TestMethod]
        public void SpokenStageNumbers()
        {
            Assert.AreEqual("일단이 분리되었습니다.", Narration.Spoken("1단이 분리되었습니다."));
            Assert.AreEqual("고도 10킬로미터를 돌파했습니다.", Narration.Spoken("고도 10킬로미터를 돌파했습니다."));
        }

        [TestMethod]
        public void LiftoffUsesBroadcastName() =>
            Assert.AreEqual("해머 오비터가 발사되었습니다.",
                Narration.ForEvent(JsonDocument.Parse("""{"type":"liftoff","vessel":"KB Hammer Orbiter 2"}""").RootElement, "해머 오비터"));

        [TestMethod]
        public void UnknownEventIsSilent()
        {
            Assert.IsNull(Narration.ForEvent(JsonDocument.Parse("""{"type":"other"}""").RootElement));
            Assert.IsNull(Narration.ForEvent(JsonDocument.Parse("""{"type":"situation","situation":"FLYING","body":"Kerbin"}""").RootElement));
        }
    }
}
