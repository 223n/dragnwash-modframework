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
    }
}
