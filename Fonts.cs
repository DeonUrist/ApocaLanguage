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

        /// Font for UI.Text / TextMesh texts whose font lacks letters of the translation (Arial). A font file in the
        /// language folder is NOT used here: Unity's legacy text draws nothing with a font Windows does not know
        /// (1.0.2/1.0.3, Cuprum) — those languages are drawn by TextMeshPro overlays instead (Overlays.cs).
        public static Font For(LanguageInfo lang)
        {
            if (lang != null && lang.SystemFonts != null) { var f = LangSystem(lang); if (f != null) return f; }
            return System();
        }

        private static readonly Dictionary<string, Font> _langSys = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        /// font.json "system": installed Windows families (Chinese/Japanese) — Unity's UI text draws installed fonts.
        private static Font LangSystem(LanguageInfo lang)
        {
            var key = string.Join("|", lang.SystemFonts);
            Font f;
            if (_langSys.TryGetValue(key, out f)) return f;
            f = null;
            try
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
                var have = lang.SystemFonts.Where(n => installed.Contains(n)).ToArray();
                if (have.Length > 0)
                {
                    f = Font.CreateDynamicFontFromOSFont(have, 16);
                    f.name = have[0];
                    f.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    UnityEngine.Object.DontDestroyOnLoad(f);
                }
                Plugin.Log.LogInfo(lang.Code + ": Windows font " + (f != null ? string.Join(", ", have) : "none of " + key + " installed -> Arial"));
            }
            catch (Exception e) { Plugin.Log.LogWarning(lang.Code + ": system font failed: " + e.Message); }
            _langSys[key] = f;
            return f;
        }

        /// font.json "systemFile": the same fonts as files, for TextMeshPro (which needs the file).
        private static Font LangSystemFile(LanguageInfo lang)
        {
            if (lang == null || lang.SystemFiles == null) return null;
            var dir = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts");
            foreach (var n in lang.SystemFiles)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return Raw(p);
            }
            return null;
        }

        public static bool Forced(LanguageInfo lang) { return false; }

        /// Font the TMP fallback asset is built from (TMP reads the file itself, so the raw path font is right there).
        private static Font TmpSource(LanguageInfo lang) { return LangSystemFile(lang) ?? System(); }

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
                    _system = Raw(p);
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

        /// new Font(path): fine for TextMeshPro and for fonts Windows has installed (Arial), but Unity's legacy UI.Text
        /// draws nothing with a font file Windows does not know (Denis, Cuprum-Regular.ttf: all translated text invisible).
        private static Font Raw(string path)
        {
            Font f;
            if (_byFile.TryGetValue(path, out f)) return f;
            try
            {
                f = new Font(path);
                f.name = Path.GetFileNameWithoutExtension(path);
                f.hideFlags = HideFlags.DontUnloadUnusedAsset;
                UnityEngine.Object.DontDestroyOnLoad(f);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cannot load font " + path + ": " + e.Message); f = null; }
            _byFile[path] = f;
            return f;
        }

        // ------------------------------------------------------------ TMP font asset from the language folder font
        private static readonly Dictionary<string, TMP_FontAsset> _assetByFile = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        /// Dynamic TextMeshPro font asset (SDF) made from the language's font file, or null.
        public static TMP_FontAsset LangAsset(LanguageInfo lang)
        {
            if (lang == null || lang.FontFile == null) return null;
            TMP_FontAsset fa;
            if (_assetByFile.TryGetValue(lang.FontFile, out fa)) return fa;
            fa = null;
            var font = Raw(lang.FontFile);
            if (font != null)
            {
                try
                {
                    fa = TMP_FontAsset.CreateFontAsset(font);
                    if (fa != null)
                    {
                        fa.name = "ApocaLanguage " + font.name;
                        fa.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        FixShader(fa);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("TextMeshPro font from " + Path.GetFileName(lang.FontFile) + " failed: " + e.Message); fa = null; }
            }
            Plugin.Log.LogInfo(lang.Code + ": TextMeshPro font " + (fa != null ? fa.name + " (shader " + (fa.material != null && fa.material.shader != null ? fa.material.shader.name : "none") + ")" : "could not be made"));
            _assetByFile[lang.FontFile] = fa;
            return fa;
        }

        /// The shader TMP picks may not be in the game build: borrow the one the game's own TMP fonts use.
        private static void FixShader(TMP_FontAsset fa)
        {
            var mat = fa.material;
            if (mat == null) return;
            if (mat.shader != null && mat.shader.isSupported && mat.shader.name != "Hidden/InternalErrorShader") return;
            foreach (var other in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (other == null || other == fa || other.material == null || other.material.shader == null) continue;
                var sh = other.material.shader;
                if (!sh.isSupported || sh.name.IndexOf("Distance Field", StringComparison.OrdinalIgnoreCase) < 0) continue;
                mat.shader = sh;
                Plugin.Log.LogInfo("TMP shader borrowed from " + other.name + ": " + sh.name);
                return;
            }
            Plugin.Log.LogWarning("No usable TextMeshPro shader found");
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
            var own = LangAsset(lang);
            if (own != null) { AddToAll(own); return; }
            var font = TmpSource(lang);
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
