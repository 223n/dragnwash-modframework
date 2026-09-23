using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The Mods screen's look: the Tool window's palette (P4) and a few shapes
    // with rounded corners, made in code from the plugin's Awake. There are no
    // image files: each shape is a small white texture drawn pixel by pixel,
    // cut as a 9-sliced sprite so one texture serves every size, and tinted by
    // the Image's colour.
    //
    // The colours are copied from ToolWindow rather than read from it: the
    // core cannot depend on a library.
    internal static class ModsLook
    {
        internal static readonly Color Panel = new Color(0.09f, 0.11f, 0.15f);
        internal static readonly Color Inset = new Color(0.055f, 0.07f, 0.10f);
        internal static readonly Color Accent = new Color(0.32f, 0.78f, 0.72f);
        internal static readonly Color Muted = new Color(0.60f, 0.66f, 0.73f);
        internal static readonly Color Error = new Color(0.96f, 0.45f, 0.40f);
        internal static readonly Color Warning = new Color(0.93f, 0.75f, 0.30f);
        internal static readonly Color Label = new Color(0.91f, 0.94f, 0.97f);
        internal static readonly Color Border = new Color(0.17f, 0.20f, 0.27f);

        // A button's face, and a row or tab under the pointer or the pad.
        internal static readonly Color Raised = new Color(0.165f, 0.196f, 0.26f);
        internal static readonly Color Hover = new Color(0.12f, 0.145f, 0.195f);

        // A switch that is off: its track and its knob.
        internal static readonly Color TrackOff = new Color(0.23f, 0.26f, 0.32f);
        internal static readonly Color KnobOff = new Color(0.77f, 0.80f, 0.85f);

        internal static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        // A scroll view's scrollSensitivity. The game's input module gives 6
        // for one notch of the wheel, so a notch moves 84, about one row of
        // the list. (The game's own scroll view had 1: a notch moved 6.)
        internal const float WheelStep = 14f;

        // How wide a scrollbar is: thin, like the Tool window's.
        private const float ScrollbarWidth = 8f;

        // Filled, with 12-unit corners.
        internal static Sprite Rounded;

        // A 3-unit line around the same shape, for the gamepad's frame.
        internal static Sprite Outline;

        // Filled, with 16-unit corners: a pill at any width, a circle when square.
        internal static Sprite Pill;

        // A 2-unit line around a pill, for tags.
        internal static Sprite PillOutline;

        // A small triangle pointing right, for a group that folds.
        internal static Sprite Triangle;

        // A magnifying glass, for the search field.
        internal static Sprite Magnifier;

        // An arrow going round, for "back to the default".
        internal static Sprite ResetArrow;

        // Must run from the plugin's Awake: a texture made later can crash
        // Direct3D 12. Anything that fails leaves its sprite null, and the
        // screen then draws square corners (or no icon) instead.
        internal static void MakeSprites()
        {
            try
            {
                Rounded = Sliced("ModsRounded", 12, 0f);
                Outline = Sliced("ModsOutline", 12, 3f);
                Pill = Sliced("ModsPill", 16, 0f);
                PillOutline = Sliced("ModsPillOutline", 16, 2f);
                Triangle = Icon("ModsTriangle", 16, (x, y) =>
                    // Pointing right: the left edge, and two slopes meeting at the right.
                    x >= 3f && x <= 13f && Math.Abs(y - 8f) <= (13f - x) * 0.55f);
                Magnifier = Icon("ModsMagnifier", 32, (x, y) =>
                {
                    float d = Distance(x, y, 13f, 19f);
                    bool ring = d >= 6f && d <= 9.5f;
                    bool handle = DistanceToSegment(x, y, 19.5f, 12.5f, 27f, 5f) <= 2f;
                    return ring || handle;
                });
                ResetArrow = Icon("ModsResetArrow", 32, (x, y) =>
                {
                    // Most of a ring, open at the top right, with an arrowhead
                    // at the open end pointing round the ring.
                    float d = Distance(x, y, 16f, 16f);
                    double angle = Math.Atan2(y - 16f, x - 16f) * 180.0 / Math.PI;
                    bool ring = d >= 8f && d <= 11f && !(angle > 10.0 && angle < 80.0);
                    bool head = InTriangle(x, y, 21.9f, 17f, 28.8f, 18.3f, 24.4f, 23.5f);
                    return ring || head;
                });
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"The Mods screen's shapes could not be made; it draws square corners instead: {ex.Message}");
            }
        }

        // A rounded rectangle of the given corner radius; a line of that
        // width around its edge, or filled when `line` is 0. The texture is
        // just big enough for the corners, and the sprite's borders are the
        // corners, so it stretches to any size without blurring them.
        private static Sprite Sliced(string name, int radius, float line)
        {
            int size = radius * 2 + 2;
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance from the pixel's centre to the edge, negative inside.
                    float qx = Math.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Math.Abs(y + 0.5f - half) - (half - radius);
                    float outside = (float)Math.Sqrt(Math.Max(qx, 0f) * Math.Max(qx, 0f) + Math.Max(qy, 0f) * Math.Max(qy, 0f));
                    float dist = outside + Math.Min(Math.Max(qx, qy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - dist);
                    if (line > 0f)
                    {
                        alpha *= Mathf.Clamp01(dist + line + 0.5f);
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            Texture2D texture = Texture(name, size, pixels);
            var border = new Vector4(radius, radius, radius, radius);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // A small picture from a test of whether a point is inside it, with
        // 4x4 samples a pixel for smooth edges. y goes up, as in the texture.
        private static Sprite Icon(string name, int size, Func<float, float, bool> inside)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            if (inside(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f))
                            {
                                hits++;
                            }
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
            }
            Texture2D texture = Texture(name, size, pixels);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static Texture2D Texture(string name, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float Distance(float x, float y, float cx, float cy)
        {
            return (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        }

        private static float DistanceToSegment(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            return Distance(x, y, ax + t * dx, ay + t * dy);
        }

        private static bool InTriangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
            float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
            float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        // ---- pieces ----

        internal static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        // An Image in a shape (or square when the shapes could not be made).
        // `radius` is the corners' radius in canvas units: for a pill, half
        // its height, so it stays round when taller than the texture's 32.
        internal static Image Shape(GameObject go, Sprite sprite, Color color, float radius = 12f)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                if (sprite == Rounded || sprite == Outline)
                {
                    image.pixelsPerUnitMultiplier = 12f / Mathf.Max(1f, radius);
                }
                else if (sprite == Pill || sprite == PillOutline)
                {
                    image.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, radius);
                }
                image.fillCenter = sprite != Outline && sprite != PillOutline;
            }
            image.color = color;
            return image;
        }

        // A label for a layout: fixed size (the game's labels shrink to fit,
        // which made rows of mixed sizes), in the given colour and style.
        internal static TMP_Text Text(Transform parent, string name, string text, float size, Color color, FontStyles style, bool wrap)
        {
            TMP_Text label = UiText.Create(parent, name, text, size);
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            label.alignment = wrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            return label;
        }

        // The width a one-line label needs for its text.
        internal static float Width(TMP_Text label)
        {
            return Mathf.Ceil(label.GetPreferredValues(label.text).x) + 2f;
        }

        internal static LayoutElement Size(GameObject go, float width = -1f, float height = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }
            if (width >= 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
            }
            if (height >= 0f)
            {
                layout.minHeight = height;
                layout.preferredHeight = height;
            }
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
            return layout;
        }

        // Button colours given as they are, not as tints: the button's Image
        // is white, so each state shows exactly the colour named here.
        internal static void Colors(Selectable selectable, Color normal, Color hover, Color pressed)
        {
            ColorBlock colors = selectable.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = hover;
            colors.pressedColor = pressed;
            colors.disabledColor = normal;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
        }

        // A switch's picture: a track with a knob at the right (on) or the
        // left (off). Only a picture; whoever holds it decides what pressing does.
        internal static RectTransform Switch(Transform parent, string name, bool on, float width, float height)
        {
            RectTransform track = Rect(parent, name);
            track.sizeDelta = new Vector2(width, height);
            Shape(track.gameObject, Pill, on ? Accent : TrackOff, height / 2f).raycastTarget = false;
            RectTransform knob = Rect(track, "Knob");
            float side = height - 6f;
            knob.anchorMin = knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
            knob.pivot = new Vector2(on ? 1f : 0f, 0.5f);
            knob.sizeDelta = new Vector2(side, side);
            knob.anchoredPosition = new Vector2(on ? -3f : 3f, 0f);
            Shape(knob.gameObject, Pill, on ? Inset : KnobOff, side / 2f).raycastTarget = false;
            return track;
        }

        // A scrollbar in the palette: no track, a thin rounded thumb in Muted
        // that turns Accent under the pointer or while dragged, a little in
        // from the panel's edge. The pad never lands on it (the selection
        // scrolls into view by itself), so it is left out of navigation.
        internal static void StyleScrollbar(Scrollbar bar)
        {
            if (bar == null)
            {
                return;
            }
            var rect = (RectTransform)bar.transform;
            rect.sizeDelta = new Vector2(ScrollbarWidth, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(-6f, rect.anchoredPosition.y);
            Image track = bar.GetComponent<Image>();
            if (track != null)
            {
                track.sprite = null;
                track.color = Clear;
            }
            if (bar.handleRect != null)
            {
                var area = bar.handleRect.parent as RectTransform;
                if (area != null && area != rect)
                {
                    area.offsetMin = new Vector2(0f, 4f);
                    area.offsetMax = new Vector2(0f, -4f);
                }
                bar.handleRect.sizeDelta = Vector2.zero;
                Image thumb = Shape(bar.handleRect.gameObject, Pill, Color.white, ScrollbarWidth / 2f);
                bar.targetGraphic = thumb;
            }
            Colors(bar, Muted, Accent, Accent);
            // A click leaves it selected; it should not stay lit after.
            ColorBlock colors = bar.colors;
            colors.selectedColor = Muted;
            bar.colors = colors;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        // A tag: a word in a thin pill of its colour, as wide as the word.
        // Returns its width.
        internal static float Tag(Transform parent, string name, string text, Color color, float size, float height)
        {
            RectTransform tag = Rect(parent, name);
            Shape(tag.gameObject, PillOutline, color, height / 2f).raycastTarget = false;
            TMP_Text label = Text(tag, "Label", text, size, color, FontStyles.Bold, false);
            label.alignment = TextAlignmentOptions.Center;
            float width = Width(label) + 22f;
            tag.sizeDelta = new Vector2(width, height);
            Size(tag.gameObject, width, height, 0f, 0f);
            return width;
        }
    }
}
