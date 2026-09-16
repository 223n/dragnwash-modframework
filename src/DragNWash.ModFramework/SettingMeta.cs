using System;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// How the Mods screen shows one setting. Put an instance in the tags of the
    /// entry's <c>ConfigDescription</c>:
    /// <code>
    /// Config.Bind("Reload", "WatchFiles", false,
    ///     new ConfigDescription("Reloads a PNG when it changes.", null,
    ///         new SettingMeta { Advanced = true, Order = 20 }));
    /// </code>
    /// A mod that does not reference the framework can use any object with the
    /// same member names (or ConfigurationManager's <c>IsAdvanced</c>,
    /// <c>DispName</c> and <c>Order</c>); the Mods screen reads them the same way.
    /// </summary>
    public sealed class SettingMeta
    {
        /// <summary>Shown instead of the config key.</summary>
        public string DisplayName { get; set; }

        /// <summary>Position within the section, lowest first; ties keep the key order.</summary>
        public int Order { get; set; }

        /// <summary>Hidden until the player turns on "Show advanced settings" on the page.</summary>
        public bool Advanced { get; set; }

        /// <summary>The page says the change takes effect after the game restarts.</summary>
        public bool RequiresRestart { get; set; }
    }

    /// <summary>
    /// How the Mods screen shows a config section. Put it in the tags of any
    /// entry of that section; the section is the entry's own.
    /// </summary>
    public sealed class SectionMeta
    {
        /// <summary>Shown instead of the section name.</summary>
        public string DisplayName { get; set; }

        /// <summary>Shown under the section heading.</summary>
        public string Description { get; set; }

        /// <summary>Position among the sections, lowest first; ties keep the name order.</summary>
        public int Order { get; set; }
    }
}
