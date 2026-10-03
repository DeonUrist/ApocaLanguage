using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace ApocaLanguage
{
    // =====================================================================
    //  The Apocasetter Mods menu (IMGUI) in the player's language: section
    //  names, setting names, descriptions, choice values and the menu's own
    //  buttons. Translations live in <LANG>\mods.json like any other text.
    //  Only while Apocasetter draws (its OnGUI / window function) do the IMGUI
    //  Label / Button / Toggle / Box calls get translated; text fields (values
    //  the player edits, the search box) are never touched, and Apocasetter
    //  itself is not changed.
    //  Apocasetter 2.x: setting titles are humanized from the key
    //  ("ShowLanguageButton" -> "Show language button") and the raw key is
    //  shown next to them in a small mono font; the key stays as it is, the
    //  title is translated through the key's translation.
    // =====================================================================
    public static class ConfigUi
    {
        private static bool _installed, _gaveUp;
        private static float _nextTry;
        private static int _depth;
        private static int _gen = -1;
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _human = new Dictionary<string, string>(StringComparer.Ordinal);
        private static FieldInfo _monoField;

        // Apocasetter 1.x formats
        private static readonly Regex RangeRx = new Regex(@"^(.*?)(  \([^()]*\.\.[^()]*\))$", RegexOptions.Singleline);
        private static readonly Regex CountRx = new Regex(@"^(.+?) \((\d+)\)$", RegexOptions.Singleline);
        private static readonly Regex SectionRx = new Regex(@"^\[(.+)\]$");

        // Texts with a variable part: English template (also the key in mods.json) + what each {n} is:
        // r = left as it is (names, versions, paths), t = translated on its own, l = comma list, each item translated
        private sealed class Pat { public Regex Rx; public string Key; public string Kinds; }
        private static readonly List<Pat> Pats = new List<Pat>();
        private static void P(string key, string kinds)
        {
            var esc = Regex.Escape(key);
            for (int i = 0; i < kinds.Length; i++) esc = esc.Replace("\\{" + i + "}", "(.+?)");
            Pats.Add(new Pat { Rx = new Regex("^" + esc + "$", RegexOptions.Singleline), Key = key, Kinds = kinds });
        }
        static ConfigUi()
        {
            P("Saved {0}", "t");
            P("Checked {0}", "t");
            P("Released {0}", "t");
            P("released {0}", "t");
            P("DEFAULT {0}", "t");
            P("{0} on GitHub", "r");
            P("by {0}", "r");
            P("INSTALL {0}", "r");
            P("DOWNLOADING {0}…", "r");
            P("UPDATE AVAILABLE  {0}", "r");
            P("{0} keeps running until you quit. The old files are backed up first; your settings stay as they are.", "r");
            P("{0} keeps running until you quit. On the next start its files are moved to BepInEx\\cache\\Apocasetter\\removed\\, so you can put them back.", "r");
            P("{0} is added before BepInEx loads plugins.", "r");
            P("Your copy ({0}) is newer than the latest GitHub release ({1}). Nothing to do.", "rr");
            P("{0} is also used by {1}", "rl");
            P("Its files are in {0} until it is enabled again.", "r");
            P("WAITING FOR A RESTART: {0}", "r");
            P("Reloaded {0}", "r");
            P("That key can't be stored in {0}", "r");
            P("Last start: {0}", "r");
            P("REMOVE {0}?", "r");
            P("{0}  (whole folder)", "r");
            P("{0} needs it and won't load without it.", "r");
            P("{0} need it and won't load without it.", "r");
            P("{0} has extra features for it and keep working without it.", "r");
            P("{0} have extra features for it and keep working without it.", "r");
            P("{0} (installed together)", "r");
            P("{0} (optional)", "r");
            P("updated to {0}", "r");
        }

        public static void TryInstall()
        {
            if (_installed || _gaveUp || Time.unscaledTime < _nextTry) return;
            _nextTry = Time.unscaledTime + 2f;
            Type ui = null;
            Assembly asc = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Apocasetter") continue;
                asc = asm;
                ui = asm.GetType("Apocasetter.SettingsUI", false);
                break;
            }
            if (ui == null) { if (Time.unscaledTime > 60f) _gaveUp = true; return; }
            _installed = true;
            try
            {
                var skin = asc.GetType("Apocasetter.GameSkin", false);
                if (skin != null) _monoField = skin.GetField("Mono", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                var h = new Harmony(Plugin.GUID + ".configui");
                var enter = new HarmonyMethod(typeof(ConfigUi).GetMethod("Enter", BindingFlags.Static | BindingFlags.NonPublic));
                var leave = new HarmonyMethod(typeof(ConfigUi).GetMethod("Leave", BindingFlags.Static | BindingFlags.NonPublic));
                int scopes = 0;
                foreach (var m in ui.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != "OnGUI" && m.Name != "Draw") continue;
                    h.Patch(m, prefix: enter, finalizer: leave);
                    scopes++;
                }
                var tr = new HarmonyMethod(typeof(ConfigUi).GetMethod("TextPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                var trs = new HarmonyMethod(typeof(ConfigUi).GetMethod("TextStylePrefix", BindingFlags.Static | BindingFlags.NonPublic));
                int n = 0;
                var names = new HashSet<string> { "Label", "Button", "Toggle", "RepeatButton", "Box" };
                foreach (var t in new[] { typeof(GUI), typeof(GUILayout) })
                    foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (!names.Contains(m.Name)) continue;
                        var ps = m.GetParameters();
                        if (!ps.Any(p => p.Name == "text" && p.ParameterType == typeof(string))) continue;
                        bool styled = ps.Any(p => p.Name == "style" && p.ParameterType == typeof(GUIStyle));
                        try { h.Patch(m, prefix: styled ? trs : tr); n++; } catch (Exception e) { Plugin.Log.LogWarning("IMGUI patch " + t.Name + "." + m.Name + ": " + e.Message); }
                    }
                var ctor = typeof(GUIContent).GetConstructor(new[] { typeof(string) });
                if (ctor != null) { h.Patch(ctor, prefix: tr); n++; }   // Apocasetter measures labels with new GUIContent(text)
                Plugin.Log.LogInfo("Mods menu translation: Apocasetter found, " + scopes + " draw method(s), " + n + " IMGUI text call(s) hooked");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Mods menu translation not installed: " + e.Message); }
        }

        private static void Enter() { _depth++; }
        private static Exception Leave(Exception __exception) { if (_depth > 0) _depth--; return __exception; }

        private static void TextPrefix(ref string text)
        {
            if (_depth <= 0 || string.IsNullOrEmpty(text) || !Translator.Active) return;
            try { text = Tr(text); } catch { }
        }

        private static void TextStylePrefix(ref string text, GUIStyle style)
        {
            if (_depth <= 0 || string.IsNullOrEmpty(text) || !Translator.Active) return;
            try
            {
                // the raw config key next to the setting title stays as it is
                if (_monoField != null && style != null && ReferenceEquals(style, _monoField.GetValue(null))) return;
                text = Tr(text);
            }
            catch { }
        }

        public static string Tr(string s)
        {
            if (_gen != Translator.Generation) { _cache.Clear(); _human.Clear(); _gen = Translator.Generation; BuildHuman(); }
            string r;
            if (_cache.TryGetValue(s, out r)) return r;
            r = Compose(s);
            if (_cache.Count > 5000) _cache.Clear();
            _cache[s] = r;
            return r;
        }

        private static string T(string s) { return Translator.TranslatePlain(s); }

        /// Text or null when nothing could be translated.
        private static string TOrNull(string s)
        {
            var r = Compose(s);
            return ReferenceEquals(r, s) || r == s ? null : r;
        }

        private static string Compose(string s)
        {
            if (!Translator.HasLetter(s)) return s;
            var t = T(s);
            if (t != null) return t;
            // 2.x setting title, humanized from the key
            if (_human.TryGetValue(s, out t)) return t;
            Match m;
            // pending change marker: "* Name" (1.x), "Title  *" (2.x)
            if (s.StartsWith("* ")) { t = TOrNull(s.Substring(2)); if (t != null) return "* " + t; }
            if (s.EndsWith("  *")) { t = TOrNull(s.Substring(0, s.Length - 3)); if (t != null) return t + "  *"; }
            if (s.StartsWith("⚠ ")) { t = TOrNull(s.Substring(2)); if (t != null) return "⚠ " + t; }
            // texts with a variable part
            foreach (var p in Pats)
            {
                m = p.Rx.Match(s);
                if (!m.Success) continue;
                var tpl = T(p.Key);
                if (tpl == null) break;
                for (int i = 0; i < p.Kinds.Length; i++)
                {
                    var v = m.Groups[i + 1].Value;
                    if (p.Kinds[i] == 't') v = TOrNull(v) ?? v;
                    else if (p.Kinds[i] == 'l') v = string.Join(", ", v.Split(new[] { ", " }, StringSplitOptions.None).Select(x => TOrNull(x) ?? x).ToArray());
                    tpl = tpl.Replace("{" + i + "}", v);
                }
                return tpl;
            }
            // "v1.2 · disabled", "github.com/x  ·  by Someone  ·  file.cfg", "Released today · x.zip · 12 KB · installs on ..."
            foreach (var sep in new[] { "  ·  ", " · " })
            {
                if (s.IndexOf(sep, StringComparison.Ordinal) < 0) continue;
                var parts = s.Split(new[] { sep }, StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++) { var x = TOrNull(parts[i]); if (x != null) { parts[i] = x; any = true; } }
                if (any) return string.Join(sep, parts);
                break;
            }
            // description + "  (min .. max)" (1.x)
            m = RangeRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return t + m.Groups[2].Value; }
            // [Section] (1.x)
            m = SectionRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return "[" + t + "]"; }
            // " Mod name (12)" (1.x) — mod names stay, but the menu's own words get translated
            m = CountRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return t + " (" + m.Groups[2].Value + ")"; }
            // chosen value + "  ▼" (1.x)
            if (s.EndsWith("  ▼")) { t = T(s.Substring(0, s.Length - 3)); if (t != null) return t + "  ▼"; }
            return s;
        }

        // ---------------------------------------------------------------- Apocasetter 2.x setting titles
        private static readonly Regex IdRx = new Regex(@"^[A-Za-z0-9_]+$");
        private static readonly Dictionary<string, string> Fix = new Dictionary<string, string>
            { { "Npc", "NPC" }, { "Ai", "AI" }, { "Hud", "HUD" }, { "Fsms", "FSMs" }, { "Json", "JSON" }, { "Ui", "UI" } };

        private static void BuildHuman()
        {
            var lang = Translator.Current;
            if (lang == null || lang.IsEnglish) return;
            foreach (var kv in lang.Map)
            {
                var k = kv.Key;
                if (k.Length < 3 || !IdRx.IsMatch(k)) continue;
                var h = Human(k);
                if (h == k || lang.Map.ContainsKey(h) || _human.ContainsKey(h)) continue;
                _human[h] = kv.Value;
            }
        }

        /// Same as Apocasetter 2.x SettingsUI.Human.
        public static string Human(string k)
        {
            if (k.IndexOf(' ') >= 0) return char.ToUpperInvariant(k[0]) + k.Substring(1);
            if (Regex.IsMatch(k, "^[A-Z0-9-]+$")) return k;
            var words = Regex.Replace(k, "([a-z0-9])([A-Z])", "$1 $2").Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string f;
                if (Fix.TryGetValue(words[i], out f)) words[i] = f;
                else if (i > 0) words[i] = words[i].ToLowerInvariant();
            }
            return string.Join(" ", words);
        }
    }
}
