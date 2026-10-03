using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ApocaLanguage
{
    // =====================================================================
    //  ApocaLanguage: full translation of Apocalypter's text.
    //  BepInEx\plugins\ApocaLanguage\<LANG>\*.json  ->  { "version": "1.0.0", "language": "RU", "Hello": "Здравствуйте", ... }
    //  Language button bottom-right on the title screen and ESC menu,
    //  also selectable in the Apocasetter Mods menu ([General] Language).
    // =====================================================================
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocalanguage";
        public const string NAME = "ApocaLanguage";
        public const string VERSION = "1.3.0";

        public static ManualLogSource Log;
        public static ConfigEntry<string> LanguageEntry;
        public static ConfigEntry<bool> ShowButton, CollectStrings, DumpNow, ExportTextures;
        private static GameObject _runner;
        private static string _stamp = "";
        private static float _nextReloadCheck, _nextSafety, _nextPrune;
        private static float _fullPassAt = -1f, _fullPassAt2 = -1f;
        private static bool _dumpRequested, _exportRequested;
        private static float _sizeChangedAt = -1f;
        public static readonly System.Collections.Generic.Dictionary<string, ConfigEntry<float>> SizeEntries =
            new System.Collections.Generic.Dictionary<string, ConfigEntry<float>>(StringComparer.OrdinalIgnoreCase);

        /// [Font size] <LANG> from the config (Mods menu), 1 when unset.
        public static float UserSize(string code)
        {
            ConfigEntry<float> e;
            if (code == null || !SizeEntries.TryGetValue(code, out e)) return 1f;
            var v = e.Value;
            return v < 0.5f ? 0.5f : v > 2f ? 2f : v;
        }

        private void Awake()
        {
            Log = Logger;
            var root = Path.GetDirectoryName(Info.Location);
            Translator.Discover(root);
            var codes = Translator.Languages.Select(l => l.Code).ToArray();

            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            LanguageEntry = Config.Bind("General", "Language", Translator.EN, new ConfigDescription(
                "Game language. EN = the game's own text; the others are the folders in BepInEx\\plugins\\ApocaLanguage (" + string.Join(", ", codes) + ")",
                new AcceptableValueList<string>(codes)));
            ShowButton = Config.Bind("General", "ShowLanguageButton", true, "Language button in the bottom-right corner of the title screen and the ESC menu");
            foreach (var l in Translator.Languages)
            {
                if (l.IsEnglish) continue;
                var e = Config.Bind("Font size", l.Code, 1.0f, new ConfigDescription(l.DisplayName + ": size of the translated text (1 = normal)",
                    new AcceptableValueRange<float>(0.5f, 2.0f)));
                e.SettingChanged += (s, a) => { _sizeChangedAt = Time.unscaledTime; };
                SizeEntries[l.Code] = e;
            }
            CollectStrings = Config.Bind("Translators", "CollectStrings", false,
                "While on, every English text the game shows is written to _collected.json, and the ones without a translation in the current language to <LANG>\\_missing.json");
            DumpNow = Config.Bind("Translators", "DumpAllTexts", false,
                "Turn on to write every text of the loaded game (also hidden menus, item names, PlayMaker text actions) to _dump.json + _dump_where.txt. Switches itself off.");
            ExportTextures = Config.Bind("Translators", "ExportUiTextures", false,
                "Turn on to save the UI pictures (tutorial pages, menu images) as PNG to _textures\\, to be edited and put in <LANG>\\Textures\\. Switches itself off.");

            LanguageEntry.SettingChanged += (s, e) => ApplyLanguage(LanguageEntry.Value);
            CollectStrings.SettingChanged += (s, e) => { Collector.On = CollectStrings.Value; if (!Collector.On) Collector.Flush(); };
            DumpNow.SettingChanged += (s, e) => { if (DumpNow.Value) _dumpRequested = true; };
            ExportTextures.SettingChanged += (s, e) => { if (ExportTextures.Value) _exportRequested = true; };
            ShowButton.SettingChanged += (s, e) => LanguageButton.Invalidate();
            Collector.On = CollectStrings.Value;
            if (DumpNow.Value) _dumpRequested = true;
            if (ExportTextures.Value) _exportRequested = true;

            Texts.Install(new Harmony(GUID));
            ApplyLanguage(LanguageEntry.Value);

            SceneManager.sceneLoaded += (sc, m) => { EnsureRunner("scene " + sc.name); ScheduleFullPass(); };
            EnsureRunner("Awake");
            Log.LogInfo(NAME + " " + VERSION + " loaded. Languages: " + string.Join(", ", codes) + "; current " + Translator.Current.Code);
        }

        private static void EnsureRunner(string why)
        {
            if (_runner != null) return;
            _runner = new GameObject("ApocaLanguage.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Runner>();
            Log.LogInfo("Runner created (" + why + ")");
        }

        private static void ScheduleFullPass()
        {
            _fullPassAt = Time.unscaledTime + 0.1f;
            _fullPassAt2 = Time.unscaledTime + 2f;
        }

        /// Called by the menu dropdown.
        public static void SelectLanguage(string code)
        {
            if (LanguageEntry.Value == code) { ApplyLanguage(code); return; }
            LanguageEntry.Value = code;   // SettingChanged -> ApplyLanguage, saved to the .cfg
        }

        public static void ApplyLanguage(string code)
        {
            try
            {
                var prev = Translator.Current;
                var lang = Translator.Find(code) ?? Translator.Languages[0];
                Overlays.Reset();
                Translator.SetCurrent(lang);
                lang = Translator.Current;
                Textures.Load(lang);
                _stamp = Translator.StampOf(lang) + "#" + Textures.StampOf(lang);
                if (!lang.IsEnglish) Fonts.EnsureTmpFallback(lang);
                int n = Texts.Refresh(true, prev, true);
                Textures.Refresh(true);
                LanguageButton.Invalidate();
                Api.RaiseChanged();
                Log.LogInfo("Language: " + lang.Code + " (" + lang.DisplayName + (lang.IsEnglish ? "" : ", " + lang.Map.Count + " translations") + "), " + n + " text component(s) refreshed");
            }
            catch (Exception e) { Log.LogError("Applying language " + code + " failed: " + e); }
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            try { LanguageButton.Tick(); } catch (Exception e) { Log.LogWarning("Language button: " + e.Message); }

            if (_fullPassAt > 0 && now >= _fullPassAt) { _fullPassAt = -1; FullPass(); }
            if (_fullPassAt2 > 0 && now >= _fullPassAt2) { _fullPassAt2 = -1; FullPass(); }

            // translators edit the files while the game runs: reload when they change
            if (now >= _nextReloadCheck)
            {
                _nextReloadCheck = now + 2f;
                var cur = Translator.Current;
                if (cur != null && !cur.IsEnglish)
                {
                    string st = "";
                    try { st = Translator.StampOf(cur) + "#" + Textures.StampOf(cur); } catch { }
                    if (st != _stamp) { Log.LogInfo(cur.Code + " files changed - reloading"); ApplyLanguage(cur.Code); }
                }
            }

            // safety net for texts that reached the screen some other way
            if (now >= _nextSafety)
            {
                _nextSafety = now + 2f;
                if (Translator.Active || Collector.On) { try { Texts.Refresh(false, Translator.Current, false); } catch (Exception e) { Log.LogWarning("Refresh: " + e.Message); } }
                if (Textures.Any) { try { Textures.Refresh(false); } catch { } }
            }

            // [Font size] changed in the Mods menu: overlays follow by themselves, the game's own texts are re-applied (debounced)
            if (_sizeChangedAt > 0 && now - _sizeChangedAt > 0.3f)
            {
                _sizeChangedAt = -1f;
                try { if (Translator.Active) Texts.Refresh(true, Translator.Current, true); } catch (Exception e) { Log.LogWarning("Size refresh: " + e.Message); }
            }

            if (now >= _nextPrune) { _nextPrune = now + 15f; Texts.Prune(); }
            Collector.Tick();

            if (_dumpRequested)
            {
                _dumpRequested = false;
                try { Collector.DumpAll(); } catch (Exception e) { Log.LogError("Dump failed: " + e); }
                if (DumpNow.Value) DumpNow.Value = false;
            }
            if (_exportRequested)
            {
                _exportRequested = false;
                try
                {
                    var dir = Path.Combine(Translator.Root, "_textures");
                    int n = Textures.Export(dir);
                    Log.LogInfo("Exported " + n + " UI texture(s) to " + dir);
                }
                catch (Exception e) { Log.LogError("Texture export failed: " + e); }
                if (ExportTextures.Value) ExportTextures.Value = false;
            }
        }

        private static void FullPass()
        {
            try
            {
                if (Translator.Active || Collector.On) Texts.Refresh(true, Translator.Current, false);
                if (Translator.Active) Fonts.EnsureTmpFallback(Translator.Current);
                Textures.Refresh(true);
            }
            catch (Exception e) { Log.LogWarning("Full pass: " + e.Message); }
        }

        internal static void OnQuit() { try { Collector.Flush(); } catch { } }
    }

    public class Runner : MonoBehaviour
    {
        private void Update() { Plugin.Tick(); }
        private void LateUpdate() { try { Overlays.Sync(); } catch (Exception e) { Plugin.Log.LogWarning("Overlays: " + e.Message); } }
        private void OnApplicationQuit() { Plugin.OnQuit(); }
    }

    /// For other mods (by reflection or reference): translate your own strings with the player's language.
    public static class Api
    {
        public static event Action LanguageChanged;
        public static string Language { get { return Translator.Current != null ? Translator.Current.Code : Translator.EN; } }
        public static string T(string english) { return Translator.Translate(english) ?? english; }
        internal static void RaiseChanged()
        {
            var h = LanguageChanged;
            if (h == null) return;
            try { h(); } catch (Exception e) { Plugin.Log.LogWarning("LanguageChanged handler: " + e.Message); }
        }
    }
}
