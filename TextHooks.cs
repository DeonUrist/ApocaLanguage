using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  Swaps text at the moment the game puts it on screen.
    //  - UI.Text / TMP_Text: Harmony prefix on the text setters (PlayMaker's
    //    UiTextSetText, SetProperty, setTextmeshProText and all C# code go through
    //    them) + postfix on OnEnable (texts that were built into the scene/prefab).
    //  - Every component remembers its English original, so switching language
    //    (or back to EN) re-translates everything that is on screen.
    //  - TextMesh (3D text) has no managed setter: handled by the refresh passes.
    //  The game's own data (FSM variables, item IDs, saves) is never changed.
    // =====================================================================
    public static class Texts
    {
        private class Rec
        {
            public UnityEngine.Object C;
            public string Orig, Shown;
            public Font OrigFont;
            public bool FontSwapped;
            public int Gen;
        }

        private static readonly Dictionary<int, Rec> _recs = new Dictionary<int, Rec>();
        private static readonly Dictionary<int, bool> _skip = new Dictionary<int, bool>();
        private static readonly HashSet<int> Own = new HashSet<int>();

        public static bool IsOwn(Component c) { return c != null && Own.Contains(c.GetInstanceID()); }

        /// Texts made by this mod (the language button) are never translated by the hooks.
        public static void MarkOwn(Component c)
        {
            if (c == null) return;
            int id = c.GetInstanceID();
            Own.Add(id);
            _skip[id] = true;
            Rec r;
            if (_recs.TryGetValue(id, out r)) { RestoreFont(c, r); _recs.Remove(id); }
        }
        private static bool _loggedError;
        public static int Patched;

        public static void Install(Harmony h)
        {
            var me = typeof(Texts);
            Patch(h, AccessTools.PropertySetter(typeof(Text), "text"), me.GetMethod("TextSetPrefix", BindingFlags.Static | BindingFlags.NonPublic), null);
            Patch(h, AccessTools.Method(typeof(Text), "OnEnable"), null, me.GetMethod("TextEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic));
            Patch(h, AccessTools.PropertySetter(typeof(TMP_Text), "text"), me.GetMethod("TmpSetPrefix", BindingFlags.Static | BindingFlags.NonPublic), null);
            Patch(h, AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string), typeof(bool) }), me.GetMethod("TmpSetTextPrefix", BindingFlags.Static | BindingFlags.NonPublic), null);
            Patch(h, AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"), null, me.GetMethod("TmpEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic));
            Patch(h, AccessTools.Method(typeof(TextMeshPro), "OnEnable"), null, me.GetMethod("TmpEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic));
            Patch(h, AccessTools.PropertySetter(typeof(RawImage), "texture"), me.GetMethod("RawTexPrefix", BindingFlags.Static | BindingFlags.NonPublic), null);
            Patch(h, AccessTools.PropertySetter(typeof(Image), "sprite"), me.GetMethod("SpritePrefix", BindingFlags.Static | BindingFlags.NonPublic), null);
            Plugin.Log.LogInfo("Text hooks: " + Patched + " method(s) patched");
        }

        private static void Patch(Harmony h, MethodBase target, MethodInfo prefix, MethodInfo postfix)
        {
            if (target == null) { Plugin.Log.LogWarning("Hook target not found (" + (prefix ?? postfix).Name + ")"); return; }
            try
            {
                h.Patch(target, prefix: prefix != null ? new HarmonyMethod(prefix) : null, postfix: postfix != null ? new HarmonyMethod(postfix) : null);
                Patched++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not patch " + target.DeclaringType.Name + "." + target.Name + ": " + e.Message); }
        }

        // ------------------------------------------------------------ hooks
        private static void TextSetPrefix(Text __instance, ref string value)
        {
            if (!Translator.Active && !Collector.On && _recs.Count == 0) return;
            try { value = Process(__instance, value); } catch (Exception e) { LogOnce(e); }
        }

        private static void TmpSetPrefix(TMP_Text __instance, ref string value)
        {
            if (!Translator.Active && !Collector.On && _recs.Count == 0) return;
            try { value = Process(__instance, value); } catch (Exception e) { LogOnce(e); }
        }

        private static void TmpSetTextPrefix(TMP_Text __instance, ref string sourceText)
        {
            if (!Translator.Active && !Collector.On && _recs.Count == 0) return;
            try { sourceText = Process(__instance, sourceText); } catch (Exception e) { LogOnce(e); }
        }

        private static void TextEnablePostfix(Text __instance)
        {
            if (!Translator.Active && !Collector.On) return;
            try { Reapply(__instance, Translator.Current, false); } catch (Exception e) { LogOnce(e); }
        }

        private static void TmpEnablePostfix(TMP_Text __instance)
        {
            if (!Translator.Active && !Collector.On) return;
            try { Reapply(__instance, Translator.Current, false); } catch (Exception e) { LogOnce(e); }
        }

        private static void RawTexPrefix(ref Texture value)
        {
            if (!Textures.Any || value == null) return;
            try { value = Textures.Replace(value); } catch (Exception e) { LogOnce(e); }
        }

        private static void SpritePrefix(ref Sprite value)
        {
            if (!Textures.Any || value == null) return;
            try { value = Textures.Replace(value); } catch (Exception e) { LogOnce(e); }
        }

        private static void LogOnce(Exception e)
        {
            if (_loggedError) return;
            _loggedError = true;
            Plugin.Log.LogError("Text hook error (shown once): " + e);
        }

        // ------------------------------------------------------------ core
        private static bool Skip(Component c, int id)
        {
            bool s;
            if (_skip.TryGetValue(id, out s)) return s;
            s = Own.Contains(id);
            if (!s)
            {
                // what the player types must not be translated (placeholders are)
                var inF = c.GetComponentInParent<InputField>();
                if (inF != null && inF.textComponent == c) s = true;
                var tinF = c.GetComponentInParent<TMP_InputField>();
                if (tinF != null && tinF.textComponent == c) s = true;
            }
            _skip[id] = s;
            return s;
        }

        /// value = what the game wants to show; returns what is shown.
        private static string Process(Component c, string value)
        {
            if (c == null) return value;
            int id = c.GetInstanceID();
            if (Skip(c, id)) return value;
            Rec r;
            _recs.TryGetValue(id, out r);
            if (string.IsNullOrEmpty(value))
            {
                if (r != null) { RestoreFont(c, r); _recs.Remove(id); }
                DetachOverlay(c, r);
                return value;
            }
            var lang = Translator.Current;
            // the game copied a text we translated (or a stale record): get the English back
            if (r != null && value == r.Shown && r.Gen == Translator.Generation) return value;
            string orig = value;
            if (lang != null && !lang.IsEnglish && lang.Reverse.Count > 0)
            {
                var back = Translator.Untranslate(lang, value);
                if (back != null) orig = back;
            }
            if (Collector.On) Collector.Seen(orig);
            string t = Translator.Translate(orig);
            if (t == null)
            {
                if (Collector.On && Translator.Active) Collector.Missing(orig);
                DetachOverlay(c, r);
                if (r != null) { RestoreFont(c, r); _recs.Remove(id); }
                return orig;
            }
            if (r == null) { r = new Rec { C = c }; _recs[id] = r; }
            r.Orig = orig; r.Shown = t; r.Gen = Translator.Generation;
            ApplyFont(c, r, t, lang);
            return t;
        }

        private static void ApplyFont(Component c, Rec r, string shown, LanguageInfo lang)
        {
            var ut = c as Text;
            var tm = c as TextMesh;
            if (ut == null && tm == null) return;   // TMP uses fallback font assets
            if (ut != null)
            {
                if (Overlays.For(lang)) { RestoreFont(c, r); Overlays.Attach(ut); return; }   // drawn by TextMeshPro with the language's font
                if (Overlays.Count > 0) Overlays.Detach(ut);
            }
            Font cur = ut != null ? ut.font : tm.font;
            Font want = null;
            if (Fonts.Forced(lang)) want = Fonts.For(lang);
            else if (Fonts.NeedsCheck(shown))
            {
                var baseFont = r.FontSwapped ? r.OrigFont : cur;
                if (!Fonts.Covers(baseFont, shown)) want = Fonts.For(lang);
            }
            if (want != null)
            {
                if (cur == want) return;
                if (!r.FontSwapped) { r.OrigFont = cur; r.FontSwapped = true; }
                SetFont(c, want);
            }
            else if (r.FontSwapped) RestoreFont(c, r);
        }

        private static void DetachOverlay(Component c, Rec r)
        {
            var ut = c as Text;
            if (ut != null && (r != null || Overlays.Count > 0)) Overlays.Detach(ut);
        }

        private static void RestoreFont(Component c, Rec r)
        {
            if (!r.FontSwapped) return;
            r.FontSwapped = false;
            if (r.OrigFont != null) SetFont(c, r.OrigFont);
        }

        private static void SetFont(Component c, Font f)
        {
            var ut = c as Text;
            if (ut != null) { ut.font = f; return; }
            var tm = c as TextMesh;
            if (tm != null)
            {
                tm.font = f;
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null && f.material != null) mr.sharedMaterial = f.material;
            }
        }

        /// Re-runs the translation of a component (language switch, scene load, OnEnable, safety net).
        public static void Reapply(Component c, LanguageInfo previous, bool force)
        {
            if (c == null) return;
            int id = c.GetInstanceID();
            if (Skip(c, id)) return;
            string cur = GetText(c);
            if (string.IsNullOrEmpty(cur)) return;
            Rec r;
            _recs.TryGetValue(id, out r);
            if (!force && r != null && r.Shown == cur && r.Gen == Translator.Generation) return;
            string orig;
            if (r != null && r.Shown == cur) orig = r.Orig;
            else orig = Translator.Untranslate(previous, cur) ?? Translator.Untranslate(Translator.Current, cur) ?? cur;
            if (r != null) r.Gen = -1;   // force Process to look again
            if (c is TextMesh)
            {
                var shown = Process(c, orig);
                if (shown != cur) ((TextMesh)c).text = shown;
                return;
            }
            if (!Translator.Active && r == null && orig == cur)   // nothing to change in EN
            {
                if (Collector.On) Collector.Seen(orig);
                return;
            }
            SetText(c, orig);   // the setter prefix translates
        }

        private static string GetText(Component c)
        {
            var ut = c as Text; if (ut != null) return ut.text;
            var tp = c as TMP_Text; if (tp != null) return tp.text;
            var tm = c as TextMesh; if (tm != null) return tm.text;
            return null;
        }

        private static void SetText(Component c, string s)
        {
            var ut = c as Text; if (ut != null) { ut.text = s; return; }
            var tp = c as TMP_Text; if (tp != null) { tp.text = s; return; }
        }

        /// English original of what component c shows (for the dump).
        public static string OriginalOf(Component c)
        {
            if (c == null) return null;
            Rec r;
            string cur = GetText(c);
            if (_recs.TryGetValue(c.GetInstanceID(), out r) && r.Shown == cur) return r.Orig;
            return Translator.Untranslate(Translator.Current, cur) ?? cur;
        }

        // ------------------------------------------------------------ passes
        /// all = include inactive scene objects (scene load / language switch); otherwise only active ones.
        public static int Refresh(bool all, LanguageInfo previous, bool force)
        {
            int n = 0;
            foreach (var c in Collect<Text>(all)) { Reapply(c, previous, force); n++; }
            foreach (var c in Collect<TMP_Text>(all)) { Reapply(c, previous, force); n++; }
            foreach (var c in Collect<TextMesh>(all)) { Reapply(c, previous, force); n++; }
            return n;
        }

        public static List<T> Collect<T>(bool all) where T : Component
        {
            var list = new List<T>();
            try
            {
                if (!all) { list.AddRange(UnityEngine.Object.FindObjectsOfType<T>()); return list; }
                foreach (var c in Resources.FindObjectsOfTypeAll<T>())
                {
                    if (c == null) continue;
                    var go = c.gameObject;
                    if (!go.scene.IsValid()) continue;   // prefabs/assets stay untouched
                    list.Add(c);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Collect " + typeof(T).Name + ": " + e.Message); }
            return list;
        }

        public static void Prune()
        {
            var dead = new List<int>();
            foreach (var kv in _recs) if (kv.Value.C == null) dead.Add(kv.Key);
            foreach (var k in dead) _recs.Remove(k);
            if (_skip.Count > 20000) _skip.Clear();
        }

        public static int Count { get { return _recs.Count; } }

        /// The font a text had before this mod switched it (or its current font).
        public static Font OriginalFontOf(Text t)
        {
            if (t == null) return null;
            Rec r;
            if (_recs.TryGetValue(t.GetInstanceID(), out r) && r.FontSwapped && r.OrigFont != null) return r.OrigFont;
            return t.font;
        }
    }
}
