using System;
using UnityEngine;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// An extra page for a mod on the Mods screen, opened with a button next to
    /// the mod's Settings. The framework clears the details panel and hands it to
    /// <see cref="Build"/>; Back returns to the list of mods.
    /// </summary>
    public sealed class ModsScreenPage
    {
        /// <summary>BepInEx GUID of the mod the page belongs to. Required.</summary>
        public string Guid { get; set; }

        /// <summary>Button label and page title, in English. Required.</summary>
        public string Title { get; set; }

        /// <summary>
        /// Fills the page. Receives the panel to build into (a RectTransform that is
        /// cleared before each call); use UnityEngine.UI and TextMeshPro components.
        /// </summary>
        public Action<RectTransform> Build { get; set; }
    }
}
