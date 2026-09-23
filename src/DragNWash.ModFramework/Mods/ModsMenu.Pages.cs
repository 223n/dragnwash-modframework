using System;
using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Mods
{
    // Pages other mods add to their entry on the Mods screen (ModsScreenPage).
    // Each is a tab of the mod's details; the tab's area is handed over to it.
    internal sealed partial class ModsMenu
    {
        internal const string TextPageFailed = "This page could not be shown. See BepInEx/LogOutput.log.";
        internal const string TextTryAgain = "Try again";

        private void BuildPage(ModsScreenPage page)
        {
            RectTransform root = ModsLook.Rect(_body, "PageContent");
            ModsLook.Stretch(root);
            try
            {
                page.Build(root);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"The page \"{page.Title}\" of {page.Guid} threw while building: {ex}");
                // Whatever the page built before it threw is hidden, so half a
                // page does not sit there looking like the whole of it. Some of
                // what a page shows can be missing for a moment only, so one
                // press builds it again rather than a restart.
                root.gameObject.SetActive(false);
                RectTransform content = ScrollArea(_body);
                Line(content, TextPageFailed, 22f, FontStyles.Normal, ModsLook.Warning, 0f);
                Spacer(content, 12f);
                RectTransform row = ModsLook.Rect(content, "TryAgainRow");
                ModsLook.Size(row.gameObject, -1f, 48f, 1f, 0f);
                GameObject button = FlatButton(row, "TryAgain", TextTryAgain, 20f, ModsLook.Raised, ModsLook.Label, () =>
                {
                    RebuildDetails(false);
                    Focus("TryAgain");
                });
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                Focus("TryAgain");
            }
        }
    }
}
