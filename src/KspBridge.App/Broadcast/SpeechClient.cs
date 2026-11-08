using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace KspBridge.App.Broadcast
{
    /// <summary>Runtime speech settings; ApiKey is the decrypted key (never logged).</summary>
    public sealed record SpeechSettings(string Region, string ApiKey, string Voice, string? Style = null, string? Rate = null);

    public static class Ssml
    {
        /// <param name="style">mstts:express-as style (e.g. narrator), or null.</param>
        /// <param name="rate">prosody rate (e.g. +30%), or null.</param>
        public static string Build(string voice, string text, string? style = null, string? rate = null)
        {
            string body = SecurityElement.Escape(text);
            if (!string.IsNullOrWhiteSpace(rate))
            {
                body = $"<prosody rate=\"{SecurityElement.Escape(rate)}\">{body}</prosody>";
            }
            if (!string.IsNullOrWhiteSpace(style))
            {
                body = $"<mstts:express-as style=\"{SecurityElement.Escape(style)}\">{body}</mstts:express-as>";
            }
            return "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"http://www.w3.org/2001/mstts\" xml:lang=\"ko-KR\">"
                + $"<voice name=\"{SecurityElement.Escape(voice)}\">{body}</voice></speak>";
        }
    }

    /// <summary>Text to speech through the Azure Speech REST API (Foundry MAI-Voice voices), cached per voice+text.</summary>
    public interface ISpeechSynthesizer
    {
        /// <summary>Returns the WAV path (24 kHz 16-bit mono PCM).</summary>
        Task<string> SynthesizeAsync(string text, string cacheDir, CancellationToken ct);
    }

    public sealed class SpeechClient(HttpClient http, SpeechSettings settings) : ISpeechSynthesizer
    {
        public async Task<string> SynthesizeAsync(string text, string cacheDir, CancellationToken ct)
        {
            string file = Path.Combine(cacheDir, CacheKey(settings.Voice + "|" + settings.Style + "|" + settings.Rate, text) + ".wav");
            if (File.Exists(file))
            {
                return file;
            }
            using HttpRequestMessage req = new(HttpMethod.Post, $"https://{settings.Region}.tts.speech.microsoft.com/cognitiveservices/v1")
            {
                Content = new StringContent(Ssml.Build(settings.Voice, text, settings.Style, settings.Rate), Encoding.UTF8, "application/ssml+xml"),
            };
            req.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
            req.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
            req.Headers.UserAgent.ParseAdd("KspBridge");
            using HttpResponseMessage res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"speech: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
            }
            byte[] wav = await res.Content.ReadAsByteArrayAsync(ct);
            Directory.CreateDirectory(cacheDir);
            string tmp = file + ".tmp";
            await File.WriteAllBytesAsync(tmp, wav, ct);
            File.Move(tmp, file, overwrite: true);
            return file;
        }

        internal static string CacheKey(string voice, string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voice + "\n" + text)))[..16].ToLowerInvariant();
    }

    public static class WavInfo
    {
        /// <summary>Duration of a PCM WAV from its header (byte rate and data chunk size).</summary>
        public static double Seconds(string path)
        {
            using FileStream fs = File.OpenRead(path);
            using BinaryReader r = new(fs);
            if (new string(r.ReadChars(4)) != "RIFF")
            {
                throw new InvalidDataException("not a RIFF file: " + path);
            }
            r.ReadInt32();
            r.ReadChars(4); // WAVE
            int byteRate = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(r.ReadChars(4));
                int size = r.ReadInt32();
                if (id == "fmt ")
                {
                    r.ReadInt16(); r.ReadInt16(); r.ReadInt32();
                    byteRate = r.ReadInt32();
                    fs.Seek(size - 12, SeekOrigin.Current);
                }
                else if (id == "data")
                {
                    // Streaming responses may carry 0 or 0xFFFFFFFF as data size: use the real remaining length.
                    long dataSize = size <= 0 || fs.Position + size > fs.Length ? fs.Length - fs.Position : size;
                    return byteRate > 0 ? (double)dataSize / byteRate : 0;
                }
                else
                {
                    fs.Seek(size + (size & 1), SeekOrigin.Current);
                }
            }
            return 0;
        }
    }
}
