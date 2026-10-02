using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ApocaLanguage
{
    // =====================================================================
    //  Minimal, forgiving JSON for translation files: one flat object of
    //  "key": "value" pairs, kept in file order. Accepts // and /* */
    //  comments, trailing commas, a UTF-8 BOM, and non-string values
    //  (numbers/bools are kept as their text; nested objects/arrays are skipped).
    // =====================================================================
    public static class Json
    {
        public class ParseException : Exception
        {
            public ParseException(string msg, int line) : base(msg + " (line " + line + ")") { }
        }

        public static List<KeyValuePair<string, string>> ParseFlatObject(string text)
        {
            var p = new Parser(text);
            return p.ParseRoot();
        }

        private class Parser
        {
            private readonly string _s;
            private int _i;
            public Parser(string s) { _s = s ?? ""; _i = 0; if (_s.Length > 0 && _s[0] == '﻿') _i = 1; }

            private int Line { get { int n = 1; for (int k = 0; k < _i && k < _s.Length; k++) if (_s[k] == '\n') n++; return n; } }
            private ParseException Err(string m) { return new ParseException(m, Line); }

            public List<KeyValuePair<string, string>> ParseRoot()
            {
                var list = new List<KeyValuePair<string, string>>();
                Ws();
                if (_i >= _s.Length) return list;
                if (_s[_i] != '{') throw Err("expected '{' at the start of the file");
                _i++;
                while (true)
                {
                    Ws();
                    if (_i >= _s.Length) throw Err("unexpected end of file (missing '}')");
                    if (_s[_i] == '}') { _i++; break; }
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] != '"') throw Err("expected a \"quoted key\"");
                    string key = Str();
                    Ws();
                    if (_i >= _s.Length || _s[_i] != ':') throw Err("expected ':' after key \"" + Short(key) + "\"");
                    _i++;
                    Ws();
                    string val = Value();
                    if (val != null) list.Add(new KeyValuePair<string, string>(key, val));
                    Ws();
                    if (_i < _s.Length && _s[_i] == ',') { _i++; continue; }
                    if (_i < _s.Length && _s[_i] == '}') { _i++; break; }
                    if (_i >= _s.Length) throw Err("unexpected end of file (missing '}')");
                    throw Err("expected ',' or '}' after the value of \"" + Short(key) + "\"");
                }
                return list;
            }

            private static string Short(string k) { return k.Length > 40 ? k.Substring(0, 40) + "..." : k; }

            private void Ws()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿') { _i++; continue; }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; continue; }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                        _i = end < 0 ? _s.Length : end + 2; continue;
                    }
                    break;
                }
            }

            private string Value()
            {
                if (_i >= _s.Length) throw Err("missing value");
                char c = _s[_i];
                if (c == '"') return Str();
                if (c == '{' || c == '[') { SkipNested(); return null; }
                int st = _i;
                while (_i < _s.Length && ",}] \t\r\n/".IndexOf(_s[_i]) < 0) _i++;
                var raw = _s.Substring(st, _i - st);
                if (raw == "null") return null;
                if (raw.Length == 0) throw Err("missing value");
                return raw;
            }

            private void SkipNested()
            {
                int depth = 0;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '"') { Str(); continue; }
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { depth--; if (depth == 0) { _i++; return; } }
                    _i++;
                }
                throw Err("unterminated object/array");
            }

            private string Str()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw Err("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) throw Err("unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '/': sb.Append('/'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Err("bad \\u escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Err("bad \\u escape");
                            sb.Append((char)code); _i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------- writing
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// Writes a flat object, one pair per line, UTF-8 without BOM.
        public static void WriteFlatObject(string path, IEnumerable<KeyValuePair<string, string>> pairs)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var kv in pairs)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  \"").Append(Escape(kv.Key)).Append("\": \"").Append(Escape(kv.Value ?? "")).Append('"');
            }
            sb.Append("\n}\n");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
