using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    // =====================================================================
    public static class ConfigUi
    {
        private static bool _installed, _gaveUp;
        private static float _nextTry;
        private static int _depth;
        private static int _gen = -1;
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Regex RangeRx = new Regex(@"^(.*?)(  \([^()]*\.\.[^()]*\))$", RegexOptions.Singleline);
        private static readonly Regex CountRx = new Regex(@"^(.+?) \((\d+)\)$", RegexOptions.Singleline);
        private static readonly Regex SectionRx = new Regex(@"^\[(.+)\]$");
        private static readonly Regex SavedKeyRx = new Regex(@"^Saved (\S.*)$");

        public static void TryInstall()
        {
            if (_installed || _gaveUp || Time.unscaledTime < _nextTry) return;
            _nextTry = Time.unscaledTime + 2f;
            Type ui = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Apocasetter") continue;
                ui = asm.GetType("Apocasetter.SettingsUI", false);
                break;
            }
            if (ui == null) { if (Time.unscaledTime > 60f) _gaveUp = true; return; }
            _installed = true;
            try
            {
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
                int n = 0;
                var names = new HashSet<string> { "Label", "Button", "Toggle", "RepeatButton", "Box" };
                foreach (var t in new[] { typeof(GUI), typeof(GUILayout) })
                    foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (!names.Contains(m.Name)) continue;
                        if (!m.GetParameters().Any(p => p.Name == "text" && p.ParameterType == typeof(string))) continue;
                        try { h.Patch(m, prefix: tr); n++; } catch (Exception e) { Plugin.Log.LogWarning("IMGUI patch " + t.Name + "." + m.Name + ": " + e.Message); }
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

        public static string Tr(string s)
        {
            if (_gen != Translator.Generation) { _cache.Clear(); _gen = Translator.Generation; }
            string r;
            if (_cache.TryGetValue(s, out r)) return r;
            r = Compose(s);
            if (_cache.Count > 5000) _cache.Clear();
            _cache[s] = r;
            return r;
        }

        private static string T(string s) { return Translator.TranslatePlain(s); }

        private static string Compose(string s)
        {
            if (!Translator.HasLetter(s)) return s;
            var t = T(s);
            if (t != null) return t;
            Match m;
            // pending change marker
            if (s.StartsWith("* ")) { t = T(s.Substring(2)); if (t != null) return "* " + t; }
            // description + "  (min .. max)"
            m = RangeRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return t + m.Groups[2].Value; }
            // [Section]
            m = SectionRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return "[" + t + "]"; }
            // " Mod name (12)"  — mod names stay, but the menu's own words get translated
            m = CountRx.Match(s);
            if (m.Success) { t = T(m.Groups[1].Value); if (t != null) return t + " (" + m.Groups[2].Value + ")"; }
            // chosen value + "  ▼"
            if (s.EndsWith("  ▼")) { t = T(s.Substring(0, s.Length - 3)); if (t != null) return t + "  ▼"; }
            // "Saved <setting>"
            m = SavedKeyRx.Match(s);
            if (m.Success)
            {
                var k = T(m.Groups[1].Value);
                var w = T("Saved {0}");
                if (w != null) return w.Replace("{0}", k ?? m.Groups[1].Value);
            }
            return s;
        }
    }
}
