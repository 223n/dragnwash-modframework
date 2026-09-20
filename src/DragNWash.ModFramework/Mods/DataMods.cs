using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// A mod with no code: a folder in <c>BepInEx/plugins</c> with a
    /// <c>mod.json</c> and, beside it, a folder per kind of content — today
    /// <c>overrides/</c> (the Overrides library) and <c>graphs/</c> (the Graphs
    /// library). Found by <see cref="DataMods"/>. Since 1.4.0.
    /// </summary>
    public sealed class DataMod
    {
        /// <summary>From <c>mod.json</c>, else made from the folder's name.</summary>
        public string Guid { get; internal set; }
        /// <summary>Its name on the Mods screen.</summary>
        public string Name { get; internal set; }
        /// <summary>Its version, <c>1.0.0</c> when the manifest gives none.</summary>
        public string Version { get; internal set; }
        /// <summary>What it says it does, or null.</summary>
        public string Description { get; internal set; }
        /// <summary>Who made it, or null.</summary>
        public string[] Authors { get; internal set; }
        /// <summary>Where to read more, or null.</summary>
        public string Website { get; internal set; }
        /// <summary>The mod's folder.</summary>
        public string Folder { get; internal set; }
        /// <summary>Its <c>mod.json</c> (or <c>mod.json.disabled</c> while it is off).</summary>
        public string ManifestPath { get; internal set; }
        /// <summary>Place in the load order (folder name order); a later mod wins a clash.</summary>
        public int Order { get; internal set; }
        /// <summary>False while the player has it switched off (<c>mod.json.disabled</c>).</summary>
        public bool On { get; internal set; }
        /// <summary>What could not be read in the manifest, for the Mods screen and the log.</summary>
        public IReadOnlyList<string> Problems { get; internal set; }

        /// <summary>
        /// The folder of one kind of content inside this mod (<c>overrides</c>,
        /// <c>graphs</c>), or null when it has none.
        /// </summary>
        public string FolderFor(string kind)
        {
            string path = Path.Combine(Folder, kind ?? "");
            return Directory.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Finds the mods with no code, once, for every library that reads a kind of
    /// content out of them, and lists them on the Mods screen so a player can
    /// switch one off like any mod. A library asks for the mods that carry its
    /// kind (<see cref="With"/>) and reads the files itself: the core knows
    /// nothing of overrides or graphs, and neither library needs the other.
    /// Since 1.4.0.
    /// </summary>
    public static class DataMods
    {
        private static List<DataMod> _found;
        private static readonly HashSet<string> Registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Largest manifest read, in bytes.</summary>
        private const long MaxManifestBytes = 256 * 1024;

        /// <summary>
        /// Every mod with no code found in <c>BepInEx/plugins</c>, in folder name
        /// order, the ones switched off included (<see cref="DataMod.On"/>). Found
        /// on the first ask.
        /// </summary>
        public static IReadOnlyList<DataMod> All => _found ?? (_found = Scan());

        /// <summary>
        /// The mods that carry a kind of content, switched on, in load order:
        /// <c>DataMods.With("overrides")</c>, <c>DataMods.With("graphs")</c>.
        /// </summary>
        public static IReadOnlyList<DataMod> With(string kind)
        {
            return All.Where(m => m.On && m.FolderFor(kind) != null).ToList();
        }

        /// <summary>Looks again, for a library that reads its files again (a reload).</summary>
        public static void Rescan()
        {
            _found = Scan();
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[data mods] A listener of DataMods.Changed threw: {ex.Message}");
            }
        }

        /// <summary>Raised after <see cref="Rescan"/>.</summary>
        public static event Action Changed;

        private static List<DataMod> Scan()
        {
            var mods = new List<DataMod>();
            string plugins = BepInEx.Paths.PluginPath;
            if (string.IsNullOrEmpty(plugins) || !Directory.Exists(plugins))
            {
                return mods;
            }
            foreach (string folder in Directory.GetDirectories(plugins).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                // A folder with a DLL is a mod with code, which BepInEx loads.
                if (Directory.GetFiles(folder, "*.dll").Length > 0)
                {
                    continue;
                }
                string on = Path.Combine(folder, "mod.json");
                string off = Path.Combine(folder, "mod.json.disabled");
                bool isOn = File.Exists(on);
                if (!isOn && !File.Exists(off))
                {
                    continue;
                }
                DataMod mod = Read(folder, isOn ? on : off, isOn);
                mod.Order = mods.Count;
                mods.Add(mod);
                Register(mod);
            }
            return mods;
        }

        private static DataMod Read(string folder, string manifestPath, bool on)
        {
            string folderName = Path.GetFileName(folder);
            var problems = new List<string>();
            var manifest = new Dictionary<string, object>();
            try
            {
                if (new FileInfo(manifestPath).Length > MaxManifestBytes)
                {
                    problems.Add($"mod.json is larger than {MaxManifestBytes / 1024} KB and was not read.");
                }
                else
                {
                    manifest = Json.Parse(File.ReadAllText(manifestPath)) as Dictionary<string, object> ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                problems.Add("mod.json could not be read: " + ex.Message);
            }
            string[] authors = null;
            if (manifest.TryGetValue("authors", out object listed) && listed is List<object> names)
            {
                authors = names.Where(n => n != null).Select(n => n.ToString()).ToArray();
            }
            else if (Json.String(manifest, "author") is string one)
            {
                authors = new[] { one };
            }
            string name = Json.String(manifest, "name");
            string guid = Json.String(manifest, "guid");
            return new DataMod
            {
                Guid = !string.IsNullOrEmpty(guid) ? guid : "data." + Slug(folderName),
                Name = !string.IsNullOrEmpty(name) ? name : folderName,
                Version = Json.String(manifest, "version") ?? "1.0.0",
                Description = Json.String(manifest, "description"),
                Authors = authors != null && authors.Length > 0 ? authors : null,
                Website = Json.String(manifest, "website"),
                Folder = folder,
                ManifestPath = manifestPath,
                On = on,
                Problems = problems,
            };
        }

        // On the Mods screen, so a player can see it and switch it off. A library
        // may say more about what its own content does (Overrides: private
        // values), which it does by registering the mod again with its own words.
        private static void Register(DataMod mod)
        {
            if (!Registered.Add(mod.Guid))
            {
                return;
            }
            try
            {
                ModFramework.RegisterDataMod(new ModInfo
                {
                    Guid = mod.Guid,
                    DisplayName = mod.Name,
                    Description = mod.Description ?? "A mod with no code.",
                    Authors = mod.Authors,
                    Website = mod.Website,
                    // Always the switched-on name: that is what the Mods screen
                    // and the preloader keep in their list, and what they rename
                    // when the player switches the mod off and on again.
                }, mod.Version, Path.Combine(mod.Folder, "mod.json"));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[data mods] {mod.Name} could not be listed on the Mods screen: {ex.Message}");
            }
        }

        private static string Slug(string text)
        {
            var kept = text.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_').ToArray();
            return kept.Length > 0 ? new string(kept).ToLowerInvariant() : "mod";
        }
    }
}
