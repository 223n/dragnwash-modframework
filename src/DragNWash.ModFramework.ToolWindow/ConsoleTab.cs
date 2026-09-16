using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The "Console" tab: the log with levels in colour, filters the player
    // chooses, and a line to type commands into.
    internal static class ConsoleTab
    {
        internal const string Title = "Console";
        private const string InputControl = "DragNWashConsoleInput";

        internal static string SourceFilter = "";

        private static ToolTab _tab;
        private static Vector2 _scroll;
        private static bool _follow = true;
        private static string _input = "";
        private static readonly List<string> History = new List<string>();
        private static int _historyIndex = -1;
        private static bool _focusInput;
        private static Dictionary<LogLevel, GUIStyle> _levelStyles;
        private static GUIStyle _dot;

        private static readonly (LogLevel level, string label)[] Toggles =
        {
            (LogLevel.Error | LogLevel.Fatal, "Error"), (LogLevel.Warning, "Warning"), (LogLevel.Message, "Message"), (LogLevel.Info, "Info"), (LogLevel.Debug, "Debug"),
        };

        internal static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            ToolWindow.AddTab(ToolWindow.Guid, Title, Draw, 40);
            lock (ToolWindow.Tabs)
            {
                _tab = ToolWindow.Tabs.Find(t => t.Title == Title && t.Owner == ToolWindow.Guid);
            }
        }

        // Called from the plugin's Update: the badge on the tab button.
        internal static void Tick()
        {
            FlushPending();
            if (_tab != null)
            {
                _tab.Title = ConsoleLog.UnseenErrors > 0 ? Title + " !" : Title;
            }
        }

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_levelStyles != null)
            {
                return;
            }
            GUIStyle Colored(Color c)
            {
                var style = new GUIStyle(s.LogLabel);
                style.normal.textColor = c;
                style.hover.textColor = c;
                style.wordWrap = true;
                style.padding = new RectOffset(4, 4, 1, 1);
                return style;
            }
            _levelStyles = new Dictionary<LogLevel, GUIStyle>
            {
                { LogLevel.Fatal, Colored(ToolWindow.ErrorColor) },
                { LogLevel.Error, Colored(ToolWindow.ErrorColor) },
                { LogLevel.Warning, Colored(ToolWindow.WarningColor) },
                { LogLevel.Message, Colored(ToolWindow.AccentColor) },
                { LogLevel.Info, Colored(new Color(0.86f, 0.91f, 0.94f)) },
                { LogLevel.Debug, Colored(ToolWindow.MutedColor) },
            };
            _dot = Colored(ToolWindow.ErrorColor);
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = ToolWindow.Styles;
            EnsureStyles(s);
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            ConsoleLog.MarkSeen();

            // Level toggles, then the source filter and Clear.
            float bx = x;
            foreach ((LogLevel level, string label) in Toggles)
            {
                bool on = (ConsoleLog.Shown & level) != 0;
                float bw = Mathf.Max(70, s.Button.CalcSize(new GUIContent(label)).x + 16);
                if (GUI.Button(new Rect(bx, y, bw, row), label, on ? s.SelectedButton : s.Button))
                {
                    ConsoleLog.Shown = on ? ConsoleLog.Shown & ~level : ConsoleLog.Shown | level;
                }
                bx += bw + 6;
            }
            if (GUI.Button(new Rect(area.xMax - pad - 70, y, 70, row), "Clear", s.Button))
            {
                ConsoleLog.Clear();
            }
            var filterRect = new Rect(bx + 6, y, Mathf.Max(60, area.xMax - pad - 80 - (bx + 6)), row);
            SourceFilter = GUI.TextField(filterRect, SourceFilter ?? "", s.TextField);
            Underline(filterRect);
            if (string.IsNullOrEmpty(SourceFilter))
            {
                GUI.Label(new Rect(filterRect.x + 6, filterRect.y, filterRect.width - 6, row), "Filter by source", s.MutedLabel);
            }
            y += row + 6;

            string note = (ConsoleLog.Shown & LogLevel.Error) == 0 ? "Errors are hidden.    " : "";
            note += $"Unity: {ConsoleLog.UnityMinimum} and above, others: {ConsoleLog.DefaultMinimum} and above (log level <source> <level>)";
            GUI.Label(new Rect(x, y, w, row), note, s.MutedLabel);
            y += row;

            // The log, oldest first, following the end unless the player scrolled up.
            var view = new Rect(x, y, w, area.yMax - pad - y - row - 8);
            ToolWindow.Fill(view, ToolWindow.InsetColor);
            List<ConsoleEntry> all = ConsoleLog.Snapshot();
            var shown = new List<ConsoleEntry>(all.Count);
            foreach (ConsoleEntry e in all)
            {
                if (ConsoleLog.IsShown(e) && (string.IsNullOrEmpty(SourceFilter) || e.Source.IndexOf(SourceFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    shown.Add(e);
                }
            }
            float inner = view.width - 20;
            var heights = new float[shown.Count];
            float total = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                heights[i] = _levelStyles[Normalize(shown[i].Level)].CalcHeight(new GUIContent(Line(shown[i])), inner);
                total += heights[i];
            }
            if (_follow)
            {
                _scroll.y = Mathf.Max(0, total - view.height);
            }
            Vector2 before = _scroll;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                if (ry + heights[i] >= _scroll.y && ry <= _scroll.y + view.height)
                {
                    GUI.Label(new Rect(0, ry, inner, heights[i]), Line(shown[i]), _levelStyles[Normalize(shown[i].Level)]);
                }
                ry += heights[i];
            }
            GUI.EndScrollView();
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            {
                _follow = false;
            }
            if (_scroll.y >= total - view.height - 2)
            {
                _follow = true;
            }
            else if (_scroll != before)
            {
                _follow = false;
            }
            y = view.yMax + 6;

            // The command line. Enter runs, up and down walk the history.
            Event ev = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == InputControl;
            if (focused && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.UpArrow && History.Count > 0)
                {
                    _historyIndex = _historyIndex < 0 ? History.Count - 1 : Mathf.Max(0, _historyIndex - 1);
                    _input = History[_historyIndex];
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.DownArrow && _historyIndex >= 0)
                {
                    _historyIndex = _historyIndex + 1 < History.Count ? _historyIndex + 1 : -1;
                    _input = _historyIndex < 0 ? "" : History[_historyIndex];
                    ev.Use();
                }
            }
            GUI.Label(new Rect(x, y, 20, row), ">", s.Label);
            GUI.SetNextControlName(InputControl);
            var inputRect = new Rect(x + 20, y, w - 20 - 70, row);
            _input = GUI.TextField(inputRect, _input ?? "", s.TextField);
            Underline(inputRect);
            if (_focusInput)
            {
                GUI.FocusControl(InputControl);
                _focusInput = false;
            }
            if (GUI.Button(new Rect(area.xMax - pad - 64, y, 64, row), "Run", s.Button))
            {
                Submit();
            }
        }

        private static void Submit()
        {
            string line = (_input ?? "").Trim();
            _input = "";
            _historyIndex = -1;
            _focusInput = true;
            _follow = true;
            if (line.Length == 0)
            {
                return;
            }
            if (History.Count == 0 || History[History.Count - 1] != line)
            {
                History.Add(line);
                if (History.Count > 100)
                {
                    History.RemoveAt(0);
                }
            }
            ConsoleCommands.Execute(line);
        }

        private static string Line(ConsoleEntry e)
        {
            string line = e.Source == ConsoleLog.CommandSource
                ? e.Text
                : $"{e.Time:HH:mm:ss} [{Short(e.Level)}] {e.Source}: {e.Text}";
            return Drawable(line);
        }

        // Log lines carry any text, including Japanese from the localization
        // mod. Drawing a character the window font has not rasterised uploads
        // a new atlas in the middle of the frame, which crashes Direct3D 12
        // (UUM-140564). So on Direct3D 12 anything beyond ASCII is drawn as
        // '?'; elsewhere new characters are shown as '?' once and prepared for
        // the next frame from Update, the way PrepareCharacters works.
        private static readonly HashSet<char> Prepared = new HashSet<char>();
        private static readonly StringBuilder Pending = new StringBuilder();
        private static readonly bool NeverPrepare = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;

        private static string Drawable(string text)
        {
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 128 || (!NeverPrepare && Prepared.Contains(c)))
                {
                    sb?.Append(c);
                    continue;
                }
                if (sb == null)
                {
                    sb = new StringBuilder(text.Length);
                    sb.Append(text, 0, i);
                }
                sb.Append('?');
                if (!NeverPrepare && Prepared.Add(c))
                {
                    Pending.Append(c);
                }
            }
            return sb == null ? text : sb.ToString();
        }

        // From the plugin's Update: rasterise what the last frame could not draw.
        private static void FlushPending()
        {
            if (Pending.Length > 0)
            {
                MenuFont.Prepare(Pending.ToString());
                Pending.Length = 0;
            }
        }

        private static string Short(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Fatal: return "F";
                case LogLevel.Error: return "E";
                case LogLevel.Warning: return "W";
                case LogLevel.Message: return "M";
                case LogLevel.Info: return "I";
                default: return "D";
            }
        }

        private static LogLevel Normalize(LogLevel level)
        {
            return _levelStyles.ContainsKey(level) ? level : LogLevel.Info;
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
