using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  Languages with their own font file (e.g. RU\Oswald-Bold.ttf).
    //  Unity's legacy UI.Text cannot draw a font file that Windows does not
    //  know, so a translated UI.Text is drawn by a TextMeshPro child instead:
    //    - child "ApocaLanguage_TMP" stretched over the text's rect,
    //    - the original text stays (layout, clicks, game logic) but is made
    //      invisible with a material whose colour alpha is 0,
    //    - every frame the child copies text, colour, size, alignment, wrapping,
    //      best fit and style, and applies font.json (size, width, thickness,
    //      outline, spacing).
    //  Untranslated text, EN, and languages without a font file are untouched.
    // =====================================================================
    public static class Overlays
    {
        public const string ChildName = "ApocaLanguage_TMP";

        private class Ov
        {
            public Text Src;
            public TextMeshProUGUI Tmp;
            public RectTransform Rt;
            public Material OrigMat;   // the text's own material (null = default)
            public bool Hidden;
            public string Last;
        }

        private static readonly Dictionary<int, Ov> _ovs = new Dictionary<int, Ov>();
        private static readonly List<int> _dead = new List<int>();
        private static Material _hideMat, _tmpMat;
        private static TMP_FontAsset _asset;
        private static LanguageInfo _lang;
        private static bool _failed, _loggedFail;
        private static int _logged;

        public static int Count { get { return _ovs.Count; } }

        /// Overlay mode for this language? (a usable font file in its folder)
        public static bool For(LanguageInfo lang)
        {
            if (lang == null || lang.IsEnglish || lang.FontFile == null) return false;
            if (!ReferenceEquals(lang, _lang)) Setup(lang);
            return _asset != null && !_failed;
        }

        private static void Setup(LanguageInfo lang)
        {
            _lang = lang;
            _failed = false;
            _asset = Fonts.LangAsset(lang);
            if (_asset == null) { _failed = true; return; }
            if (_tmpMat != null) UnityEngine.Object.Destroy(_tmpMat);
            _tmpMat = new Material(_asset.material) { name = "ApocaLanguage " + lang.Code };
            _tmpMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try
            {
                if (_tmpMat.HasProperty("_FaceDilate")) _tmpMat.SetFloat("_FaceDilate", lang.Thickness);
                if (lang.Outline > 0f && _tmpMat.HasProperty("_OutlineWidth"))
                {
                    _tmpMat.EnableKeyword("OUTLINE_ON");
                    _tmpMat.SetFloat("_OutlineWidth", lang.Outline);
                    if (_tmpMat.HasProperty("_OutlineColor")) _tmpMat.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.85f));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Font style: " + e.Message); }
            Plugin.Log.LogInfo(lang.Code + ": translated texts drawn by TextMeshPro with " + _asset.name
                + " (size x" + lang.FontSize + ", width x" + lang.FontWidth + ", thickness " + lang.Thickness + ", outline " + lang.Outline
                + ", spacing " + lang.Spacing + (lang.Upper ? ", capitals" : "") + ")");
            // existing overlays pick up the new font/material on their next sync
            foreach (var o in _ovs.Values) if (o.Tmp != null) { o.Tmp.font = _asset; o.Tmp.fontSharedMaterial = _tmpMat; }
        }

        private static Material HideMat
        {
            get
            {
                if (_hideMat == null)
                {
                    _hideMat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "ApocaLanguage hidden text", color = new Color(1f, 1f, 1f, 0f) };
                    _hideMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return _hideMat;
            }
        }

        /// The text now shows a translation in an overlay language.
        public static void Attach(Text t)
        {
            if (t == null) return;
            int id = t.GetInstanceID();
            if (_ovs.ContainsKey(id)) return;
            var o = new Ov { Src = t };
            // a copy of a game object that already had an overlay (e.g. a mod cloning a menu button): adopt it
            var child = t.transform.Find(ChildName);
            if (child != null)
            {
                o.Tmp = child.GetComponent<TextMeshProUGUI>();
                o.Rt = child as RectTransform;
                if (t.material == HideMat || ReferenceEquals(GetOwnMat(t), _hideMat)) { o.Hidden = true; o.OrigMat = null; }
            }
            _ovs[id] = o;
        }

        /// The text is no longer translated (or the language has no font file): back to the game's own rendering.
        public static void Detach(Text t)
        {
            if (t == null) return;
            Ov o;
            if (_ovs.TryGetValue(t.GetInstanceID(), out o))
            {
                Remove(o);
                _ovs.Remove(t.GetInstanceID());
                return;
            }
            // a copied object carrying a hidden material / overlay child we never saw
            if (_hideMat != null && ReferenceEquals(GetOwnMat(t), _hideMat))
            {
                t.material = null;
                var child = t.transform.Find(ChildName);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static Material GetOwnMat(Graphic g)
        {
            var m = g.material;
            return m == g.defaultMaterial ? null : m;
        }

        private static void Remove(Ov o)
        {
            if (o.Src != null && o.Hidden) o.Src.material = o.OrigMat;
            if (o.Tmp != null) UnityEngine.Object.Destroy(o.Tmp.gameObject);
            o.Hidden = false;
            o.Tmp = null;
        }

        /// Language changed: drop everything (the refresh pass attaches again where needed).
        public static void Reset()
        {
            foreach (var o in _ovs.Values) Remove(o);
            _ovs.Clear();
            _lang = null;
        }

        // ------------------------------------------------------------ per frame
        public static void Sync()
        {
            if (_ovs.Count == 0) return;
            var lang = Translator.Current;
            foreach (var kv in _ovs)
            {
                var o = kv.Value;
                if (o.Src == null) { if (o.Tmp != null) UnityEngine.Object.Destroy(o.Tmp.gameObject); _dead.Add(kv.Key); continue; }
                if (!o.Src.gameObject.activeInHierarchy) continue;
                try
                {
                    if (o.Tmp == null && !Create(o)) { _dead.Add(kv.Key); continue; }
                    Update(o, lang);
                }
                catch (Exception e)
                {
                    if (!_loggedFail) { _loggedFail = true; Plugin.Log.LogError("Font overlay error (shown once): " + e); }
                    Remove(o);
                    _dead.Add(kv.Key);
                }
            }
            foreach (var k in _dead) _ovs.Remove(k);
            _dead.Clear();
        }

        private static bool Create(Ov o)
        {
            if (_asset == null || _tmpMat == null) return false;
            var go = new GameObject(ChildName, typeof(RectTransform));
            go.layer = o.Src.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(o.Src.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            Texts.MarkOwn(tmp);
            tmp.font = _asset;
            tmp.fontSharedMaterial = _tmpMat;
            tmp.raycastTarget = false;
            tmp.isOrthographic = true;
            o.Tmp = tmp;
            o.Rt = rt;
            o.Last = null;
            // only now hide the original (if anything above failed, the game text stays visible)
            o.OrigMat = GetOwnMat(o.Src);
            o.Src.material = HideMat;
            o.Hidden = true;
            if (_logged < 40)
            {
                _logged++;
                var r = o.Src.rectTransform.rect;
                Plugin.Log.LogInfo("Overlay " + Textures.PathOf(o.Src.transform) + ": size " + o.Src.fontSize + (o.Src.resizeTextForBestFit ? " bestfit " + o.Src.resizeTextMinSize + "-" + o.Src.resizeTextMaxSize : "")
                    + " rect " + (int)r.width + "x" + (int)r.height + " " + o.Src.horizontalOverflow + "/" + o.Src.verticalOverflow + " align " + o.Src.alignment
                    + " alpha " + o.Src.color.a.ToString("0.##") + "/" + o.Src.canvasRenderer.GetAlpha().ToString("0.##") + " scale " + o.Src.transform.lossyScale.x.ToString("0.###"));
            }
            return true;
        }

        private static void Update(Ov o, LanguageInfo lang)
        {
            var s = o.Src;
            var tmp = o.Tmp;
            if (tmp.enabled != s.enabled) tmp.enabled = s.enabled;
            if (!s.enabled) return;
            if (!o.Hidden) { o.OrigMat = GetOwnMat(s); s.material = HideMat; o.Hidden = true; }
            else if (!ReferenceEquals(s.material, _hideMat)) { o.OrigMat = GetOwnMat(s); s.material = HideMat; }   // the game set another material

            var text = s.text;
            if (!ReferenceEquals(text, o.Last)) { tmp.text = text; o.Last = text; }

            float k = lang != null ? lang.FontSize : 1f;
            var c = s.color;
            c.a *= s.canvasRenderer.GetAlpha();
            if (tmp.color != c) tmp.color = c;
            tmp.richText = s.supportRichText;
            if (s.resizeTextForBestFit)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = s.resizeTextMinSize * k;
                tmp.fontSizeMax = s.resizeTextMaxSize * k;
            }
            else
            {
                if (tmp.enableAutoSizing) tmp.enableAutoSizing = false;
                float fs = s.fontSize * k;
                if (Math.Abs(tmp.fontSize - fs) > 0.01f) tmp.fontSize = fs;
            }
            var al = Map(s.alignment);
            if (tmp.alignment != al) tmp.alignment = al;
            bool wrap = s.horizontalOverflow == HorizontalWrapMode.Wrap;
            if (tmp.enableWordWrapping != wrap) tmp.enableWordWrapping = wrap;
            // never truncate: the game's rects are sized for its own font; a taller line (Oswald) in a Truncate rect
            // made TextMeshPro drop the whole line (1.1.0: menu buttons, LOADING, HUD stats invisible)
            if (tmp.overflowMode != TextOverflowModes.Overflow) tmp.overflowMode = TextOverflowModes.Overflow;
            var st = FontStyles.Normal;
            if (s.fontStyle == FontStyle.Bold || s.fontStyle == FontStyle.BoldAndItalic) st |= FontStyles.Bold;
            if (s.fontStyle == FontStyle.Italic || s.fontStyle == FontStyle.BoldAndItalic) st |= FontStyles.Italic;
            if (tmp.fontStyle != st) tmp.fontStyle = st;
            float ls = (s.lineSpacing - 1f) * 100f;
            if (Math.Abs(tmp.lineSpacing - ls) > 0.01f) tmp.lineSpacing = ls;
            float sp = lang != null ? lang.Spacing : 0f;
            if (Math.Abs(tmp.characterSpacing - sp) > 0.01f) tmp.characterSpacing = sp;

            // horizontal squeeze around the aligned edge; the rect gets wider by the same factor so wrapping matches
            float w = lang != null ? lang.FontWidth : 1f;
            float px = HAlign(s.alignment);
            var piv = new Vector2(px, 0.5f);
            if (o.Rt.pivot != piv) o.Rt.pivot = piv;
            var scale = new Vector3(w, 1f, 1f);
            if (o.Rt.localScale != scale) o.Rt.localScale = scale;
            float pw = s.rectTransform.rect.width;
            var sd = new Vector2(Math.Abs(w - 1f) < 0.001f ? 0f : pw * (1f / w - 1f), 0f);
            if (o.Rt.sizeDelta != sd) o.Rt.sizeDelta = sd;
            if (o.Rt.anchoredPosition != Vector2.zero) o.Rt.anchoredPosition = Vector2.zero;
            if (!ReferenceEquals(tmp.font, _asset)) tmp.font = _asset;
            if (!ReferenceEquals(tmp.fontSharedMaterial, _tmpMat)) tmp.fontSharedMaterial = _tmpMat;
        }

        private static float HAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: return 0f;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: return 1f;
                default: return 0.5f;
            }
        }

        private static TextAlignmentOptions Map(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }
    }
}
