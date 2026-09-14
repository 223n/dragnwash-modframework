using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The Mods screen. A game Menu, so MenuManager shows and hides it, and the
    // game handles the cursor and pad input as on any other screen.
    //
    // One row per mod: name, version and authors, the description, and a status
    // line on the left, an On/Off button on the right. Each piece is its own
    // label so fixed words such as "Required" can be translated. Switching takes
    // effect at the next launch.
    internal sealed class ModsMenu : Menu
    {
        internal RectTransform Content;

        // The Back button's pointing hand is drawn just right of the button,
        // over the start of the list; keep the text clear of it.
        private const float LeftMargin = 110f;
        private const float RowHeight = 170f;

        private List<ModCatalog.Entry> _entries = new List<ModCatalog.Entry>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private ModCatalog.Entry _confirming;

        protected override void OnShow(MenuResponseTransition response)
        {
            base.OnShow(response);
            _confirming = null;
            try
            {
                _entries = ModCatalog.Build();
                Rebuild();
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

        private void Rebuild()
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
                _rows.Add(CreateRow(entry));
            }
        }

        private GameObject CreateRow(ModCatalog.Entry entry)
        {
            var row = new GameObject("Mod " + entry.Name, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;
            layout.flexibleWidth = 1f;

            // A dark band behind each row keeps the text readable over the scene.
            var band = new GameObject("Band", typeof(RectTransform));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(row.transform, false);
            bandRect.anchorMin = Vector2.zero;
            bandRect.anchorMax = Vector2.one;
            bandRect.offsetMin = new Vector2(LeftMargin - 20f, 8f);
            bandRect.offsetMax = new Vector2(0f, -8f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = new Color(0f, 0f, 0f, 0.45f);
            bandImage.raycastTarget = false;

            float textRight = entry.CanSwitch ? 0.72f : 1f;
            AddLine(row.transform, "Name", NameLine(entry), UiText.NameSize, 0.62f, 0.95f, textRight);

            string description = entry.Info?.Description;
            if (!string.IsNullOrEmpty(description))
            {
                AddLine(row.transform, "Description", description, UiText.BodySize, 0.33f, 0.62f, textRight);
            }

            string status = Status(entry);
            if (status != null)
            {
                TMP_Text s = AddLine(row.transform, "Status", status, UiText.BodySize, 0.06f, 0.33f, textRight);
                s.fontStyle |= FontStyles.Italic;
            }

            if (entry.CanSwitch)
            {
                CreateSwitch(row.transform, entry);
            }
            return row;
        }

        private static TMP_Text AddLine(Transform row, string name, string text, float size, float bottom, float top, float right)
        {
            TMP_Text label = UiText.Create(row, name, text, size);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = new Vector2(LeftMargin, 0f);
            rect.offsetMax = new Vector2(-12f, 0f);
            return label;
        }

        private void CreateSwitch(Transform row, ModCatalog.Entry entry)
        {
            var go = new GameObject("Switch", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(row, false);
            rect.anchorMin = new Vector2(0.75f, 0.22f);
            rect.anchorMax = new Vector2(0.98f, 0.78f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = go.AddComponent<Image>();
            background.color = entry.WantOn ? new Color(0.36f, 0.62f, 0.36f, 1f) : new Color(0.62f, 0.3f, 0.27f, 1f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            TMP_Text label = UiText.Create(go.transform, "Label", entry.WantOn ? "On" : "Off", UiText.ButtonSize);
            label.alignment = TextAlignmentOptions.Center;

            button.onClick.AddListener(() => OnSwitch(entry));
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
                        Rebuild();
                        return;
                    }
                }
                _confirming = null;
                ModCatalog.SetWantOn(_entries, entry, !entry.WantOn);
                Rebuild();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not switch {entry.Name}: {ex}");
            }
        }

        private static string NameLine(ModCatalog.Entry entry)
        {
            string line = Escape(entry.DisplayName);
            if (!string.IsNullOrEmpty(entry.Version))
            {
                line += "  <size=75%>v" + Escape(entry.Version) + "</size>";
            }
            string[] authors = entry.Info?.Authors;
            if (authors != null && authors.Length > 0)
            {
                line += "  <size=75%>" + Escape(string.Join(", ", authors)) + "</size>";
            }
            return line;
        }

        private string Status(ModCatalog.Entry entry)
        {
            if (entry.IsFramework)
            {
                return "Required";
            }
            if (_confirming == entry)
            {
                string names = string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g)));
                return $"Needed by {names}. Press Off again to switch it off anyway.";
            }
            if (entry.Loaded && !entry.WantOn)
            {
                return "Off from the next launch";
            }
            if (!entry.Loaded && entry.WantOn)
            {
                return "On from the next launch";
            }
            if (entry.RelativePath == null)
            {
                return "Installed outside BepInEx/plugins, so it cannot be switched off here";
            }
            return null;
        }

        // Mod names and descriptions are plain text, not rich text.
        private static string Escape(string text)
        {
            return (text ?? "").Replace("<", "<noparse><</noparse>");
        }
    }
}
