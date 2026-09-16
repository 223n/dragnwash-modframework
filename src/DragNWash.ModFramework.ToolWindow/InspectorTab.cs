using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Node = DragNWash.ModFramework.ToolWindow.InspectorModel.Node;
using Member = DragNWash.ModFramework.ToolWindow.InspectorModel.Member;

namespace DragNWash.ModFramework.ToolWindow
{
    // The "Inspector" tab: the scene's objects, a selected object's components,
    // and a selected component's (or material's) members, readable and editable
    // while the game runs. Three panes side by side; in a narrow window, three
    // pages. Nothing is saved: an edit lives until the scene reloads or the
    // game quits. See docs/INSPECTOR.md.
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
        private static readonly Dictionary<string, string> RowErrors = new Dictionary<string, string>();
        private static readonly HashSet<string> ExpandedLists = new HashSet<string>();
        private static string _focusedControl = "";
        private static int _actedFrame = -1;
        private static bool _noteShown;
        private static string _status = "";

        // Narrow window: which pane is the page.
        private static int _page;

        private static GUIStyle _cell, _mutedCell, _errorCell, _accentCell;

        internal static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = ToolWindow.AddTab(ToolWindow.Guid, Title, Draw, 45);
            GameEvents.OnSceneLoaded(ToolWindow.Guid, (scene, mode) => _dirty = true);
            GameEvents.OnSceneUnloaded(ToolWindow.Guid, scene => _dirty = true);
            ToolWindow.AddCommand(ToolWindow.Guid, "inspect",
                "inspect | inspect <name or path> [component] | inspect set <path> <component> <member> <value>",
                Command, Complete);
        }

        // ---- selection (also from ToolWindow.Inspect and the console) --------------

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
            InspectorModel.ExpandTo(go != null ? go.transform : null, Expanded);
            _dirty = true;
            SetTarget(go);
            _scrollComponents = Vector2.zero;
        }

        private static void SetTarget(object target)
        {
            _target = target;
            _members = null;
            _rendererUsers = -1;
            Drafts.Clear();
            RowErrors.Clear();
            Frozen.Clear();
            ExpandedLists.Clear();
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
            _errorCell.normal.textColor = ToolWindow.ErrorColor;
            _errorCell.hover.textColor = ToolWindow.ErrorColor;
            _accentCell = new GUIStyle(_cell);
            _accentCell.normal.textColor = ToolWindow.AccentColor;
            _accentCell.hover.textColor = ToolWindow.AccentColor;
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = ToolWindow.Styles;
            EnsureStyles(s);
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
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
                else if (_focusedControl.StartsWith(ControlPrefix, StringComparison.Ordinal) && Drafts.ContainsKey(_focusedControl))
                {
                    Apply(_focusedControl);
                    _actedFrame = Time.frameCount;
                    ev.Use();
                }
            }

            if (!_noteShown)
            {
                _noteShown = true;
                _status = "Edits are not saved: they last until the scene reloads or the game quits.";
            }

            bool narrow = area.width < NarrowWidth;
            // Top row: search, refresh, and in a narrow window the page's back button.
            float bx = x;
            if (narrow && _page > 0)
            {
                if (GUI.Button(new Rect(bx, y, 70, row), "< Back", s.Button))
                {
                    _page--;
                }
                bx += 78;
            }
            if (GUI.Button(new Rect(bx, y, 80, row), "Refresh", s.Button))
            {
                _dirty = true;
                _members = null;
            }
            bx += 88;
            var searchRect = new Rect(bx, y, Mathf.Max(80, x + w - bx), row);
            _search = GUI.TextField(searchRect, _search ?? "", s.TextField);
            Underline(searchRect);
            if (string.IsNullOrEmpty(_search))
            {
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y, searchRect.width - 6, row), "Search objects by name", s.MutedLabel);
            }
            y += row + 6;
            GUI.Label(new Rect(x, y, w, row), Drawable(_status), s.MutedLabel);
            y += row;

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
            }

            float bodyHeight = area.yMax - pad - y;
            if (narrow)
            {
                var pane = new Rect(x, y, w, bodyHeight);
                switch (_page)
                {
                    case 0: DrawHierarchy(pane, s, row); break;
                    case 1: DrawComponents(pane, s, row); break;
                    default: DrawMembers(pane, s, row); break;
                }
            }
            else
            {
                float gap = 8;
                float w1 = (w - 2 * gap) * 0.30f, w2 = (w - 2 * gap) * 0.25f, w3 = (w - 2 * gap) - w1 - w2;
                DrawHierarchy(new Rect(x, y, w1, bodyHeight), s, row);
                DrawComponents(new Rect(x + w1 + gap, y, w2, bodyHeight), s, row);
                DrawMembers(new Rect(x + w1 + w2 + 2 * gap, y, w3, bodyHeight), s, row);
            }
        }

        // The tree, or the search results, one row per object.
        private static void DrawHierarchy(Rect pane, ToolWindowStyles s, float row)
        {
            ToolWindow.Fill(pane, ToolWindow.InsetColor);
            List<Node> nodes = _results ?? _tree ?? new List<Node>();
            float inner = pane.width - 20;
            ToolWindow.ApplyScroll(pane, ref _scrollTree);
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
                        float indent = 4 + n.Depth * 14;
                        if (_results == null && n.HasChildren)
                        {
                            int id = n.Transform.GetInstanceID();
                            bool open = Expanded.Contains(id);
                            if (GUI.Button(new Rect(indent, ry, 20, row), open ? "-" : "+", s.MutedLabel))
                            {
                                if (open) Expanded.Remove(id); else Expanded.Add(id);
                                _dirty = true;
                            }
                        }
                        bool selected = !ReferenceEquals(_object, null) && ReferenceEquals(_object, n.Transform.gameObject);
                        var label = new Rect(indent + 22, ry, inner - indent - 22, row);
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

        // The selected object's components, and its renderers' materials.
        private static void DrawComponents(Rect pane, ToolWindowStyles s, float row)
        {
            ToolWindow.Fill(pane, ToolWindow.InsetColor);
            if (ReferenceEquals(_object, null) || !_object)
            {
                GUI.Label(new Rect(pane.x + 8, pane.y + 4, pane.width - 16, row), "Select an object.", s.MutedLabel);
                return;
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
            float inner = pane.width - 20;
            float headerRows = 2;
            ToolWindow.ApplyScroll(pane, ref _scrollComponents);
            _scrollComponents = GUI.BeginScrollView(pane, _scrollComponents, new Rect(0, 0, inner, Mathf.Max(pane.height, (entries.Count + headerRows) * row)), false, false);
            GUI.Label(new Rect(4, 0, inner - 4, row), Drawable(InspectorModel.PathOf(_object.transform)), _mutedCell);
            GUI.Label(new Rect(4, row, inner - 4, row), $"{(_object.activeInHierarchy ? "active" : "inactive")}   tag {_object.tag}   layer {LayerMask.LayerToName(_object.layer)}", _mutedCell);
            float ry = headerRows * row;
            foreach (KeyValuePair<string, object> e in entries)
            {
                bool selected = ReferenceEquals(e.Value, _target);
                bool enabled = !(e.Value is Behaviour b) || b.enabled;
                if (GUI.Button(new Rect(4, ry, inner - 4, row), Drawable(e.Key), selected ? _accentCell : (enabled ? _cell : _mutedCell)))
                {
                    if (e.Value != null)
                    {
                        SetTarget(e.Value);
                        _page = 2;
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }

        // The selected component's or material's members, one row each.
        private static void DrawMembers(Rect pane, ToolWindowStyles s, float row)
        {
            ToolWindow.Fill(pane, ToolWindow.InsetColor);
            if (_target == null)
            {
                GUI.Label(new Rect(pane.x + 8, pane.y + 4, pane.width - 16, row), "Select a component or a material.", s.MutedLabel);
                return;
            }
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
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
            var rows = new List<RowInfo>();
            foreach (Member m in members)
            {
                if (m.IsPrivate && !_showPrivate)
                {
                    continue;
                }
                rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name, Getter = () => m.Get(_target), Setter = m.CanWrite ? (Action<object>)(v => m.Set(_target, v)) : null, Label = m.Name, Type = m.Type });
                if (InspectorModel.IsList(m.Type) && ExpandedLists.Contains(m.Name))
                {
                    object listValue = SafeGet(m, () => m.Get(_target));
                    if (listValue is IList list)
                    {
                        Type element = m.Type.IsArray ? m.Type.GetElementType() : (m.Type.IsGenericType ? m.Type.GetGenericArguments()[0] : typeof(object));
                        int count = Mathf.Min(list.Count, 200);
                        for (int i = 0; i < count; i++)
                        {
                            int index = i;
                            rows.Add(new RowInfo
                            {
                                Member = m, Key = ControlPrefix + m.Name + "[" + i + "]", Label = $"    [{i}]", Type = element,
                                Getter = () => list[index], Setter = list.IsReadOnly ? null : (Action<object>)(v => list[index] = v),
                            });
                        }
                        if (list.Count > count)
                        {
                            rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name + "[more]", Label = $"    ... {list.Count - count} more", Type = typeof(string), Getter = () => "" });
                        }
                    }
                }
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float total = 0;
            foreach (RowInfo r in rows)
            {
                total += RowErrors.ContainsKey(r.Key) ? row * 2 : row;
            }
            ToolWindow.ApplyScroll(view, ref _scrollMembers);
            _scrollMembers = GUI.BeginScrollView(view, _scrollMembers, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            float nameWidth = Mathf.Clamp(inner * 0.38f, 120, 320);
            foreach (RowInfo r in rows)
            {
                float h = RowErrors.ContainsKey(r.Key) ? row * 2 : row;
                if (ry + h >= _scrollMembers.y && ry <= _scrollMembers.y + view.height)
                {
                    DrawRow(r, new Rect(0, ry, inner, row), nameWidth, s, row);
                }
                ry += h;
            }
            GUI.EndScrollView();
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
            GUI.Label(new Rect(rect.x, rect.y + row * 0.5f, nameWidth - 6, row * 0.5f), "", _mutedCell);
            var valueRect = new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth, row);

            // The value: frozen text, or read now (on Repaint; other passes reuse the last).
            string key = r.Key;
            object value = null;
            bool haveValue = false;
            if (_freeze && Frozen.TryGetValue(key, out string frozen))
            {
                // Frozen rows show their text only; the control below still gets a draft from it.
                value = frozen;
                haveValue = true;
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
            string shown = r.Member.Failure != null ? "error: " + r.Member.Failure : (value is string sv && _freeze && Frozen.ContainsKey(key) ? sv : InspectorModel.Format(value));
            Type t = r.Type;
            bool editable = r.Setter != null && haveValue && !(_freeze && Frozen.ContainsKey(key) && value is string) && InspectorModel.IsEditableType(t);

            if (r.Member.Failure != null)
            {
                GUI.Label(valueRect, Drawable(shown), _errorCell);
            }
            else if (t == typeof(bool) && editable)
            {
                bool b = value is bool bb && bb;
                if (GUI.Button(new Rect(valueRect.x, valueRect.y + 2, 80, row - 4), b ? "true" : "false", b ? s.SelectedButton : s.Button))
                {
                    TrySet(r, !b);
                }
            }
            else if (t != null && t.IsEnum && editable)
            {
                if (GUI.Button(new Rect(valueRect.x, valueRect.y + 2, Mathf.Min(valueRect.width, 220), row - 4), Drawable(shown) + "  >", s.Button))
                {
                    TrySet(r, InspectorModel.NextEnum(t, value));
                }
            }
            else if (editable)
            {
                // A text field with a draft while it has focus; Enter applies, Set too.
                bool focused = _focusedControl == key;
                string text;
                if (focused)
                {
                    if (!Drafts.TryGetValue(key, out text))
                    {
                        text = shown;
                        Drafts[key] = text;
                    }
                }
                else
                {
                    if (Drafts.ContainsKey(key) && ev.type == EventType.Repaint)
                    {
                        Drafts.Remove(key);
                    }
                    text = shown;
                }
                float fieldWidth = valueRect.width - 50 - (t == typeof(Color) || t == typeof(Color32) ? row : 0);
                GUI.SetNextControlName(key);
                var fieldRect = new Rect(valueRect.x, valueRect.y, Mathf.Max(60, fieldWidth), row);
                string after = GUI.TextField(fieldRect, text ?? "", s.TextField);
                if (focused && after != text)
                {
                    Drafts[key] = after;
                }
                Underline(fieldRect);
                float bx = fieldRect.xMax + 4;
                if (t == typeof(Color) || t == typeof(Color32))
                {
                    Color c = value is Color cc ? cc : value is Color32 c32 ? (Color)c32 : Color.clear;
                    ToolWindow.Fill(new Rect(bx, valueRect.y + 4, row - 8, row - 8), c);
                    bx += row;
                }
                if (GUI.Button(new Rect(bx, valueRect.y + 2, 44, row - 4), "Set", s.Button))
                {
                    if (!Drafts.ContainsKey(key))
                    {
                        Drafts[key] = after;
                    }
                    Apply(key, r);
                }
            }
            else if (value is UnityEngine.Object uo && uo)
            {
                GUI.Label(new Rect(valueRect.x, valueRect.y, valueRect.width - 50, row), Drawable(shown), _mutedCell);
                bool canGo = uo is GameObject || uo is Component || uo is Material || uo is Texture;
                if (canGo && GUI.Button(new Rect(valueRect.xMax - 44, valueRect.y + 2, 44, row - 4), "Go", s.Button))
                {
                    if (uo is Texture)
                    {
                        ToolWindow.Open("Assets");
                        _status = $"Texture {uo.name}: see the Assets tab.";
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
                GUI.Label(new Rect(valueRect.x, valueRect.y, valueRect.width - 80, row), Drawable(shown), _mutedCell);
                if (GUI.Button(new Rect(valueRect.xMax - 74, valueRect.y + 2, 74, row - 4), open ? "Collapse" : "Expand", s.Button))
                {
                    if (open) ExpandedLists.Remove(r.Member.Name); else ExpandedLists.Add(r.Member.Name);
                }
            }
            else
            {
                GUI.Label(valueRect, Drawable(shown), _mutedCell);
            }

            if (RowErrors.TryGetValue(key, out string error))
            {
                GUI.Label(new Rect(rect.x + nameWidth, rect.y + row, rect.width - nameWidth, row), Drawable(error), _errorCell);
            }
        }

        // Enter on a focused field: find its row again by key and apply.
        private static void Apply(string key)
        {
            foreach (Member m in MembersOfTarget())
            {
                if (ControlPrefix + m.Name == key)
                {
                    Apply(key, new RowInfo { Member = m, Key = key, Type = m.Type, Getter = () => m.Get(_target), Setter = m.CanWrite ? (Action<object>)(v => m.Set(_target, v)) : null });
                    return;
                }
                if (key.StartsWith(ControlPrefix + m.Name + "[", StringComparison.Ordinal) && SafeGet(m, () => m.Get(_target)) is IList list)
                {
                    string indexText = key.Substring((ControlPrefix + m.Name + "[").Length).TrimEnd(']');
                    if (int.TryParse(indexText, out int index) && index >= 0 && index < list.Count)
                    {
                        Type element = m.Type.IsArray ? m.Type.GetElementType() : (m.Type.IsGenericType ? m.Type.GetGenericArguments()[0] : typeof(object));
                        Apply(key, new RowInfo { Member = m, Key = key, Type = element, Getter = () => list[index], Setter = list.IsReadOnly ? null : (Action<object>)(v => list[index] = v) });
                    }
                    return;
                }
            }
        }

        private static void Apply(string key, RowInfo r)
        {
            if (!Drafts.TryGetValue(key, out string text) || r.Setter == null)
            {
                return;
            }
            object parsed = InspectorModel.Parse(r.Type, text, out string error);
            if (error != null)
            {
                RowErrors[key] = "Not accepted: " + error;
                return;
            }
            if (TrySet(r, parsed))
            {
                Drafts.Remove(key);
            }
        }

        private static bool TrySet(RowInfo r, object value)
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
                ToolWindow.Open(Title);
                var lines = new StringBuilder();
                foreach (Member m in MembersOfTarget())
                {
                    if (m.IsPrivate) continue;
                    lines.Append(m.Name).Append(" = ").Append(InspectorModel.Format(SafeGet(m, () => m.Get(_target)))).Append('\n');
                }
                return lines.ToString().TrimEnd();
            }
            ToolWindow.Open(Title);
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
                var names = new List<string> { "set" };
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
            return ConsoleTab.Drawable(text ?? "");
        }

        private static void Underline(Rect field)
        {
            Color was = GUI.color;
            GUI.color = ToolWindow.AccentColor;
            GUI.DrawTexture(new Rect(field.x, field.yMax - 2, field.width, 2), Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
