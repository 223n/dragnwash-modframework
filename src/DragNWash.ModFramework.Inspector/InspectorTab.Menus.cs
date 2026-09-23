using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's two menus: the toolbar's Edit/View tool menu (move,
    // rotate, scale, edit mesh, highlight, bones, wireframe, free camera, debug
    // view...), and a row's menu (now, reset to original, undo, copy,
    // show in History). Shared drawing: the frame, scrolling and input-swallow
    // that make a menu sit on top of everything under it.
    internal static partial class InspectorTab
    {
        private static RowInfo _menuRow;
        private static int _menuComponent = -1;   // which x/y/z field was right-clicked, or -1 for the row
        // The menu lists an enum row's values to pick from, instead of the row's actions.
        private static bool _menuValues;
        private static Vector2 _menuAt;

        // Opens a row's menu at a point in the coordinates being drawn in (a
        // scroll view's too): the right click's place, or under the button
        // that opened it.
        private static void OpenRowMenu(RowInfo r, int component, bool values, Vector2 at)
        {
            _menuRow = r;
            _menuComponent = component;
            _menuValues = values;
            _valuesWidth = 0;
            _menuAt = GUIUtility.GUIToScreenPoint(at) - _tabScreenOrigin;
        }

        // A menu item's mark: a tick for something on, a filled or an empty
        // dot for one of several choices. ASCII where the window font lacks them.
        private const string MarkCheck = "\u2713", MarkOn = "\u25CF", MarkOff = "\u25CB";
        private static string Mark(bool on, bool choice)
        {
            string mark = choice ? (on ? MarkOn : MarkOff) : (on ? MarkCheck : "");
            if (mark.Length > 0 && !TW.CanDraw(mark))
            {
                mark = on ? (choice ? "*" : "x") : "";
            }
            return mark;
        }
        // ---- the toolbar menus ---------------------------------------------------------------

        private static string _toolMenu;
        private static Rect _lastToolRect;
        // Where the menus were laid out last, for hiding the pointer under them.
        private static Rect _menuBoxShown;
        private static Rect _toolMenuBoxShown;
        private static Vector2 _pointer;
        private static bool _pointerHidden;
        private static Rect _toolbarRect;
        private static Rect _toolMenuButton;
        private static void OpenToolMenu(string name)
        {
            _toolMenu = _toolMenu == name ? null : name;
            _toolMenuButton = _lastToolRect;
            _toolMenuScroll = 0;
        }
        private static void DrawToolMenu(Rect area, ToolWindowStyles s, float row)
        {
            if (_toolMenu == null)
            {
                return;
            }
            Event ev = Event.current;
            // Each item: its words, whether it is on, what a click does, whether
            // the menu stays open after it, and its mark ("" for none, null
            // for a menu without the marks' column). A heading has no click.
            var items = new List<(string label, bool on, Action click, bool keep, string mark)>();
            void Heading(string text) => items.Add((text, false, null, true, ""));
            void Check(string text, bool on, Action click, bool keep = true) => items.Add((text, on, click, keep, Mark(on, false)));
            void Choice(string text, bool on, Action click) => items.Add((text, on, click, true, Mark(on, true)));
            GameObject sel = SelectedObject;
            if (_toolMenu == "edit")
            {
                items.Add((IconMove + " Move" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.GizmoMove), InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Move, () => ToggleGizmo(InspectorGizmo.GizmoMode.Move), false, null));
                items.Add((IconRotate + " Rotate" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.GizmoRotate), InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Rotate, () => ToggleGizmo(InspectorGizmo.GizmoMode.Rotate), false, null));
                items.Add((IconScale + " Scale" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.GizmoScale), InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Scale, () => ToggleGizmo(InspectorGizmo.GizmoMode.Scale), false, null));
                items.Add((IconEditMesh + " Edit mesh vertices" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.EditMesh) + " - experimental", InspectorMesh.Editing, () => { if (InspectorMesh.Editing) InspectorMesh.StopEditing(); else InspectorMesh.Editing = true; }, false, null));
                if (InspectorGizmo.HasOriginal(sel)) items.Add(("Reset transform", false, () => InspectorGizmo.ResetTransform(sel), false, null));
                if (InspectorMesh.HasEdited(sel)) items.Add(("Reset mesh", false, () => InspectorMesh.ResetMesh(sel), false, null));
            }
            else
            {
                // By what the items are about; one of the debug view's scopes
                // at a time, the rest on or off each.
                Heading("OVER THE GAME");
                Check("Highlight the selection" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.Highlight), InspectorPick.Highlight, () => InspectorPick.Highlight = !InspectorPick.Highlight);
                Check("Bones" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.Bones), InspectorBones.Show, () => InspectorBones.Show = !InspectorBones.Show);
                Check("Wireframe" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.Wireframe), InspectorMesh.Wireframe, () => InspectorMesh.Wireframe = !InspectorMesh.Wireframe);
                Check("Free camera" + InspectorShortcuts.Suffix(InspectorShortcuts.Shortcut.FreeCamera), InspectorFreeCamera.Active, InspectorFreeCamera.Toggle, false);
                Heading("DEBUG VIEW (PICK ONE)");
                Choice("Everything the camera sees", InspectorDebugView.Mode == InspectorDebugView.Scope.Visible, () => InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Visible ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Visible);
                Choice("The selection's children", InspectorDebugView.Mode == InspectorDebugView.Scope.Children, () => InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Children ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Children);
                Choice("What the search text matches", InspectorDebugView.Mode == InspectorDebugView.Scope.Filter, () => { InspectorDebugView.Filter = _search ?? ""; InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Filter ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Filter; });
                Heading("IT SHOWS");
                Check("Colliders and triggers", InspectorDebugView.Colliders, () => InspectorDebugView.Colliders = !InspectorDebugView.Colliders);
                Check("Lights", InspectorDebugView.Lights, () => InspectorDebugView.Lights = !InspectorDebugView.Lights);
                if (InspectorBodies.Available)
                {
                    Check("Rigidbodies: centre of mass, velocity", InspectorDebugView.Bodies, () => InspectorDebugView.Bodies = !InspectorDebugView.Bodies);
                }
                Heading("HOW IT DRAWS");
                Check("Of the selection only", InspectorDebugView.SelectionOnly, () => InspectorDebugView.SelectionOnly = !InspectorDebugView.SelectionOnly);
                Check("Collider and light shapes", InspectorDebugView.Shapes, () => InspectorDebugView.Shapes = !InspectorDebugView.Shapes);
                Check("Screen rectangles", InspectorDebugView.Rects, () => InspectorDebugView.Rects = !InspectorDebugView.Rects);
                Check("3D boxes", InspectorDebugView.Boxes, () => InspectorDebugView.Boxes = !InspectorDebugView.Boxes);
                Check("Names (near the pointer when many)", InspectorDebugView.Names, () => InspectorDebugView.Names = !InspectorDebugView.Names);
                Heading("IN THE PANE");
                if (InspectorBodies.Available)
                {
                    Check("Rigidbodies list", _showBodies, () => { _showBodies = !_showBodies; if (_showBodies) { _showHistory = false; _showScenes = false; _showUsedBy = false; if (_page == 0) _page = 1; } }, false);
                }
                Check("Scenes and levels", _showScenes, () => { _showScenes = !_showScenes; if (_showScenes) { _showHistory = false; _showBodies = false; _showUsedBy = false; if (_page == 0) _page = 1; } }, false);
            }
            float lineH = row - 2;
            float markWidth = items.Exists(i => i.mark != null) ? 18 : 0;
            float width = 200;
            foreach (var item in items)
            {
                width = Mathf.Max(width, s.Button.CalcSize(new GUIContent(Drawable(item.label))).x + 24 + markWidth);
            }
            // The window clips whatever is drawn past its edge, so the menu is
            // kept inside it: no wider than the tab, no taller than the room
            // under its button, and a longer list scrolls with the wheel.
            width = Mathf.Min(width, area.width - 8);
            float contentHeight = items.Count * lineH + 8;
            float top = _toolMenuButton.yMax + 2;
            float height = Mathf.Max(lineH + 8, Mathf.Min(contentHeight, area.yMax - top));
            var box = new Rect(Mathf.Clamp(_toolMenuButton.x, area.x, area.xMax - width), top, width, height);
            _toolMenuBoxShown = box;
            if (ev.type == EventType.MouseDown && !box.Contains(ev.mousePosition) && !_toolMenuButton.Contains(ev.mousePosition))
            {
                _toolMenu = null;
                // Closing it is all the click does, so nothing under the menu
                // answers it; on the toolbar it still opens the other menu.
                if (!_toolbarRect.Contains(ev.mousePosition))
                {
                    ev.Use();
                }
                return;
            }
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                _toolMenu = null;
                ev.Use();
                return;
            }
            ScrollMenu(ev, box, contentHeight, ref _toolMenuScroll);
            MenuFrame(box);
            TW.Fill(box, TW.PanelColor);
            TW.Fill(new Rect(box.x, box.y, 3, box.height), TW.AccentColor);
            GUI.BeginGroup(box);
            float y = 4 - _toolMenuScroll;
            foreach (var item in items)
            {
                var line = new Rect(8, y, box.width - 16, lineH);
                y += lineH;
                if (line.yMax <= 0 || line.y >= box.height) continue;
                if (item.click == null)
                {
                    // A heading: dim capitals, at the size the font was made for.
                    GUI.Label(line, item.label, _mutedCell);
                    continue;
                }
                if (item.on)
                {
                    TW.Fill(new Rect(3, line.y, box.width - 3, lineH), TW.InsetColor);
                }
                if (markWidth > 0)
                {
                    GUI.Label(new Rect(line.x, line.y, markWidth, lineH), item.mark ?? "", item.on ? _accentCell : _mutedCell);
                }
                if (GUI.Button(new Rect(line.x + markWidth, line.y, line.width - markWidth, lineH), Drawable(item.label), item.on ? _accentCell : _cell))
                {
                    item.click();
                    if (!item.keep) _toolMenu = null;
                }
            }
            GUI.EndGroup();
            MenuScrollbar(box, contentHeight, _toolMenuScroll);
            Swallow(ev, box);
        }
        private static float _toolMenuScroll;
        private static float _menuScroll;
        private static RowInfo _menuScrollRow;
        // A shadow below and to the right, and a thin frame, so a menu reads as
        // lying on top of what is under it. Painted only; the box itself takes
        // the input.
        private static void MenuFrame(Rect box)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            var shadow = new Color(0f, 0f, 0f, 0.18f);
            for (int i = 1; i <= 3; i++)
            {
                TW.Fill(new Rect(box.x + i * 2, box.yMax, box.width, i * 2), shadow);
                TW.Fill(new Rect(box.xMax, box.y + i * 2, i * 2, box.height), shadow);
            }
            var edge = new Color(TW.MutedColor.r, TW.MutedColor.g, TW.MutedColor.b, 0.55f);
            TW.Fill(new Rect(box.x - 1, box.y - 1, box.width + 2, 1), edge);
            TW.Fill(new Rect(box.x - 1, box.yMax, box.width + 2, 1), edge);
            TW.Fill(new Rect(box.x - 1, box.y, 1, box.height), edge);
            TW.Fill(new Rect(box.xMax, box.y, 1, box.height), edge);
        }
        // The wheel over a menu taller than its box moves the list.
        private static void ScrollMenu(Event ev, Rect box, float contentHeight, ref float scroll)
        {
            if (ev.type == EventType.ScrollWheel && box.Contains(ev.mousePosition))
            {
                scroll += ev.delta.y * 20f;
            }
            // And the gamepad's stick, as in the panes: the View menu is
            // taller than a short window, and a pad has no wheel.
            var pad = new Vector2(0f, scroll);
            if (TW.ApplyScroll(box, ref pad))
            {
                scroll = pad.y;
            }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - box.height));
        }
        // A thin bar on the right edge, only when the list does not fit.
        private static void MenuScrollbar(Rect box, float contentHeight, float scroll)
        {
            if (contentHeight <= box.height + 0.5f)
            {
                return;
            }
            float track = box.height - 4;
            float thumb = Mathf.Max(16f, track * box.height / contentHeight);
            float t = scroll / (contentHeight - box.height);
            TW.Fill(new Rect(box.xMax - 5, box.y + 2 + (track - thumb) * t, 3, thumb), TW.MutedColor);
        }
        // Every mouse event over an open menu is the menu's, buttons first:
        // whatever is drawn under it - a row, a field, a button - sees none of
        // them, and the wheel does not scroll the pane behind.
        private static void Swallow(Event ev, Rect box)
        {
            if ((ev.isMouse || ev.type == EventType.ScrollWheel || ev.type == EventType.ContextClick) && box.Contains(ev.mousePosition))
            {
                ev.Use();
            }
        }
        private static bool narrowWindow(Rect area)
        {
            return area.width < NarrowWidth;
        }
        private static void ToggleGizmo(InspectorGizmo.GizmoMode m)
        {
            InspectorGizmo.Mode = InspectorGizmo.Mode == m ? InspectorGizmo.GizmoMode.None : m;
        }
        private static void DrawMenu(Rect area, ToolWindowStyles s, float row)
        {
            if (_menuRow == null)
            {
                return;
            }
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                _menuRow = null;
                ev.Use();
                return;
            }
            RowInfo r = _menuRow;
            if (_menuValues)
            {
                DrawValuesMenu(area, s, row, r, ev);
                return;
            }
            string member = MemberId(r);
            bool hasOriginal = InspectorHistory.TryOriginal(_target, member, out object original);
            bool hasPrevious = InspectorHistory.TryPrevious(_target, member, out object previous);
            object now = SafeGet(r.Member, r.Getter);
            var items = new List<KeyValuePair<string, Action>>();
            // One component (x, y, z, r, g, b, a) of a composite: its own original and previous.
            if (_menuComponent >= 0 && InspectorModel.IsComposite(r.Type) && now != null)
            {
                int ci = _menuComponent;
                string[] labels = InspectorModel.ComponentLabels(r.Type);
                string cname = ci < labels.Length ? labels[ci] : "?";
                float[] parts = InspectorModel.Components(now);
                items.Add(new KeyValuePair<string, Action>($"{cname} now:  {(ci < parts.Length ? InspectorModel.Fmt(parts[ci]) : "?")}", null));
                if (hasOriginal && original != null)
                {
                    float[] o = InspectorModel.Components(original);
                    if (ci < o.Length)
                    {
                        items.Add(new KeyValuePair<string, Action>($"Reset {cname} to original:  {InspectorModel.Fmt(o[ci])}", () =>
                        {
                            float[] cur = InspectorModel.Components(SafeGet(r.Member, r.Getter));
                            if (ci < cur.Length) { cur[ci] = o[ci]; TrySet(r, InspectorModel.Compose(r.Type, cur)); }
                        }));
                    }
                }
                if (hasPrevious && previous != null)
                {
                    float[] pv = InspectorModel.Components(previous);
                    if (ci < pv.Length)
                    {
                        items.Add(new KeyValuePair<string, Action>($"Undo {cname}: back to  {InspectorModel.Fmt(pv[ci])}", () =>
                        {
                            float[] cur = InspectorModel.Components(SafeGet(r.Member, r.Getter));
                            if (ci < cur.Length) { cur[ci] = pv[ci]; TrySet(r, InspectorModel.Compose(r.Type, cur)); }
                        }));
                    }
                }
            }
            items.Add(new KeyValuePair<string, Action>("now:  " + InspectorModel.Format(now), null));
            if (hasOriginal)
            {
                items.Add(new KeyValuePair<string, Action>("Reset to original:  " + InspectorModel.Format(original), () =>
                {
                    if (TrySet(r, original)) { InspectorHistory.ForgetOriginal(_target, member); Drafts.Remove(r.Key); }
                }));
            }
            if (hasPrevious)
            {
                items.Add(new KeyValuePair<string, Action>("Undo: back to  " + InspectorModel.Format(previous), () => { TrySet(r, previous); Drafts.Remove(r.Key); }));
            }
            items.Add(new KeyValuePair<string, Action>("Copy value", () => GUIUtility.systemCopyBuffer = InspectorModel.Format(now)));
            items.Add(new KeyValuePair<string, Action>("Copy name", () => GUIUtility.systemCopyBuffer = r.Member.Name));
            if (InspectorHistory.For(_target, member).Count > 0)
            {
                items.Add(new KeyValuePair<string, Action>("Show in History", () => _showHistory = true));
            }
            DrawMenuBox(area, s, row, r, ev, items, null);
        }

        // An enum row's values, the one it has marked: picking one sets it.
        // The values and the menu's width are kept for the type, since one
        // like KeyCode has hundreds and the menu is drawn several times a frame.
        private static Type _valuesType;
        private static Array _values;
        private static string[] _valueNames;
        private static float _valuesWidth;

        private static void DrawValuesMenu(Rect area, ToolWindowStyles s, float row, RowInfo r, Event ev)
        {
            object now = SafeGet(r.Member, r.Getter);
            var items = new List<KeyValuePair<string, Action>>();
            var marks = new List<string>();
            if (r.Type != null && r.Type.IsEnum)
            {
                if (_valuesType != r.Type)
                {
                    _valuesType = r.Type;
                    _values = Enum.GetValues(r.Type);
                    _valueNames = Enum.GetNames(r.Type);
                    _valuesWidth = 0;
                }
                for (int i = 0; i < _values.Length && i < _valueNames.Length; i++)
                {
                    object value = _values.GetValue(i);
                    marks.Add(Mark(Equals(value, now), true));
                    items.Add(new KeyValuePair<string, Action>(_valueNames[i], () => { TrySet(r, value); Drafts.Remove(r.Key); }));
                }
            }
            DrawMenuBox(area, s, row, r, ev, items, marks, ref _valuesWidth);
        }

        private static void DrawMenuBox(Rect area, ToolWindowStyles s, float row, RowInfo r, Event ev, List<KeyValuePair<string, Action>> items, List<string> marks)
        {
            float width = 0;
            DrawMenuBox(area, s, row, r, ev, items, marks, ref width);
        }

        // The row menus' box: framed, kept inside the tab, scrolling with the
        // wheel when it is taller than the tab. Marks, when given, go in a
        // column of their own before the items. A width above 0 is used as
        // it is; otherwise the items are measured and it is set.
        private static void DrawMenuBox(Rect area, ToolWindowStyles s, float row, RowInfo r, Event ev, List<KeyValuePair<string, Action>> items, List<string> marks, ref float measured)
        {
            float markWidth = marks != null ? 18 : 0;
            float lineH = row - 4;
            if (measured <= 0)
            {
                measured = marks != null ? 140 : 200;
                foreach (KeyValuePair<string, Action> item in items)
                {
                    measured = Mathf.Max(measured, s.Button.CalcSize(new GUIContent(Drawable(item.Key))).x + 16 + markWidth);
                }
            }
            float width = Mathf.Min(measured, area.width - 8);
            float contentHeight = items.Count * lineH + 8;
            float height = Mathf.Min(contentHeight, area.height);
            var box = new Rect(Mathf.Clamp(_menuAt.x, area.x, area.xMax - width), Mathf.Clamp(_menuAt.y, area.y, area.yMax - height), width, height);
            _menuBoxShown = box;
            if (!ReferenceEquals(_menuScrollRow, r))
            {
                _menuScrollRow = r;
                _menuScroll = 0;
                // A long list of values opens on the one that is set.
                int set = marks == null ? -1 : marks.FindIndex(m => m.Length > 0 && m != MarkOff);
                if (set >= 0)
                {
                    _menuScroll = Mathf.Max(0f, 4 + set * lineH - (height - lineH) / 2);
                }
            }
            // A click outside closes it, and does nothing else; a click inside
            // is handled by the buttons.
            if (ev.type == EventType.MouseDown && !box.Contains(ev.mousePosition))
            {
                _menuRow = null;
                ev.Use();
                return;
            }
            ScrollMenu(ev, box, contentHeight, ref _menuScroll);
            MenuFrame(box);
            TW.Fill(box, TW.PanelColor);
            TW.Fill(new Rect(box.x, box.y, 3, box.height), TW.AccentColor);
            GUI.BeginGroup(box);
            float y = 4 - _menuScroll;
            for (int i = 0; i < items.Count; i++)
            {
                KeyValuePair<string, Action> item = items[i];
                var line = new Rect(6, y, box.width - 12, lineH);
                y += lineH;
                if (line.yMax <= 0 || line.y >= box.height) continue;
                string mark = marks != null && i < marks.Count ? marks[i] : "";
                bool on = mark.Length > 0 && mark != MarkOff;
                if (on)
                {
                    TW.Fill(new Rect(3, line.y, box.width - 3, lineH), TW.InsetColor);
                }
                if (markWidth > 0)
                {
                    GUI.Label(new Rect(line.x, line.y, markWidth, lineH), mark, on ? _accentCell : _mutedCell);
                }
                var text = new Rect(line.x + markWidth, line.y, line.width - markWidth, lineH);
                if (item.Value == null)
                {
                    GUI.Label(text, Drawable(item.Key), _mutedCell);
                }
                else if (GUI.Button(text, Drawable(item.Key), on ? _accentCell : _cell))
                {
                    item.Value();
                    _menuRow = null;
                }
            }
            GUI.EndGroup();
            MenuScrollbar(box, contentHeight, _menuScroll);
            Swallow(ev, box);
        }
    }
}
