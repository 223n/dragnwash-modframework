using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Mods
{
    // TextMeshPro labels in the game's own font, so framework UI looks like the
    // game's and translation mods can pick the text up like any other UI text.
    internal static class UiText
    {
        internal const float TitleSize = 64f;
        internal const float ButtonSize = 34f;
        internal const float BodySize = 28f;

        private static TMP_FontAsset _font;
        private static Color _color = new Color(0.16f, 0.12f, 0.1f, 1f);
        private static bool _looked;

        internal static TMP_Text Create(Transform parent, string name, string text, float size)
        {
            FindGameStyle();
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }
            label.color = _color;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * 0.5f;
            label.fontSizeMax = size;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        // Borrow the font and colour of a label the game already shows.
        private static void FindGameStyle()
        {
            if (_looked && _font != null)
            {
                return;
            }
            _looked = true;
            foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.font == null || t.gameObject.scene.name == null)
                {
                    continue;
                }
                if (t.name == "Label" && t.transform.GetComponentInParent<Menu>(true) != null)
                {
                    _font = t.font;
                    _color = t.color;
                    return;
                }
                if (_font == null)
                {
                    _font = t.font;
                }
            }
            if (_font == null)
            {
                _font = TMP_Settings.defaultFontAsset;
            }
        }
    }
}
