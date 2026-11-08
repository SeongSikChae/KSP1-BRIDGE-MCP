using System.Net;
using KspBridge.App.Broadcast;

namespace KspBridge.App.Tests
{
    internal static class TestWav
    {
        /// <summary>16-bit mono PCM WAV of the given length (silence).</summary>
        public static byte[] Make(double seconds, int sampleRate = 24000)
        {
            int dataSize = (int)(seconds * sampleRate) * 2;
            using MemoryStream ms = new();
            using BinaryWriter w = new(ms);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + dataSize); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(dataSize); w.Write(new byte[dataSize]);
            return ms.ToArray();
        }

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kspbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestClass]
    public sealed class SpeechClientTests
    {
        [TestMethod]
        public async Task SynthesizeSendsSsmlAndCaches()
        {
            List<HttpRequestMessage> requests = [];
            List<string> bodies = [];
            RecordingHandler handler = new(req =>
            {
                requests.Add(req);
                bodies.Add(req.Content!.ReadAsStringAsync().Result);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestWav.Make(1.0)) };
            });
            SpeechClient client = new(new HttpClient(handler), new SpeechSettings("southeastasia", "k3y", "ko-KR-Haena:MAI-Voice-2.1-Flash"));
            string dir = TestWav.TempDir();

            string first = await client.SynthesizeAsync("누리호가 <발사> & 성공", dir, default);
            string second = await client.SynthesizeAsync("누리호가 <발사> & 성공", dir, default);

            Assert.AreEqual(first, second);
            Assert.HasCount(1, requests);
            HttpRequestMessage r = requests[0];
            Assert.AreEqual("https://southeastasia.tts.speech.microsoft.com/cognitiveservices/v1", r.RequestUri!.ToString());
            Assert.AreEqual("k3y", r.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
            Assert.AreEqual("riff-24khz-16bit-mono-pcm", r.Headers.GetValues("X-Microsoft-OutputFormat").Single());
            Assert.AreEqual("application/ssml+xml", r.Content!.Headers.ContentType!.MediaType);
            StringAssert.Contains(bodies[0], "<voice name=\"ko-KR-Haena:MAI-Voice-2.1-Flash\">누리호가 &lt;발사&gt; &amp; 성공</voice>");
            Assert.AreEqual(1.0, WavInfo.Seconds(first), 0.001);
        }

        [TestMethod]
        public void SsmlStyleAndRate()
        {
            string ssml = Ssml.Build("ko-KR-Harper:MAI-Voice-2.1", "구", "narrator", "+30%");
            StringAssert.Contains(ssml, "xmlns:mstts=\"http://www.w3.org/2001/mstts\"");
            StringAssert.Contains(ssml, "<voice name=\"ko-KR-Harper:MAI-Voice-2.1\"><mstts:express-as style=\"narrator\"><prosody rate=\"+30%\">구</prosody></mstts:express-as></voice>");
            StringAssert.Contains(Ssml.Build("v", "x"), "<voice name=\"v\">x</voice>");
        }

        [TestMethod]
        public async Task SynthesizeErrorDoesNotLeakKey()
        {
            RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            SpeechClient client = new(new HttpClient(handler), new SpeechSettings("southeastasia", "secret-key", "v"));
            HttpRequestException ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.SynthesizeAsync("x", TestWav.TempDir(), default));
            StringAssert.Contains(ex.Message, "401");
            Assert.IsFalse(ex.Message.Contains("secret-key"));
        }

        [TestMethod]
        public void WavSecondsFromHeader()
        {
            string path = Path.Combine(TestWav.TempDir(), "a.wav");
            File.WriteAllBytes(path, TestWav.Make(2.5));
            Assert.AreEqual(2.5, WavInfo.Seconds(path), 0.001);
        }
    }
}
