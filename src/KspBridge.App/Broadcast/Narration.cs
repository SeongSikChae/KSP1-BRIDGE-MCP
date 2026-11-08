using System.Globalization;
using System.Text.Json;

namespace KspBridge.App.Broadcast
{
    /// <summary>A subtitle/narration line on the recording timeline (seconds from the first recorded frame).</summary>
    /// <param name="Beep">Beep length in seconds (0 = none).</param>
    /// <param name="Say">Text sent to speech when it differs from the subtitle (e.g. "구" for "9").</param>
    /// <param name="Fixed">Countdown line: keeps its time; event lines are moved after a line still playing.</param>
    public sealed record Cue(double At, string Text, string? Audio = null, double Beep = 0, string? Say = null, bool Fixed = false);

    /// <summary>Korean broadcast lines (countdown and flight events).</summary>
    public static class Narration
    {
        /// <summary>Name followed by the subject particle 이/가 chosen by the final sound.</summary>
        public static string Subject(string name)
        {
            string n = name.TrimEnd();
            if (n.Length == 0)
            {
                return name;
            }
            char c = n[^1];
            string word = n[(n.LastIndexOf(' ') + 1)..];
            bool batchim = c switch
            {
                >= '가' and <= '힣' => (c - '가') % 28 != 0,
                >= '0' and <= '9' => "013678".Contains(c),          // 영 일 삼 육 칠 팔
                // A single letter or an acronym is read by letter names: 엘 엠 엔 알
                _ when word.All(char.IsUpper) => "LMNR".Contains(char.ToUpperInvariant(c)),
                // English word: ends in a nasal or l sound (Kerbin → 커빈, Mun → 문, Eagle → 이글)
                _ => word.EndsWith("ng", StringComparison.OrdinalIgnoreCase) || "mnlMNL".Contains(c)
                     || word.EndsWith("le", StringComparison.OrdinalIgnoreCase),
            };
            return n + (batchim ? "이" : "가");
        }

        private static readonly string[] SinoDigits = ["영", "일", "이", "삼", "사", "오", "육", "칠", "팔", "구"];

        /// <summary>Sino-Korean reading of 0..99 (십, 이십삼 ...), as launch countdowns are read.</summary>
        public static string Sino(int n)
        {
            if (n is < 0 or > 99)
            {
                return n.ToString(CultureInfo.InvariantCulture);
            }
            if (n < 10)
            {
                return SinoDigits[n];
            }
            int tens = n / 10, ones = n % 10;
            return (tens > 1 ? SinoDigits[tens] : "") + "십" + (ones > 0 ? SinoDigits[ones] : "");
        }

        /// <summary>
        /// Countdown lines from T-seconds to T-0 (T = seconds before ignition): one short spoken number per second
        /// (십, 구 … 일, subtitles 10, 9 … 1) so each fits in its second, then a long beep at T-0 (no word: the liftoff line follows). The longer "발사 20초 전" line
        /// is only used at T-20, where the next line is 10 s away.
        /// </summary>
        public static IEnumerable<(int T, string Text, string Say, double Beep)> Countdown(string craft, int seconds)
        {
            if (seconds >= 20)
            {
                yield return (20, $"{craft}, 발사 20초 전입니다.", $"{craft}, 발사 이십 초 전입니다.", 0);
            }
            for (int t = Math.Min(seconds, 10); t >= 1; t--)
            {
                yield return (t, t.ToString(CultureInfo.InvariantCulture), Sino(t), 0.15);
            }
            yield return (0, "", "", 0.6);   // T-0: ignition beep only; the liftoff line follows
        }

        /// <summary>Spoken form of an event line: stage numbers read in Sino-Korean ("1단" → "일단").</summary>
        public static string Spoken(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)단", m => Sino(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) + "단");

        /// <summary>Line for a /api/broadcast/events event, or null when it is not narrated.</summary>
        /// <param name="craft">Broadcast name (start_broadcast craftName); the vessel name when null.</param>
        public static string? ForEvent(JsonElement e, string? craft = null)
        {
            switch (Str(e, "type"))
            {
                case "liftoff":
                    return $"{Subject(string.IsNullOrWhiteSpace(craft) ? Str(e, "vessel") : craft)} 발사되었습니다.";
                case "separation":
                    return e.TryGetProperty("index", out JsonElement i) ? $"{i.GetInt32()}단이 분리되었습니다." : null;
                case "altitude":
                    if (e.TryGetProperty("atmosphere", out JsonElement atmo) && atmo.ValueKind == JsonValueKind.True)
                    {
                        return "대기권을 벗어났습니다.";
                    }
                    double km = e.GetProperty("meters").GetDouble() / 1000;
                    return $"고도 {km.ToString("0.#", CultureInfo.InvariantCulture)}킬로미터를 돌파했습니다.";
                case "situation":
                    return Str(e, "situation") switch
                    {
                        "ORBITING" => $"{Str(e, "body")} 궤도에 진입했습니다.",
                        "LANDED" => $"{Str(e, "body")}에 착륙했습니다.",
                        "SPLASHED" => $"{Str(e, "body")}에 착수했습니다.",
                        _ => null,
                    };
                case "soi":
                    return $"{Str(e, "to")} 영향권에 진입했습니다.";
                default:
                    return null;
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    }
}
