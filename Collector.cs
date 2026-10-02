using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  Translator tools.
    //  CollectStrings (while playing): every English text the game shows is
    //    added to  _collected.json  (all languages)  and, when it has no
    //    translation in the current language, to  <LANG>\_missing.json.
    //    Numbers are written as {0} {1} ... ("Day {0}").
    //  DumpAllTexts (one shot): every text in the loaded scenes and prefabs
    //    (also hidden ones), the item names and the literal texts of the game's
    //    PlayMaker text actions -> _dump.json + _dump_where.txt (where each one is).
    //  Files starting with "_" are never loaded as translations.
    // =====================================================================
    public static class Collector
    {
        public static bool On;
        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> _missing = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static bool _dirty, _loadedSeen;
        private static float _nextFlush;

        public static string CollectedPath { get { return Path.Combine(Translator.Root, "_collected.json"); } }

        /// The key a translator should write for s (trimmed, numbers -> {n}), or null if s is not text.
        public static string KeyOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Length < 2 || s.Length > 4000 || !Translator.HasLetter(s)) return null;
            List<string> nums;
            var tpl = Translator.Template(s, out nums);
            return tpl ?? s;
        }

        public static void Seen(string s)
        {
            if (string.IsNullOrEmpty(s) || !Translator.HasLetter(s)) return;
            if (_seen.Contains(s)) return;
            if (_seen.Count > 200000) _seen.Clear();
            var k = KeyOf(s);
            _seen.Add(s);
            if (k != null && k != s) _seen.Add(k);
            if (k != null) { EnsureSeenLoaded(); if (_all.Add(k)) _dirty = true; }
        }

        private static readonly HashSet<string> _all = new HashSet<string>(StringComparer.Ordinal);

        public static void Missing(string s)
        {
            var lang = Translator.Current;
            if (lang == null || lang.IsEnglish) return;
            var k = KeyOf(s);
            if (k == null || lang.Reverse.ContainsKey(s.Trim())) return;
            HashSet<string> set;
            if (!_missing.TryGetValue(lang.Code, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in ReadKeys(MissingPath(lang))) set.Add(kv);
                _missing[lang.Code] = set;
            }
            if (set.Add(k)) _dirty = true;
        }

        private static string MissingPath(LanguageInfo lang) { return Path.Combine(lang.Folder, "_missing.json"); }

        private static void EnsureSeenLoaded()
        {
            if (_loadedSeen) return;
            _loadedSeen = true;
            foreach (var k in ReadKeys(CollectedPath)) _all.Add(k);
        }

        private static IEnumerable<string> ReadKeys(string path)
        {
            if (!File.Exists(path)) return new string[0];
            try
            {
                return Json.ParseFlatObject(File.ReadAllText(path, System.Text.Encoding.UTF8))
                    .Select(kv => kv.Key).Where(k => Array.IndexOf(Translator.HeaderKeys, k) < 0).ToList();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cannot read " + path + ": " + e.Message); return new string[0]; }
        }

        public static void Tick()
        {
            if (!_dirty || Time.unscaledTime < _nextFlush) return;
            _nextFlush = Time.unscaledTime + 10f;
            Flush();
        }

        public static void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                EnsureSeenLoaded();
                Write(CollectedPath, "EN", _all);
                foreach (var kv in _missing)
                {
                    var lang = Translator.Find(kv.Key);
                    if (lang == null || lang.IsEnglish) continue;
                    // drop what has been translated since
                    var still = kv.Value.Where(k => Translator.TranslateIn(lang, k) == null).ToList();
                    Write(MissingPath(lang), lang.Code, still);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Collector flush failed: " + e.Message); }
        }

        private static void Write(string path, string code, IEnumerable<string> keys)
        {
            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("version", Plugin.VERSION),
                new KeyValuePair<string, string>("language", code),
            };
            pairs.AddRange(keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Select(k => new KeyValuePair<string, string>(k, "")));
            Json.WriteFlatObject(path, pairs);
        }

        // ------------------------------------------------------------ one-shot dump
        public static void DumpAll()
        {
            var where = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Action<string, string> add = (text, src) =>
            {
                var k = KeyOf(text);
                if (k == null) return;
                List<string> l;
                if (!where.TryGetValue(k, out l)) { l = new List<string>(); where[k] = l; }
                if (l.Count < 8 && !l.Contains(src)) l.Add(src);
            };

            int comps = 0;
            foreach (var c in Resources.FindObjectsOfTypeAll<Text>()) { if (c == null) continue; add(Texts.OriginalOf(c), Where(c)); comps++; }
            foreach (var c in Resources.FindObjectsOfTypeAll<TMP_Text>()) { if (c == null) continue; add(Texts.OriginalOf(c), Where(c)); comps++; }
            foreach (var c in Resources.FindObjectsOfTypeAll<TextMesh>()) { if (c == null) continue; add(Texts.OriginalOf(c), Where(c)); comps++; }

            int fsms = 0;
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                try
                {
                    if (fsm == null || fsm.Fsm == null) continue;
                    fsms++;
                    var F = fsm.Fsm;
                    string at = Where(fsm) + " [" + fsm.FsmName + "]";
                    foreach (var sv in F.Variables.StringVariables)
                        if (InterestingVar(fsm.FsmName, sv.Name)) add(sv.Value, at + " var " + sv.Name);
                    foreach (var st in F.States)
                    {
                        FsmStateAction[] acts;
                        try { acts = st.Actions; } catch { continue; }
                        if (acts == null) continue;
                        foreach (var a in acts)
                        {
                            if (a == null) continue;
                            var tn = a.GetType().Name;
                            bool textAction = tn.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("GUI", StringComparison.Ordinal) >= 0;
                            foreach (var fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (fi.FieldType != typeof(FsmString)) continue;
                                if (!textAction && fi.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                var fs = fi.GetValue(a) as FsmString;
                                if (fs == null || fs.UsesVariable) continue;
                                add(fs.Value, at + " " + st.Name + "/" + tn + "." + fi.Name);
                            }
                        }
                    }
                }
                catch { }
            }

            var root = Translator.Root;
            Write(Path.Combine(root, "_dump.json"), "EN", where.Keys);
            var lines = where.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key.Replace("\n", "\\n") + "\n      " + string.Join("\n      ", kv.Value.ToArray())).ToArray();
            File.WriteAllLines(Path.Combine(root, "_dump_where.txt"), lines, new System.Text.UTF8Encoding(false));
            Plugin.Log.LogInfo("Dump: " + where.Count + " distinct texts from " + comps + " text components and " + fsms + " FSMs -> " + Path.Combine(root, "_dump.json"));

            // what is still untranslated in the current language
            var lang = Translator.Current;
            if (lang != null && !lang.IsEnglish)
            {
                var miss = where.Keys.Where(k => Translator.TranslateIn(lang, k) == null).ToList();
                Write(Path.Combine(lang.Folder, "_untranslated.json"), lang.Code, miss);
                Plugin.Log.LogInfo("Dump: " + miss.Count + " of them have no " + lang.Code + " translation -> " + lang.Code + "\\_untranslated.json");
            }
        }

        private static bool InterestingVar(string fsmName, string varName)
        {
            if (string.Equals(fsmName, "ItemName", StringComparison.OrdinalIgnoreCase)) return true;
            var v = varName.ToLowerInvariant();
            return v.Contains("name") || v.Contains("text") || v.Contains("desc") || v.Contains("info") || v.Contains("title")
                || v.Contains("message") || v.Contains("hint") || v.Contains("label");
        }

        private static string Where(Component c)
        {
            var go = c.gameObject;
            var p = Textures.PathOf(c.transform);
            return (go.scene.IsValid() ? "" : "(prefab) ") + p;
        }
    }
}
