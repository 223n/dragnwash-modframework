using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Assets
{
    /// <summary>One texture a mod replaces: the file it came from and the loaded texture.</summary>
    public sealed class TextureReplacement
    {
        internal TextureReplacement() { }

        /// <summary>Name of the game texture it replaces (the file name without extension).</summary>
        public string Name { get; internal set; }

        /// <summary>Folder under BepInEx/plugins the file came from, as the mod's name.</summary>
        public string Mod { get; internal set; }

        /// <summary>The file.</summary>
        public string Path { get; internal set; }

        /// <summary>The loaded replacement, or null when the file could not be read.</summary>
        public Texture2D Texture { get; internal set; }

        /// <summary>Materials and sprites it has been put into so far.</summary>
        public int Applied { get; internal set; }

        /// <summary>Other mods that ship a replacement with the same name and lost to this one.</summary>
        public List<string> Overrides { get; } = new List<string>();

        /// <summary>Why the last reload of this file failed, or null when it loaded.</summary>
        public string Problem { get; internal set; }

        internal string ContentHash { get; set; }
    }

    /// <summary>What <see cref="AssetReplacements.ReloadFiles"/> did to one file.</summary>
    public sealed class ReloadResult
    {
        internal ReloadResult(string name, string status) { Name = name; Status = status; }

        /// <summary>Texture name.</summary>
        public string Name { get; }

        /// <summary>"reloaded", "unchanged", or the problem.</summary>
        public string Status { get; }
    }

    /// <summary>
    /// Replaces the game's textures with PNG files that mods ship, without
    /// touching the game's files: <c>BepInEx/plugins/&lt;Mod&gt;/assets/textures/&lt;texture name&gt;.png</c>
    /// replaces the loaded texture of that name wherever a material or sprite
    /// uses it. Experimental (Assets 1.1).
    /// </summary>
    /// <remarks>
    /// Files are read at startup (safe on Direct3D 12); they are put into
    /// materials and sprites when each scene loads, and again on
    /// <see cref="ApplyNow"/>. When two mods replace the same texture, the one
    /// whose folder sorts last wins and both are named in the log and in the
    /// Tool window, never silently.
    /// </remarks>
    public static class AssetReplacements
    {
        private static readonly Dictionary<string, TextureReplacement> ByName = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Texture2D> Replacements = new HashSet<Texture2D>();
        private static readonly Dictionary<Sprite, Sprite> SpriteFor = new Dictionary<Sprite, Sprite>();
        private static bool _hooked;
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        private static volatile bool _reloadRequested;
        private static float _reloadRequestedAt;

        /// <summary>Set by the last <see cref="ReloadFiles"/>: one entry per file it looked at.</summary>
        public static IReadOnlyList<ReloadResult> LastReload { get; private set; } = new ReloadResult[0];

        /// <summary>True while reloading is refused: switched off in the config, or after the game crashed during a reload.</summary>
        public static bool ReloadDisabled { get; internal set; }

        /// <summary>Why <see cref="ReloadDisabled"/> is set, for the Assets tab.</summary>
        public static string ReloadDisabledReason { get; internal set; }

        /// <summary>Every replacement found, by texture name.</summary>
        public static IReadOnlyCollection<TextureReplacement> All => ByName.Values;

        /// <summary>Replacements that lost to another mod's file for the same name.</summary>
        public static int ConflictCount
        {
            get
            {
                int n = 0;
                foreach (TextureReplacement r in ByName.Values)
                {
                    n += r.Overrides.Count;
                }
                return n;
            }
        }

        /// <summary>True when <paramref name="texture"/> is a replacement a mod shipped.</summary>
        public static bool IsReplacement(Texture2D texture)
        {
            return texture != null && Replacements.Contains(texture);
        }

        // Scans every plugin folder and loads the files. Called from the library's Awake.
        internal static void LoadAll()
        {
            string root = Paths.PluginPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }
            string[] mods = Directory.GetDirectories(root);
            Array.Sort(mods, StringComparer.OrdinalIgnoreCase);
            foreach (string modDir in mods)
            {
                string textures = System.IO.Path.Combine(modDir, "assets", "textures");
                if (!Directory.Exists(textures))
                {
                    continue;
                }
                string mod = System.IO.Path.GetFileName(modDir);
                string[] files = Directory.GetFiles(textures, "*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    var replacement = new TextureReplacement { Name = name, Mod = mod, Path = file, Texture = GameAssets.LoadTexture(file) };
                    if (replacement.Texture == null)
                    {
                        continue;
                    }
                    replacement.Texture.name = name;
                    replacement.ContentHash = HashFile(file);
                    if (ByName.TryGetValue(name, out TextureReplacement earlier))
                    {
                        replacement.Overrides.AddRange(earlier.Overrides);
                        replacement.Overrides.Add(earlier.Mod);
                        Replacements.Remove(earlier.Texture);
                        AssetsLibraryPlugin.Log.LogWarning($"Texture \"{name}\" is replaced by both {earlier.Mod} and {mod}; {mod} wins.");
                    }
                    ByName[name] = replacement;
                    Replacements.Add(replacement.Texture);
                }
            }
            if (ByName.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"{ByName.Count} texture replacement(s) loaded from mods.");
                if (!_hooked)
                {
                    SceneManager.sceneLoaded += (scene, mode) => ApplyNow();
                    ModFramework.Ready += () => ApplyNow();
                    _hooked = true;
                }
            }
        }

        /// <summary>
        /// Puts every replacement into the materials and sprites that use the
        /// original, now. Runs by itself when a scene loads; call it after the
        /// game created new materials or sprites. Returns how many places changed.
        /// </summary>
        public static int ApplyNow()
        {
            if (ByName.Count == 0)
            {
                return 0;
            }
            int changed = 0;
            try
            {
                changed += ApplyToMaterials();
                changed += ApplyToSprites();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Applying texture replacements failed: {ex}");
            }
            return changed;
        }

        /// <summary>
        /// Reads every replacement file again and swaps in the ones that changed,
        /// then applies them. A file that cannot be read keeps its old texture
        /// and is reported. Uploads textures while the game runs, which on
        /// Direct3D 12 can crash it; see <see cref="ReloadDisabled"/>.
        /// </summary>
        public static IReadOnlyList<ReloadResult> ReloadFiles()
        {
            var results = new List<ReloadResult>();
            if (ReloadDisabled)
            {
                results.Add(new ReloadResult("(reload)", ReloadDisabledReason ?? "reloading is switched off"));
                LastReload = results;
                return results;
            }
            var changed = new List<TextureReplacement>();
            foreach (TextureReplacement r in new List<TextureReplacement>(ByName.Values))
            {
                if (!File.Exists(r.Path))
                {
                    results.Add(new ReloadResult(r.Name, "file removed; the loaded texture stays until the game restarts"));
                    continue;
                }
                if (HashFile(r.Path) == r.ContentHash)
                {
                    results.Add(new ReloadResult(r.Name, "unchanged"));
                    continue;
                }
                changed.Add(r);
            }
            if (changed.Count == 0)
            {
                LastReload = results;
                return results;
            }
            var names = new List<string>();
            foreach (TextureReplacement r in changed)
            {
                names.Add(r.Name);
            }
            ReloadGuard.Begin(string.Join(" ", names));
            try
            {
                foreach (TextureReplacement r in changed)
                {
                    Texture2D fresh = GameAssets.LoadTextureFresh(r.Path, out string error);
                    if (fresh == null)
                    {
                        r.Problem = error;
                        results.Add(new ReloadResult(r.Name, error));
                        AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" was not reloaded: {error}");
                        continue;
                    }
                    fresh.name = r.Name;
                    Texture2D old = r.Texture;
                    Replacements.Remove(old);
                    Replacements.Add(fresh);
                    r.Texture = fresh;
                    r.Problem = null;
                    r.ContentHash = HashFile(r.Path);
                    r.Applied = 0;
                    var stale = new List<Sprite>();
                    foreach (KeyValuePair<Sprite, Sprite> kv in SpriteFor)
                    {
                        if (kv.Value != null && kv.Value.texture == old)
                        {
                            stale.Add(kv.Key);
                        }
                    }
                    foreach (Sprite s in stale)
                    {
                        SpriteFor.Remove(s);
                    }
                    ReplaceEverywhere(old, fresh);
                    results.Add(new ReloadResult(r.Name, "reloaded"));
                }
                ApplyNow();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Reloading texture replacements failed: {ex}");
                results.Add(new ReloadResult("(reload)", ex.Message));
            }
            finally
            {
                ReloadGuard.End();
            }
            LastReload = results;
            return results;
        }

        // Materials and sprite users that hold the previous replacement get the new one.
        private static void ReplaceEverywhere(Texture2D old, Texture2D fresh)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    if (m.GetTexture(property) == old)
                    {
                        m.SetTexture(property, fresh);
                    }
                }
            }
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && image.sprite != null && image.sprite.texture == old)
                {
                    image.sprite = ReCut(image.sprite, fresh);
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && renderer.sprite != null && renderer.sprite.texture == old)
                {
                    renderer.sprite = ReCut(renderer.sprite, fresh);
                }
            }
        }

        // The same sprite, cut from the new texture.
        private static Sprite ReCut(Sprite previous, Texture2D fresh)
        {
            Rect rect = previous.rect;
            float sx = (float)fresh.width / previous.texture.width, sy = (float)fresh.height / previous.texture.height;
            var scaled = new Rect(rect.x * sx, rect.y * sy, rect.width * sx, rect.height * sy);
            Vector2 pivot = new Vector2(previous.pivot.x / rect.width, previous.pivot.y / rect.height);
            Sprite sprite = Sprite.Create(fresh, scaled, pivot, previous.pixelsPerUnit * sx, 0, SpriteMeshType.FullRect, previous.border);
            sprite.name = previous.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream f = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(f));
                }
            }
            catch
            {
                return null;
            }
        }

        // Watches every replacement folder and asks for a reload on the next
        // Update, half a second after the last change, so an editor saving in
        // several steps triggers one reload. Never on Direct3D 12.
        internal static void WatchFiles()
        {
            if (Watchers.Count > 0 || !GameFonts.RuntimeUploadsAreSafe)
            {
                return;
            }
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextureReplacement r in ByName.Values)
            {
                folders.Add(System.IO.Path.GetDirectoryName(r.Path));
            }
            foreach (string folder in folders)
            {
                try
                {
                    var w = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    w.Changed += (s, e) => { _reloadRequested = true; };
                    w.Created += (s, e) => { _reloadRequested = true; };
                    w.Renamed += (s, e) => { _reloadRequested = true; };
                    w.EnableRaisingEvents = true;
                    Watchers.Add(w);
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"Could not watch {folder}: {ex.Message}");
                }
            }
            if (Watchers.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"Watching {Watchers.Count} texture folder(s) for changes.");
            }
        }

        // From the plugin's Update: runs the reload a watcher asked for.
        internal static void Tick()
        {
            if (ReloadDisabled)
            {
                _reloadRequested = false;
            }
            if (!_reloadRequested)
            {
                _reloadRequestedAt = 0f;
                return;
            }
            if (_reloadRequestedAt == 0f)
            {
                _reloadRequestedAt = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _reloadRequestedAt < 0.5f)
            {
                return;
            }
            _reloadRequested = false;
            _reloadRequestedAt = 0f;
            int n = 0;
            foreach (ReloadResult r in ReloadFiles())
            {
                if (r.Status == "reloaded")
                {
                    n++;
                }
            }
            AssetsLibraryPlugin.Log.LogMessage($"Texture files changed on disk: {n} replacement(s) reloaded.");
        }

        private static int ApplyToMaterials()
        {
            int changed = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    Texture current = m.GetTexture(property);
                    if (current == null || Replacements.Contains(current as Texture2D) ||
                        !ByName.TryGetValue(current.name, out TextureReplacement r))
                    {
                        continue;
                    }
                    m.SetTexture(property, r.Texture);
                    r.Applied++;
                    changed++;
                }
            }
            return changed;
        }

        private static int ApplyToSprites()
        {
            int changed = 0;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && TryReplacementSprite(image.sprite, out Sprite s))
                {
                    image.sprite = s;
                    changed++;
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && TryReplacementSprite(renderer.sprite, out Sprite s))
                {
                    renderer.sprite = s;
                    changed++;
                }
            }
            return changed;
        }

        // A sprite cut from the replacement texture with the original's rect,
        // pivot and pixels-per-unit, made once per original sprite. The
        // replacement image should have the original's size; a different size
        // is scaled to keep the same rect in the texture's proportions.
        private static bool TryReplacementSprite(Sprite original, out Sprite replacement)
        {
            replacement = null;
            if (original == null || original.texture == null || Replacements.Contains(original.texture))
            {
                return false;
            }
            if (SpriteFor.TryGetValue(original, out replacement) && replacement != null)
            {
                return true;
            }
            if (!ByName.TryGetValue(original.texture.name, out TextureReplacement r))
            {
                return false;
            }
            Rect rect = original.rect;
            float sx = (float)r.Texture.width / original.texture.width, sy = (float)r.Texture.height / original.texture.height;
            var scaled = new Rect(rect.x * sx, rect.y * sy, rect.width * sx, rect.height * sy);
            Vector2 pivot = new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height);
            replacement = Sprite.Create(r.Texture, scaled, pivot, original.pixelsPerUnit * sx, 0, SpriteMeshType.FullRect, original.border);
            replacement.name = original.name;
            replacement.hideFlags = HideFlags.DontUnloadUnusedAsset;
            SpriteFor[original] = replacement;
            r.Applied++;
            return true;
        }
    }
}
