using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  A native-looking "LANGUAGE: ENGLISH" button in the bottom-right corner
    //  of the title screen and the ESC menu (a clone of one of the game's own
    //  menu buttons, like Apocasetter's MODS button). Clicking it opens a list
    //  of the installed languages above it, each in its own language.
    // =====================================================================
    public static class LanguageButton
    {
        private static readonly string[] TemplateNames = { "Settings", "Credits", "Tutorial", "Codex", "Quit", "Quit_To_Menu", "Exit", "Options", "NewGame", "LoadGameSlots" };
        private const float Margin = 30f, Gap = 4f;

        private class Slot
        {
            public Canvas Canvas;
            public GameObject Button;
            public Vector2 Size;
            public bool Caps;
            public Button[] GameButtons = new Button[0];
            public readonly List<GameObject> Items = new List<GameObject>();
            public bool Open;
            public int LabelGen = -1;
            public Font OrigFont;
            public float BaseY = Margin;
        }

        private static readonly List<Slot> _slots = new List<Slot>();
        private static float _nextScan;

        private static bool IsMenuButton(Button b)
        {
            if (b == null || !b.isActiveAndEnabled) return false;
            var n = b.gameObject.name;
            return TemplateNames.Any(t => string.Equals(t, n, StringComparison.OrdinalIgnoreCase));
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;
            _slots.RemoveAll(x => x.Canvas == null || x.Button == null);
            bool enabled = Plugin.ShowButton == null || Plugin.ShowButton.Value;

            if (enabled)
            {
                Canvas[] canvases;
                try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(); } catch { return; }
                foreach (var c in canvases)
                {
                    if (c == null || !c.enabled || !c.isActiveAndEnabled || c.transform.lossyScale.x <= 0.0001f) continue;
                    if (!c.isRootCanvas) continue;
                    var slot = _slots.FirstOrDefault(x => x.Canvas == c);
                    if (slot == null)
                    {
                        var buttons = c.GetComponentsInChildren<Button>(true);
                        if (!buttons.Any(IsMenuButton)) continue;
                        slot = new Slot { Canvas = c };
                        if (!Inject(slot, buttons)) continue;
                        _slots.Add(slot);
                    }
                    slot.GameButtons = c.GetComponentsInChildren<Button>(true).Where(b => !b.transform.IsChildOf(slot.Button.transform) && !slot.Items.Any(i => i != null && b.transform.IsChildOf(i.transform))).ToArray();
                }
            }

            foreach (var slot in _slots)
            {
                var c = slot.Canvas;
                bool vis = enabled && c.enabled && c.isActiveAndEnabled && c.transform.lossyScale.x > 0.0001f && slot.GameButtons.Any(IsMenuButton);
                if (vis != slot.Button.activeSelf) slot.Button.SetActive(vis);
                if (!vis && slot.Open) Close(slot);
                if (vis)
                {
                    float by = ClearY(slot);
                    if (Mathf.Abs(by - slot.BaseY) > 0.5f) { slot.BaseY = by; Place(slot); }
                    slot.Button.transform.SetAsLastSibling();
                    foreach (var i in slot.Items) if (i != null) i.transform.SetAsLastSibling();
                    if (slot.LabelGen != Translator.Generation) { UpdateLabel(slot); slot.LabelGen = Translator.Generation; }
                }
            }
        }

        /// Height above the bottom edge where the button does not cover any game button in that corner
        /// (the ESC menu has CREDITS / DEBUG there).
        private static readonly Vector3[] _corners = new Vector3[4];
        private static float ClearY(Slot slot)
        {
            var crt = slot.Canvas.GetComponent<RectTransform>();
            if (crt == null) return Margin;
            var cr = crt.rect;
            float right = cr.xMax - Margin, left = right - slot.Size.x;
            float y = Margin;
            foreach (var b in slot.GameButtons)
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                var rt = b.transform as RectTransform;
                if (rt == null) continue;
                rt.GetWorldCorners(_corners);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    var p = crt.InverseTransformPoint(_corners[i]);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                if (maxX - minX < 1f || maxY - minY < 1f) continue;
                if (maxX <= left || minX >= right) continue;                 // not in our column
                float bottom = minY - cr.yMin, top = maxY - cr.yMin;
                if (bottom > cr.height * 0.4f) continue;                     // not in the bottom part of the screen
                y = Mathf.Max(y, top + 12f);
            }
            return y;
        }

        private static void Place(Slot slot)
        {
            var rt = slot.Button.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(-Margin, slot.BaseY);
            for (int i = 0; i < slot.Items.Count; i++)
            {
                if (slot.Items[i] == null) continue;
                var irt = slot.Items[i].GetComponent<RectTransform>();
                irt.anchoredPosition = new Vector2(-Margin, slot.BaseY + (i + 1) * (slot.Size.y + Gap));
            }
        }

        public static void Invalidate() { foreach (var s in _slots) s.LabelGen = -1; }

        // ------------------------------------------------------------ building
        private static bool Inject(Slot slot, Button[] buttons)
        {
            var canvas = slot.Canvas.gameObject;
            try
            {
                var template = buttons.Where(IsMenuButton)
                    .OrderBy(b => { var i = Array.FindIndex(TemplateNames, n => string.Equals(n, b.gameObject.name, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; })
                    .FirstOrDefault() ?? buttons.FirstOrDefault(IsMenuButton);
                if (template == null) return false;

                var label0 = template.GetComponentInChildren<Text>(true);
                var tlabel0 = template.GetComponentInChildren<TMP_Text>(true);
                string sample = label0 != null ? Texts.OriginalOf(label0) : tlabel0 != null ? Texts.OriginalOf(tlabel0) : "";
                slot.Caps = string.IsNullOrEmpty(sample) || sample == sample.ToUpperInvariant();
                slot.OrigFont = label0 != null ? Texts.OriginalFontOf(label0) : null;

                var trt = template.GetComponent<RectTransform>();
                var size = trt != null ? trt.rect.size : new Vector2(300, 55);
                if (size.x < 10 || size.y < 10) size = new Vector2(300, 55);
                slot.Size = size;

                slot.Button = MakeButton(template.gameObject, canvas.transform, "ApocaLanguage_Button", size, 0, () => Toggle(slot));
                slot.Button.SetActive(false);
                UpdateLabel(slot);
                Plugin.Log.LogInfo("Language button added to " + Textures.PathOf(canvas.transform) + " (template '" + template.gameObject.name + "', " + (int)size.x + "x" + (int)size.y + ")");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Language button failed on " + canvas.name + ": " + e);
                if (slot.Button != null) UnityEngine.Object.Destroy(slot.Button);
                slot.Button = null;
                return false;
            }
        }

        private static GameObject MakeButton(GameObject template, Transform parent, string name, Vector2 size, int row, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(template, parent);
            go.name = name;
            // a game button drawn through a font overlay: drop the copied overlay and show the copy's own text again
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr != go.transform && tr.name == Overlays.ChildName) UnityEngine.Object.DestroyImmediate(tr.gameObject);
            foreach (var t in go.GetComponentsInChildren<Text>(true)) { Texts.MarkOwn(t); Overlays.Detach(t); t.material = null; }
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) Texts.MarkOwn(t);
            // strip the game's logic (PlayMaker FSMs, nested dialogs, arrows ...)
            foreach (var f in go.GetComponentsInChildren<PlayMakerFSM>(true)) UnityEngine.Object.DestroyImmediate(f);
            foreach (var f in go.GetComponentsInChildren<PlayMakerProxyBase>(true)) UnityEngine.Object.DestroyImmediate(f);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var tn = mb.GetType().Name;
                if (tn.StartsWith("PlayMaker", StringComparison.Ordinal) || mb is UnityEngine.EventSystems.EventTrigger)
                    try { UnityEngine.Object.DestroyImmediate(mb); } catch { }
            }
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var c = go.transform.GetChild(i);
                bool isText = c.GetComponentInChildren<Text>(true) != null || c.GetComponentInChildren<TMP_Text>(true) != null;
                bool isGraphic = c.GetComponent<Graphic>() != null;
                if (!isText && !isGraphic) UnityEngine.Object.DestroyImmediate(c.gameObject);
                else c.gameObject.SetActive(true);
            }
            var btn = go.GetComponentInChildren<Button>(true);
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => onClick());
            btn.interactable = true;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var cg = go.GetComponent<CanvasGroup>(); if (cg != null) { cg.alpha = 1; cg.interactable = true; cg.blocksRaycasts = true; }

            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(-Margin, Margin + row * (size.y + Gap));
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            go.transform.SetAsLastSibling();
            return go;
        }

        private static void SetLabel(Slot slot, GameObject go, string text, LanguageInfo fontLang)
        {
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                var baseFont = slot.OrigFont ?? t.font;
                Font f = baseFont;
                if (Fonts.Forced(fontLang) || (Fonts.NeedsCheck(text) && !Fonts.Covers(baseFont, text))) f = Fonts.For(fontLang) ?? baseFont;
                if (f != null && t.font != f) t.font = f;
                t.text = text;
                if (ReferenceEquals(fontLang, Translator.Current) && Overlays.For(fontLang)) { if (t.font != baseFont && baseFont != null) t.font = baseFont; Overlays.Attach(t); }
                else Overlays.Detach(t);
            }
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = text;
        }

        private static string Caps(Slot slot, string s) { return slot.Caps ? s.ToUpperInvariant() : s; }

        private static void UpdateLabel(Slot slot)
        {
            if (slot.Button == null) return;
            var cur = Translator.Current ?? Translator.Languages[0];
            var word = Translator.TranslateIn(cur, "Language") ?? "Language";
            SetLabel(slot, slot.Button, Caps(slot, word + ": " + cur.DisplayName), cur);
            if (slot.Open) { Close(slot); Open(slot); }
        }

        // ------------------------------------------------------------ dropdown
        private static void Toggle(Slot slot)
        {
            if (slot.Open) Close(slot); else Open(slot);
        }

        private static void Open(Slot slot)
        {
            Close(slot);
            slot.Open = true;
            int row = 1;
            // listed bottom-up, so the first language ends up at the top of the list
            var langs = Translator.Languages.ToList();
            langs.Reverse();
            foreach (var lang in langs)
            {
                var l = lang;
                var item = MakeButton(slot.Button, slot.Canvas.transform, "ApocaLanguage_" + l.Code, slot.Size, row++, () => Choose(slot, l));
                bool current = Translator.Current != null && Translator.Current.Code == l.Code;
                SetLabel(slot, item, Caps(slot, (current ? "> " : "") + l.DisplayName + (current ? " <" : "")), l);
                item.SetActive(true);
                slot.Items.Add(item);
            }
            Place(slot);
        }

        private static void Close(Slot slot)
        {
            foreach (var i in slot.Items) if (i != null) UnityEngine.Object.Destroy(i);
            slot.Items.Clear();
            slot.Open = false;
        }

        private static void Choose(Slot slot, LanguageInfo lang)
        {
            Close(slot);
            Plugin.Log.LogInfo("Language chosen in the menu: " + lang.Code);
            Plugin.SelectLanguage(lang.Code);
        }
    }
}
