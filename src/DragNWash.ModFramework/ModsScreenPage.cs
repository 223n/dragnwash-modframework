using System;
using UnityEngine;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// An extra page for a mod on the Mods screen: a tab of the mod's details,
    /// after About, Settings and Internet. The framework hands the tab's area to
    /// <see cref="Build"/>; Back steps out of the page to the row of tabs.
    /// </summary>
    public sealed class ModsScreenPage
    {
        /// <summary>BepInEx GUID of the mod the page belongs to. Required.</summary>
        public string Guid { get; set; }

        /// <summary>The tab's title, in English. Required.</summary>
        public string Title { get; set; }

        /// <summary>
        /// Fills the page. Receives the panel to build into (a RectTransform that is
        /// cleared before each call); use UnityEngine.UI and TextMeshPro components.
        /// </summary>
        public Action<RectTransform> Build { get; set; }
    }
}
