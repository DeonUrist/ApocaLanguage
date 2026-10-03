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
            return UiFont(lang) ?? System();
        }

        /// True when lang brings its own font that Unity's UI text can draw (then it is used for every translated text).
        public static bool Forced(LanguageInfo lang) { return UiFont(lang) != null; }

        private static Font UiFont(LanguageInfo lang)
        {
            return lang != null && lang.FontFile != null ? UiFromFile(lang.FontFile) : null;
        }

        /// Font the TMP fallback asset is built from (TMP reads the file itself, so the raw path font is right there).
        private static Font TmpSource(LanguageInfo lang)
        {
            if (lang != null && lang.FontFile != null) { var f = Raw(lang.FontFile); if (f != null) return f; }
            return System();
        }

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

        private static readonly Dictionary<string, Font> _ui = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        [global::System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);
        private const uint FR_PRIVATE = 0x10;

        /// A font made from the file that UI.Text can really draw, or null (then the game font / Arial are used).
        private static Font UiFromFile(string path)
        {
            Font f;
            if (_ui.TryGetValue(path, out f)) return f;
            f = null;
            string file = Path.GetFileName(path);
            string family = null;
            try { family = TtfFamily(path); } catch (Exception e) { Plugin.Log.LogWarning(file + ": cannot read the font name: " + e.Message); }
            bool installed = false;
            try { installed = family != null && Font.GetOSInstalledFontNames().Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase)); } catch { }

            // 1. Windows already has a font of that family (installed by the player): Unity's own OS font path draws it.
            if (installed)
            {
                var os = OsFont(family);
                if (Renders(os, file + " (installed font '" + family + "')")) f = os;
            }
            // 2. Register the file for this process only (nothing is installed or copied), then use it as an OS font.
            if (f == null && family != null)
            {
                int added = 0;
                try { added = AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero); } catch (Exception e) { Plugin.Log.LogWarning(file + ": AddFontResourceEx failed: " + e.Message); }
                bool listed = false;
                try { listed = Font.GetOSInstalledFontNames().Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase)); } catch { }
                Plugin.Log.LogInfo(file + ": family '" + family + "', registered for this game session: " + (added > 0) + ", visible to Unity: " + listed);
                if (listed)
                {
                    var os = OsFont(family);
                    if (Renders(os, file + " (session font '" + family + "')")) f = os;
                }
            }
            // 3. The raw file font (works when Unity can resolve it after all).
            if (f == null)
            {
                var raw = Raw(path);
                if (raw != null && installed && Renders(raw, file + " (file)")) f = raw;
            }
            if (f == null)
                Plugin.Log.LogWarning(file + ": Unity's UI text cannot draw this font from the language folder (it would be invisible). "
                    + "Translated texts keep the game font, or Arial where letters are missing. To use it, install the font in Windows "
                    + "(right-click the file -> 'Install for all users') and restart the game; TextMeshPro texts use the file directly.");
            else Plugin.Log.LogInfo(file + ": used for translated texts");
            _ui[path] = f;
            return f;
        }

        private static Font OsFont(string family)
        {
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(family, 16);
                if (f != null) { f.hideFlags = HideFlags.DontUnloadUnusedAsset; UnityEngine.Object.DontDestroyOnLoad(f); }
                return f;
            }
            catch (Exception e) { Plugin.Log.LogWarning("CreateDynamicFontFromOSFont(" + family + "): " + e.Message); return null; }
        }

        /// Can Unity rasterize glyphs of f for UI text?
        private static bool Renders(Font f, string what)
        {
            if (f == null) return false;
            try
            {
                const string sample = "Aa";
                f.RequestCharactersInTexture(sample, 32, FontStyle.Normal);
                foreach (char c in sample)
                {
                    CharacterInfo ci;
                    if (!f.GetCharacterInfo(c, out ci, 32, FontStyle.Normal) || (ci.advance <= 0 && ci.glyphWidth <= 0))
                    {
                        Plugin.Log.LogInfo(what + ": no glyph for '" + c + "' -> not usable");
                        return false;
                    }
                }
                var mat = f.material;
                if (mat == null || mat.mainTexture == null) { Plugin.Log.LogInfo(what + ": no font texture -> not usable"); return false; }
                return true;
            }
            catch (Exception e) { Plugin.Log.LogInfo(what + ": test failed: " + e.Message); return false; }
        }

        /// Family name (name ID 1) from a .ttf/.otf file.
        private static string TtfFamily(string path)
        {
            var b = File.ReadAllBytes(path);
            Func<int, int> u16 = o => (b[o] << 8) | b[o + 1];
            Func<int, long> u32 = o => ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];
            int numTables = u16(4);
            for (int t = 0; t < numTables; t++)
            {
                int rec = 12 + t * 16;
                string tag = global::System.Text.Encoding.ASCII.GetString(b, rec, 4);
                if (tag != "name") continue;
                int off = (int)u32(rec + 8);
                int count = u16(off + 2), strOff = off + u16(off + 4);
                string mac = null;
                for (int i = 0; i < count; i++)
                {
                    int r = off + 6 + i * 12;
                    int platform = u16(r), lang = u16(r + 4), nameId = u16(r + 6), len = u16(r + 8), so = u16(r + 10);
                    if (nameId != 1) continue;
                    if (platform == 3 && (lang == 0x409 || mac == null))
                    {
                        var s = global::System.Text.Encoding.BigEndianUnicode.GetString(b, strOff + so, len);
                        if (lang == 0x409) return s;
                        mac = s;
                    }
                    else if (platform == 1 && mac == null) mac = global::System.Text.Encoding.ASCII.GetString(b, strOff + so, len);
                }
                return mac;
            }
            return null;
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
