using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The Mods screen. A game Menu, so MenuManager shows and hides it, and the
    // game handles the cursor and pad input as on any other screen.
    //
    // Two columns, like a settings app: the installed mods on the left (in
    // the game's scroll view, ModsMenu.List.cs), the selected one's details
    // on the right with its switch, notes and tabs (ModsMenu.Details.cs).
    // Every fixed word is its own label so translation mods can translate it.
    // Switching takes effect at the next launch.
    internal sealed partial class ModsMenu : Menu
    {
        internal RectTransform Content;
        internal RectTransform Details;

        // Strings shown on this screen. Translation packs key rows by the exact
        // English, so change them only together with the packs.
        internal const string TextRequired = "Required";
        internal const string TextOn = "On";
        internal const string TextOff = "Off";
        internal const string TextOffNextLaunch = "Off from the next launch";
        internal const string TextOnNextLaunch = "On from the next launch";
        internal const string TextConfirmOff = "Other mods need this one. Press Off again to switch it off anyway.";
        internal const string TextOutsidePlugins = "Installed outside BepInEx/plugins, so it cannot be switched off here";
        internal const string TextNeededBy = "Needed by";
        internal const string TextUses = "Uses";
        internal const string TextUnavailable = "Unavailable on this game build:";
        internal const string TextConflictTag = "Conflict";
        internal const string TextSameCode = "Changes the same game code as:";
        internal const string TextSameCodeRisky = "Changes the same game code as, and may override:";
        internal const string TextUpdateTag = "Update";
        internal const string TextReloaded = "Reloaded";
        internal const string TextNewVersion = "New version available:";
        internal const string TextOpenReleasePage = "Open release page";
        internal const string TextUninstall = "Uninstall";
        internal const string TextCancelUninstall = "Cancel uninstall";
        internal const string TextConfirmUninstall = "Press Uninstall again to remove this mod when the game next starts. Your settings for it are removed too.";
        internal const string TextUninstallNextLaunch = "Removed when the game next starts";

        private List<PatchConflicts.Conflict> _conflicts = new List<PatchConflicts.Conflict>();

        // The Back button's pointing hand is drawn just right of the button,
        // over the start of the list; keep the text clear of it.
        private const float ListLeftMargin = 110f;
        internal const float ListPanelLeft = ListLeftMargin - 34f;
        private const float RowHeight = 80f;


        private List<ModCatalog.Entry> _entries = new List<ModCatalog.Entry>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _detailParts = new List<GameObject>();
        private ModCatalog.Entry _selected;
        private ModCatalog.Entry _confirming;
        private ModCatalog.Entry _confirmingUninstall;

        protected override void OnShow(MenuResponseTransition response)
        {
            base.OnShow(response);
            _confirming = null;
            _confirmingUninstall = null;
            _tab = TabAbout;
            _query = "";
            _filter = ListFilter.All;
            _search?.SetTextWithoutNotify("");
            DropCheck();
            try
            {
                // The loaded mods at once; the rest when the check is in (ModsMenu.Check.cs).
                _entries = ModCatalog.Build(null);
                _conflicts = new List<PatchConflicts.Conflict>();
                _selected = _entries.FirstOrDefault(e => SameMod(e, _selected)) ?? FirstShown(_entries);
                StartCheck();
                RebuildList();
                RebuildDetails(false);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not list mods: {ex}");
            }
        }

        public override MenuResponse OnEvent(MenuEvent e)
        {
            if (e is MenuEventUserIntent intent && (intent.name == "Back" || intent.name == "Cancel"))
            {
                if (StepBack())
                {
                    return new MenuResponseIgnored();
                }
                return new MenuResponseTransition("Menu_Options", "Player left the Mods screen.");
            }
            return new MenuResponseIgnored();
        }

        // The details panel changed size (the window was resized). Its notes
        // measure the panel to decide where labels wrap, so lay them out again,
        // keeping the focused button. A page another mod built is left alone:
        // building it again could lose what the player has done on it.
        internal void OnDetailsResized()
        {
            if (OnModPage || !isActiveAndEnabled)
            {
                return;
            }
            // The list's filters and tags are measured too.
            RebuildKeepingFocus(true);
        }

        internal void Select(ModCatalog.Entry entry)
        {
            if (entry == null || ReferenceEquals(entry, _selected))
            {
                return;
            }
            _selected = entry;
            _confirming = null;
            _confirmingUninstall = null;
            _tab = TabAbout;
            MarkShownRow();
            RebuildDetails(false);
        }

        // ---- list ----

        private void RebuildList()
        {
            foreach (GameObject row in _rows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }
            _rows.Clear();

            BuildModList();
            if (Checking)
            {
                _rows.Add(CreateCheckingRow());
            }
        }

        // ---- details ----

        private void RebuildDetails(bool focusSwitch)
        {
            // Hidden at once, destroyed at the end of the frame: a search for
            // a button to focus never finds the old ones.
            foreach (GameObject part in _detailParts)
            {
                if (part != null)
                {
                    part.SetActive(false);
                    Destroy(part);
                }
            }
            _detailParts.Clear();
            _spinners.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

            ModCatalog.Entry entry = _selected;
            if (Details == null || entry == null)
            {
                return;
            }
            BuildDetails(entry, focusSwitch);
        }

        private static void OpenReleasePage(string url)
        {
            try
            {
                Application.OpenURL(url);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not open {url}: {ex.Message}");
            }
        }

        // An update check finished while the screen is open: show its result,
        // keeping what the pad or keyboard had selected. Settings and pages are
        // left alone until the player comes back to the list.
        internal void OnUpdatesChanged()
        {
            RebuildKeepingFocus();
        }

        private GameObject Part(string name, float left, float right, float bottom, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Details, false);
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _detailParts.Add(go);
            return go;
        }

        private List<PatchConflicts.Conflict> ConflictsOf(ModCatalog.Entry entry)
        {
            return entry.Guid == null ? new List<PatchConflicts.Conflict>() : _conflicts.Where(c => c.Guids.Contains(entry.Guid)).ToList();
        }

        // First press asks, second press records it; on a mod waiting to be
        // uninstalled the button takes the wish back.
        private void OnUninstall(ModCatalog.Entry entry)
        {
            if (Checking)
            {
                return;
            }
            try
            {
                if (!entry.PendingUninstall && _confirmingUninstall != entry)
                {
                    _confirming = null;
                    _confirmingUninstall = entry;
                    RebuildDetails(false);
                    Focus("Uninstall");
                    return;
                }
                _confirmingUninstall = null;
                ModCatalog.SetUninstall(_entries, entry, !entry.PendingUninstall);
                RebuildList();
                RebuildDetails(false);
                Focus("Uninstall");
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not change the uninstall of {entry.Name}: {ex}");
            }
        }

        private void OnSwitch(ModCatalog.Entry entry)
        {
            if (Checking)
            {
                return;
            }
            try
            {
                if (entry.WantOn)
                {
                    bool needed = entry.Dependents.Any(g => _entries.Any(x => x.Guid == g && x.WantOn));
                    if (needed && _confirming != entry)
                    {
                        _confirming = entry;
                        RebuildDetails(true);
                        return;
                    }
                }
                _confirming = null;
                ModCatalog.SetWantOn(_entries, entry, !entry.WantOn);
                RebuildList();
                RebuildDetails(true);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not switch {entry.Name}: {ex}");
            }
        }

        private string Status(ModCatalog.Entry entry)
        {
            if (entry.IsFramework)
            {
                return TextRequired;
            }
            if (_confirmingUninstall == entry)
            {
                return TextConfirmUninstall;
            }
            if (entry.PendingUninstall)
            {
                return TextUninstallNextLaunch;
            }
            if (_confirming == entry)
            {
                return TextConfirmOff;
            }
            if (entry.ProblemLabel != null && entry.WantOn)
            {
                return entry.ProblemLabel;
            }
            if (entry.Loaded && !entry.WantOn)
            {
                return TextOffNextLaunch;
            }
            if (!entry.Loaded && entry.WantOn)
            {
                return TextOnNextLaunch;
            }
            if (entry.RelativePath == null)
            {
                return TextOutsidePlugins;
            }
            return null;
        }

        private static bool SameMod(ModCatalog.Entry a, ModCatalog.Entry b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            return a.Guid == b.Guid && string.Equals(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
        }

        // Mod names and descriptions are plain text, not rich text.
        private static string Escape(string text)
        {
            return (text ?? "").Replace("<", "<noparse><</noparse>");
        }
    }

    // Tells the menu when the details panel is resized, at most once a frame
    // and only once the size has settled for that frame.
    internal sealed class DetailsResizeWatcher : UIBehaviour
    {
        internal ModsMenu Menu;
        private Vector2 _builtFor;
        private bool _dirty;

        protected override void OnEnable()
        {
            base.OnEnable();
            _builtFor = ((RectTransform)transform).rect.size;
            _dirty = false;
        }

        protected override void OnRectTransformDimensionsChange()
        {
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            Vector2 size = ((RectTransform)transform).rect.size;
            if (Mathf.Abs(size.x - _builtFor.x) < 1f && Mathf.Abs(size.y - _builtFor.y) < 1f)
            {
                return;
            }
            _builtFor = size;
            try
            {
                Menu?.OnDetailsResized();
            }
            catch (System.Exception ex)
            {
                ModFramework.Log.LogError($"Could not lay out the Mods screen again: {ex}");
            }
        }
    }

    // Tells the menu when an update check has a result, and when the check of
    // the mods' files is in.
    internal sealed class UpdateResultWatcher : MonoBehaviour
    {
        internal ModsMenu Menu;
        private int _seen = -1;
        private int _seenNetwork = -1;

        private void OnEnable()
        {
            _seen = Updates.UpdateCheck.Revision;
            _seenNetwork = NetworkWatch.Revision;
        }

        // Also when the framework sees a mod connect somewhere new.
        private void LateUpdate()
        {
            try
            {
                Menu?.PollCheck();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not show the check of the mods: {ex}");
            }
            if (_seen == Updates.UpdateCheck.Revision && _seenNetwork == NetworkWatch.Revision)
            {
                return;
            }
            _seen = Updates.UpdateCheck.Revision;
            _seenNetwork = NetworkWatch.Revision;
            try
            {
                Menu?.OnUpdatesChanged();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not show update check results: {ex}");
            }
        }
    }
}
