using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ApocaLanguage
{
    /// One language folder (RU, DE, ...). EN is built in and has no folder.
    public class LanguageInfo
    {
        public string Code;          // folder name, upper case
        public string Folder;        // full path, null for EN
        public string DisplayName;   // native name shown in the dropdown
        public string Version = "";
        public string FontFile;      // optional .ttf/.otf inside the folder (or font.json "file")
        // font.json (optional, in the language folder): how translated texts are drawn with FontFile
        public bool Upper;           // "uppercase": every translation in capitals
        public float FontSize = 1f;  // "size": multiplier on the game's font size
        public float FontWidth = 1f; // "width": horizontal scale (0.85 = narrower)
        public float Thickness;      // "thickness": 0..0.5 extra weight (TextMeshPro face dilate)
        public float Outline;        // "outline": 0..0.5 dark outline width
        public float Spacing;        // "spacing": extra letter spacing (TextMeshPro units, can be negative)
        public string[] SystemFonts; // "system": installed Windows font families used instead of Arial (e.g. Microsoft YaHei for Chinese)
        public string[] SystemFiles; // "systemFile": their files in C:\Windows\Fonts (for TextMeshPro texts)
        public Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> MapIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Stamp = "";    // file names + mtimes + sizes, for hot reload
        public bool IsEnglish { get { return Folder == null; } }
    }

    // =====================================================================
    //  Loads <plugin>/<LANG>/*.json and translates strings.
    //  Lookup order for a game string s:
    //    1. exact key
    //    2. same without leading/trailing whitespace (whitespace is kept)
    //    3. case-insensitive key (ALL-CAPS source -> upper-cased translation)
    //    4. numbers as placeholders: "Day 12" -> key "Day {0}" -> "День {0}" -> "День 12"
    //    5. multi-line text: every line on its own (1-4)
    //  Keys starting with "_" and the header keys (version, language, name)
    //  are not translations. Empty values mean "not translated yet".
    // =====================================================================
    public static class Translator
    {
        public const string EN = "EN";
        public static readonly string[] HeaderKeys = { "version", "language", "name" };

        public static string Root;   // plugin folder
        public static readonly List<LanguageInfo> Languages = new List<LanguageInfo>();
        public static LanguageInfo Current;
        public static int Generation;   // bumps on every language change / reload

        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private const int CacheLimit = 40000;

        private static readonly Dictionary<string, string> NativeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "EN", "English" }, { "RU", "Русский" }, { "DE", "Deutsch" }, { "FR", "Français" }, { "ES", "Español" },
            { "IT", "Italiano" }, { "PL", "Polski" }, { "PT", "Português" }, { "PT-BR", "Português (Brasil)" }, { "BR", "Português (Brasil)" },
            { "UK", "Українська" }, { "UA", "Українська" }, { "BE", "Беларуская" }, { "CS", "Čeština" }, { "CZ", "Čeština" }, { "SK", "Slovenčina" },
            { "TR", "Türkçe" }, { "NL", "Nederlands" }, { "SV", "Svenska" }, { "NO", "Norsk" }, { "DA", "Dansk" }, { "FI", "Suomi" },
            { "HU", "Magyar" }, { "RO", "Română" }, { "BG", "Български" }, { "SR", "Српски" }, { "HR", "Hrvatski" }, { "EL", "Ελληνικά" },
            { "ZH", "简体中文" }, { "CN", "简体中文" }, { "ZH-TW", "繁體中文" }, { "TW", "繁體中文" }, { "ZH-HK", "繁體中文" }, { "JA", "日本語" }, { "JP", "日本語" }, { "KO", "한국어" }, { "KK", "Қазақша" }, { "LT", "Lietuvių" },
            { "LV", "Latviešu" }, { "ET", "Eesti" }, { "KA", "ქართული" }, { "HY", "Հայերեն" }, { "AZ", "Azərbaycan" }, { "UZ", "Oʻzbek" },
        };

        public static string NativeName(string code)
        {
            string n;
            return NativeNames.TryGetValue(code, out n) ? n : code;
        }

        // ------------------------------------------------------------ discovery
        public static void Discover(string root)
        {
            Root = root;
            Languages.Clear();
            Languages.Add(new LanguageInfo { Code = EN, Folder = null, DisplayName = NativeName(EN), Version = "game" });
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (!IsLanguageCode(name)) continue;
                if (!Directory.GetFiles(dir, "*.json").Any(f => !Path.GetFileName(f).StartsWith("_"))) continue;
                var code = name.ToUpperInvariant();
                if (code == EN) continue;
                var info = new LanguageInfo { Code = code, Folder = dir, DisplayName = NativeName(code) };
                Load(info);
                Languages.Add(info);
            }
        }

        private static bool IsLanguageCode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2 || s.Length > 7 || s.StartsWith("_")) return false;
            foreach (char c in s) if (!(char.IsLetter(c) || c == '-' || c == '_')) return false;
            return true;
        }

        public static LanguageInfo Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return Languages.FirstOrDefault(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------ loading
        public static string StampOf(LanguageInfo lang)
        {
            if (lang == null || lang.IsEnglish || !Directory.Exists(lang.Folder)) return "";
            var sb = new StringBuilder();
            foreach (var f in Directory.GetFiles(lang.Folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var n = Path.GetFileName(f);
                if (n.StartsWith("_") || n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var fi = new FileInfo(f);
                sb.Append(n).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('|').Append(fi.Length).Append(';');
            }
            return sb.ToString();
        }

        public static void Load(LanguageInfo lang)
        {
            lang.Map.Clear(); lang.MapIgnoreCase.Clear(); lang.Reverse.Clear();
            lang.FontFile = null;
            lang.Upper = false; lang.FontSize = 1f; lang.FontWidth = 1f; lang.Thickness = 0f; lang.Outline = 0f; lang.Spacing = 0f;
            lang.SystemFonts = null; lang.SystemFiles = null;
            if (lang.IsEnglish) return;
            lang.Stamp = StampOf(lang);
            int files = 0, entries = 0, empty = 0;
            foreach (var f in Directory.GetFiles(lang.Folder).OrderBy(f => string.Equals(Path.GetFileName(f), "font.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                                             .ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var n = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if ((ext == ".ttf" || ext == ".otf") && lang.FontFile == null) { lang.FontFile = f; continue; }
                if (ext != ".json" || n.StartsWith("_")) continue;
                if (string.Equals(n, "font.json", StringComparison.OrdinalIgnoreCase)) { LoadFontStyle(lang, f); continue; }
                List<KeyValuePair<string, string>> pairs;
                try { pairs = Json.ParseFlatObject(File.ReadAllText(f, Encoding.UTF8)); }
                catch (Exception e) { Plugin.Log.LogError(lang.Code + ": cannot read " + n + ": " + e.Message); continue; }
                files++;
                foreach (var kv in pairs)
                {
                    var key = kv.Key;
                    if (key == "version") { lang.Version = kv.Value; continue; }
                    if (key == "language")
                    {
                        if (!string.Equals(kv.Value, lang.Code, StringComparison.OrdinalIgnoreCase))
                            Plugin.Log.LogWarning(lang.Code + "/" + n + ": \"language\" is \"" + kv.Value + "\" but the folder is " + lang.Code + " (the folder name is used)");
                        continue;
                    }
                    if (key == "name") { if (!string.IsNullOrEmpty(kv.Value)) lang.DisplayName = kv.Value; continue; }
                    if (key.Length == 0 || key.StartsWith("_")) continue;
                    if (string.IsNullOrEmpty(kv.Value)) { empty++; continue; }
                    if (lang.Map.ContainsKey(key)) Plugin.Log.LogWarning(lang.Code + "/" + n + ": duplicate key \"" + key + "\" (the later one wins)");
                    lang.Map[key] = kv.Value;
                    entries++;
                }
            }
            if (lang.Upper)   // keys stay as they are; the shown text is upper-cased in Translate
                Plugin.Log.LogInfo(lang.Code + ": translations shown in capitals (font.json uppercase)");
            foreach (var kv in lang.Map)
            {
                if (!lang.MapIgnoreCase.ContainsKey(kv.Key)) lang.MapIgnoreCase[kv.Key] = kv.Value;
                if (!lang.Reverse.ContainsKey(kv.Value)) lang.Reverse[kv.Value] = kv.Key;
                if (lang.Upper) { var up = UpperOutsideTags(kv.Value); if (!lang.Reverse.ContainsKey(up)) lang.Reverse[up] = kv.Key; }
            }
            Plugin.Log.LogInfo("Language " + lang.Code + " (" + lang.DisplayName + ") v" + lang.Version + ": " + entries + " translations from " + files + " file(s)"
                + (empty > 0 ? ", " + empty + " still empty" : "") + (lang.FontFile != null ? ", font " + Path.GetFileName(lang.FontFile) : ""));
        }

        private static string[] SplitList(string v)
        {
            var a = v.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            return a.Length > 0 ? a : null;
        }

        private static void LoadFontStyle(LanguageInfo lang, string path)
        {
            try
            {
                foreach (var kv in Json.ParseFlatObject(File.ReadAllText(path, Encoding.UTF8)))
                {
                    var v = (kv.Value ?? "").Trim();
                    float x;
                    bool num = float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                    switch (kv.Key.Trim().ToLowerInvariant())
                    {
                        case "file":
                            if (v.Length > 0)
                            {
                                var fp = Path.Combine(lang.Folder, v);
                                if (File.Exists(fp)) lang.FontFile = fp;
                                else Plugin.Log.LogWarning(lang.Code + "/font.json: font file \"" + v + "\" not found in the folder");
                            }
                            break;
                        case "uppercase": lang.Upper = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"; break;
                        case "size": if (num && x > 0.2f && x < 5f) lang.FontSize = x; break;
                        case "width": if (num && x > 0.3f && x < 3f) lang.FontWidth = x; break;
                        case "thickness": if (num) lang.Thickness = Math.Max(-0.5f, Math.Min(1f, x)); break;
                        case "outline": if (num) lang.Outline = Math.Max(0f, Math.Min(1f, x)); break;
                        case "spacing": if (num) lang.Spacing = x; break;
                        case "system": lang.SystemFonts = SplitList(v); break;
                        case "systemfile": lang.SystemFiles = SplitList(v); break;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogError(lang.Code + ": cannot read font.json: " + e.Message); }
        }

        public static void SetCurrent(LanguageInfo lang)
        {
            if (lang == null) lang = Languages[0];
            if (!lang.IsEnglish)
            {
                // a fresh object, so the previous one keeps its maps for switching texts back
                var fresh = new LanguageInfo { Code = lang.Code, Folder = lang.Folder, DisplayName = NativeName(lang.Code) };
                Load(fresh);
                int i = Languages.IndexOf(lang);
                if (i >= 0) Languages[i] = fresh;
                lang = fresh;
            }
            Current = lang;
            _cache.Clear();
            Generation++;
        }

        public static bool Active { get { return Current != null && !Current.IsEnglish && Current.Map.Count > 0; } }

        // ------------------------------------------------------------ lookup
        public static bool HasLetter(string s)
        {
            for (int i = 0; i < s.Length; i++) if (char.IsLetter(s[i])) return true;
            return false;
        }

        /// Translation of s in the current language, or null if there is none.
        public static string Translate(string s)
        {
            if (!Active || string.IsNullOrEmpty(s)) return null;
            string r;
            if (_cache.TryGetValue(s, out r)) return r;
            if (!HasLetter(s) || s.Length > 8000) return null;   // numbers etc. are never cached
            r = Lookup(Current, s, true);
            if (r != null && Current.Upper) r = UpperOutsideTags(r);
            if (_cache.Count > CacheLimit) _cache.Clear();
            _cache[s] = r;
            return r;
        }

        /// Translation in the current language as written in the file (no font.json uppercase) — for mod menus (IMGUI).
        public static string TranslatePlain(string s)
        {
            if (!Active || string.IsNullOrEmpty(s) || !HasLetter(s) || s.Length > 8000) return null;
            return Lookup(Current, s, true);
        }

        /// Translation in a given language (used for the dropdown labels).
        public static string TranslateIn(LanguageInfo lang, string s)
        {
            if (lang == null || lang.IsEnglish || string.IsNullOrEmpty(s)) return null;
            return Lookup(lang, s, true);
        }

        /// If s is a translation produced by lang, the original English text.
        public static string Untranslate(LanguageInfo lang, string s)
        {
            if (lang == null || lang.IsEnglish || string.IsNullOrEmpty(s)) return null;
            string r;
            return lang.Reverse.TryGetValue(s, out r) ? r : null;
        }

        private static string Lookup(LanguageInfo lang, string s, bool allowLines)
        {
            string v;
            if (lang.Map.TryGetValue(s, out v)) return v;

            // whitespace around the text
            int a = 0, b = s.Length;
            while (a < b && char.IsWhiteSpace(s[a])) a++;
            while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
            string core = (a == 0 && b == s.Length) ? s : s.Substring(a, b - a);
            string lead = s.Substring(0, a), trail = s.Substring(b);
            if (core.Length == 0) return null;
            if (!ReferenceEquals(core, s) && lang.Map.TryGetValue(core, out v)) return lead + v + trail;

            // case
            if (lang.MapIgnoreCase.TryGetValue(core, out v)) return lead + MatchCase(core, v) + trail;

            // numbers as {0} {1} ...
            List<string> nums;
            string tpl = Template(core, out nums);
            if (tpl != null)
            {
                if (lang.Map.TryGetValue(tpl, out v) || lang.MapIgnoreCase.TryGetValue(tpl, out v))
                {
                    var filled = Fill(v, nums);
                    if (!lang.Map.ContainsKey(tpl)) filled = MatchCase(core, filled);
                    return lead + filled + trail;
                }
            }

            // multi-line: translate line by line
            if (allowLines && core.IndexOf('\n') >= 0)
            {
                var lines = core.Split('\n');
                bool any = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    bool cr = line.EndsWith("\r");
                    if (cr) line = line.Substring(0, line.Length - 1);
                    if (line.Trim().Length == 0 || !HasLetter(line)) continue;
                    var t = Lookup(lang, line, false);
                    if (t != null) { lines[i] = t + (cr ? "\r" : ""); any = true; }
                }
                if (any) return lead + string.Join("\n", lines) + trail;
            }
            return null;
        }

        private static string MatchCase(string source, string translated)
        {
            if (HasLetter(source) && source == source.ToUpperInvariant() && source != source.ToLowerInvariant())
                return UpperOutsideTags(translated);
            return translated;
        }

        /// Upper-cases the text but leaves rich-text tags and {n} placeholders alone.
        private static string UpperOutsideTags(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') inTag = true;
                if (inTag) sb.Append(c); else sb.Append(char.ToUpperInvariant(c));
                if (c == '>') inTag = false;
            }
            return sb.ToString();
        }

        /// "Day 12 of 30" -> "Day {0} of {1}" (+ the numbers). Null if there are no digits.
        public static string Template(string s, out List<string> nums)
        {
            nums = null;
            int i = 0;
            StringBuilder sb = null;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    int st = i;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                    if (sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, st); nums = new List<string>(); }
                    if (nums.Count >= 10) { sb.Append(s, st, i - st); continue; }
                    sb.Append('{').Append(nums.Count).Append('}');
                    nums.Add(s.Substring(st, i - st));
                    continue;
                }
                if (sb != null) sb.Append(c);
                i++;
            }
            return sb == null ? null : sb.ToString();
        }

        private static string Fill(string v, List<string> nums)
        {
            for (int k = 0; k < nums.Count; k++) v = v.Replace("{" + k + "}", nums[k]);
            return v;
        }
    }
}
