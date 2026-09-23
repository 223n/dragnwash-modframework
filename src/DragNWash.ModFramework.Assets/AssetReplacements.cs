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

        /// <summary>The language it applies to, or null when it applies whatever the language (a plain replacement).</summary>
        public string Language { get; internal set; }

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
    //
    // Split by responsibility into AssetReplacements.<Part>.cs beside this file:
    // Languages (per-language folders and switching language), Apply (putting
    // replacements in and taking them back) and Reload (reading changed files
    // again, and watching the folders). This file keeps the state, the lookups
    // and the startup scan of plugin folders.
    public static partial class AssetReplacements
    {
        private static readonly Dictionary<string, TextureReplacement> ByName = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Texture2D> Replacements = new HashSet<Texture2D>();
        private static readonly Dictionary<Sprite, Sprite> SpriteFor = new Dictionary<Sprite, Sprite>();
        private static bool _hooked;

        // Replacements per language (AddLanguageFolder). Only the language in use
        // is loaded; its files are in LanguageByName while they apply.
        private sealed class LanguageFolder
        {
            public string Guid, Root, Subfolder, Mod;
            public bool Enabled = true;
        }
        private static readonly List<LanguageFolder> LanguageFolders = new List<LanguageFolder>();
        private static readonly Dictionary<string, TextureReplacement> LanguageByName = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<TextureReplacement, LanguageFolder> FolderOf = new Dictionary<TextureReplacement, LanguageFolder>();
        private static string _loadedLanguage;

        // Every replacement read: plain ones, then the current language's.
        internal static List<TextureReplacement> AllRead()
        {
            var all = new List<TextureReplacement>(ByName.Values);
            all.AddRange(LanguageByName.Values);
            return all;
        }

        // What was there before a replacement went in, so it can be taken back:
        // per material property, and per replacement sprite. Holding the
        // originals keeps Unity from unloading them while they are replaced.
        private static readonly Dictionary<string, Texture> MaterialOriginal = new Dictionary<string, Texture>();
        private static readonly Dictionary<Sprite, Sprite> OriginalSprite = new Dictionary<Sprite, Sprite>();

        /// <summary>
        /// The language whose pictures apply after a restart, or null. Set on
        /// Direct3D 12, where a language change cannot load textures while the
        /// game runs; the previous language's pictures are taken back meanwhile.
        /// </summary>
        public static string PendingLanguage { get; private set; }

        /// <summary>Raised after replacements were loaded, taken back or re-applied because of a language change or <see cref="SetLanguageFoldersEnabled"/>.</summary>
        public static event Action Changed;
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        private static volatile bool _reloadRequested;
        private static float _reloadRequestedAt;

        /// <summary>Set by the last <see cref="ReloadFiles"/>: one entry per file it looked at.</summary>
        public static IReadOnlyList<ReloadResult> LastReload { get; private set; } = new ReloadResult[0];

        /// <summary>True while reloading is refused: switched off in the config, or after the game crashed during a reload.</summary>
        public static bool ReloadDisabled { get; internal set; }

        /// <summary>Why <see cref="ReloadDisabled"/> is set, for the Assets tab.</summary>
        public static string ReloadDisabledReason { get; internal set; }

        /// <summary>Every replacement found: the plain ones by texture name, then the current language's.</summary>
        public static IReadOnlyCollection<TextureReplacement> All
        {
            get
            {
                var all = new List<TextureReplacement>(ByName.Values);
                all.AddRange(LanguageByName.Values);
                return all;
            }
        }

        /// <summary>Replacements that lost to another mod's file for the same name.</summary>
        public static int ConflictCount
        {
            get
            {
                int n = 0;
                foreach (TextureReplacement r in All)
                {
                    n += r.Overrides.Count;
                }
                return n;
            }
        }

        // The replacement that applies to a texture of this name now: the current
        // language's picture when its folder is on, else a plain replacement.
        private static TextureReplacement Effective(string name)
        {
            if (name != null && LanguageByName.TryGetValue(name, out TextureReplacement l) && l.Texture != null && FolderOf.TryGetValue(l, out LanguageFolder f) && f.Enabled)
            {
                return l;
            }
            return name != null && ByName.TryGetValue(name, out TextureReplacement r) && r.Texture != null ? r : null;
        }

        // Direct3D 12 waiting for a restart: the loaded pictures stay in memory
        // but no longer apply, as if their folders were off.
        private static readonly HashSet<TextureReplacement> Suspended = new HashSet<TextureReplacement>();

        private static TextureReplacement Load(string file, string mod)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            var replacement = new TextureReplacement { Name = name, Mod = mod, Path = file, Texture = GameAssets.LoadTexture(file) };
            if (replacement.Texture == null)
            {
                return null;
            }
            replacement.Texture.name = name;
            replacement.ContentHash = HashFile(file);
            return replacement;
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }
            GameEvents.OnSceneLoaded(GameFonts.Guid, (scene, mode) => ApplyNow());
            ModFramework.Ready += () => ApplyNow();
            _hooked = true;
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
                    TextureReplacement replacement = Load(file, mod);
                    if (replacement == null)
                    {
                        continue;
                    }
                    string name = replacement.Name;
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
                Hook();
            }
        }
    }
}
