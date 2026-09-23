using System;
using System.Collections.Generic;
using System.IO;

namespace DragNWash.ModFramework.Assets
{
    // Texture replacements that apply in one language: the folders mods add,
    // switching them on and off, following a language change, and loading a
    // language's pictures with its fallbacks.
    public static partial class AssetReplacements
    {
        /// <summary>
        /// Adds replacements that apply only in one language:
        /// <c>&lt;root&gt;/&lt;language&gt;/&lt;subfolder&gt;/&lt;texture name&gt;.png</c>
        /// applies while <see cref="GameFonts.Language"/> is that language.
        /// Only the language in use is loaded. Call from Awake, after
        /// <see cref="GameFonts.SetLanguage"/>. A language picture wins over a
        /// plain replacement of the same texture; both are named in the log.
        /// Experimental (Assets 1.2).
        /// </summary>
        public static void AddLanguageFolder(string guid, string root, string subfolder)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(root))
            {
                return;
            }
            LanguageFolders.RemoveAll(f => f.Guid == guid);
            var folder = new LanguageFolder { Guid = guid, Root = root, Subfolder = subfolder ?? "", Mod = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(root.TrimEnd('/', '\\'))) };
            LanguageFolders.Add(folder);
            string language = GameFonts.Language;
            if (_loadedLanguage == null || string.Equals(_loadedLanguage, language, StringComparison.OrdinalIgnoreCase))
            {
                // Startup (or the same language): loading now is safe.
                _loadedLanguage = language;
                LoadLanguage(folder, language);
                Hook();
                ApplyNow();
            }
            else if (!GameFonts.RuntimeUploadsAreSafe)
            {
                PendingLanguage = language;
            }
            else
            {
                LoadLanguage(folder, _loadedLanguage);
                ApplyNow();
            }
        }

        /// <summary>
        /// Switches one mod's language pictures off or on. Off takes them back at
        /// once; on applies them again, loading them first where that is safe
        /// (on Direct3D 12 pictures that were never loaded wait for a restart).
        /// </summary>
        public static void SetLanguageFoldersEnabled(string guid, bool on)
        {
            foreach (LanguageFolder f in LanguageFolders)
            {
                if (f.Guid != guid || f.Enabled == on)
                {
                    continue;
                }
                f.Enabled = on;
                if (on && !HasLoaded(f))
                {
                    if (GameFonts.RuntimeUploadsAreSafe)
                    {
                        LoadLanguage(f, _loadedLanguage);
                    }
                    else
                    {
                        PendingLanguage = _loadedLanguage;
                    }
                }
            }
            RefreshAll();
        }

        private static bool HasLoaded(LanguageFolder folder)
        {
            foreach (LanguageFolder f in FolderOf.Values)
            {
                if (f == folder)
                {
                    return true;
                }
            }
            return false;
        }

        // From GameFonts.SetLanguage.
        internal static void OnLanguageSet(string language)
        {
            if (LanguageFolders.Count == 0)
            {
                return;
            }
            try
            {
                if (string.Equals(language, _loadedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    // Back to the language loaded at startup: its pictures apply again.
                    foreach (TextureReplacement r in Suspended)
                    {
                        LanguageByName[r.Name] = r;
                    }
                    Suspended.Clear();
                    PendingLanguage = null;
                    RefreshAll();
                    return;
                }
                if (!GameFonts.RuntimeUploadsAreSafe)
                {
                    // Taking pictures back needs no upload; loading the new ones does.
                    PendingLanguage = language;
                    DisableLoaded();
                    RefreshAll();
                    AssetsLibraryPlugin.Log.LogInfo($"Pictures for \"{language}\" apply after a restart (Direct3D 12 cannot load textures while the game runs).");
                    return;
                }
                var previous = new List<TextureReplacement>(LanguageByName.Values);
                LanguageByName.Clear();
                FolderOf.Clear();
                RefreshAll(previous);
                foreach (TextureReplacement r in previous)
                {
                    Replacements.Remove(r.Texture);
                    if (r.Texture != null)
                    {
                        UnityEngine.Object.Destroy(r.Texture);
                    }
                }
                _loadedLanguage = language;
                PendingLanguage = null;
                foreach (LanguageFolder f in LanguageFolders)
                {
                    LoadLanguage(f, language);
                }
                RefreshAll();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Switching language pictures to \"{language}\" failed: {ex}");
            }
        }

        private static void DisableLoaded()
        {
            foreach (TextureReplacement r in LanguageByName.Values)
            {
                Suspended.Add(r);
            }
            LanguageByName.Clear();
        }

        // The pictures for a language, then for each language its fallback.txt
        // names (one per line, in order) the pictures it lacks. What no language
        // in the chain has falls back to a plain replacement, then to the game's.
        private static void LoadLanguage(LanguageFolder folder, string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<string> { language };
            foreach (string fallback in FallbacksOf(folder, language))
            {
                if (!chain.Exists(l => string.Equals(l, fallback, StringComparison.OrdinalIgnoreCase)))
                {
                    chain.Add(fallback);
                }
            }
            foreach (string source in chain)
            {
                LoadLanguageFiles(folder, language, source, names);
            }
        }

        private static List<string> FallbacksOf(LanguageFolder folder, string language)
        {
            var list = new List<string>();
            string file = System.IO.Path.Combine(LanguageDir(folder, language), "fallback.txt");
            try
            {
                if (!File.Exists(file))
                {
                    return list;
                }
                foreach (string line in File.ReadAllLines(file))
                {
                    string l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (l.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || l.Contains(".."))
                    {
                        AssetsLibraryPlugin.Log.LogWarning($"{file}: \"{l}\" is not a language folder name; skipped.");
                        continue;
                    }
                    list.Add(l);
                }
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogWarning($"{file} could not be read: {ex.Message}");
            }
            return list;
        }

        private static string LanguageDir(LanguageFolder folder, string language) =>
            System.IO.Path.Combine(System.IO.Path.Combine(folder.Root, language), folder.Subfolder);

        private static void LoadLanguageFiles(LanguageFolder folder, string language, string source, HashSet<string> taken)
        {
            string dir = LanguageDir(folder, source);
            if (!Directory.Exists(dir))
            {
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int loaded = 0;
            foreach (string file in files)
            {
                // A picture the language (or an earlier fallback) has already wins.
                if (taken.Contains(System.IO.Path.GetFileNameWithoutExtension(file)))
                {
                    continue;
                }
                TextureReplacement r = Load(file, folder.Mod);
                if (r == null)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"{file} could not be loaded; the texture falls back to the next picture, or the game's.");
                    continue;
                }
                taken.Add(r.Name);
                r.Language = source;
                if (LanguageByName.TryGetValue(r.Name, out TextureReplacement earlier))
                {
                    r.Overrides.AddRange(earlier.Overrides);
                    r.Overrides.Add(earlier.Mod);
                    Replacements.Remove(earlier.Texture);
                    FolderOf.Remove(earlier);
                    AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" has a {language} picture in both {earlier.Mod} and {r.Mod}; {r.Mod} wins.");
                }
                if (ByName.TryGetValue(r.Name, out TextureReplacement plain))
                {
                    r.Overrides.Add(plain.Mod);
                    AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" is replaced by {plain.Mod} and has a {language} picture in {r.Mod}; the {language} picture applies while that language is in use.");
                }
                LanguageByName[r.Name] = r;
                FolderOf[r] = folder;
                Replacements.Add(r.Texture);
                loaded++;
            }
            if (loaded > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo(source == language
                    ? $"{loaded} {language} picture(s) loaded from {folder.Mod}."
                    : $"{loaded} {source} picture(s) loaded from {folder.Mod} as fallbacks for {language}.");
            }
        }
    }
}
