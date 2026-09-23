using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's keys a player can change: eleven KeyboardShortcut
    // settings in the plugin's [Keys] section. Being settings, the Mods screen
    // shows them with its Change button and its note on shared keys, and the
    // "?" panel changes the same settings in place. The defaults are the keys
    // the tab always had; KeyboardShortcut.Empty is "none", the action has no
    // key. The keys that mean the same in every tool (arrows, Home, End, Page
    // up and down, Ctrl+Z, Ctrl+Up, Esc, ?) and the free camera's flying keys
    // stay fixed.
    internal static class InspectorShortcuts
    {
        internal enum Shortcut { GizmoMove, GizmoRotate, GizmoScale, GizmoOff, Pick, Highlight, Tree, FreeCamera, Bones, Wireframe, EditMesh }

        internal const int Count = 11;

        // Config key, default key, the name on the Mods screen, and what it does.
        private static readonly (string key, KeyCode code, string name, string description)[] Settings =
        {
            ("GizmoMove", KeyCode.W, "Gizmo move", "Turns the move gizmo on or off for the selected object."),
            ("GizmoRotate", KeyCode.E, "Gizmo rotate", "Turns the rotate gizmo on or off."),
            ("GizmoScale", KeyCode.R, "Gizmo scale", "Turns the scale gizmo on or off."),
            ("GizmoOff", KeyCode.Q, "Gizmo off", "Puts the gizmo away."),
            ("Pick", KeyCode.P, "Pick", "Starts pick mode, where a click on an object in the game selects it. Press it again to stop."),
            ("Highlight", KeyCode.H, "Highlight", "Shows or hides the outline around the selection."),
            ("Tree", KeyCode.T, "Tree", "Shows or hides the tree in Scene, or the list in Objects."),
            ("FreeCamera", KeyCode.C, "Free camera", "Turns the free camera on or off."),
            ("Bones", KeyCode.B, "Bones", "Shows or hides the selection's bones."),
            ("Wireframe", KeyCode.N, "Wireframe", "Shows or hides the selection's wireframe."),
            ("EditMesh", KeyCode.M, "Edit mesh", "Turns mesh editing on or off (experimental)."),
        };

        private static readonly ConfigEntry<KeyboardShortcut>[] Entries = new ConfigEntry<KeyboardShortcut>[Count];

        internal static void Bind(ConfigFile config)
        {
            for (int i = 0; i < Count; i++)
            {
                var (key, code, name, description) = Settings[i];
                // One SectionMeta speaks for the whole section.
                object[] tags = i == 0
                    ? new object[] { new SettingMeta { DisplayName = name, Order = i }, new SectionMeta { DisplayName = "Keys", Description = "The Inspector tab's shortcuts in the F1 window. They work while no text field has the keyboard, and the tab's ? panel changes them too. None means the action has no key." } }
                    : new object[] { new SettingMeta { DisplayName = name, Order = i } };
                Entries[i] = config.Bind("Keys", key, new KeyboardShortcut(code), new ConfigDescription(description, null, tags));
                Entries[i].SettingChanged += (sender, args) => _notesDue = 0f;
            }
        }

        internal static KeyboardShortcut Get(Shortcut s)
        {
            ConfigEntry<KeyboardShortcut> entry = Entries[(int)s];
            return entry != null ? entry.Value : KeyboardShortcut.Empty;
        }

        internal static void Set(Shortcut s, KeyboardShortcut value)
        {
            ConfigEntry<KeyboardShortcut> entry = Entries[(int)s];
            if (entry != null)
            {
                entry.Value = value;
            }
        }

        internal static void ResetAll()
        {
            foreach (ConfigEntry<KeyboardShortcut> entry in Entries)
            {
                if (entry != null)
                {
                    entry.Value = (KeyboardShortcut)entry.DefaultValue;
                }
            }
        }

        // ---- names -----------------------------------------------------------------------

        // The name the action has on the Mods screen: "Gizmo move".
        internal static string Title(Shortcut s) => Settings[(int)s].name;

        // "Ctrl+P", or "" when the action has no key.
        internal static string Name(Shortcut s) => Name(Get(s));

        // " (P)" to put after a menu item or a tooltip, or nothing when the
        // action has no key.
        internal static string Suffix(Shortcut s)
        {
            string name = Name(s);
            return name.Length == 0 ? "" : " (" + name + ")";
        }

        internal static string Name(KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None)
            {
                return "";
            }
            bool ctrl = false, alt = false, shift = false, cmd = false;
            var others = new List<string>();
            foreach (KeyCode m in k.Modifiers)
            {
                switch (Held(m))
                {
                    case Mod.Ctrl: ctrl = true; break;
                    case Mod.Alt: alt = true; break;
                    case Mod.Shift: shift = true; break;
                    case Mod.Cmd: cmd = true; break;
                    default: others.Add(KeyName(m)); break;
                }
            }
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (cmd) parts.Add("Cmd");
            parts.AddRange(others);
            parts.Add(KeyName(k.MainKey));
            return string.Join("+", parts);
        }

        // A key as it is printed on the keyboard, where Unity's name differs.
        internal static string KeyName(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + ((int)key - (int)KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.PageUp: return "Page Up";
                case KeyCode.PageDown: return "Page Down";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                default: return key.ToString();
            }
        }

        // ---- matching a key press ------------------------------------------------------

        [Flags]
        internal enum Mod { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Cmd = 8 }

        // Which modifier a key is, or None for any other key.
        internal static Mod Held(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl: case KeyCode.RightControl: return Mod.Ctrl;
                case KeyCode.LeftAlt: case KeyCode.RightAlt: case KeyCode.AltGr: return Mod.Alt;
                case KeyCode.LeftShift: case KeyCode.RightShift: return Mod.Shift;
                case KeyCode.LeftCommand: case KeyCode.RightCommand: case KeyCode.LeftWindows: case KeyCode.RightWindows: return Mod.Cmd;
                default: return Mod.None;
            }
        }

        internal static bool IsModifier(KeyCode key) => Held(key) != Mod.None;

        internal static Mod HeldIn(Event ev)
        {
            return (ev.control ? Mod.Ctrl : 0) | (ev.alt ? Mod.Alt : 0) | (ev.shift ? Mod.Shift : 0) | (ev.command ? Mod.Cmd : 0);
        }

        // A shortcut from a key press in the "?" panel: the key with the
        // modifiers held, as the Mods screen's Change takes it.
        internal static KeyboardShortcut From(Event ev)
        {
            var mods = new List<KeyCode>();
            if (ev.control) mods.Add(KeyCode.LeftControl);
            if (ev.alt) mods.Add(KeyCode.LeftAlt);
            if (ev.shift) mods.Add(KeyCode.LeftShift);
            if (ev.command) mods.Add(KeyCode.LeftCommand);
            return new KeyboardShortcut(ev.keyCode, mods.ToArray());
        }

        // The actions a KeyDown answers. A shortcut answers when the modifiers
        // held are the ones it names. One that names none also answers with
        // Shift or Alt held, as the keys always did, unless another shortcut
        // names exactly what is held (Shift+P then goes to that one alone).
        // Ctrl or Cmd held never reaches a shortcut that does not name it.
        // Two actions on one key both answer: the panel warns, it does not refuse.
        internal static List<Shortcut> Pressed(Event ev)
        {
            var exact = new List<Shortcut>();
            var loose = new List<Shortcut>();
            if (ev.keyCode == KeyCode.None || IsModifier(ev.keyCode))
            {
                return exact;
            }
            Mod held = HeldIn(ev);
            for (int i = 0; i < Count; i++)
            {
                KeyboardShortcut k = Get((Shortcut)i);
                if (k.MainKey != ev.keyCode)
                {
                    continue;
                }
                Mod named = Mod.None;
                bool othersHeld = true;
                foreach (KeyCode m in k.Modifiers)
                {
                    Mod mod = Held(m);
                    if (mod != Mod.None) named |= mod;
                    else othersHeld &= KeyHeld(m);
                }
                if (!othersHeld)
                {
                    continue;
                }
                if (named == held) exact.Add((Shortcut)i);
                else if (named == Mod.None && (held & (Mod.Ctrl | Mod.Cmd)) == 0) loose.Add((Shortcut)i);
            }
            return exact.Count > 0 ? exact : loose;
        }

        // A key other than a modifier that a shortcut names beside its main
        // key (a config file can say "P + Space"); IMGUI's event has no flag
        // for it, so the keyboard is asked. The "?" panel asks it too, for
        // when the key that ended a wait is let go.
        internal static bool KeyHeld(KeyCode key)
        {
            try
            {
                return UnityInput.Current.GetKey(key);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- shared keys -----------------------------------------------------------------

        // The Mods screen's note for each action whose key another setting
        // has too (any loaded plugin, this one included), or null. Asking every
        // plugin's config is not free, so the notes are kept and asked again
        // after a change here, and once a second while the panel shows them,
        // for a change made on the Mods screen or by another mod.
        private static readonly string[] Notes = new string[Count];
        private static float _notesDue;

        internal static string Note(Shortcut s)
        {
            if (Time.unscaledTime >= _notesDue)
            {
                _notesDue = Time.unscaledTime + 1f;
                for (int i = 0; i < Count; i++)
                {
                    Notes[i] = Entries[i] != null ? ModFramework.SharedKeyNote(Entries[i]) : null;
                }
            }
            return Notes[(int)s];
        }
    }
}
