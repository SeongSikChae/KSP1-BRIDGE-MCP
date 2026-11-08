using System.Globalization;
using System.Text;

namespace KspBridge.App.Broadcast
{
    /// <summary>ASS subtitles burned into the broadcast video.</summary>
    public static class AssWriter
    {
        public const double MinSeconds = 2.5;

        /// <param name="cues">Cues ordered by time with the narration length of each (0 when there is no audio).</param>
        public static string Build(IReadOnlyList<(Cue Cue, double Duration)> cues, string font)
        {
            StringBuilder sb = new();
            sb.Append("[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nWrapStyle: 0\n\n");
            sb.Append("[V4+ Styles]\n");
            sb.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
            sb.Append($"Style: Default,{font},64,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,1,0,0,0,100,100,0,0,1,3,1,2,60,60,60,1\n\n");
            sb.Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
            for (int i = 0; i < cues.Count; i++)
            {
                double start = cues[i].Cue.At;
                double end = start + Math.Max(cues[i].Duration, MinSeconds);
                if (i + 1 < cues.Count)
                {
                    end = Math.Min(end, cues[i + 1].Cue.At);
                }
                if (end <= start || string.IsNullOrWhiteSpace(cues[i].Cue.Text))
                {
                    continue;
                }
                string text = cues[i].Cue.Text.Replace("\r", "").Replace("\n", "\\N").Replace("{", "(").Replace("}", ")");
                sb.Append($"Dialogue: 0,{Time(start)},{Time(end)},Default,,0,0,0,,{text}\n");
            }
            return sb.ToString();
        }

        /// <summary>h:mm:ss.cc</summary>
        public static string Time(double seconds)
        {
            long cs = (long)Math.Round(Math.Max(0, seconds) * 100);
            return string.Create(CultureInfo.InvariantCulture, $"{cs / 360000}:{cs / 6000 % 60:00}:{cs / 100 % 60:00}.{cs % 100:00}");
        }
    }
}
