using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Shortcut = DragNWash.ModFramework.Inspector.InspectorShortcuts.Shortcut;

namespace DragNWash.ModFramework.Inspector
{
    // The "?" panel: the tab's keyboard shortcuts, and where the ones that can
    // change are changed. A key that can change is a small bordered chip; a
    // click on it waits for the next key pressed (with Ctrl, Shift or Alt
    // held, as on the Mods screen), Esc stops waiting and Backspace leaves the
    // action with no key. The keys that stay fixed are plain text above and
    // below. A key that another setting has too (in any mod, this one
    // included) gets a warning bar on its chip and the Mods screen's note
    // under its row. The keys are settings (InspectorShortcuts), so a change
    // here is a change on the Mods screen too.
    internal static partial class InspectorTab
    {
        private static bool _showKeys;
        private static Rect _keysBoxShown;
        private static GUIStyle _keyCell, _chipCell, _chipNoneCell, _chipCaptureCell, _keyWarningCell, _keyWarningWrapCell, _linkCell, _linkHoverCell;

        // The chip's edge (the panel's line colour) and the row of the chip
        // that waits for a key, a shade over the panel.
        private static readonly Color KeyEdgeColor = new Color(0.165f, 0.2f, 0.26f);
        private static readonly Color KeyRowHotColor = new Color(0.114f, 0.141f, 0.192f);

        private static readonly string[][] FixedKeysAbove =
        {
            new[] { "Arrows", "move in the list" },
            new[] { "Left / Right", "close / open a node" },
        };
        private static readonly string[][] FixedKeysBelow =
        {
            new[] { "Ctrl+Z", "undo the last edit" },
            new[] { "Ctrl+Up", "select the parent" },
            new[] { "Esc", "leave pick mode / close a menu" },
        };
        private static readonly (Shortcut[] keys, string what)[] ChangeableKeys =
        {
            (new[] { Shortcut.GizmoMove, Shortcut.GizmoRotate, Shortcut.GizmoScale }, "move / rotate / scale gizmo"),
            (new[] { Shortcut.GizmoOff }, "gizmo off"),
            (new[] { Shortcut.Pick }, "pick an object in the game"),
            (new[] { Shortcut.Highlight }, "highlight"),
            (new[] { Shortcut.Tree }, "tree on / off"),
            (new[] { Shortcut.FreeCamera }, "free camera"),
            (new[] { Shortcut.Bones }, "bones"),
            (new[] { Shortcut.Wireframe }, "wireframe"),
            (new[] { Shortcut.EditMesh }, "edit mesh (experimental)"),
        };

        // The action whose chip waits for a key, and the last frame the panel
        // showed it: a tab that is no longer drawn (another tab, the window
        // closed) stops the waiting.
        private static Shortcut? _capturing;
        private static int _captureShownFrame;
        // The key that ended the wait, until it is let go: a key held a moment
        // too long repeats, and the repeat is no press of the new shortcut.
        private static KeyCode _takenKey;
        // The action a key was just given to, what it had before, and the
        // frame: a key that makes "?" only says so in the character half of
        // its press, which comes after the key, and then gets its old key back.
        private static Shortcut? _takenFor;
        private static KeyboardShortcut _takenBefore;
        private static int _takenFrame;
        // How far the panel is scrolled, where the tab is too short for it.
        private static float _keysScroll;

