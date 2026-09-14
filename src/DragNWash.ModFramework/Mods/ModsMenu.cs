using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The Mods screen. A game Menu, so MenuManager shows and hides it, and the
    // game handles the cursor and pad input as on any other screen.
    //
    // One row per mod: name, version, authors and description on the left, an
    // On/Off button on the right. Switching takes effect at the next launch.
    internal sealed class ModsMenu : Menu
    {
        internal RectTransform Content;

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
            layout.minHeight = 150f;
            layout.preferredHeight = 150f;
            layout.flexibleWidth = 1f;

            TMP_Text info = UiText.Create(row.transform, "Info", Describe(entry), UiText.BodySize);
            info.alignment = TextAlignmentOptions.MidlineLeft;
            info.enableAutoSizing = false;
            info.fontSize = UiText.BodySize;
            info.textWrappingMode = TextWrappingModes.Normal;
            info.overflowMode = TextOverflowModes.Ellipsis;
            var infoRect = (RectTransform)info.transform;
            infoRect.anchorMax = new Vector2(entry.CanSwitch ? 0.74f : 1f, 1f);

            if (entry.CanSwitch)
            {
                CreateSwitch(row.transform, entry);
            }
            return row;
        }

        private void CreateSwitch(Transform row, ModCatalog.Entry entry)
        {
            var go = new GameObject("Switch", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(row, false);
            rect.anchorMin = new Vector2(0.77f, 0.2f);
            rect.anchorMax = new Vector2(1f, 0.8f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = go.AddComponent<Image>();
            background.color = entry.WantOn ? new Color(0.45f, 0.75f, 0.45f, 0.55f) : new Color(0.8f, 0.45f, 0.4f, 0.55f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.selectedColor = new Color(1f, 1f, 1f, 1f);
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            button.colors = colors;

            TMP_Text label = UiText.Create(go.transform, "Label", entry.WantOn ? "On" : "Off", UiText.ButtonSize);
            label.alignment = TextAlignmentOptions.Center;

            button.onClick.AddListener(() => OnSwitch(entry));
        }

        private void OnSwitch(ModCatalog.Entry entry)
        {
            try
            {
                bool turningOff = entry.WantOn;
                if (turningOff)
                {
                    List<string> needed = entry.Dependents
                        .Where(g => _entries.Any(x => x.Guid == g && x.WantOn))
                        .ToList();
                    if (needed.Count > 0 && _confirming != entry)
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

        private string Describe(ModCatalog.Entry entry)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Escape(entry.Name)).Append("</b>");
            if (!string.IsNullOrEmpty(entry.Version))
            {
                sb.Append("  <size=80%>v").Append(Escape(entry.Version)).Append("</size>");
            }

            ModInfo info = entry.Info;
            if (info?.Authors != null && info.Authors.Length > 0)
            {
                sb.Append("  <size=80%>").Append(Escape(string.Join(", ", info.Authors))).Append("</size>");
            }
            if (!string.IsNullOrEmpty(info?.Description))
            {
                sb.Append("\n<size=80%>").Append(Escape(info.Description)).Append("</size>");
            }

            string status = Status(entry);
            if (status != null)
            {
                sb.Append("\n<size=75%><i>").Append(Escape(status)).Append("</i></size>");
            }
            return sb.ToString();
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
            if (!entry.Loaded)
            {
                return "Off";
            }
            if (entry.RelativePath == null)
            {
                return "Installed outside BepInEx/plugins; cannot be switched off here";
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
