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
    // Two columns, like Forge's mod list: the installed mods on the left (in
    // the game's scroll view), details of the selected one on the right with
    // its On/Off button. Every fixed word is its own label so translation mods
    // can translate it. Switching takes effect at the next launch.
    internal sealed class ModsMenu : Menu
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

        // The Back button's pointing hand is drawn just right of the button,
        // over the start of the list; keep the text clear of it.
        private const float ListLeftMargin = 110f;
        private const float RowHeight = 96f;

        private static readonly Color BandColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color OnColor = new Color(0.36f, 0.62f, 0.36f, 1f);
        private static readonly Color OffColor = new Color(0.62f, 0.3f, 0.27f, 1f);

        private List<ModCatalog.Entry> _entries = new List<ModCatalog.Entry>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _detailParts = new List<GameObject>();
        private ModCatalog.Entry _selected;
        private ModCatalog.Entry _confirming;

        protected override void OnShow(MenuResponseTransition response)
        {
            base.OnShow(response);
            _confirming = null;
            try
            {
                _entries = ModCatalog.Build();
                _selected = _entries.FirstOrDefault(e => SameMod(e, _selected)) ?? _entries.FirstOrDefault();
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
                return new MenuResponseTransition("Menu_Options", "Player left the Mods screen.");
            }
            return new MenuResponseIgnored();
        }

        internal void Select(ModCatalog.Entry entry)
        {
            if (entry == null || ReferenceEquals(entry, _selected))
            {
                return;
            }
            _selected = entry;
            _confirming = null;
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

            foreach (ModCatalog.Entry entry in _entries)
            {
                _rows.Add(CreateListRow(entry));
            }
        }

        private GameObject CreateListRow(ModCatalog.Entry entry)
        {
            var row = new GameObject("Mod " + entry.Name, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;
            layout.flexibleWidth = 1f;

            var band = new GameObject("Band", typeof(RectTransform));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(row.transform, false);
            bandRect.anchorMin = Vector2.zero;
            bandRect.anchorMax = Vector2.one;
            bandRect.offsetMin = new Vector2(ListLeftMargin - 20f, 6f);
            bandRect.offsetMax = new Vector2(0f, -6f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;

            Button button = band.AddComponent<Button>();
            button.targetGraphic = bandImage;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.7f);
            colors.highlightedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.selectedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.pressedColor = new Color(0.45f, 0.6f, 0.9f, 1f);
            button.colors = colors;
            ModRowSelect select = band.AddComponent<ModRowSelect>();
            select.Menu = this;
            select.Entry = entry;
            button.onClick.AddListener(() => Select(entry));

            TMP_Text name = UiText.Create(band.transform, "Name", Escape(entry.DisplayName), UiText.NameSize);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var nameRect = (RectTransform)name.transform;
            nameRect.anchorMax = new Vector2(0.72f, 1f);
            nameRect.offsetMin = new Vector2(20f, 0f);

            if (!entry.IsFramework)
            {
                TMP_Text state = UiText.Create(band.transform, "State", entry.WantOn ? TextOn : TextOff, UiText.BodySize);
                state.alignment = TextAlignmentOptions.MidlineRight;
                state.color = entry.WantOn ? new Color(0.65f, 0.95f, 0.65f, 1f) : new Color(1f, 0.6f, 0.55f, 1f);
                var stateRect = (RectTransform)state.transform;
                stateRect.anchorMin = new Vector2(0.72f, 0f);
                stateRect.offsetMax = new Vector2(-20f, 0f);
            }
            return row;
        }

        // ---- details ----

        private void RebuildDetails(bool focusSwitch)
        {
            foreach (GameObject part in _detailParts)
            {
                if (part != null)
                {
                    Destroy(part);
                }
            }
            _detailParts.Clear();

            ModCatalog.Entry entry = _selected;
            if (Details == null || entry == null)
            {
                return;
            }

            GameObject band = Part("Band", 0f, 1f, 0f, 1f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;
            bandImage.raycastTarget = false;

            Label("Name", Escape(entry.DisplayName), UiText.TitleSize * 0.6f, 0.86f, 0.98f, false);

            string meta = string.IsNullOrEmpty(entry.Version) ? "" : "v" + Escape(entry.Version);
            string[] authors = entry.Info?.Authors;
            if (authors != null && authors.Length > 0)
            {
                meta += (meta.Length > 0 ? "   " : "") + Escape(string.Join(", ", authors));
            }
            if (meta.Length > 0)
            {
                Label("Meta", meta, UiText.BodySize, 0.78f, 0.86f, false);
            }

            string description = entry.Info?.Description;
            if (!string.IsNullOrEmpty(description))
            {
                TMP_Text d = Label("Description", description, UiText.BodySize, 0.5f, 0.77f, true);
                d.alignment = TextAlignmentOptions.TopLeft;
            }

            string website = entry.Info?.Website;
            if (!string.IsNullOrEmpty(website))
            {
                Label("Website", Escape(website), UiText.BodySize * 0.85f, 0.42f, 0.5f, false);
            }

            string status = Status(entry);
            if (status != null)
            {
                TMP_Text s = Label("Status", status, UiText.BodySize, 0.26f, 0.41f, true);
                s.fontStyle |= FontStyles.Italic;
                s.alignment = TextAlignmentOptions.TopLeft;
            }

            if (_confirming == entry)
            {
                Label("NeededByLabel", TextNeededBy, UiText.BodySize, 0.19f, 0.26f, false).fontStyle |= FontStyles.Bold;
                string names = string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g)));
                TMP_Text n = Label("NeededBy", Escape(names), UiText.BodySize, 0.19f, 0.26f, false);
                ((RectTransform)n.transform).offsetMin = new Vector2(260f, 0f);
            }

            if (entry.CanSwitch)
            {
                GameObject button = CreateSwitch(entry);
                if (focusSwitch && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(button);
                }
            }
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

        private TMP_Text Label(string name, string text, float size, float bottom, float top, bool wrap)
        {
            TMP_Text label = UiText.Create(Details, name, text, size);
            _detailParts.Add(label.gameObject);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (wrap)
            {
                label.enableAutoSizing = false;
                label.fontSize = size;
            }
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = new Vector2(28f, 0f);
            rect.offsetMax = new Vector2(-28f, 0f);
            return label;
        }

        private GameObject CreateSwitch(ModCatalog.Entry entry)
        {
            GameObject go = Part("Switch", 0.04f, 0.4f, 0.04f, 0.17f);
            Image background = go.AddComponent<Image>();
            background.color = entry.WantOn ? OnColor : OffColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            TMP_Text label = UiText.Create(go.transform, "Label", entry.WantOn ? TextOn : TextOff, UiText.ButtonSize);
            label.alignment = TextAlignmentOptions.Center;

            button.onClick.AddListener(() => OnSwitch(entry));
            return go;
        }

        private void OnSwitch(ModCatalog.Entry entry)
        {
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
            if (_confirming == entry)
            {
                return TextConfirmOff;
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

    // Shows a mod's details as soon as its row is selected, by mouse or pad.
    internal sealed class ModRowSelect : MonoBehaviour, ISelectHandler
    {
        internal ModsMenu Menu;
        internal ModCatalog.Entry Entry;

        public void OnSelect(BaseEventData eventData)
        {
            Menu?.Select(Entry);
        }
    }
}
