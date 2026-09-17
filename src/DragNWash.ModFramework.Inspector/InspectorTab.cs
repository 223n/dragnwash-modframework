using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Node = DragNWash.ModFramework.Inspector.InspectorModel.Node;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The "Inspector" tab: the scene's objects, a selected object's components,
    // and a selected component's (or material's) members, readable and editable
    // while the game runs. Three panes side by side; in a narrow window, three
    // pages. Nothing is saved: an edit lives until the scene reloads or the
    // game quits, and Reset puts back what a row held before its first edit.
    // See docs/INSPECTOR.md.
    internal static class InspectorTab
    {
        internal const string Title = "Inspector";
        private const string ControlPrefix = "DnWInspect:";
        private const float NarrowWidth = 700f;

        private static IDisposable _tab;

        // Hierarchy.
        private static List<Node> _tree;
        private static readonly HashSet<int> Expanded = new HashSet<int>();
        private static string _search = "";
        private static string _searched;
        private static List<Node> _results;
        private static bool _dirty = true;
        // Set when the selection changed from outside the tree (pick, Go, Parent,
        // console): the tree scrolls to show it on its next draw.
        private static bool _revealSelection;
        private static Vector2 _scrollTree, _scrollComponents, _scrollMembers;

        // Selection.
        private static GameObject _object;
        private static object _target;          // GameObject, Component or Material
        private static List<Member> _members;
        private static int _rendererUsers = -1;

        // Members pane.
        private static bool _showPrivate;
        private static bool _freeze;
        private static readonly Dictionary<string, string> Frozen = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Drafts = new Dictionary<string, string>();
        // What each draft started from: leaving a field applies it only when it was typed in,
        // never a stale copy of a value the game moved on from meanwhile.
        private static readonly Dictionary<string, string> DraftStart = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> RowErrors = new Dictionary<string, string>();
        // Press and hold on a number field, then move up or down: the value
        // changes with the mouse (Unity's own inspector does this on the label).
        private static string _pressKey;
        private static Vector2 _pressMouse;
        private static float _pressValue;
        private static bool _numberDragging;
        private static object _dragBefore;
        private static RowInfo _dragRow;

        // A Transform shows its position, rotation and scale first; the rest waits behind a toggle.
        private static bool _allMembers;

        // The row menu (right click) and the History view.
        private static RowInfo _menuRow;
        private static int _menuComponent = -1;   // which x/y/z field was right-clicked, or -1 for the row
        private static Vector2 _menuAt;
        private static bool _showHistory;
        private static Vector2 _scrollHistory;
        private static readonly HashSet<string> ExpandedLists = new HashSet<string>();
        private static readonly List<RowInfo> Rows = new List<RowInfo>();
        private static string _focusedControl = "";
        private static int _actedFrame = -1;
        private static bool _noteShown;
        private static string _status = "";

        // Narrow window: which pane is the page.
        private static int _page;

        private static GUIStyle _cell, _mutedCell, _errorCell, _accentCell, _toggleStyle;

        /// <summary>The selected object, or null (also when it was destroyed).</summary>
        internal static GameObject SelectedObject => ReferenceEquals(_object, null) || !_object ? null : _object;

        internal static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = TW.AddTab(TW.Guid, Title, Draw, 45);
            TW.PrepareCharacters(IconHierarchy + IconPick + IconHighlight + IconParent + IconMove + IconRotate + IconScale + IconResetTransform + IconHistory + IconRefresh + IconCamera);
            GameEvents.OnSceneLoaded(TW.Guid, (scene, mode) => _dirty = true);
            GameEvents.OnSceneUnloaded(TW.Guid, scene => _dirty = true);
            TW.AddCommand(TW.Guid, "inspect",
                "inspect | inspect <name or path> [component] | inspect set <path> <component> <member> <value> | inspect pick",
                Command, Complete);
        }

        // ---- selection (also from TW.Inspect, the console and pick mode) ----

        internal static void Select(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            switch (target)
            {
                case GameObject go:
                    SelectObject(go);
                    break;
                case Component c:
                    SelectObject(c.gameObject);
                    SetTarget(c);
                    break;
                case Material m:
                    SetTarget(m);
                    break;
                default:
                    _status = $"{target.name} ({target.GetType().Name}) is not an object, a component or a material.";
                    return;
            }
            _page = _target != null && !(_target is GameObject) ? 2 : 1;
        }

        private static void SelectObject(GameObject go)
        {
            _object = go;
            _status = go != null ? InspectorModel.PathOf(go.transform) : "";
            InspectorModel.ExpandTo(go != null ? go.transform : null, Expanded);
            _dirty = true;
            _revealSelection = go != null;
            SetTarget(go);
            _scrollComponents = Vector2.zero;
        }

        private static void SetTarget(object target)
        {
            _target = target;
            // A new selection shows its members, not the history that was open.
            _showHistory = false;
            _members = null;
            _rendererUsers = -1;
            Drafts.Clear();
            DraftStart.Clear();
            RowErrors.Clear();
            Frozen.Clear();
            _menuRow = null;
            ExpandedLists.Clear();
            InspectorColorPicker.Close();
            _scrollMembers = Vector2.zero;
        }

        private static List<Member> MembersOfTarget()
        {
            if (_members != null)
            {
                return _members;
            }
            switch (_target)
            {
                case Material m:
                    _members = InspectorModel.MaterialMembers(m);
                    break;
                case GameObject _:
                    _members = InspectorModel.GameObjectMembers();
                    break;
                case null:
                    _members = new List<Member>();
                    break;
                default:
                    _members = InspectorModel.MembersOf(_target.GetType());
                    break;
            }
            return _members;
        }

        // ---- drawing -------------------------------------------------------------------

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_cell != null)
            {
                return;
            }
            _cell = new GUIStyle(s.Label) { wordWrap = false, clipping = TextClipping.Clip };
            _mutedCell = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _errorCell = new GUIStyle(_mutedCell);
            _errorCell.normal.textColor = TW.ErrorColor;
            _errorCell.hover.textColor = TW.ErrorColor;
            _accentCell = new GUIStyle(_cell);
            _accentCell.normal.textColor = TW.AccentColor;
            _accentCell.hover.textColor = TW.AccentColor;
        }

        private static Vector2 _tabScreenOrigin;

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = TW.Styles;
            EnsureStyles(s);
            _tabScreenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            float row = TW.RowHeight, pad = TW.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            Event ev = Event.current;

            // A destroyed selection is dropped, not thrown on. Unity's == null is
            // already true for a destroyed object, so the reference itself is
            // tested first and the object's liveness second.
            if (!ReferenceEquals(_object, null) && !_object)
            {
                _object = null;
                SetTarget(null);
                _status = "The selected object was destroyed.";
            }
            if (_target is UnityEngine.Object uo && !uo)
            {
                SetTarget(_object);
                _status = "The selected component was destroyed.";
            }
            if (ev.type == EventType.Repaint)
            {
                _focusedControl = GUI.GetNameOfFocusedControl() ?? "";
            }
            // Enter applies the draft of the focused row. Keys are handled before
            // the fields are drawn (see ConsoleTab for why), and the character
            // half of the key is swallowed in the same frame.
            bool enter = ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.character == (char)10 || ev.character == (char)13;
            if (ev.type == EventType.KeyDown && enter)
            {
                if (_actedFrame == Time.frameCount)
                {
                    ev.Use();
                }
                else if (_focusedControl.StartsWith(ControlPrefix, StringComparison.Ordinal))
                {
                    Apply(RowKeyOf(_focusedControl));
                    _actedFrame = Time.frameCount;
                    ev.Use();
                }
            }

            if (!_noteShown)
            {
                _noteShown = true;
                _status = "Experimental. Edits are not saved. Keys: W/E/R gizmo, Q off, P pick, H highlight, T tree, C camera, Ctrl+Z undo, Ctrl+Up parent.";
            }
            // Shortcuts, only while no field has the keyboard (typing must not
            // trigger them), and only for keys the fields never use anyway.
            if (ev.type == EventType.KeyDown && string.IsNullOrEmpty(_focusedControl) && !InspectorColorPicker.Open)
            {
                bool ctrl = ev.control || ev.command;
                bool handled = true;
                if (InspectorFreeCamera.Flying)
                {
                    handled = false;
                }
                else switch (ev.keyCode)
                {
                    case KeyCode.C: if (!ctrl) InspectorFreeCamera.Toggle(); else handled = false; break;
                    case KeyCode.W: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Move); else handled = false; break;
                    case KeyCode.E: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Rotate); else handled = false; break;
                    case KeyCode.R: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Scale); else handled = false; break;
                    case KeyCode.Q: if (!ctrl) InspectorGizmo.Mode = InspectorGizmo.GizmoMode.None; else handled = false; break;
                    case KeyCode.P: if (!ctrl) { if (InspectorPick.Picking) InspectorPick.End(); else InspectorPick.Begin(); } else handled = false; break;
                    case KeyCode.H: if (!ctrl) InspectorPick.Highlight = !InspectorPick.Highlight; else handled = false; break;
                    case KeyCode.T: if (!ctrl) { _showHierarchy = !_showHierarchy; if (narrowWindow(area)) _page = _showHierarchy ? 0 : 1; } else handled = false; break;
                    case KeyCode.Z: if (ctrl) _status = InspectorHistory.Undo(); else handled = false; break;
                    case KeyCode.UpArrow: if (ctrl && SelectedObject != null && SelectedObject.transform.parent != null) Select(SelectedObject.transform.parent.gameObject); else handled = false; break;
                    case KeyCode.Escape:
                        if (_menuRow != null) _menuRow = null;
                        else if (InspectorPick.Picking) InspectorPick.End();
                        else if (InspectorGizmo.Mode != InspectorGizmo.GizmoMode.None) InspectorGizmo.Mode = InspectorGizmo.GizmoMode.None;
                        else handled = false;
                        break;
                    default: handled = false; break;
                }
                if (handled)
                {
                    ev.Use();
                }
            }

            bool narrow = area.width < NarrowWidth;
            // One toolbar row: selection, gizmo, history, search. Icons where
            // the window font has them, short words where it does not; the row
            // wraps in a narrow window.
            float bx = x;
            void Place(float width, Func<Rect, bool> button)
            {
                if (bx + width > x + w && bx > x)
                {
                    bx = x;
                    y += row + 6;
                }
                button(new Rect(bx, y, width, row));
                bx += width + 6;
            }
            void Tool(string icon, string word, string tip, bool on, Action click)
            {
                string label = TW.CanDraw(icon) && icon != word ? icon + " " + word : word;
                float width = Mathf.Max(38, s.Button.CalcSize(new GUIContent(label)).x + 14);
                Place(width, rect =>
                {
                    if (GUI.Button(rect, new GUIContent(label, tip), on ? s.SelectedButton : s.Button)) click();
                    return true;
                });
            }
            if (narrow && _page > 0)
            {
                Tool("<", "<", "Back", false, () => _page--);
            }
            Tool(IconHierarchy, "Tree", "Show or hide the hierarchy", _showHierarchy, () => { _showHierarchy = !_showHierarchy; if (narrow) _page = _showHierarchy ? 0 : 1; });
            Tool(IconPick, "Pick", "Pick: click an object in the game", InspectorPick.Picking, () => { if (InspectorPick.Picking) InspectorPick.End(); else InspectorPick.Begin(); });
            Tool(IconHighlight, "Highlight", "Outline the selected object in the game", InspectorPick.Highlight, () => InspectorPick.Highlight = !InspectorPick.Highlight);
            if (SelectedObject != null && SelectedObject.transform.parent != null)
            {
                Tool(IconParent, "Parent", "Select the parent", false, () => Select(SelectedObject.transform.parent.gameObject));
            }
            bx += 6;
            Tool(IconMove, "Move", "Gizmo: move", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Move, () => ToggleGizmo(InspectorGizmo.GizmoMode.Move));
            Tool(IconRotate, "Rotate", "Gizmo: rotate", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Rotate, () => ToggleGizmo(InspectorGizmo.GizmoMode.Rotate));
            Tool(IconScale, "Scale", "Gizmo: scale", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Scale, () => ToggleGizmo(InspectorGizmo.GizmoMode.Scale));
            if (InspectorGizmo.HasOriginal(SelectedObject))
            {
                Tool(IconResetTransform, "Reset", "Reset transform to before the first drag", false, () => InspectorGizmo.ResetTransform(SelectedObject));
            }
            Tool(IconCamera, "Camera", "Free camera: fly around with the right mouse button held (C)", InspectorFreeCamera.Active, InspectorFreeCamera.Toggle);
            bx += 6;
            Tool(IconHistory, InspectorHistory.Count > 0 ? "History " + InspectorHistory.Count : "History", "History of edits", _showHistory, () => _showHistory = !_showHistory);
            Tool(IconRefresh, "Refresh", "Rebuild the tree and reread the members", false, () => { _dirty = true; _members = null; });
            if (x + w - bx < 140)
            {
                bx = x;
                y += row + 6;
            }
            var searchRect = new Rect(bx, y, x + w - bx, row);
            _search = GUI.TextField(searchRect, _search ?? "", s.TextField);
            Underline(searchRect);
            if (string.IsNullOrEmpty(_search))
            {
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y, searchRect.width - 6, row), "Search", s.MutedLabel);
            }
            y += row + 6;

            // The breadcrumb: the selection's path, each ancestor a button.
            if (SelectedObject != null)
            {
                float cx = x;
                var chain = new List<Transform>();
                for (Transform p = SelectedObject.transform; p != null; p = p.parent) chain.Add(p);
                chain.Reverse();
                GUI.Label(new Rect(cx, y, w, row), "", _mutedCell);
                for (int i = 0; i < chain.Count; i++)
                {
                    bool last = i == chain.Count - 1;
                    string name = Drawable(chain[i].name);
                    float cw = Mathf.Min((last ? _accentCell : _mutedCell).CalcSize(new GUIContent(name)).x + 6, w * 0.5f);
                    if (cx + cw + 16 > x + w)
                    {
                        GUI.Label(new Rect(cx, y, x + w - cx, row), "...", _mutedCell);
                        break;
                    }
                    if (GUI.Button(new Rect(cx, y, cw, row), name, last ? _accentCell : _mutedCell) && !last)
                    {
                        Select(chain[i].gameObject);
                    }
                    cx += cw;
                    if (!last)
                    {
                        GUI.Label(new Rect(cx, y, 14, row), "/", _mutedCell);
                        cx += 14;
                    }
                }
            }
            else
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_status), s.MutedLabel);
            }
            y += row + 2;
            if (InspectorFreeCamera.Active)
            {
                GUI.Label(new Rect(x, y, w, row), InspectorFreeCamera.Status(), _accentCell);
                y += row;
            }

            if (_dirty)
            {
                _tree = InspectorModel.BuildTree(Expanded);
                _dirty = false;
            }
            if (_search != _searched)
            {
                _searched = _search;
                _results = string.IsNullOrEmpty(_search) ? null : InspectorModel.Search(_search);
                _scrollTree = Vector2.zero;
                if (_results != null) _showHierarchy = true;
            }

            float bodyHeight = area.yMax - pad - y;
            if (narrow)
            {
                var pane = new Rect(x, y, w, bodyHeight);
                if (_page == 0 && _showHierarchy) DrawHierarchy(pane, s, row);
                else if (_showHistory) DrawHistory(pane, s, row);
                else DrawMembers(pane, s, row);
            }
            else if (_showHierarchy)
            {
                float gap = 8;
                float w1 = (w - gap) * 0.32f, w2 = (w - gap) - w1;
                DrawHierarchy(new Rect(x, y, w1, bodyHeight), s, row);
                if (_showHistory) DrawHistory(new Rect(x + w1 + gap, y, w2, bodyHeight), s, row);
                else DrawMembers(new Rect(x + w1 + gap, y, w2, bodyHeight), s, row);
            }
            else
            {
                if (_showHistory) DrawHistory(new Rect(x, y, w, bodyHeight), s, row);
                else DrawMembers(new Rect(x, y, w, bodyHeight), s, row);
            }
            DrawMenu(area, s, row);
        }

        private static bool narrowWindow(Rect area)
        {
            return area.width < NarrowWidth;
        }

        private static void ToggleGizmo(InspectorGizmo.GizmoMode m)
        {
            InspectorGizmo.Mode = InspectorGizmo.Mode == m ? InspectorGizmo.GizmoMode.None : m;
        }

        // Toolbar icons, prepared at Install so drawing them uploads nothing;
        // a glyph the font lacks falls back to a word.
        private const string IconHierarchy = "\u2630", IconPick = "\u25CE", IconHighlight = "\u25A3", IconParent = "\u2191",
            IconMove = "\u2725", IconRotate = "\u21BB", IconScale = "\u229E", IconResetTransform = "Reset", IconHistory = "\u25D0", IconCamera = "\u25C9", IconRefresh = "\u21BA";
        private static bool _showHierarchy;

        // ---- the row menu (right click on a row) ------------------------------------------

        private static string MemberId(RowInfo r)
        {
            return r.Key.Substring(ControlPrefix.Length);
        }

        private static string WhereLabel()
        {
            string where = SelectedObject != null ? InspectorModel.PathOf(SelectedObject.transform) : "";
            string what = _target is Material m ? "Material " + m.name : _target is GameObject ? "GameObject" : _target?.GetType().Name ?? "";
            return string.IsNullOrEmpty(where) ? what : where + " : " + what;
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
                        items.Add(new KeyValuePair<string, Action>($"{cname} back to previous:  {InspectorModel.Fmt(pv[ci])}", () =>
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
                items.Add(new KeyValuePair<string, Action>("Back to previous:  " + InspectorModel.Format(previous), () => { TrySet(r, previous); Drafts.Remove(r.Key); }));
            }
            items.Add(new KeyValuePair<string, Action>("Copy value", () => GUIUtility.systemCopyBuffer = InspectorModel.Format(now)));
            items.Add(new KeyValuePair<string, Action>("Copy name", () => GUIUtility.systemCopyBuffer = r.Member.Name));
            if (InspectorHistory.For(_target, member).Count > 0)
            {
                items.Add(new KeyValuePair<string, Action>("Show in History", () => _showHistory = true));
            }
            float lineH = row - 4;
            float width = 200;
            foreach (KeyValuePair<string, Action> item in items)
            {
                width = Mathf.Max(width, s.Button.CalcSize(new GUIContent(Drawable(item.Key))).x + 16);
            }
            width = Mathf.Min(width, area.width - 8);
            var box = new Rect(Mathf.Clamp(_menuAt.x, area.x, area.xMax - width), Mathf.Clamp(_menuAt.y, area.y, area.yMax - items.Count * lineH - 8), width, items.Count * lineH + 8);
            // A click outside closes it; a click inside is handled by the buttons.
            if (ev.type == EventType.MouseDown && !box.Contains(ev.mousePosition))
            {
                _menuRow = null;
                return;
            }
            TW.Fill(box, TW.PanelColor);
            TW.Fill(new Rect(box.x, box.y, 3, box.height), TW.AccentColor);
            float y = box.y + 4;
            foreach (KeyValuePair<string, Action> item in items)
            {
                var line = new Rect(box.x + 6, y, box.width - 12, lineH);
                if (item.Value == null)
                {
                    GUI.Label(line, Drawable(item.Key), _mutedCell);
                }
                else if (GUI.Button(line, Drawable(item.Key), _cell))
                {
                    item.Value();
                    _menuRow = null;
                }
                y += lineH;
            }
        }

        // ---- the History view ----------------------------------------------------------------

        private static void DrawHistory(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            GUI.Label(new Rect(x, y, w, row), $"History: {InspectorHistory.Count} edit(s) this session, newest first. Nothing is saved.", _mutedCell);
            y += row;
            float bx = x;
            if (GUI.Button(new Rect(bx, y, 100, row), "Undo last", s.Button))
            {
                _status = InspectorHistory.Undo();
            }
            bx += 108;
            if (GUI.Button(new Rect(bx, y, 70, row), "Clear", s.Button))
            {
                InspectorHistory.Clear();
            }
            bx += 78;
            if (GUI.Button(new Rect(bx, y, 120, row), "< Members", s.Button))
            {
                _showHistory = false;
            }
            y += row + 4;
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            IReadOnlyList<InspectorHistory.Entry> all = InspectorHistory.All;
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollHistory);
            _scrollHistory = GUI.BeginScrollView(view, _scrollHistory, new Rect(0, 0, inner, Mathf.Max(view.height, all.Count * lineH)), false, false);
            float ry = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                InspectorHistory.Entry e = all[i];
                if (ry + lineH >= _scrollHistory.y && ry <= _scrollHistory.y + view.height)
                {
                    GUIStyle nameStyle = e.Reverted ? _mutedCell : _cell;
                    GUI.Label(new Rect(0, ry, inner - 160, row), Drawable($"{e.Time:HH:mm:ss}  {e.Member}" + (e.Reverted ? "  (reverted)" : "")), nameStyle);
                    GUI.Label(new Rect(0, ry + row, inner - 160, row), Drawable($"{e.Label}:  {InspectorModel.Format(e.Before)}  ->  {InspectorModel.Format(e.After)}"), _mutedCell);
                    if (GUI.Button(new Rect(inner - 154, ry + 2, 72, row - 4), e.Reverted ? "Redo" : "Revert", s.Button))
                    {
                        bool redo = e.Reverted;
                        string problem = redo ? InspectorHistory.Reapply(e) : InspectorHistory.Revert(e);
                        _status = problem != null
                            ? $"{(redo ? "Redo" : "Revert")} of {e.Member} failed: {problem}"
                            : $"{(redo ? "Reapplied" : "Reverted")} {e.Member} = {InspectorModel.Format(redo ? e.After : e.Before)}";
                        InspectorPlugin.Log.LogInfo($"[inspector] {_status}");
                    }
                    if (GUI.Button(new Rect(inner - 76, ry + 2, 72, row - 4), "Copy", s.Button))
                    {
                        GUIUtility.systemCopyBuffer = $"{e.Label} {e.Member} = {InspectorModel.Format(e.After)}";
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }

        // The tree, or the search results, one row per object.
        private static void DrawHierarchy(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            List<Node> nodes = _results ?? _tree ?? new List<Node>();
            float inner = pane.width - 20;
            // Follow the selection: scroll so its row sits in the upper third of the pane.
            if (_revealSelection && !ReferenceEquals(_object, null) && _object)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Transform != null && nodes[i].Transform && ReferenceEquals(nodes[i].Transform.gameObject, _object))
                    {
                        float target = i * row - pane.height / 3f;
                        _scrollTree.y = Mathf.Clamp(target, 0, Mathf.Max(0, nodes.Count * row - pane.height));
                        _revealSelection = false;
                        break;
                    }
                }
            }
            TW.ApplyScroll(pane, ref _scrollTree);
            _scrollTree = GUI.BeginScrollView(pane, _scrollTree, new Rect(0, 0, inner, Mathf.Max(pane.height, nodes.Count * row)), false, false);
            float ry = 0;
            foreach (Node n in nodes)
            {
                if (ry + row >= _scrollTree.y && ry <= _scrollTree.y + pane.height)
                {
                    if (n.Transform == null)
                    {
                        GUI.Label(new Rect(4, ry, inner - 4, row), Drawable(n.Name.ToUpperInvariant()), _mutedCell);
                    }
                    else if (n.Transform)
                    {
                        const float step = 18;
                        float indent = 4 + n.Depth * step;
                        // Indent guides: one faint line per ancestor level, through
                        // the middle of that level's toggle, and a short tick to this
                        // row that stops before the row's own toggle.
                        var guide = new Color(TW.MutedColor.r, TW.MutedColor.g, TW.MutedColor.b, 0.35f);
                        for (int d = 1; d < n.Depth; d++)
                        {
                            TW.Fill(new Rect(4 + d * step + 9, ry, 1, row), guide);
                        }
                        if (n.Depth > 1)
                        {
                            TW.Fill(new Rect(4 + (n.Depth - 1) * step + 9, ry + row / 2, step - 12, 1), guide);
                        }
                        if (_results == null && n.HasChildren)
                        {
                            int id = n.Transform.GetInstanceID();
                            bool open = Expanded.Contains(id);
                            if (GUI.Button(new Rect(indent, ry, 18, row), open ? "-" : "+", _toggleStyle ?? (_toggleStyle = new GUIStyle(s.MutedLabel) { alignment = TextAnchor.MiddleCenter })))
                            {
                                if (open) Expanded.Remove(id); else Expanded.Add(id);
                                _dirty = true;
                            }
                        }
                        bool selected = !ReferenceEquals(_object, null) && ReferenceEquals(_object, n.Transform.gameObject);
                        var label = new Rect(indent + 20, ry, inner - indent - 20, row);
                        if (selected)
                        {
                            TW.Fill(new Rect(0, ry, inner, row), TW.PanelColor);
                        }
                        string text = _results != null ? n.Path : n.Name;
                        if (GUI.Button(label, Drawable(text), selected ? _accentCell : (n.Active ? _cell : _mutedCell)))
                        {
                            SelectObject(n.Transform.gameObject);
                            _page = 1;
                        }
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }

        // The selected object's components (and its renderers' materials) as a
        // strip of buttons above the members; the selected one is highlighted.
        // Returns the height used.
        private static float DrawComponentStrip(Rect pane, ToolWindowStyles s, float row)
        {
            if (ReferenceEquals(_object, null) || !_object)
            {
                GUI.Label(new Rect(pane.x + 8, pane.y + 4, pane.width - 16, row), "Select an object: Pick one in the game, or open the tree.", s.MutedLabel);
                return row + 4;
            }
            var entries = new List<KeyValuePair<string, object>>();
            entries.Add(new KeyValuePair<string, object>("GameObject", _object));
            foreach (Component c in _object.GetComponents<Component>())
            {
                entries.Add(new KeyValuePair<string, object>(c == null ? "(missing script)" : c.GetType().Name, c));
            }
            foreach (Renderer r in _object.GetComponents<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null)
                    {
                        entries.Add(new KeyValuePair<string, object>("Material: " + m.name, m));
                    }
                }
            }
            float x = pane.x + 4, y = pane.y + 4, w = pane.width - 8;
            float bx = x;
            float lineH = row - 4;
            foreach (KeyValuePair<string, object> e in entries)
            {
                bool selected = ReferenceEquals(e.Value, _target);
                bool enabled = !(e.Value is Behaviour b) || b.enabled;
                string label = Drawable(e.Key);
                float bw = Mathf.Min(w, s.Button.CalcSize(new GUIContent(label)).x + 14);
                if (bx + bw > x + w && bx > x)
                {
                    bx = x;
                    y += lineH + 4;
                }
                GUIStyle style = selected ? s.SelectedButton : s.Button;
                if (GUI.Button(new Rect(bx, y, bw, lineH), label, style) && e.Value != null)
                {
                    SetTarget(e.Value);
                    _page = 1;
                }
                if (!enabled && !selected)
                {
                    TW.Fill(new Rect(bx, y + lineH - 2, bw, 2), TW.MutedColor);
                }
                bx += bw + 4;
            }
            y += lineH + 4;
            GUI.Label(new Rect(x, y, w, row), $"{(_object.activeInHierarchy ? "active" : "inactive")}   tag {_object.tag}   layer {LayerMask.LayerToName(_object.layer)}", _mutedCell);
            y += row;
            return y - pane.y;
        }

        // The selected component's or material's members, one row each.
        private static void DrawMembers(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float used = DrawComponentStrip(pane, s, row);
            if (_target == null)
            {
                return;
            }
            float x = pane.x + 4, y = pane.y + used + 2, w = pane.width - 8;
            // Header: what is selected, the toggles.
            string heading = _target is Material mat
                ? $"{mat.name}  ({mat.shader?.name})"
                : _target is GameObject go ? go.name + "  (GameObject)" : $"{_target.GetType().Name}";
            GUI.Label(new Rect(x, y, w, row), Drawable(heading), _cell);
            y += row;
            if (_target is Material m2)
            {
                if (_rendererUsers < 0)
                {
                    _rendererUsers = InspectorModel.RendererCount(m2);
                }
                GUI.Label(new Rect(x, y, w, row), $"Shared by {_rendererUsers} renderer(s); a change shows on all of them.", _mutedCell);
                y += row;
            }
            else if (!(_target is GameObject))
            {
                float bx = x;
                if (GUI.Button(new Rect(bx, y, 120, row), "Show private", _showPrivate ? s.SelectedButton : s.Button))
                {
                    _showPrivate = !_showPrivate;
                    if (_showPrivate) _status = "Private members: setting them is the mod author's own risk.";
                }
                bx += 128;
                if (GUI.Button(new Rect(bx, y, 80, row), "Freeze", _freeze ? s.SelectedButton : s.Button))
                {
                    _freeze = !_freeze;
                    Frozen.Clear();
                }
                bx += 88;
                if (_target is Behaviour beh)
                {
                    if (GUI.Button(new Rect(bx, y, 90, row), beh.enabled ? "Enabled" : "Disabled", beh.enabled ? s.SelectedButton : s.Button))
                    {
                        beh.enabled = !beh.enabled;
                    }
                }
                y += row + 4;
            }

            // The rows. Values are read on Repaint only, and kept while frozen.
            List<Member> members = MembersOfTarget();
            bool compactTransform = _target is Transform && !_allMembers;
            if (_target is Transform)
            {
                if (GUI.Button(new Rect(x, y, 130, row), _allMembers ? "Fewer members" : "All members", _allMembers ? s.SelectedButton : s.Button))
                {
                    _allMembers = !_allMembers;
                }
                y += row + 4;
            }
            Rows.Clear();
            foreach (Member m in members)
            {
                if (m.IsPrivate && !_showPrivate)
                {
                    continue;
                }
                if (compactTransform && Array.IndexOf(CompactTransformMembers, m.Name) < 0)
                {
                    continue;
                }
                object tgt = _target;
                Rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name, Getter = () => m.Get(tgt), Setter = m.CanWrite ? (Action<object>)(v => m.Set(tgt, v)) : null, Label = m.Name, Type = m.Type });
                if (InspectorModel.IsList(m.Type) && ExpandedLists.Contains(m.Name))
                {
                    object listValue = SafeGet(m, () => m.Get(tgt));
                    if (listValue is IList list)
                    {
                        Type element = m.Type.IsArray ? m.Type.GetElementType() : (m.Type.IsGenericType ? m.Type.GetGenericArguments()[0] : typeof(object));
                        int count = Mathf.Min(list.Count, 200);
                        for (int i = 0; i < count; i++)
                        {
                            int index = i;
                            Rows.Add(new RowInfo
                            {
                                Member = m, Key = ControlPrefix + m.Name + "[" + i + "]", Label = $"    [{i}]", Type = element,
                                Getter = () => list[index], Setter = list.IsReadOnly ? null : (Action<object>)(v => list[index] = v),
                            });
                        }
                        if (list.Count > count)
                        {
                            Rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name + "[more]", Label = $"    ... {list.Count - count} more", Type = typeof(string), Getter = () => "" });
                        }
                    }
                }
            }
            // The colour picker sits at the bottom of the pane while it is open.
            float pickerHeight = InspectorColorPicker.Open ? InspectorColorPicker.Height + 4 : 0;
            var view = new Rect(x, y, w, pane.yMax - y - 2 - pickerHeight);
            float inner = view.width - 20;
            float nameWidth = Mathf.Clamp(inner * 0.34f, 110, 300);
            float total = 0;
            foreach (RowInfo r in Rows)
            {
                total += RowHeight(r, inner, nameWidth, row);
            }
            TW.ApplyScroll(view, ref _scrollMembers);
            _scrollMembers = GUI.BeginScrollView(view, _scrollMembers, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            foreach (RowInfo r in Rows)
            {
                float h = RowHeight(r, inner, nameWidth, row);
                if (ry + h >= _scrollMembers.y && ry <= _scrollMembers.y + view.height)
                {
                    DrawRow(r, new Rect(0, ry, inner, row), nameWidth, s, row);
                }
                ry += h;
            }
            GUI.EndScrollView();
            if (InspectorColorPicker.Open)
            {
                InspectorColorPicker.Draw(new Rect(x, view.yMax + 4, w, InspectorColorPicker.Height), s);
            }
        }

        // Fields narrower than this are unreadable; the composite then goes on
        // a second line across the whole width.
        private const float MinFieldWidth = 68f;

        private static bool Stacked(RowInfo r, float inner, float nameWidth)
        {
            if (!InspectorModel.IsComposite(r.Type) || r.Setter == null)
            {
                return false;
            }
            int n = InspectorModel.ComponentLabels(r.Type).Length;
            bool isColor = r.Type == typeof(Color) || r.Type == typeof(Color32);
            float extras = 8 + (isColor ? TW.RowHeight : 0) + (InspectorHistory.TryOriginal(_target, MemberId(r), out _) ? 60 : 0);
            return (inner - nameWidth - extras - n * 18) / n < MinFieldWidth;
        }

        private static float RowHeight(RowInfo r, float inner, float nameWidth, float row)
        {
            float h = Stacked(r, inner, nameWidth) ? row * 2 : row;
            return RowErrors.ContainsKey(r.Key) ? h + row : h;
        }

        private static readonly string[] CompactTransformMembers = { "localPosition", "localEulerAngles", "localScale", "position", "eulerAngles", "parent" };

        // Press-and-drag on a number field. Returns true when the value should
        // change to `next` this event. The press itself is not used, so a plain
        // click still focuses the field for typing; the drag takes over once the
        // pointer has moved four pixels up or down.
        private static bool DragNumber(RowInfo r, string key, Rect field, float current, out float next)
        {
            next = current;
            Event ev = Event.current;
            int control = GUIUtility.GetControlID(key.GetHashCode(), FocusType.Passive);
            if (ev.type == EventType.MouseDown && ev.button == 0 && field.Contains(ev.mousePosition))
            {
                _pressKey = key;
                _pressMouse = ev.mousePosition;
                _pressValue = current;
                _numberDragging = false;
                return false;
            }
            if (_pressKey != key)
            {
                return false;
            }
            if (ev.type == EventType.MouseDrag)
            {
                if (!_numberDragging)
                {
                    if (Mathf.Abs(ev.mousePosition.y - _pressMouse.y) < 4f)
                    {
                        return false;
                    }
                    _numberDragging = true;
                    _dragRow = r;
                    _dragBefore = SafeGet(r.Member, r.Getter);
                    GUIUtility.hotControl = control;
                    GUIUtility.keyboardControl = 0;
                    Drafts.Remove(key);
                }
                // Up increases. A pixel moves the value by a hundredth of its size, at least 0.01.
                float step = Mathf.Max(0.01f, Mathf.Abs(_pressValue) * 0.01f);
                next = _pressValue + (_pressMouse.y - ev.mousePosition.y) * step;
                ev.Use();
                return true;
            }
            if (ev.type == EventType.MouseUp)
            {
                if (_numberDragging)
                {
                    if (GUIUtility.hotControl == control)
                    {
                        GUIUtility.hotControl = 0;
                    }
                    object after = SafeGet(r.Member, r.Getter);
                    if (_dragBefore != null && after != null && !Equals(_dragBefore, after))
                    {
                        InspectorHistory.Record(_target, WhereLabel(), MemberId(r), _dragBefore, after, r.Getter, r.Setter);
                    }
                    ev.Use();
                }
                _pressKey = null;
                _numberDragging = false;
                _dragRow = null;
            }
            return false;
        }

        // Sets without a history entry: the drag records once, at its end.
        private static bool SetRaw(RowInfo r, object value)
        {
            try
            {
                r.Setter(value);
                RowErrors.Remove(r.Key);
                Frozen.Remove(r.Key);
                return true;
            }
            catch (Exception ex)
            {
                RowErrors[r.Key] = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private sealed class RowInfo
        {
            public Member Member;
            public string Key;
            public string Label;
            public Type Type;
            public Func<object> Getter;
            public Action<object> Setter;
        }

        private static object SafeGet(Member m, Func<object> get)
        {
            try
            {
                object v = get();
                m.Failure = null;
                return v;
            }
            catch (Exception ex)
            {
                m.Failure = (ex.InnerException ?? ex).Message;
                return null;
            }
        }

        private static void DrawRow(RowInfo r, Rect rect, float nameWidth, ToolWindowStyles s, float row)
        {
            Event ev = Event.current;
            string label = r.Label + (r.Member.IsPrivate && r.Label == r.Member.Name ? "  (private)" : "");
            GUI.Label(new Rect(rect.x, rect.y, nameWidth - 6, row), Drawable(label), r.Member.IsPrivate ? _mutedCell : _cell);
            var valueRect = new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth, row);

            // The value: frozen text, or read now.
            string key = r.Key;
            object value;
            bool haveValue;
            bool frozen = _freeze && Frozen.ContainsKey(key);
            if (frozen)
            {
                value = null;
                haveValue = false;
            }
            else
            {
                value = SafeGet(r.Member, r.Getter);
                haveValue = r.Member.Failure == null;
                if (_freeze && haveValue)
                {
                    Frozen[key] = InspectorModel.Format(value);
                }
            }
            string shown = r.Member.Failure != null ? "error: " + r.Member.Failure : frozen ? Frozen[key] : InspectorModel.Format(value);
            Type t = r.Type;
            bool editable = r.Setter != null && haveValue && InspectorModel.IsEditableType(t);

            // Reset, when the row was edited: the value it held before the first edit.
            float right = valueRect.xMax;
            if (InspectorHistory.TryOriginal(_target, MemberId(r), out object original))
            {
                if (GUI.Button(new Rect(right - 54, valueRect.y + 2, 54, row - 4), "Reset", s.Button))
                {
                    // A picker open on this row records what it changed so far and
                    // stays open; the reset colour becomes its new baseline.
                    if (InspectorColorPicker.Open && InspectorColorPicker.Key == key)
                    {
                        InspectorColorPicker.CommitPending();
                    }
                    if (TrySet(r, original))
                    {
                        InspectorHistory.ForgetOriginal(_target, MemberId(r));
                        Drafts.Remove(key);
                    }
                }
                right -= 60;
            }
            var control = new Rect(valueRect.x, valueRect.y, right - valueRect.x, row);

            if (r.Member.Failure != null)
            {
                GUI.Label(control, Drawable(shown), _errorCell);
            }
            else if (frozen)
            {
                GUI.Label(control, Drawable(shown), _mutedCell);
            }
            else if (t == typeof(bool) && editable)
            {
                bool b = value is bool bb && bb;
                if (GUI.Button(new Rect(control.x, control.y + 2, 80, row - 4), b ? "true" : "false", b ? s.SelectedButton : s.Button))
                {
                    TrySet(r, !b);
                }
            }
            else if (t != null && t.IsEnum && editable)
            {
                if (GUI.Button(new Rect(control.x, control.y + 2, Mathf.Min(control.width, 220), row - 4), Drawable(shown) + "  >", s.Button))
                {
                    TrySet(r, InspectorModel.NextEnum(t, value));
                }
            }
            else if (editable && InspectorModel.IsComposite(t))
            {
                if (Stacked(r, rect.width, nameWidth))
                {
                    // Second line, full width, under the name.
                    DrawComposite(r, new Rect(rect.x + 12, rect.y + row, right - rect.x - 12, row), value, s, row);
                }
                else
                {
                    DrawComposite(r, control, value, s, row);
                }
            }
            else if (editable)
            {
                // A text field with a draft while it has focus; Enter or leaving the field applies.
                float fieldWidth = control.width;
                var fieldRect = new Rect(control.x, control.y, Mathf.Max(60, fieldWidth), row);
                if (InspectorModel.IsNumber(t) && value != null && DragNumber(r, key, fieldRect, Convert.ToSingle(value, CultureInfo.InvariantCulture), out float dragged))
                {
                    bool whole = t != typeof(float) && t != typeof(double) && t != typeof(decimal);
                    object v = InspectorModel.Parse(t, (whole ? Mathf.Round(dragged) : dragged).ToString(whole ? "0" : "0.####", CultureInfo.InvariantCulture), out string err);
                    if (err == null && SetRaw(r, v))
                    {
                        value = v;
                        shown = InspectorModel.Format(v);
                    }
                }
                string text = FieldText(key, shown, ev);
                GUI.SetNextControlName(key);
                string after = GUI.TextField(fieldRect, text ?? "", s.TextField);
                if (_focusedControl == key && after != text)
                {
                    Drafts[key] = after;
                }
                Underline(fieldRect);
            }
            else if (value is UnityEngine.Object uo && uo)
            {
                GUI.Label(new Rect(control.x, control.y, control.width - 50, row), Drawable(shown), _mutedCell);
                bool canGo = uo is GameObject || uo is Component || uo is Material || uo is Texture;
                if (canGo && GUI.Button(new Rect(control.xMax - 44, control.y + 2, 44, row - 4), "Go", s.Button))
                {
                    if (uo is Texture)
                    {
                        // The Assets library lists it, when it is loaded; reached by
                        // reflection so the Inspector does not depend on it.
                        Type catalog = Type.GetType("DragNWash.ModFramework.Assets.AssetCatalog, DragNWash.ModFramework.Assets", false);
                        System.Reflection.MethodInfo show = catalog?.GetMethod("ShowInToolWindow", new[] { typeof(string) });
                        if (show != null)
                        {
                            show.Invoke(null, new object[] { uo.name });
                        }
                        else
                        {
                            _status = $"Texture {uo.name}: the Assets library is not loaded.";
                        }
                    }
                    else
                    {
                        Select(uo);
                    }
                }
            }
            else if (t != null && InspectorModel.IsList(t) && haveValue && value != null && r.Label == r.Member.Name)
            {
                bool open = ExpandedLists.Contains(r.Member.Name);
                GUI.Label(new Rect(control.x, control.y, control.width - 80, row), Drawable(shown), _mutedCell);
                if (GUI.Button(new Rect(control.xMax - 74, control.y + 2, 74, row - 4), open ? "Collapse" : "Expand", s.Button))
                {
                    if (open) ExpandedLists.Remove(r.Member.Name); else ExpandedLists.Add(r.Member.Name);
                }
            }
            else
            {
                GUI.Label(control, Drawable(shown), _mutedCell);
            }

            if (RowErrors.TryGetValue(key, out string error))
            {
                float errorY = rect.y + (Stacked(r, rect.width, nameWidth) ? row * 2 : row);
                GUI.Label(new Rect(rect.x + nameWidth, errorY, rect.width - nameWidth, row), Drawable(error), _errorCell);
            }
            // Right click anywhere else on the row: the row's menu. A component
            // field's own right click was used above, so it is not overridden here.
            float rowHeight = Stacked(r, rect.width, nameWidth) ? row * 2 : row;
            if (ev.type == EventType.MouseDown && ev.button == 1 && new Rect(rect.x, rect.y, rect.width, rowHeight).Contains(ev.mousePosition))
            {
                _menuRow = r;
                _menuComponent = -1;
                _menuAt = GUIUtility.GUIToScreenPoint(ev.mousePosition) - _tabScreenOrigin;
                ev.Use();
            }
        }

        // One small field per component (x, y, z ...), and for colours a swatch
        // that opens the picker.
        private static void DrawComposite(RowInfo r, Rect control, object value, ToolWindowStyles s, float row)
        {
            Event ev = Event.current;
            Type t = r.Type;
            bool isColor = t == typeof(Color) || t == typeof(Color32);
            string[] labels = InspectorModel.ComponentLabels(t);
            float[] parts = InspectorModel.Components(value);
            float swatch = isColor ? row : 0;
            float setWidth = 0;
            float labelWidth = 14;
            float available = control.width - swatch - setWidth - 8 - labels.Length * (labelWidth + 4);
            float fieldWidth = Mathf.Max(36, available / labels.Length);
            float bx = control.x;
            for (int i = 0; i < labels.Length; i++)
            {
                string key = r.Key + "#" + i;
                GUI.Label(new Rect(bx, control.y, labelWidth, row), labels[i], _mutedCell);
                bx += labelWidth;
                var fieldRect = new Rect(bx, control.y, fieldWidth, row);
                if (ev.type == EventType.MouseDown && ev.button == 1 && fieldRect.Contains(ev.mousePosition))
                {
                    _menuRow = r;
                    _menuComponent = i;
                    _menuAt = GUIUtility.GUIToScreenPoint(ev.mousePosition) - _tabScreenOrigin;
                    ev.Use();
                }
                if (i < parts.Length && DragNumber(r, key, fieldRect, parts[i], out float dragged))
                {
                    parts[i] = dragged;
                    object composed = InspectorModel.Compose(t, parts);
                    if (composed != null && SetRaw(r, composed))
                    {
                        value = composed;
                    }
                }
                string shown = i < parts.Length ? Fmt(t, parts[i]) : "";
                string text = FieldText(key, shown, ev);
                GUI.SetNextControlName(key);
                string after = GUI.TextField(fieldRect, text ?? "", s.TextField);
                if (_focusedControl == key && after != text)
                {
                    Drafts[key] = after;
                }
                Underline(fieldRect);
                bx += fieldWidth + 4;
            }
            if (isColor)
            {
                Color c = value is Color cc ? cc : value is Color32 c32 ? (Color)c32 : Color.clear;
                var swatchRect = new Rect(bx + 2, control.y + 4, row - 8, row - 8);
                TW.Fill(swatchRect, c);
                if (GUI.Button(swatchRect, "", s.MutedLabel))
                {
                    if (InspectorColorPicker.Open && InspectorColorPicker.Key == r.Key)
                    {
                        InspectorColorPicker.Close();
                    }
                    else
                    {
                        RowInfo row2 = r;
                        bool as32 = t == typeof(Color32);
                        InspectorColorPicker.Show(r.Key, c,
                            picked => SetRaw(row2, as32 ? (object)(Color32)picked : picked),
                            (before, after) => InspectorHistory.Record(_target, WhereLabel(), MemberId(row2),
                                as32 ? (object)(Color32)before : before, as32 ? (object)(Color32)after : after, row2.Getter, row2.Setter));
                    }
                }
                if (InspectorColorPicker.Open && InspectorColorPicker.Key == r.Key)
                {
                    InspectorColorPicker.Sync(c);
                }
                bx += row;
            }
        }

        private static string Fmt(Type t, float f)
        {
            if (t == typeof(Vector2Int) || t == typeof(Vector3Int) || t == typeof(Color32))
            {
                return ((int)f).ToString(CultureInfo.InvariantCulture);
            }
            return InspectorModel.Fmt(f);
        }

        // The text a field shows: its draft while focused, else the live value
        // (and a stale draft is dropped on Repaint).
        private static string FieldText(string key, string shown, Event ev)
        {
            if (_focusedControl == key)
            {
                if (!Drafts.TryGetValue(key, out string text))
                {
                    text = shown;
                    Drafts[key] = text;
                    DraftStart[key] = text;
                }
                return text;
            }
            if (Drafts.ContainsKey(key) && ev.type == EventType.Repaint)
            {
                // Focus left the field: a draft the person typed in is applied, then dropped.
                bool typed = !DraftStart.TryGetValue(key, out string start) || Drafts[key] != start;
                if (typed && !_numberDragging)
                {
                    Apply(RowKeyOf(key));
                }
                Drafts.Remove(key);
                DraftStart.Remove(key);
            }
            return shown;
        }

        private static string RowKeyOf(string control)
        {
            int hash = control.IndexOf('#');
            return hash < 0 ? control : control.Substring(0, hash);
        }

        // Applies a row's draft(s): the single field, or the composite's
        // components, drafts where they exist and the live value elsewhere.
        private static void Apply(string rowKey)
        {
            RowInfo r = Rows.Find(x => x.Key == rowKey);
            if (r == null || r.Setter == null)
            {
                return;
            }
            object parsed;
            string error = null;
            if (InspectorModel.IsComposite(r.Type))
            {
                object current = SafeGet(r.Member, r.Getter);
                if (r.Member.Failure != null)
                {
                    return;
                }
                float[] parts = InspectorModel.Components(current);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (Drafts.TryGetValue(rowKey + "#" + i, out string draft))
                    {
                        if (!float.TryParse(draft.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        {
                            error = $"{InspectorModel.ComponentLabels(r.Type)[i]}: not a number";
                            break;
                        }
                        parts[i] = f;
                    }
                }
                parsed = error == null ? InspectorModel.Compose(r.Type, parts) : null;
            }
            else
            {
                if (!Drafts.TryGetValue(rowKey, out string text))
                {
                    return;
                }
                parsed = InspectorModel.Parse(r.Type, text, out error);
            }
            if (error != null)
            {
                RowErrors[rowKey] = "Not accepted: " + error;
                return;
            }
            if (TrySet(r, parsed))
            {
                Drafts.Remove(rowKey);
                for (int i = 0; i < 6; i++)
                {
                    Drafts.Remove(rowKey + "#" + i);
                }
            }
        }

        private static bool TrySet(RowInfo r, object value)
        {
            try
            {
                object before = SafeGet(r.Member, r.Getter);
                bool haveBefore = r.Member.Failure == null;
                r.Setter(value);
                if (haveBefore)
                {
                    object target = _target;
                    Func<object> get = r.Getter;
                    Action<object> set = r.Setter;
                    InspectorHistory.Record(target, WhereLabel(), MemberId(r), before, value, get, set);
                }
                RowErrors.Remove(r.Key);
                Frozen.Remove(r.Key);
                return true;
            }
            catch (Exception ex)
            {
                RowErrors[r.Key] = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        // ---- console -------------------------------------------------------------------

        private static string Command(string[] args)
        {
            if (args.Length == 0)
            {
                var sb = new StringBuilder();
                foreach (Node n in InspectorModel.BuildTree(new HashSet<int>()))
                {
                    sb.Append(n.Transform == null ? n.Name.ToUpperInvariant() : "  " + n.Name + (n.HasChildren ? "/" : "")).Append('\n');
                }
                return sb.ToString().TrimEnd();
            }
            if (args[0].Equals("pick", StringComparison.OrdinalIgnoreCase))
            {
                TW.Open(Title);
                InspectorPick.Begin();
                return "Click an object in the game; Escape cancels.";
            }
            if (args[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 5)
                {
                    return "inspect set <path> <component> <member> <value>";
                }
                GameObject go = InspectorModel.Find(args[1]);
                if (go == null) return $"No object at \"{args[1]}\".";
                object target = FindComponent(go, args[2]);
                if (target == null) return $"{go.name} has no component \"{args[2]}\".";
                Member member = null;
                foreach (Member m in target is Material mm ? InspectorModel.MaterialMembers(mm) : InspectorModel.MembersOf(target.GetType()))
                {
                    if (m.Name.Equals(args[3], StringComparison.OrdinalIgnoreCase)) { member = m; break; }
                }
                if (member == null) return $"{args[2]} has no member \"{args[3]}\".";
                if (!member.CanWrite) return $"{args[3]} is read-only.";
                string valueText = string.Join(" ", args, 4, args.Length - 4);
                object parsed = InspectorModel.Parse(member.Type, valueText, out string error);
                if (error != null) return $"Not accepted: {error}.";
                try
                {
                    member.Set(target, parsed);
                }
                catch (Exception ex)
                {
                    return $"{args[3]}: {(ex.InnerException ?? ex).Message}";
                }
                return $"{InspectorModel.PathOf(go.transform)} {args[2]}.{member.Name} = {InspectorModel.Format(member.Get(target))}";
            }
            GameObject found = InspectorModel.Find(args[0]);
            if (found == null)
            {
                return $"No object named or at \"{args[0]}\".";
            }
            SelectObject(found);
            _page = 1;
            if (args.Length > 1)
            {
                object c = FindComponent(found, args[1]);
                if (c == null) return $"{found.name} has no component \"{args[1]}\".";
                SetTarget(c);
                _page = 2;
                TW.Open(Title);
                var lines = new StringBuilder();
                foreach (Member m in MembersOfTarget())
                {
                    if (m.IsPrivate) continue;
                    lines.Append(m.Name).Append(" = ").Append(InspectorModel.Format(SafeGet(m, () => m.Get(_target)))).Append('\n');
                }
                return lines.ToString().TrimEnd();
            }
            TW.Open(Title);
            var names = new List<string>();
            foreach (Component c in found.GetComponents<Component>())
            {
                names.Add(c == null ? "(missing)" : c.GetType().Name);
            }
            return $"{InspectorModel.PathOf(found.transform)}: {string.Join(", ", names)}";
        }

        private static object FindComponent(GameObject go, string name)
        {
            if (name.Equals("GameObject", StringComparison.OrdinalIgnoreCase))
            {
                return go;
            }
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            foreach (Renderer r in go.GetComponents<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> Complete(string[] args)
        {
            if (args.Length == 1)
            {
                var names = new List<string> { "set", "pick" };
                string partial = args[0];
                if (partial.Length >= 2)
                {
                    foreach (Node n in InspectorModel.Search(partial, 30))
                    {
                        names.Add(n.Path.IndexOf(' ') >= 0 ? "\"" + n.Path + "\"" : n.Path);
                    }
                }
                return names;
            }
            bool set = args[0].Equals("set", StringComparison.OrdinalIgnoreCase);
            int pathIndex = set ? 1 : 0;
            if (args.Length == pathIndex + 1 && set)
            {
                var names = new List<string>();
                if (args[1].Length >= 2)
                {
                    foreach (Node n in InspectorModel.Search(args[1], 30))
                    {
                        names.Add(n.Path.IndexOf(' ') >= 0 ? "\"" + n.Path + "\"" : n.Path);
                    }
                }
                return names;
            }
            if (args.Length == pathIndex + 2)
            {
                GameObject go = InspectorModel.Find(args[pathIndex]);
                var names = new List<string>();
                if (go != null)
                {
                    names.Add("GameObject");
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c != null) names.Add(c.GetType().Name);
                    }
                }
                return names;
            }
            if (set && args.Length == 4)
            {
                GameObject go = InspectorModel.Find(args[1]);
                object target = go != null ? FindComponent(go, args[2]) : null;
                var names = new List<string>();
                if (target != null)
                {
                    foreach (Member m in target is Material mm ? InspectorModel.MaterialMembers(mm) : InspectorModel.MembersOf(target.GetType()))
                    {
                        if (m.CanWrite && !m.IsPrivate) names.Add(m.Name);
                    }
                }
                return names;
            }
            return new string[0];
        }

        // ---- helpers -------------------------------------------------------------------

        private static string Drawable(string text)
        {
            return TW.Drawable(text ?? "");
        }

        private static void Underline(Rect field)
        {
            Color was = GUI.color;
            GUI.color = TW.AccentColor;
            GUI.DrawTexture(new Rect(field.x, field.yMax - 2, field.width, 2), Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
