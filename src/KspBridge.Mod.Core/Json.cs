using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// Small JSON reader/writer (KSP ships no JSON library and bundling one risks clashes with other mods).
    /// Values: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool, null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            Reader reader = new Reader(text);
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.End)
                throw reader.Error("unexpected trailing characters");
            return value;
        }

        /// <summary>Parses a JSON object; empty text gives an empty object.</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            object value = Parse(text);
            if (value == null)
                return new Dictionary<string, object>();
            return value as Dictionary<string, object> ?? throw new FormatException("JSON object expected");
        }

        public static string Write(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case double d:
                    WriteNumber(sb, d);
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem)
                            sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                sb.Append("null");
                return;
            }
            if (Math.Abs(d) < 1e15 && d == Math.Floor(d))
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text;
            private int pos;

            public Reader(string text) => this.text = text;

            public bool End => pos >= text.Length;

            public FormatException Error(string message) => new FormatException("JSON " + message + " at " + pos);

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (End)
                    throw Error("unexpected end");
                char c = text[pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
                    throw Error("expected " + word);
                pos += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                Dictionary<string, object> map = new Dictionary<string, object>();
                pos++;
                SkipWhitespace();
                if (!End && text[pos] == '}')
                {
                    pos++;
                    return map;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (End || text[pos] != '"')
                        throw Error("expected property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (End || text[pos] != ':')
                        throw Error("expected ':'");
                    pos++;
                    map[key] = ReadValue();
                    SkipWhitespace();
                    if (End)
                        throw Error("unexpected end in object");
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (text[pos] == '}')
                    {
                        pos++;
                        return map;
                    }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                List<object> list = new List<object>();
                pos++;
                SkipWhitespace();
                if (!End && text[pos] == ']')
                {
                    pos++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (End)
                        throw Error("unexpected end in array");
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (text[pos] == ']')
                    {
                        pos++;
                        return list;
                    }
                    throw Error("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                StringBuilder sb = new StringBuilder();
                pos++;
                while (true)
                {
                    if (End)
                        throw Error("unterminated string");
                    char c = text[pos++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (End)
                        throw Error("unterminated escape");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length)
                                throw Error("bad unicode escape");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default:
                            throw Error("bad escape '\\" + e + "'");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = pos;
                if (text[pos] == '-')
                    pos++;
                while (pos < text.Length && "0123456789.eE+-".IndexOf(text[pos]) >= 0)
                    pos++;
                string token = text.Substring(start, pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error("bad number '" + token + "'");
                return value;
            }
        }
    }

    /// <summary>Typed reads from parsed JSON objects; failures become bad_request.</summary>
    public static class JsonArgs
    {
        public static Dictionary<string, object> Obj(this Dictionary<string, object> map, string key, bool required = false)
        {
            if (map != null && map.TryGetValue(key, out object v) && v != null)
                return v as Dictionary<string, object> ?? throw BridgeException.BadRequest("'" + key + "' must be an object");
            if (required)
                throw BridgeException.BadRequest("'" + key + "' is required");
            return null;
        }

        public static List<object> Arr(this Dictionary<string, object> map, string key, bool required = false)
        {
            if (map != null && map.TryGetValue(key, out object v) && v != null)
                return v as List<object> ?? throw BridgeException.BadRequest("'" + key + "' must be an array");
            if (required)
                throw BridgeException.BadRequest("'" + key + "' is required");
            return null;
        }

        public static string Str(this Dictionary<string, object> map, string key, string fallback = null, bool required = false)
        {
            if (map != null && map.TryGetValue(key, out object v) && v != null)
                return v as string ?? throw BridgeException.BadRequest("'" + key + "' must be a string");
            if (required)
                throw BridgeException.BadRequest("'" + key + "' is required");
            return fallback;
        }

        public static double? Num(this Dictionary<string, object> map, string key, bool required = false)
        {
            if (map != null && map.TryGetValue(key, out object v) && v != null)
                return v is double d ? d : throw BridgeException.BadRequest("'" + key + "' must be a number");
            if (required)
                throw BridgeException.BadRequest("'" + key + "' is required");
            return null;
        }

        public static bool Bool(this Dictionary<string, object> map, string key, bool fallback = false)
        {
            if (map != null && map.TryGetValue(key, out object v) && v != null)
                return v is bool b ? b : throw BridgeException.BadRequest("'" + key + "' must be a boolean");
            return fallback;
        }
    }
}
