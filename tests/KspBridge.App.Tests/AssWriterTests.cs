using KspBridge.App.Broadcast;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class AssWriterTests
    {
        [TestMethod]
        [DataRow(0.0, "0:00:00.00")]
        [DataRow(65.25, "0:01:05.25")]
        [DataRow(3725.5, "1:02:05.50")]
        public void TimeFormat(double seconds, string expected) => Assert.AreEqual(expected, AssWriter.Time(seconds));

        [TestMethod]
        public void DialogueTimingAndFont()
        {
            string ass = AssWriter.Build(
            [
                (new Cue(1.0, "9"), 0.4),          // cut at the next cue
                (new Cue(2.0, "8"), 0.4),
                (new Cue(10.0, "누리호가 발사되었습니다."), 3.2),  // narration longer than the minimum
                (new Cue(30.0, "끝"), 0),          // minimum 2.5 s
            ], "Malgun Gothic");

            StringAssert.Contains(ass, "Style: Default,Malgun Gothic,64,");
            StringAssert.Contains(ass, "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,9\n");
            StringAssert.Contains(ass, "Dialogue: 0,0:00:02.00,0:00:04.50,Default,,0,0,0,,8\n");
            StringAssert.Contains(ass, "Dialogue: 0,0:00:10.00,0:00:13.20,Default,,0,0,0,,누리호가 발사되었습니다.\n");
            StringAssert.Contains(ass, "Dialogue: 0,0:00:30.00,0:00:32.50,Default,,0,0,0,,끝\n");
        }
    }
}
