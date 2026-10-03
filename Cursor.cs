using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  Texts shown at the cursor when looking at something in the world
    //  (item names, "Take: F", "grab: lmb", trailer prompts) get their own
    //  size per language: [Cursor text size] <LANG>, on top of [Font size].
    //  They are the HUD canvas texts the game's FSMs address through the
    //  globals UI_ItemName/_2/_3/_4 and UI_ItemUse, plus ItemGrab and
    //  AttachTrailer. Applied every frame (a handful of texts) so it also
    //  works for untranslated text and EN.
    // =====================================================================
    public static class Cursor
    {
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
            { "ItemName", "ItemName_2", "ItemName_3", "ItemName_4", "ItemUse", "ItemGrab", "AttachTrailer" };

        private class Orig { public Text T; public int Size, Min, Max; }
        private static readonly Dictionary<int, Orig> _texts = new Dictionary<int, Orig>();
        private static float _nextScan;

        public static bool Is(Text t) { return t != null && _texts.ContainsKey(t.GetInstanceID()); }

        /// Size factor the text should have on top of its game size (cursor setting × [Font size] for translated game-font texts).
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan || _texts.Count == 0)
            {
                _nextScan = now + (_texts.Count == 0 ? 2f : 5f);
                Scan();
            }
            if (_texts.Count == 0) return;
            var lang = Translator.Current;
            string code = lang != null ? lang.Code : Translator.EN;
            float kc = Plugin.CursorSize(code);
            bool overlayLang = Overlays.For(lang);
            List<int> dead = null;
            foreach (var kv in _texts)
            {
                var o = kv.Value;
                if (o.T == null) { (dead ?? (dead = new List<int>())).Add(kv.Key); continue; }
                float k = kc;
                // translated text drawn with the game's text: [Font size] applies too (overlay languages scale in Overlays)
                if (!overlayLang && lang != null && !lang.IsEnglish && Texts.IsTranslated(o.T)) k *= lang.FontSize * Plugin.UserSize(code);
                int fs = Math.Max(1, (int)Math.Round(o.Size * k));
                if (o.T.fontSize != fs) o.T.fontSize = fs;
                if (o.T.resizeTextForBestFit)
                {
                    int mn = Math.Max(1, (int)Math.Round(o.Min * k)), mx = Math.Max(1, (int)Math.Round(o.Max * k));
                    if (o.T.resizeTextMinSize != mn) o.T.resizeTextMinSize = mn;
                    if (o.T.resizeTextMaxSize != mx) o.T.resizeTextMaxSize = mx;
                }
            }
            if (dead != null) foreach (var d in dead) _texts.Remove(d);
        }

        private static void Scan()
        {
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (t == null || !Names.Contains(t.gameObject.name) || !t.gameObject.scene.IsValid()) continue;
                    var root = t.transform.root;
                    if (root == null || root.name != "Canvas") continue;     // the HUD canvas (not the codex or menus)
                    int id = t.GetInstanceID();
                    if (_texts.ContainsKey(id)) continue;
                    _texts[id] = new Orig { T = t, Size = t.fontSize, Min = t.resizeTextMinSize, Max = t.resizeTextMaxSize };
                    Plugin.Log.LogInfo("Cursor text: " + Textures.PathOf(t.transform) + " (size " + t.fontSize + ")");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cursor text scan: " + e.Message); }
        }
    }
}
