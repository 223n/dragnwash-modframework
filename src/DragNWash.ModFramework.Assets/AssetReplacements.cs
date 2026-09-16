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
