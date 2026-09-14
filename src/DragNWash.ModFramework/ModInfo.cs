namespace DragNWash.ModFramework
{
    /// <summary>
    /// What the Mods screen shows about a mod beyond the name and version BepInEx
    /// already knows. Pass it to <see cref="ModFramework.Register"/>.
    /// </summary>
    public sealed class ModInfo
    {
        /// <summary>The mod's BepInEx GUID. Required.</summary>
        public string Guid { get; set; }

        /// <summary>
        /// Name for players, e.g. "Drag'n Wash Localization". BepInEx plugin names
        /// often cannot hold spaces or punctuation; this can. Defaults to the
        /// BepInEx name.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>One or two sentences on what the mod does.</summary>
        public string Description { get; set; }

        /// <summary>Who made the mod.</summary>
        public string[] Authors { get; set; }

        /// <summary>Project page or download page.</summary>
        public string Website { get; set; }

        /// <summary>
        /// True for a library: a prerequisite mod other mods build on. The Mods
        /// screen shows it as a library and lists the mods that need it.
        /// </summary>
        public bool IsLibrary { get; set; }

        /// <summary>
        /// Icon shown next to the name on the Mods screen, ideally square. Create it
        /// in the plugin's Awake: on Direct3D 12 a texture made later can crash the
        /// game. Wins over <see cref="IconPath"/>.
        /// </summary>
        public UnityEngine.Texture2D Icon { get; set; }

        /// <summary>
        /// A PNG or JPG file to load as the icon when the mod registers, for example
        /// <c>Path.Combine(Path.GetDirectoryName(Info.Location), "icon.png")</c>.
        /// Register from Awake so it loads at startup.
        /// </summary>
        public string IconPath { get; set; }
    }
}
