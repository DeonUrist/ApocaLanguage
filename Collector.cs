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
            if (s.IndexOf("(Seed: ", StringComparison.Ordinal) >= 0) return null;   // save slot labels = the player's save names
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
        // Each run is merged into the previous _dump.json / _dump_where.txt, so the title screen and a loaded game
        // can be dumped one after the other.
        public static void DumpAll()
        {
            var root = Translator.Root;
            var jsonPath = Path.Combine(root, "_dump.json");
            var wherePath = Path.Combine(root, "_dump_where.txt");
            var where = ReadWhere(wherePath);
            foreach (var k in ReadKeys(jsonPath)) if (!where.ContainsKey(k)) where[k] = new List<string>();
            int before = where.Count;
            Action<string, string> add = (text, src) =>
            {
                var k = KeyOf(text);
                if (k == null) return;
                List<string> l;
                if (!where.TryGetValue(k, out l)) { l = new List<string>(); where[k] = l; }
                if (l.Count < 8 && !l.Contains(src)) l.Add(src);
            };

            int comps = 0;
            foreach (var c in Resources.FindObjectsOfTypeAll<Text>()) { if (c == null || Texts.IsOwn(c)) continue; add(Texts.OriginalOf(c), Where(c)); comps++; }
            foreach (var c in Resources.FindObjectsOfTypeAll<TMP_Text>()) { if (c == null || Texts.IsOwn(c)) continue; add(Texts.OriginalOf(c), Where(c)); comps++; }
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
                        foreach (var p in ActionStrings(st))
                            add(p.Value, at + " " + st.Name + "/" + p.Key);
                }
                catch (Exception e) { if (fsms < 3) Plugin.Log.LogWarning("Dump FSM " + fsm.FsmName + ": " + e.Message); }
            }

            Write(jsonPath, "EN", where.Keys);
            var lines = where.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key.Replace("\n", "\\n") + (kv.Value.Count > 0 ? "\n      " + string.Join("\n      ", kv.Value.ToArray()) : "")).ToArray();
            File.WriteAllLines(wherePath, lines, new System.Text.UTF8Encoding(false));
            Plugin.Log.LogInfo("Dump: " + where.Count + " distinct texts (" + (where.Count - before) + " new) from " + comps + " text components and " + fsms + " FSMs -> " + jsonPath);

            // what is still untranslated in the current language
            var lang = Translator.Current;
            if (lang != null && !lang.IsEnglish)
            {
                var miss = where.Keys.Where(k => Translator.TranslateIn(lang, k) == null).ToList();
                Write(Path.Combine(lang.Folder, "_untranslated.json"), lang.Code, miss);
                Plugin.Log.LogInfo("Dump: " + miss.Count + " of them have no " + lang.Code + " translation -> " + lang.Code + "\\_untranslated.json");
            }
        }

        private static Dictionary<string, List<string>> ReadWhere(string path)
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (!File.Exists(path)) return d;
            try
            {
                List<string> cur = null;
                foreach (var line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
                {
                    if (line.StartsWith("      ")) { if (cur != null && cur.Count < 8) cur.Add(line.Substring(6)); continue; }
                    if (line.Length == 0) continue;
                    var k = line.Replace("\\n", "\n");
                    if (!d.TryGetValue(k, out cur)) { cur = new List<string>(); d[k] = cur; }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cannot read " + path + ": " + e.Message); }
            return d;
        }

        // The string parameters of a state's actions, read from PlayMaker's serialized ActionData.
        // (Touching FsmState.Actions on an FSM that has not started makes PlayMaker build the actions without an Fsm
        //  and log "Error Loading Action" NullReferenceExceptions, so the actions themselves are never created here.)
        private static readonly FieldInfo F_actionData = typeof(FsmState).GetField("actionData", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo AD(string n) { return typeof(ActionData).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic); }
        private static readonly FieldInfo F_names = AD("actionNames"), F_start = AD("actionStartIndex"), F_pName = AD("paramName"),
            F_pType = AD("paramDataType"), F_pPos = AD("paramDataPos"), F_fsmStr = AD("fsmStringParams"), F_str = AD("stringParams");

        private static List<KeyValuePair<string, string>> ActionStrings(FsmState st)
        {
            var res = new List<KeyValuePair<string, string>>();
            if (F_actionData == null || F_names == null || F_start == null || F_pName == null || F_pType == null || F_pPos == null) return res;
            var ad = F_actionData.GetValue(st);
            if (ad == null) return res;
            var names = F_names.GetValue(ad) as System.Collections.IList;
            var starts = F_start.GetValue(ad) as System.Collections.IList;
            var pNames = F_pName.GetValue(ad) as System.Collections.IList;
            var pTypes = F_pType.GetValue(ad) as System.Collections.IList;
            var pPos = F_pPos.GetValue(ad) as System.Collections.IList;
            var fsmStr = F_fsmStr != null ? F_fsmStr.GetValue(ad) as System.Collections.IList : null;
            var strs = F_str != null ? F_str.GetValue(ad) as System.Collections.IList : null;
            if (names == null || starts == null || pNames == null || pTypes == null || pPos == null) return res;
            for (int k = 0; k < names.Count && k < starts.Count; k++)
            {
                var full = names[k] as string ?? "";
                var tn = full.Substring(full.LastIndexOf('.') + 1);
                bool textAction = tn.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("GUI", StringComparison.Ordinal) >= 0;
                int a = (int)starts[k], b = k + 1 < starts.Count ? (int)starts[k + 1] : pNames.Count;
                for (int i = a; i < b && i < pNames.Count && i < pTypes.Count && i < pPos.Count; i++)
                {
                    var pn = pNames[i] as string ?? "";
                    if (!textAction && pn.IndexOf("text", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var type = pTypes[i] != null ? pTypes[i].ToString() : "";
                    int pos = (int)pPos[i];
                    string val = null;
                    if (type == "FsmString" && fsmStr != null && pos >= 0 && pos < fsmStr.Count)
                    {
                        var fs = fsmStr[pos] as FsmString;
                        if (fs != null && !fs.UseVariable) val = fs.Value;
                    }
                    else if (type == "String" && strs != null && pos >= 0 && pos < strs.Count) val = strs[pos] as string;
                    if (!string.IsNullOrEmpty(val)) res.Add(new KeyValuePair<string, string>(tn + "." + pn, val));
                }
            }
            return res;
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