        private static void EnsureKeyStyles(ToolWindowStyles s)
        {
            if (_chipCell != null)
            {
                return;
            }
            _chipCell = new GUIStyle(_keyCell) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(2, 2, 0, 0) };
            _chipNoneCell = new GUIStyle(_mutedCell) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(2, 2, 0, 0) };
            _chipCaptureCell = new GUIStyle(_chipCell);
            _chipCaptureCell.normal.textColor = TW.AccentColor;
            _chipCaptureCell.hover.textColor = TW.AccentColor;
            _keyWarningCell = new GUIStyle(_cell);
            _keyWarningCell.normal.textColor = TW.WarningColor;
            _keyWarningCell.hover.textColor = TW.WarningColor;
            _keyWarningWrapCell = new GUIStyle(_keyWarningCell) { wordWrap = true };
            _linkCell = new GUIStyle(_mutedCell);
            _linkHoverCell = new GUIStyle(_mutedCell);
            _linkHoverCell.normal.textColor = s.Label.normal.textColor;
            _linkHoverCell.hover.textColor = s.Label.normal.textColor;
        }

        // ---- waiting for a key ------------------------------------------------------------

        private static void StartCapture(Shortcut shortcut)
        {
            _capturing = shortcut;
            _captureShownFrame = Time.frameCount;
            // The next key is the chip's, not a field's.
            GUIUtility.keyboardControl = 0;
        }

        // Also when the window closes: a key let go while it is closed is
        // never seen going up.
        internal static void EndCapture()
        {
            _capturing = null;
            _takenKey = KeyCode.None;
            _takenFor = null;
        }

        // From Draw, before anything else looks at the keys: ends a wait the
        // panel no longer shows, and gives the chip every key press while it
        // waits, so no other shortcut runs meanwhile.
        private static void CaptureKeys(Event ev)
        {
            if (_capturing != null && (!_showKeys || _captureShownFrame < Time.frameCount - 2))
            {
                EndCapture();
            }
            if (_capturing == null)
            {
                if (_takenFor != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.None && ev.character == '?' && _takenFrame == Time.frameCount)
                {
                    // "?" on a keyboard where it is not Shift+/: that key stays
                    // the panel's, and the chip waits again.
                    InspectorShortcuts.Set(_takenFor.Value, _takenBefore);
                    Shortcut again = _takenFor.Value;
                    EndCapture();
                    StartCapture(again);
                    TW.ShowNotice("? is a fixed key, so it can't be used here.", NoticeKind.Warning, 4f);
                    ev.Use();
                    return;
                }
                if (_takenKey != KeyCode.None)
                {
                    // Let go: the KeyUp can go to another tab or be lost, so
                    // the keyboard is asked once a frame as well.
                    if (ev.type == EventType.Repaint && !InspectorShortcuts.KeyHeld(_takenKey)) _takenKey = KeyCode.None;
                    else if (ev.keyCode != KeyCode.None && !InspectorShortcuts.IsModifier(ev.keyCode))
                    {
                        if (ev.keyCode != _takenKey || ev.type == EventType.KeyUp) _takenKey = KeyCode.None;
                        else if (ev.type == EventType.KeyDown) ev.Use();
                    }
                }
                return;
            }
            if (ev.type != EventType.KeyDown)
            {
                return;
            }
            Shortcut shortcut = _capturing.Value;
            KeyCode key = ev.keyCode;
            ev.Use();
            // The character half of a key press, or a modifier held for the key to come.
            if (key == KeyCode.None || InspectorShortcuts.IsModifier(key))
            {
                return;
            }
            if (key == KeyCode.Escape)
            {
                EndCapture();
                _takenKey = key;
                return;
            }
            if (key == KeyCode.Backspace)
            {
                InspectorShortcuts.Set(shortcut, KeyboardShortcut.Empty);
                EndCapture();
                _takenKey = key;
                return;
            }
            string fixedKey = FixedKeyName(key, ev);
            if (fixedKey != null)
            {
                TW.ShowNotice(fixedKey + " is a fixed key, so it can't be used here.", NoticeKind.Warning, 4f);
                return;
            }
            KeyboardShortcut before = InspectorShortcuts.Get(shortcut);
            InspectorShortcuts.Set(shortcut, InspectorShortcuts.From(ev));
            EndCapture();
            _takenKey = key;
            _takenFor = shortcut;
            _takenBefore = before;
            _takenFrame = Time.frameCount;
        }

        // The keys that mean the same in every tool, by name, or null. Shift+/
        // is "?" on US and Japanese keyboards; elsewhere the character says it.
        private static string FixedKeyName(KeyCode key, Event ev)
        {
            bool ctrl = ev.control || ev.command;
            if (IsFixedKey(key, ctrl))
            {
                return key == KeyCode.Z ? "Ctrl+Z" : InspectorShortcuts.KeyName(key);
            }
            if (key == KeyCode.Question || ev.character == '?' || (key == KeyCode.Slash && ev.shift))
            {
                return "?";
            }
            return null;
        }

        // Arrows, Home, End, Page up and down, Esc and Ctrl+Z: handled before
        // the shortcuts, whatever a setting says.
        private static bool IsFixedKey(KeyCode key, bool ctrl)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                case KeyCode.Escape:
                    return true;
                case KeyCode.Z:
                    return ctrl;
                default:
                    return false;
            }
        }

        // What each changeable key does.
        private static void RunShortcut(Shortcut shortcut, Rect area)
        {
            switch (shortcut)
            {
                case Shortcut.GizmoMove: ToggleGizmo(InspectorGizmo.GizmoMode.Move); break;
                case Shortcut.GizmoRotate: ToggleGizmo(InspectorGizmo.GizmoMode.Rotate); break;
                case Shortcut.GizmoScale: ToggleGizmo(InspectorGizmo.GizmoMode.Scale); break;
                case Shortcut.GizmoOff: InspectorGizmo.Mode = InspectorGizmo.GizmoMode.None; break;
                case Shortcut.Pick: if (InspectorPick.Picking) InspectorPick.End(); else InspectorPick.Begin(); break;
                case Shortcut.Highlight: InspectorPick.Highlight = !InspectorPick.Highlight; break;
                case Shortcut.Tree:
                    bool shown = _objectsMode ? (_showObjectList = !_showObjectList) : (_showHierarchy = !_showHierarchy);
                    if (narrowWindow(area)) _page = shown ? 0 : 1;
                    break;
                case Shortcut.FreeCamera: InspectorFreeCamera.Toggle(); break;
                case Shortcut.Bones: InspectorBones.Show = !InspectorBones.Show; break;
                case Shortcut.Wireframe: InspectorMesh.Wireframe = !InspectorMesh.Wireframe; break;
                case Shortcut.EditMesh: if (InspectorMesh.Editing) InspectorMesh.StopEditing(); else InspectorMesh.Editing = true; break;
            }
        }

        // ---- the panel -------------------------------------------------------------------

        // In a panel on the right under the toolbar, until ? or Esc. Lies over
        // the panes like a menu: its input is taken before they are drawn, and
        // it is painted after them. Both passes lay it out the same way. The
        // chips and Reset all keys are buttons, so a pad or the Steam Deck's
        // trackpad presses them as it presses the menus' items.
        private static void DrawKeys(Rect area, ToolWindowStyles s)
        {
            if (!_showKeys)
            {
                return;
            }
            EnsureKeyStyles(s);
            Event ev = Event.current;
            bool paint = ev.type == EventType.Repaint;
            bool clickToCheck = ev.type == EventType.MouseDown && _capturing != null;

            // The heading, the keys, their warnings and the closing line. The
            // lines draw closer together where the tab is short, so the last
            // keys are not cut off; a warning wraps like the Mods screen's note
            // where there is room, and is one cut line (whole on hover) where
            // there is not. Shorter still, the panel scrolls with the wheel.
            const float separator = 9f;
            const float edges = 10 + 4 + 6 + 8 + 2 * separator;
            int lines = 1 + FixedKeysAbove.Length + ChangeableKeys.Length + FixedKeysBelow.Length + 1;
            float width = Mathf.Min(420f, area.width - 8);
            float w = width - 24;
            float top = _toolbarRect.yMax + 2;
            float room = area.yMax - top - 4;
            int warnings = 0;
            float wrapped = 0f;
            foreach (var row in ChangeableKeys)
            {
                foreach (Shortcut k in row.keys)
                {
                    string note = InspectorShortcuts.Note(k);
                    if (note == null) continue;
                    warnings++;
                    wrapped += WarningHeight(note, w);
                }
            }
            bool wrap = edges + lines * 18f + wrapped <= room;
            float lineH = wrap
                ? Mathf.Clamp(Mathf.Floor((room - edges - wrapped) / lines), 18f, 22f)
                : Mathf.Clamp(Mathf.Floor((room - edges) / (lines + warnings)), 18f, 22f);
            float contentHeight = edges + lines * lineH + (wrap ? wrapped : warnings * lineH);
            float height = Mathf.Min(contentHeight, room);
            var box = new Rect(Mathf.Max(area.x + 4, area.xMax - TW.Padding - width), top, width, height);
            _keysBoxShown = box;
            ScrollMenu(ev, box, contentHeight, ref _keysScroll);
            if (paint)
            {
                MenuFrame(box);
                TW.Fill(box, TW.PanelColor);
                if (_capturing != null) _captureShownFrame = Time.frameCount;
            }
            GUI.BeginGroup(box);
            float x = 12, y = 10 - _keysScroll;

            // Heading, and on the right what a click on a key does.
            string clickNote = "Click a key to change it";
            float noteWidth = Mathf.Min(_mutedCell.CalcSize(new GUIContent(clickNote)).x, w * 0.45f);
            if (paint)
            {
                GUI.Label(new Rect(x, y, w - noteWidth - 12, lineH), TW.Elide("KEYS   (while no field has the keyboard)", _cell, w - noteWidth - 12), _cell);
                GUI.Label(new Rect(x + w - noteWidth, y, noteWidth, lineH), TW.Elide(clickNote, _mutedCell, noteWidth), _mutedCell);
            }
            y += lineH + 4;

            float keyWidth = KeyColumnWidth(w);
            foreach (string[] k in FixedKeysAbove)
            {
                FixedKeyRow(k, x, ref y, w, keyWidth, lineH, paint);
            }
            KeySeparator(x, ref y, w, separator, paint);
            foreach (var row in ChangeableKeys)
            {
                bool hot = _capturing != null && System.Array.IndexOf(row.keys, _capturing.Value) >= 0;
                if (paint && hot)
                {
                    TW.Fill(new Rect(x - 6, y, w + 12, lineH), KeyRowHotColor);
                }
                // The chip that waits may reach into the words beside it, so
                // the column keeps its width and no other row moves.
                float rowRoom = hot ? Mathf.Max(keyWidth, w * 0.6f) : keyWidth;
                float gaps = 4 * (row.keys.Length - 1);
                float most = (rowRoom - gaps) / row.keys.Length;
                bool squeeze = RowChipsWidth(row.keys, true) > rowRoom;
                float cx = x;
                foreach (Shortcut k in row.keys)
                {
                    string label = ChipLabel(k, true, out GUIStyle style);
                    float chipWidth = ChipWidth(label, style);
                    // Chips that do not fit share the room and cut their words.
                    if (squeeze) chipWidth = Mathf.Min(chipWidth, most);
                    var chip = new Rect(cx, y + 1, chipWidth, lineH - 2);
                    cx += chipWidth + 4;
                    if (paint)
                    {
                        PaintChip(chip, k, label, style, ev);
                    }
                    // A click on the chip that waits stops the wait; on
                    // another, that one waits instead.
                    if (Pressable(chip, box.height, out Rect hit) && GUI.Button(hit, GUIContent.none, GUIStyle.none))
                    {
                        if (_capturing == k) EndCapture();
                        else StartCapture(k);
                    }
                }
                if (paint)
                {
                    float dx = Mathf.Max(x + keyWidth + 8, cx + 4);
                    GUI.Label(new Rect(dx, y, x + w - dx, lineH), TW.Elide(row.what, _mutedCell, x + w - dx), _mutedCell);
                }
                y += lineH;
                foreach (Shortcut k in row.keys)
                {
                    string note = InspectorShortcuts.Note(k);
                    if (note == null)
                    {
                        continue;
                    }
                    float noteHeight = wrap ? WarningHeight(note, w) : lineH;
                    if (paint)
                    {
                        var line = new Rect(x, y, w, noteHeight);
                        if (wrap)
                        {
                            GUI.Label(line, note, _keyWarningWrapCell);
                        }
                        else
                        {
                            string shown = TW.Elide(note, _keyWarningCell, w);
                            GUI.Label(line, shown, _keyWarningCell);
                            if (shown != note) TW.Hint(line, note);
                        }
                    }
                    y += noteHeight;
                }
            }
            KeySeparator(x, ref y, w, separator, paint);
            foreach (string[] k in FixedKeysBelow)
            {
                FixedKeyRow(k, x, ref y, w, keyWidth, lineH, paint);
            }
            y += 6;

            // The closing line, and Reset all keys at its end like the
            // window's Reset window.
            var resetContent = new GUIContent("Reset all keys");
            float resetWidth = _linkCell.CalcSize(resetContent).x;
            var resetRect = new Rect(x + w - resetWidth, y, resetWidth, lineH);
            bool onReset = resetRect.Contains(ev.mousePosition);
            if (paint)
            {
                GUI.Label(new Rect(x, y, w - resetWidth - 12, lineH), TW.Elide("While a key is being set: Esc cancels, Backspace clears it.", s.Hint, w - resetWidth - 12), s.Hint);
                GUI.Label(resetRect, resetContent, onReset ? _linkHoverCell : _linkCell);
                TW.Fill(new Rect(resetRect.x, resetRect.center.y + _linkCell.lineHeight / 2f, resetRect.width, 1), onReset ? s.Label.normal.textColor : TW.MutedColor);
                TW.Hint(resetRect, "Reset all keys: every key that can change goes back to the one it had at first.");
            }
            if (Pressable(resetRect, box.height, out Rect resetHit) && GUI.Button(resetHit, GUIContent.none, GUIStyle.none))
            {
                EndCapture();
                InspectorShortcuts.ResetAll();
                TW.ShowNotice("The Inspector's keys are back to the ones they had at first.", NoticeKind.Info, 5f);
            }
            GUI.EndGroup();
            if (paint)
            {
                MenuScrollbar(box, contentHeight, _keysScroll);
            }
            // A click anywhere else while a chip waits stops the wait (a click
            // on a chip or on Reset all keys was taken by its button).
            if (clickToCheck && ev.type == EventType.MouseDown)
            {
                EndCapture();
            }
            if (!paint)
            {
                Swallow(ev, box);
            }
        }

        // The part of a chip or a link inside the panel, which is all of it
        // unless the panel is scrolled; false when none of it is.
        private static bool Pressable(Rect rect, float boxHeight, out Rect inside)
        {
            float top = Mathf.Max(rect.y, 0f), bottom = Mathf.Min(rect.yMax, boxHeight);
            inside = new Rect(rect.x, top, rect.width, bottom - top);
            return bottom > top;
        }

        private static float WarningHeight(string note, float w)
        {
            return _keyWarningWrapCell.CalcHeight(new GUIContent(note), w) + 2f;
        }

        private static void FixedKeyRow(string[] k, float x, ref float y, float w, float keyWidth, float lineH, bool paint)
        {
            if (paint)
            {
                GUI.Label(new Rect(x, y, keyWidth, lineH), k[0], _keyCell);
                GUI.Label(new Rect(x + keyWidth + 8, y, w - keyWidth - 8, lineH), TW.Elide(k[1], _mutedCell, w - keyWidth - 8), _mutedCell);
            }
            y += lineH;
        }

        private static void KeySeparator(float x, ref float y, float w, float height, bool paint)
        {
            if (paint)
            {
                TW.Fill(new Rect(x, y + Mathf.Floor(height / 2), w, 1), KeyEdgeColor);
            }
            y += height;
        }

        // The key column: wide enough for the widest row of chips or fixed
        // key, at least 100 and at most a little over half the panel. By the
        // keys that are set, not by "Press a key…", so it does not jump while
        // a chip waits.
        private static float KeyColumnWidth(float w)
        {
            float widest = 100f;
            foreach (string[] k in FixedKeysAbove) widest = Mathf.Max(widest, _keyCell.CalcSize(new GUIContent(k[0])).x);
            foreach (string[] k in FixedKeysBelow) widest = Mathf.Max(widest, _keyCell.CalcSize(new GUIContent(k[0])).x);
            foreach (var row in ChangeableKeys) widest = Mathf.Max(widest, RowChipsWidth(row.keys, false));
            return Mathf.Min(widest, w * 0.55f);
        }

        private static float RowChipsWidth(Shortcut[] keys, bool asShown)
        {
            float total = 4 * (keys.Length - 1);
            foreach (Shortcut k in keys)
            {
                string label = ChipLabel(k, asShown, out GUIStyle style);
                total += ChipWidth(label, style);
            }
            return total;
        }

        private static float ChipWidth(string label, GUIStyle style)
        {
            return Mathf.Max(28f, style.CalcSize(new GUIContent(label)).x + 14f);
        }

        // What the chip says: the key, "none", or while it waits (and asShown)
        // "Press a key…".
        private static string ChipLabel(Shortcut k, bool asShown, out GUIStyle style)
        {
            if (asShown && _capturing == k)
            {
                style = _chipCaptureCell;
                return TW.CanDraw("…") ? "Press a key…" : "Press a key...";
            }
            string name = InspectorShortcuts.Name(k);
            if (name.Length == 0)
            {
                style = _chipNoneCell;
                return "none";
            }
            style = _chipCell;
            return name;
        }

        // An inset face with a 1 px edge in the line colour, the accent under
        // the pointer; while it waits for a key, accent words and a 2 px accent
        // line along the bottom; a 3 px warning bar on the left when another
        // setting has the key too.
        private static void PaintChip(Rect chip, Shortcut k, string label, GUIStyle style, Event ev)
        {
            bool waiting = _capturing == k;
            bool hover = chip.Contains(ev.mousePosition);
            TW.Fill(chip, TW.InsetColor);
            Color edge = waiting || hover ? TW.AccentColor : KeyEdgeColor;
            TW.Fill(new Rect(chip.x, chip.y, chip.width, 1), edge);
            TW.Fill(new Rect(chip.x, chip.yMax - 1, chip.width, 1), edge);
            TW.Fill(new Rect(chip.x, chip.y, 1, chip.height), edge);
            TW.Fill(new Rect(chip.xMax - 1, chip.y, 1, chip.height), edge);
            if (waiting)
            {
                TW.Fill(new Rect(chip.x, chip.yMax - 2, chip.width, 2), TW.AccentColor);
            }
            else if (InspectorShortcuts.Note(k) != null)
            {
                TW.Fill(new Rect(chip.x, chip.y, 3, chip.height), TW.WarningColor);
            }
            float inset = InspectorShortcuts.Note(k) != null && !waiting ? 3f : 0f;
            var text = new Rect(chip.x + inset, chip.y, chip.width - inset, chip.height);
            GUI.Label(text, TW.Elide(label, style, text.width - 6), style);
            if (hover && !waiting)
            {
                string name = InspectorShortcuts.Name(k);
                TW.Hint(chip, InspectorShortcuts.Title(k) + (name.Length > 0 ? " (" + name + ")" : " (no key)") + ": click, then press the new key.");
            }
        }
    }
}
