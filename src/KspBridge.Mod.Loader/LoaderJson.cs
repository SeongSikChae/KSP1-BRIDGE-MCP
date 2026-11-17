using System.Globalization;
using System.Text;

namespace KspBridge.Mod.Loader
{
    /// <summary>Just enough JSON for loader responses. The logic generation has its own full JSON.</summary>
    internal static class LoaderJson
    {
        public static string Quote(string value)
        {
            if (value == null)
                return "null";
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string Error(string code, string message)
            => "{\"ok\":false,\"error\":{\"code\":" + Quote(code) + ",\"message\":" + Quote(message) + "}}";

        public static string Ok(string dataJson) => "{\"ok\":true,\"data\":" + dataJson + "}";
    }
}
