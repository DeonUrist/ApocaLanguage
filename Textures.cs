using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaLanguage
{
    // =====================================================================
    //  Pictures with text baked in (tutorial pages, signs on menus ...):
    //  <LANG>\Textures\<texture name>.png replaces the UI texture with that name
    //  in RawImage and Image components while the language is active.
    //  The translator tools can export the game's UI textures to start from.
    // =====================================================================
    public static class Textures
    {
        private static readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _loaded = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Texture> _origTex = new Dictionary<int, Texture>();     // replacement id -> original
        private static readonly Dictionary<int, Texture> _texRepl = new Dictionary<int, Texture>();     // original id -> replacement (null = none)
        private static readonly Dictionary<int, Sprite> _spriteRepl = new Dictionary<int, Sprite>();    // original sprite id -> replacement
        private static readonly Dictionary<int, Sprite> _origSprite = new Dictionary<int, Sprite>();    // replacement id -> original

        public static bool Any { get { return _files.Count > 0; } }
        public static int FileCount { get { return _files.Count; } }

        public static void Load(LanguageInfo lang)
        {
            _files.Clear();
            foreach (var t in _loaded.Values) if (t != null) UnityEngine.Object.Destroy(t);
            _loaded.Clear();
            _spriteRepl.Clear();
            _texRepl.Clear();
            if (lang == null || lang.IsEnglish) return;
            var dir = Path.Combine(lang.Folder, "Textures");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                _files[Path.GetFileNameWithoutExtension(f)] = f;
            }
            if (_files.Count > 0) Plugin.Log.LogInfo(lang.Code + ": " + _files.Count + " replacement texture(s)");
        }

        public static string StampOf(LanguageInfo lang)
        {
            if (lang == null || lang.IsEnglish) return "";
            var dir = Path.Combine(lang.Folder, "Textures");
            if (!Directory.Exists(dir)) return "";
            return string.Join(";", Directory.GetFiles(dir).OrderBy(f => f).Select(f => { var fi = new FileInfo(f); return fi.Name + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Length; }).ToArray());
        }

        private static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (_loaded.TryGetValue(name, out t)) return t;
            string path;
            if (!_files.TryGetValue(name, out path)) return null;
            try
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), false)) { UnityEngine.Object.Destroy(t); t = null; }
                else { t.name = name; t.wrapMode = TextureWrapMode.Clamp; t.hideFlags = HideFlags.DontUnloadUnusedAsset; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Cannot load " + path + ": " + e.Message); t = null; }
            _loaded[name] = t;
            return t;
        }

        public static Texture Replace(Texture tex)
        {
            if (tex == null) return tex;
            int id = tex.GetInstanceID();
            Texture cached;
            if (_texRepl.TryGetValue(id, out cached)) return cached != null ? cached : tex;
            if (_origTex.ContainsKey(id)) return tex;
            var r = Get(tex.name);
            _texRepl[id] = r;
            if (r == null) return tex;
            _origTex[r.GetInstanceID()] = tex;
            return r;
        }

        public static Sprite Replace(Sprite sp)
        {
            if (sp == null) return sp;
            Sprite rs;
            if (_spriteRepl.TryGetValue(sp.GetInstanceID(), out rs)) return rs != null ? rs : sp;
            if (sp.texture == null || _origSprite.ContainsKey(sp.GetInstanceID())) return sp;
            var tex = Get(sp.texture.name);
            if (tex == null) { _spriteRepl[sp.GetInstanceID()] = null; return sp; }
            try
            {
                float kx = tex.width / (float)sp.texture.width, ky = tex.height / (float)sp.texture.height;
                var rect = new Rect(sp.rect.x * kx, sp.rect.y * ky, sp.rect.width * kx, sp.rect.height * ky);
                var pivot = new Vector2(sp.pivot.x / sp.rect.width, sp.pivot.y / sp.rect.height);
                var b = sp.border;
                rs = Sprite.Create(tex, rect, pivot, sp.pixelsPerUnit * kx, 0, SpriteMeshType.FullRect, new Vector4(b.x * kx, b.y * ky, b.z * kx, b.w * ky));
                rs.name = sp.name;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sprite " + sp.name + ": " + e.Message); rs = null; }
            _spriteRepl[sp.GetInstanceID()] = rs;
            if (rs == null) return sp;
            _origSprite[rs.GetInstanceID()] = sp;
            return rs;
        }

        /// Puts originals back and re-applies the current language's replacements.
        public static void Refresh(bool all)
        {
            if (!Any && _origTex.Count == 0 && _origSprite.Count == 0) return;
            foreach (var ri in Texts.Collect<RawImage>(all))
            {
                var t = ri.texture;
                if (t == null) continue;
                Texture o;
                if (_origTex.TryGetValue(t.GetInstanceID(), out o)) t = o;
                ri.texture = t;  // prefix replaces it again if the language has a file for it
            }
            foreach (var im in Texts.Collect<Image>(all))
            {
                var s = im.sprite;
                if (s == null) continue;
                Sprite o;
                if (_origSprite.TryGetValue(s.GetInstanceID(), out o)) s = o;
                im.sprite = s;
            }
        }

        // ------------------------------------------------------------ export (translator tool)
        public static int Export(string folder)
        {
            Directory.CreateDirectory(folder);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            int n = 0;
            var texs = new List<KeyValuePair<Texture, string>>();
            foreach (var ri in Texts.Collect<RawImage>(true)) if (ri.texture != null) texs.Add(new KeyValuePair<Texture, string>(Orig(ri.texture), PathOf(ri.transform)));
            foreach (var im in Texts.Collect<Image>(true)) if (im.sprite != null && im.sprite.texture != null) texs.Add(new KeyValuePair<Texture, string>(Orig(im.sprite).texture, PathOf(im.transform)));
            foreach (var kv in texs)
            {
                var t = kv.Key;
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                list.Add(t.name + "\t" + t.width + "x" + t.height + "\t" + kv.Value);
                if (!done.Add(t.name)) continue;
                if (t.width > 4096 || t.height > 4096) continue;
                try
                {
                    var png = ToPng(t);
                    if (png == null) continue;
                    File.WriteAllBytes(System.IO.Path.Combine(folder, Safe(t.name) + ".png"), png);
                    n++;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Export " + t.name + ": " + e.Message); }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            File.WriteAllLines(System.IO.Path.Combine(folder, "_where_used.txt"), list.ToArray());
            return n;
        }

        private static Texture Orig(Texture t) { Texture o; return t != null && _origTex.TryGetValue(t.GetInstanceID(), out o) ? o : t; }
        private static Sprite Orig(Sprite s) { Sprite o; return s != null && _origSprite.TryGetValue(s.GetInstanceID(), out o) ? o : s; }

        private static byte[] ToPng(Texture t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                tmp.Apply();
                var bytes = ImageConversion.EncodeToPNG(tmp);
                UnityEngine.Object.Destroy(tmp);
                return bytes;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string Safe(string n)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        public static string PathOf(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
