using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Mods
{
    // TextMeshPro labels styled like the labels in the game's Options screen
    // (white text that reads over the scene behind the menu), so framework UI
    // looks like the game's and translation mods can pick the text up like any
    // other UI text.
    internal static class UiText
    {
        internal const float TitleSize = 72f;
        internal const float ButtonSize = 44f;
        internal const float NameSize = 34f;
        internal const float BodySize = 26f;

        private static TMP_Text _gameLabel;

        internal static TMP_Text Create(Transform parent, string name, string text, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            Style(label);
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * 0.5f;
            label.fontSizeMax = size;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        // Game font, material and colour when a settings label can be found,
        // otherwise white with a dark outline.
        internal static void Style(TMP_Text label)
        {
            TMP_Text game = FindGameLabel();
            if (game != null)
            {
                label.font = game.font;
                label.fontSharedMaterial = game.fontSharedMaterial;
                label.color = game.color;
                label.fontStyle = game.fontStyle;
                return;
            }
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(20, 16, 14, 255);
        }

        // A label from the Options screen's settings rows: those are drawn over
        // the same background as the Mods screen.
        private static TMP_Text FindGameLabel()
        {
            if (_gameLabel != null)
            {
                return _gameLabel;
            }
            foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.font == null || t.gameObject.scene.name == null || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }
                Menu menu = t.GetComponentInParent<Menu>(true);
                if (menu == null || ((Component)menu).gameObject.name != "Menu_Options" || !t.transform.GetPath().Contains("Scroll View/Viewport/Content"))
                {
                    continue;
                }
                Color c = t.color;
                if (c.r + c.g + c.b > 2.4f && c.a > 0.9f)
                {
                    _gameLabel = t;
                    return t;
                }
            }
            return null;
        }

        private static string GetPath(this Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                path = p.name + "/" + path;
            }
            return path;
        }
    }
}
