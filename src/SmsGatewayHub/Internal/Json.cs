// SMSGatewayHub .NET SDK - tiny dependency-free JSON reader/writer.
// Parses to Dictionary<string, object>, List<object>, string, long, double, bool and null.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SmsGatewayHub.Internal
{
    internal static class Json
    {
        private const int MaxDepth = 128;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON text is null");
            var p = new Parser(text);
            p.SkipWs();
            object v = p.ReadValue(0);
            p.SkipWs();
            if (p.Pos != text.Length) throw new FormatException("Unexpected trailing characters in JSON");
            return v;
        }

        public static bool TryParse(string text, out object value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Object graph too deep to serialize");
            if (v == null || v is DBNull) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is char) { WriteString(sb, v.ToString()); return; }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }
            if (v is Guid) { WriteString(sb, v.ToString()); return; }
            if (v is DateTime) { WriteString(sb, ((DateTime)v).ToString("o", CultureInfo.InvariantCulture)); return; }
            if (v is DateTimeOffset) { WriteString(sb, ((DateTimeOffset)v).ToString("o", CultureInfo.InvariantCulture)); return; }
            if (v is double)
            {
                double d = (double)v;
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is float)
            {
                float f = (float)v;
                if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is sbyte || v is uint || v is ulong || v is ushort || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, e.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var en = v as IEnumerable;
            if (en != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            // Plain objects / anonymous types: public readable instance properties.
            sb.Append('{');
            bool firstProp = true;
            foreach (PropertyInfo pi in v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
                if (!firstProp) sb.Append(',');
                firstProp = false;
                WriteString(sb, pi.Name);
                sb.Append(':');
                Write(sb, pi.GetValue(v, null), depth + 1);
            }
            sb.Append('}');
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;

            public Parser(string s) { _s = s; }

            public void SkipWs()
            {
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿') Pos++;
                    else break;
                }
            }

            private FormatException Err(string what)
            {
                return new FormatException("Invalid JSON at position " + Pos + ": " + what);
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Err("nesting too deep");
                if (Pos >= _s.Length) throw Err("unexpected end");
                char c = _s[Pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Err("unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw Err("expected " + word);
                Pos += word.Length;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                Pos++; // {
                SkipWs();
                if (Pos < _s.Length && _s[Pos] == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    if (Pos >= _s.Length || _s[Pos] != '"') throw Err("expected property name");
                    string key = ReadString();
                    SkipWs();
                    if (Pos >= _s.Length || _s[Pos] != ':') throw Err("expected ':'");
                    Pos++;
                    SkipWs();
                    d[key] = ReadValue(depth + 1);
                    SkipWs();
                    if (Pos >= _s.Length) throw Err("unexpected end in object");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == '}') { Pos++; return d; }
                    throw Err("expected ',' or '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                Pos++; // [
                SkipWs();
                if (Pos < _s.Length && _s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (Pos >= _s.Length) throw Err("unexpected end in array");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == ']') { Pos++; return list; }
                    throw Err("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                Pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= _s.Length) throw Err("unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= _s.Length) throw Err("bad escape");
                    char e = _s[Pos++];
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
                            if (Pos + 4 > _s.Length) throw Err("bad unicode escape");
                            int code;
                            if (!int.TryParse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw Err("bad unicode escape");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default: throw Err("bad escape");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                bool isFloat = false;
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c >= '0' && c <= '9') { Pos++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; Pos++; continue; }
                    break;
                }
                string num = _s.Substring(start, Pos - start);
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw Err("bad number");
            }
        }
    }
}
