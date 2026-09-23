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

        // Characters asked about inside OnGUI, checked on the next Update.
        private static readonly HashSet<char> _unchecked = new HashSet<char>();

        private static bool Drawable(Font font, char c)
        {
            if (c < 128) return true;
            if (_drawable.TryGetValue(c, out bool ok)) return ok;
            if (font.dynamic && Event.current != null)
            {
                // Checking rasterises into the font, and doing that while the
                // window draws uploads its texture mid-frame (UUM-140564 on
                // Direct3D 12): '?' this once, as the console does.
                _unchecked.Add(c);
                return false;
            }
            Check(font, new[] { c });
            return _drawable[c];
        }

        // From the plugin's Update: what OnGUI could not check, all at once.
        internal static void CheckQueued()
        {
            if (_unchecked.Count == 0) return;
            var chars = new char[_unchecked.Count];
            _unchecked.CopyTo(chars);
            _unchecked.Clear();
            if (MenuFont.Font != null) Check(MenuFont.Font, chars);
        }

        // One rasterisation for all of them, so a tab full of new text
        // uploads the font's texture once, not once per character.
        private static void Check(Font font, char[] chars)
        {
            try
            {
                if (!font.dynamic)
                {
                    foreach (char c in chars) _drawable[c] = font.HasCharacter(c);
                    return;
                }
                font.RequestCharactersInTexture(new string(chars) + Undefined, _size, FontStyle.Normal);
                bool boxed = font.GetCharacterInfo(Undefined, out CharacterInfo none, _size, FontStyle.Normal);
                foreach (char c in chars)
                {
                    _drawable[c] = font.GetCharacterInfo(c, out CharacterInfo info, _size, FontStyle.Normal)
                                   && info.advance > 0
                                   && !(boxed && none.uvBottomLeft == info.uvBottomLeft && none.uvTopRight == info.uvTopRight);
                }
            }
            catch
            {
                foreach (char c in chars) _drawable[c] = false;
            }
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
