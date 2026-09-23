using DragNWash.ModFramework.Mods;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// The Mods screen's text colours and two of its pieces, for a
    /// <see cref="ModsScreenPage"/> that should look like the rest of the
    /// screen. The panel a page sits on is see-through (frosted glass over the
    /// game, or a dark tint), so text in fixed colours can get hard to read
    /// over a bright picture; these keep 4.5:1 or more over the brightest one.
    /// They follow the look in use, and the screen builds an open page again
    /// when the look changes, so read them while building. Since 1.5.0.
    /// </summary>
    public static class ModsScreenLook
    {
        /// <summary>Normal text. Since 1.5.0.</summary>
        public static Color Text => ModsLook.Label;

        /// <summary>Small or secondary text: labels, hints, notes. Since 1.5.0.</summary>
        public static Color Muted => ModsLook.Muted;

        /// <summary>The accent colour as text: something that is on or going well. Since 1.5.0.</summary>
        public static Color Accent => ModsLook.AccentText;

        /// <summary>Something that failed or is wrong, as text. Since 1.5.0.</summary>
        public static Color Error => ModsLook.ErrorText;

        /// <summary>Something that needs a look, as text. Since 1.5.0.</summary>
        public static Color Warning => ModsLook.Warning;

        /// <summary>
        /// A card like a setting's row on the Settings tab: a darker
        /// see-through fill with rounded corners, which lays its children out
        /// in a column with the rows' inner padding. Put it in a layout group
        /// (it takes the width it is given and the height its children need),
        /// and add the card's contents to it. Since 1.5.0.
        /// </summary>
        public static RectTransform Card(RectTransform parent)
        {
            return ModsMenu.RowFrame(parent, "Card");
        }

        /// <summary>
        /// A button like the screen's own: a raised rounded face, lighter under
        /// the pointer or the gamepad, and a bold label, as wide as the label
        /// and 44 high. It has a LayoutElement of that size, so it keeps it in
        /// a layout group. The label is the button's TMP_Text child. Since 1.5.0.
        /// </summary>
        public static Button Button(RectTransform parent, string text, UnityAction onClick)
        {
            GameObject button = ModsMenu.FlatButton(parent, "Button", text, 19f, ModsLook.Raised, ModsLook.Label, onClick);
            Vector2 size = ((RectTransform)button.transform).sizeDelta;
            ModsLook.Size(button, size.x, size.y, 0f, 0f);
            return button.GetComponent<Button>();
        }
    }
}
