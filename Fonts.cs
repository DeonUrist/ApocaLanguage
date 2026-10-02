using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ApocaLanguage
{
    // =====================================================================
    //  Fonts for translated text.
    //  - A .ttf/.otf file in the language folder is used for every translated
    //    UI text of that language.
    //  - Otherwise the game font is kept, and only texts whose font lacks a
    //    character of the translation (Cyrillic in a Latin-only font, ü, é ...)
    //    switch to a Windows system font (Arial, Segoe UI, Tahoma, Verdana).
    //  - TextMeshPro texts get the same font as a fallback font asset.
    // =====================================================================
    public static class Fonts
    {
        private static readonly string[] SystemFiles = { "arial.ttf", "segoeui.ttf", "tahoma.ttf", "verdana.ttf", "calibri.ttf" };
        private static readonly Dictionary<string, Font> _byFile = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Dictionary<char, bool>> _covers = new Dictionary<int, Dictionary<char, bool>>();
        private static readonly Dictionary<int, TMP_FontAsset> _tmpByFont = new Dictionary<int, TMP_FontAsset>();
        private static readonly HashSet<string> _tmpDoneFor = new HashSet<string>();
        private static Font _system;
        private static bool _systemTried;

        /// Font used for translated texts of lang that need another font.
        public static Font For(LanguageInfo lang)
        {
            if (lang != null && lang.FontFile != null)
            {
                var f = FromFile(lang.FontFile);
                if (f != null) return f;
            }
            return System();
        }

        /// True when lang brings its own font (then it is used for every translated text).
        public static bool Forced(LanguageInfo lang) { return lang != null && lang.FontFile != null && FromFile(lang.FontFile) != null; }

        public static Font System()
        {
            if (_systemTried) return _system;
            _systemTried = true;
            try
            {
                var dir = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts");
                foreach (var n in SystemFiles)
                {
                    var p = Path.Combine(dir, n);
                    if (!File.Exists(p)) continue;
                    _system = FromFile(p);
                    if (_system != null) break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("System font lookup failed: " + e.Message); }
            if (_system == null)
            {
                try { _system = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Tahoma", "Verdana" }, 16); }
                catch (Exception e) { Plugin.Log.LogWarning("CreateDynamicFontFromOSFont failed: " + e.Message); }
            }
            Plugin.Log.LogInfo("Fallback font: " + (_system != null ? _system.name : "none"));
            return _system;
        }

        private static Font FromFile(string path)
        {
            Font f;
            if (_byFile.TryGetValue(path, out f)) return f;
            try
            {
                f = new Font(path);     // UnityEngine.Font(string) loads a font file when given a path
                f.name = Path.GetFileNameWithoutExtension(path);
                f.hideFlags = HideFlags.DontUnloadUnusedAsset;
                UnityEngine.Object.DontDestroyOnLoad(f);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cannot load font " + path + ": " + e.Message); f = null; }
            _byFile[path] = f;
            return f;
        }

        /// Does font f have every non-ASCII character of s (rich-text tags ignored)?
        public static bool Covers(Font f, string s)
        {
            if (f == null) return false;
            Dictionary<char, bool> map;
            int id = f.GetInstanceID();
            if (!_covers.TryGetValue(id, out map)) { map = new Dictionary<char, bool>(); _covers[id] = map; }
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag || c < 128 || char.IsWhiteSpace(c) || char.IsSurrogate(c)) continue;
                bool has;
                if (!map.TryGetValue(c, out has))
                {
                    try { has = f.HasCharacter(c); } catch { has = true; }
                    map[c] = has;
                }
                if (!has) return false;
            }
            return true;
        }

        public static bool NeedsCheck(string s)
        {
            foreach (char c in s) if (c >= 128) return true;
            return false;
        }

        // ------------------------------------------------------------ TextMeshPro
        /// Adds the language font as a fallback to every TMP font asset (once per language / font).
        public static void EnsureTmpFallback(LanguageInfo lang)
        {
            if (lang == null || lang.IsEnglish) return;
            var font = For(lang);
            if (font == null) return;
            var key = lang.Code + "|" + font.GetInstanceID();
            if (_tmpDoneFor.Contains(key)) { AddToAll(_tmpByFont.ContainsKey(font.GetInstanceID()) ? _tmpByFont[font.GetInstanceID()] : null); return; }
            _tmpDoneFor.Add(key);
            TMP_FontAsset fa;
            if (!_tmpByFont.TryGetValue(font.GetInstanceID(), out fa))
            {
                try
                {
                    fa = TMP_FontAsset.CreateFontAsset(font);
                    if (fa != null) { fa.name = "ApocaLanguage " + font.name; fa.hideFlags = HideFlags.DontUnloadUnusedAsset; }
                }
                catch (Exception e) { Plugin.Log.LogWarning("TMP fallback font for " + lang.Code + " failed: " + e.Message); fa = null; }
                _tmpByFont[font.GetInstanceID()] = fa;
                Plugin.Log.LogInfo("TMP fallback font for " + lang.Code + ": " + (fa != null ? fa.name : "none"));
            }
            AddToAll(fa);
        }

        private static void AddToAll(TMP_FontAsset fa)
        {
            if (fa == null) return;
            try
            {
                var global = TMP_Settings.fallbackFontAssets;
                if (global != null && !global.Contains(fa)) global.Add(fa);
            }
            catch { }
            try
            {
                foreach (var a in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (a == null || a == fa) continue;
                    if (a.fallbackFontAssetTable == null) a.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (!a.fallbackFontAssetTable.Contains(fa)) a.fallbackFontAssetTable.Add(fa);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("TMP fallback: " + e.Message); }
        }
    }
}
