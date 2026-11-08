using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KspBridge.Json
{
    /// <summary>
    /// Minimal JSON for the mod (stock KSP ships no JSON library).
    /// Serialize: IDictionary (string keys), IEnumerable, string, bool, numbers, enums (as string), null.
    /// Parse: object → Dictionary&lt;string, object&gt;, array → List&lt;object&gt;, number → double, string, bool, null.
    /// </summary>
    public static class Json
    {
        public static string Serialize(object value)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
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
                case int or long or short or byte or uint or ulong or ushort or sbyte:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case IDictionary dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        Write(sb, entry.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                sb.Append("null");
            }
            else
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
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
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Parses JSON text. Throws FormatException on invalid input.</summary>
        public static object Parse(string text)
        {
            Parser parser = new Parser(text);
            object result = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw new FormatException("Unexpected trailing characters at " + parser.Position);
            }
            return result;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s ?? string.Empty;
            }

            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private char Peek()
            {
                if (_i >= _s.Length) throw new FormatException("Unexpected end of JSON");
                return _s[_i];
            }

            private void Expect(char c)
            {
                if (Peek() != c) throw new FormatException($"Expected '{c}' at {_i}");
                _i++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new FormatException($"Unexpected character '{c}' at {_i}");
                }
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw new FormatException($"Expected '{word}' at {_i}");
                }
                _i += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Expect('{');
                Dictionary<string, object> result = new Dictionary<string, object>();
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            private List<object> ParseArray()
            {
                Expect('[');
                List<object> result = new List<object>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect(']');
                    return result;
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    _i++;
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    char e = Peek();
                    _i++;
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
                            if (_i + 4 > _s.Length) throw new FormatException("Bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default:
                            throw new FormatException($"Bad escape '\\{e}' at {_i}");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    throw new FormatException($"Bad number '{token}' at {start}");
                }
                return d;
            }
        }
    }
}
