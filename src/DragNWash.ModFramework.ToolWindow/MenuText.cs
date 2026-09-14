using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // IMGUI can only draw with an OS font, and inside Steam's Linux runtime
    // (Steam Deck) no OS font has CJK glyphs, so Japanese or Chinese text in
    // the menu came out as tofu boxes. While the menu is drawing, every
    // string that reaches IMGUI passes through GUIContent.Temp; characters
    // the menu font cannot draw are swapped for '?' there. The game's own
    // text is unaffected (it goes through TextMeshPro with the file-loaded
    // fallback fonts).
    internal static class MenuText
    {
        private static Font _font;
        private static bool _active;
        private static readonly Dictionary<char, bool> _drawable = new Dictionary<char, bool>();

        public static void Install(Harmony harmony)
        {
            MethodInfo temp = AccessTools.Method(typeof(GUIContent), "Temp", new[] { typeof(string) });
            if (temp != null)
                harmony.Patch(temp, prefix: new HarmonyMethod(typeof(MenuText), nameof(BeforeTemp)));
        }

        public static void Begin(Font menuFont, int size)
        {
            _font = menuFont;
            _size = size;
            _active = menuFont != null;
        }

        public static void End()
        {
            _active = false;
        }

        public static bool CanDraw(Font font, int size, string text)
        {
            _size = size;
            if (font == null || string.IsNullOrEmpty(text)) return true;
            foreach (char c in text)
            {
                if (!Drawable(font, c)) return false;
            }
            return true;
        }

        // Unity's dynamic fonts answer HasCharacter with true and then draw
        // the fallback's "missing glyph" box, so compare the glyph with the
        // one an unassigned code point (U+0378) gets: same box, not drawable.
        private const char Undefined = '͸';
        private static int _size;

        private static bool Drawable(Font font, char c)
        {
            if (c < 128) return true;
            if (_drawable.TryGetValue(c, out bool ok)) return ok;
            try
            {
                if (!font.dynamic)
                {
                    ok = font.HasCharacter(c);
                }
                else
                {
                    font.RequestCharactersInTexture(new string(new[] { c, Undefined }), _size, FontStyle.Normal);
                    ok = font.GetCharacterInfo(c, out CharacterInfo info, _size, FontStyle.Normal)
                         && info.advance > 0
                         && !(font.GetCharacterInfo(Undefined, out CharacterInfo none, _size, FontStyle.Normal)
                              && none.uvBottomLeft == info.uvBottomLeft && none.uvTopRight == info.uvTopRight);
                }
            }
            catch { ok = false; }
            _drawable[c] = ok;
            return ok;
        }

        private static void BeforeTemp(ref string t)
        {
            if (!_active || t == null) return;
            StringBuilder sb = null;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c < 128 || Drawable(_font, c))
                {
                    sb?.Append(c);
                    continue;
                }
                if (sb == null)
                {
                    sb = new StringBuilder(t.Length);
                    sb.Append(t, 0, i);
                }
                // A surrogate pair is one character on screen.
                if (char.IsHighSurrogate(c) && i + 1 < t.Length) i++;
                sb.Append('?');
            }
            if (sb != null) t = sb.ToString();
        }
    }
}
