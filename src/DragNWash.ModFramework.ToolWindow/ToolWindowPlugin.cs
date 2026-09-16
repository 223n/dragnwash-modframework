using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DragNWash.ModFramework.ToolWindow
{
    // The tool window library's BepInEx entry point, and the window itself.
    [BepInPlugin(ToolWindow.Guid, "DragNWash.ModFramework.ToolWindow", ToolWindow.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class ToolWindowPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ToolWindowPlugin Instance;

        private const float HeaderHeight = 48f;
        private const float GripSize = 22f;
        private const float CloseMargin = 58f;

        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private ConfigEntry<string> _fontMode;
        private ConfigEntry<string> _consoleShow;
        private ConfigEntry<string> _consoleLevels;
        private ConfigEntry<bool> _consoleTrace;
        private bool _savingConsole;

        internal bool ShowWindow;
        internal string Notice = string.Empty;

        private bool _wasOpen;
        private Rect _windowRect = new Rect(24, 24, 780, 580);
        private ToolTab _current;
        private Texture2D _background;
        private GUIStyle _windowStyle;
        private bool _stylesReady;
        private bool _pointerGrabbed;
        private bool _inputBlockingBroken;
        private bool _resizing;
        private int _resizeControl;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
        private Vector2? _requestedSize;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            ModFramework.Register(new ModInfo
            {
                Guid = ToolWindow.Guid,
                DisplayName = "Drag'n Wash ModFramework: Tool window",
                Description = "One shared in-game window (F1) where mods add tabs for their developer and debug tools, usable with mouse, gamepad and on the Steam Deck.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            _toggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F1),
                "Shows and hides the tool window. Only while Developer tools are on (Options > Mods > Drag'n Wash ModFramework).");
            // A developer tool: it closes when the switch goes off, and SetOpen
            // and the key refuse to open it while the switch is off.
            DeveloperTools.Changed += () =>
            {
                if (!DeveloperTools.Enabled && ShowWindow)
                {
                    ShowWindow = false;
                }
            };
            _fontMode = Config.Bind("General", "FontMode", "auto",
                "Font for the tool window: auto (an OS font with Japanese and Chinese, else the bundled one), builtin (Unity's built-in font, ASCII only), skin (the IMGUI skin's font).");

            // Built now rather than when the window first opens: Texture2D.Apply
            // uploads to the GPU, and doing that on the frame the window opens is
            // the Direct3D 12 crash (UUM-140564).
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.08f, 0.95f));
            _background.Apply();
            MenuFont.Create(_fontMode.Value);

            var harmony = new Harmony(ToolWindow.Guid);
            Install("Pad and trackpad clicks", () => VirtualClick.Install(harmony));
            Install("Characters the font lacks", () => MenuText.Install(harmony));
            GameHooks.Require(ToolWindow.Guid, "Free cursor while open", AccessTools.TypeByName("GameStateManager") != null, "GameStateManager");
            Install("Free cursor while open", () => CursorUnlock.Install(harmony));
            ToolWindow.IsAvailable = true;

            Install("Console", SetUpConsole);
        }

        // The console: every BepInEx log line with its level, the player's
        // choice of what to show (kept in the config), and commands.
        private void SetUpConsole()
        {
            _consoleShow = Config.Bind("Console", "Show", "Error,Warning,Message,Info",
                "Levels the Console tab shows at all, comma separated: Fatal, Error, Warning, Message, Info, Debug. Everything is still written to BepInEx/LogOutput.log.");
            _consoleLevels = Config.Bind("Console", "Levels", "unity:Warning, default:Info",
                "The least severe level shown per log source, comma separated, as source:level. 'unity' is Unity's own log, 'default' every source without its own entry, anything else a source name as the log prints it (e.g. DragNWash.ModFramework.Assets:Debug).");
            _consoleTrace = Config.Bind("Console", "TraceInput", false,
                "Writes every key the Console tab sees, and what it did with it, to the log as DragNWash.ConsoleTrace. For debugging the console's input.");
            ConsoleTab.Trace = _consoleTrace.Value;
            _consoleTrace.SettingChanged += (s, e) => ConsoleTab.Trace = _consoleTrace.Value;
            ApplyConsoleConfig();
            _consoleShow.SettingChanged += (s, e) => { if (!_savingConsole) ApplyConsoleConfig(); };
            _consoleLevels.SettingChanged += (s, e) => { if (!_savingConsole) ApplyConsoleConfig(); };
            ConsoleLog.Changed += SaveConsoleConfig;

            BepInEx.Logging.Logger.Listeners.Add(new ConsoleLog.Listener());
            ConsoleCommands.RegisterBuiltIns();
            ConsoleTab.Install();
        }

        private void ApplyConsoleConfig()
        {
            LogLevel shown = LogLevel.None;
            foreach (string part in (_consoleShow.Value ?? "").Split(','))
            {
                if (ConsoleLog.TryParseLevel(part, out LogLevel level))
                {
                    shown |= level;
                    if (level == LogLevel.Error) shown |= LogLevel.Fatal;
                }
            }
            _savingConsole = true;
            try
            {
                ConsoleLog.Shown = shown;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in (_consoleLevels.Value ?? "").Split(','))
                {
                    string[] kv = part.Split(':');
                    if (kv.Length != 2 || !ConsoleLog.TryParseLevel(kv[1], out LogLevel minimum))
                    {
                        continue;
                    }
                    string source = kv[0].Trim();
                    if (source.Equals("unity", StringComparison.OrdinalIgnoreCase)) ConsoleLog.UnityMinimum = minimum;
                    else if (source.Equals("default", StringComparison.OrdinalIgnoreCase)) ConsoleLog.DefaultMinimum = minimum;
                    else { ConsoleLog.SetMinimum(source, minimum); keep.Add(source); }
                }
                foreach (KeyValuePair<string, LogLevel> kv in ConsoleLog.Minimums)
                {
                    if (!keep.Contains(kv.Key)) ConsoleLog.SetMinimum(kv.Key, null);
                }
            }
            finally
            {
                _savingConsole = false;
            }
        }

        private void SaveConsoleConfig()
        {
            if (_savingConsole)
            {
                return;
            }
            _savingConsole = true;
            try
            {
                var show = new List<string>();
                foreach (LogLevel l in new[] { LogLevel.Fatal, LogLevel.Error, LogLevel.Warning, LogLevel.Message, LogLevel.Info, LogLevel.Debug })
                {
                    if ((ConsoleLog.Shown & l) != 0 && l != LogLevel.Fatal) show.Add(l.ToString());
                }
                _consoleShow.Value = string.Join(",", show);
                var levels = new List<string> { "unity:" + ConsoleLog.UnityMinimum, "default:" + ConsoleLog.DefaultMinimum };
                foreach (KeyValuePair<string, LogLevel> kv in ConsoleLog.Minimums)
                {
                    levels.Add(kv.Key + ":" + kv.Value);
                }
                _consoleLevels.Value = string.Join(", ", levels);
            }
            finally
            {
                _savingConsole = false;
            }
        }

        private static void Install(string feature, Action install)
        {
            try
            {
                install();
            }
            catch (Exception ex)
            {
                GameHooks.Require(ToolWindow.Guid, feature, false, ex.Message);
            }
        }

        internal void SetOpen(bool open, string tabTitle)
        {
            if (open && !ToolsOn())
            {
                return;
            }
            ShowWindow = open;
            if (open && tabTitle != null)
            {
                lock (ToolWindow.Tabs)
                {
                    ToolTab tab = ToolWindow.Tabs.FirstOrDefault(t => t.Title == tabTitle);
                    if (tab != null)
                    {
                        Select(tab);
                    }
                }
            }
        }

        private bool _saidToolsOff;

        // False while developer tools are off, saying why once per switch-off.
        private bool ToolsOn()
        {
            if (DeveloperTools.Enabled)
            {
                _saidToolsOff = false;
                return true;
            }
            if (!_saidToolsOff)
            {
                _saidToolsOff = true;
                Log.LogMessage("The Tool window stays closed: developer tools are off. Turn them on in Options > Mods > Drag'n Wash ModFramework > Developer tools.");
            }
            return false;
        }

        private void Select(ToolTab tab)
        {
            if (!ReferenceEquals(tab, _current))
            {
                _current = tab;
                Notice = string.Empty;
            }
        }

        private void Update()
        {
            MenuFont.FlushQueued();
            ConsoleTab.Tick();

            if (_toggleKey.Value.IsDown() && (ShowWindow || ToolsOn()))
            {
                ShowWindow = !ShowWindow;
            }
            // The window also closes from its own X button, so follow the state
            // here rather than only on the key.
            if (ShowWindow != _wasOpen)
            {
                _wasOpen = ShowWindow;
                if (ShowWindow)
                {
                    CursorUnlock.Hold(this);
                }
                else
                {
                    CursorUnlock.Release();
                    VirtualClick.Cancel();
                }
                ToolWindow.RaiseOpenChanged(ShowWindow);
            }

            if (ShowWindow)
            {
                CursorUnlock.Tick();
                VirtualClick.Poll();
                if (VirtualClick.UpdateDrag(ref _windowRect, HeaderHeight, CloseMargin, GripSize))
                {
                    _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
                }
            }

            // Last, and swallowing its own failures, so a problem here never
            // stops the rest of Update.
            if (!_inputBlockingBroken)
            {
                try
                {
                    UpdateInputBlocking();
                }
                catch (Exception ex)
                {
                    _inputBlockingBroken = true;
                    InputBlocker.SetBlocking(false);
                    Log.LogWarning($"Input blocking disabled: {ex.Message}");
                }
            }
        }

        // Suspend the game's input while the pointer works the window. A held
        // button keeps the block after the pointer leaves, so dragging the window
        // past its edge does not hand the drag to the game mid-gesture. The
        // pointer is read through the Input System: this game has legacy input
        // switched off, so every UnityEngine.Input read throws.
        private void UpdateInputBlocking()
        {
            Mouse mouse = Mouse.current;
            if (!ShowWindow || mouse == null)
            {
                _pointerGrabbed = false;
                InputBlocker.SetBlocking(false);
                return;
            }
            Vector2 position = mouse.position.ReadValue();
            // The Input System measures from the bottom left, IMGUI from the top.
            bool over = _windowRect.Contains(new Vector2(position.x, Screen.height - position.y));
            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                _pointerGrabbed = over;
            }
            else if (!held)
            {
                _pointerGrabbed = false;
            }
            InputBlocker.SetBlocking(over || _pointerGrabbed);
        }

        private void OnDestroy()
        {
            // Leaving the game's input suspended would soft-lock it.
            InputBlocker.SetBlocking(false);
            CursorUnlock.Release();
        }

        internal static Rect Clamp(Rect rect, float screenWidth, float screenHeight)
        {
            float width = Mathf.Max(1, screenWidth);
            float height = Mathf.Max(1, screenHeight);
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(420, width), width);
            rect.height = Mathf.Clamp(rect.height, Mathf.Min(340, height), height);
            rect.x = Mathf.Clamp(rect.x, 0, width - rect.width);
            rect.y = Mathf.Clamp(rect.y, 0, height - rect.height);
            return rect;
        }

        private GUIStyle Style(GUIStyle basis, Color textColor)
        {
            var style = new GUIStyle(basis)
            {
                font = MenuFont.Font != null ? MenuFont.Font : basis.font,
                fontSize = MenuFont.Size,
                fontStyle = MenuFont.Bold ? FontStyle.Bold : FontStyle.Normal,
                richText = false,
            };
            foreach (GUIStyleState state in States(style))
            {
                state.textColor = textColor;
            }
            return style;
        }

        private static IEnumerable<GUIStyleState> States(GUIStyle s)
        {
            return new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused };
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }
            _stylesReady = true;
            _windowStyle = Style(GUI.skin.window, Color.white);
            _windowStyle.normal.background = _background;
            _windowStyle.onNormal.background = _background;
            _windowStyle.focused.background = _background;
            _windowStyle.border = new RectOffset(0, 0, 0, 0);
            _windowStyle.padding = new RectOffset(0, 0, 0, 0);

            ToolWindowStyles styles = ToolWindow.Styles;
            styles.Label = Style(GUI.skin.label, new Color(0.91f, 0.94f, 0.97f));
            styles.Label.alignment = TextAnchor.MiddleLeft;
            styles.MutedLabel = Style(styles.Label, ToolWindow.MutedColor);
            styles.WrappedLabel = Style(styles.MutedLabel, ToolWindow.MutedColor);
            styles.WrappedLabel.wordWrap = true;
            styles.WrappedLabel.alignment = TextAnchor.UpperLeft;
            styles.LogLabel = Style(styles.Label, new Color(0.86f, 0.91f, 0.94f));
            styles.LogLabel.wordWrap = true;
            styles.LogLabel.alignment = TextAnchor.UpperLeft;
            styles.LogLabel.padding = new RectOffset(4, 4, 4, 4);
            styles.Button = Style(GUI.skin.button, new Color(0.88f, 0.92f, 0.95f));
            styles.Button.padding = new RectOffset(10, 10, 4, 4);
            styles.SelectedButton = Style(styles.Button, ToolWindow.AccentColor);
            // Not GUI.skin.textField: its built-in textures would be uploaded on
            // first draw, while the window is open. Our own 1x1 texture instead.
            styles.TextField = Style(styles.Label, new Color(0.91f, 0.94f, 0.97f));
            styles.TextField.padding = new RectOffset(8, 8, 4, 4);
            styles.TextField.border = new RectOffset(0, 0, 0, 0);
            foreach (GUIStyleState state in States(styles.TextField))
            {
                state.background = _background;
            }
        }

        private void OnGUI()
        {
            if (!ShowWindow)
            {
                if (_resizing && GUIUtility.hotControl == _resizeControl)
                {
                    GUIUtility.hotControl = 0;
                }
                _resizing = false;
                return;
            }

            EnsureStyles();
            _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
            // Pad and trackpad presses that never reach IMGUI as mouse buttons
            // (Steam Deck) become clicks in VirtualClick.
            VirtualClick.Observe(Event.current);
            Color color = GUI.color, background = GUI.backgroundColor, content = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                MenuText.Begin(MenuFont.Font, MenuFont.Size);
                _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, string.Empty, _windowStyle);
                MenuText.End();
                // GUI.Window returns its own rectangle after the callback; apply a
                // resize afterwards so that cannot undo it.
                if (_requestedSize.HasValue)
                {
                    _windowRect.size = _requestedSize.Value;
                    _requestedSize = null;
                    _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
                }
            }
            finally
            {
                MenuText.End();
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
            }
        }

        private void DrawWindow(int id)
        {
            ToolWindowStyles styles = ToolWindow.Styles;
            float width = _windowRect.width;
            float height = _windowRect.height;
            float bodyWidth = width - ToolWindow.Padding * 2;

            ToolWindow.Fill(new Rect(0, 0, width, HeaderHeight), ToolWindow.PanelColor);
            ToolWindow.Fill(new Rect(0, 0, 4, HeaderHeight), ToolWindow.AccentColor);
            GUI.Label(new Rect(ToolWindow.Padding, 8, width - 80, 32), "DRAG'N WASH  /  TOOLS", styles.Label);
            if (GUI.Button(new Rect(width - 46, 10, 30, 28), "X", styles.Button))
            {
                ShowWindow = false;
            }

            ToolTab[] tabs;
            lock (ToolWindow.Tabs)
            {
                tabs = ToolWindow.Tabs.ToArray();
            }
            if (_current == null || Array.IndexOf(tabs, _current) < 0)
            {
                Select(tabs.Length > 0 ? tabs[0] : null);
            }

            // Tab buttons, as wide as their titles, wrapping onto more rows.
            float x = ToolWindow.Padding, y = HeaderHeight + 8;
            foreach (ToolTab tab in tabs)
            {
                float w = Mathf.Max(90, styles.Button.CalcSize(new GUIContent(tab.Title)).x + 12);
                if (x + w > width - ToolWindow.Padding && x > ToolWindow.Padding)
                {
                    x = ToolWindow.Padding;
                    y += ToolWindow.RowHeight + 8;
                }
                if (GUI.Button(new Rect(x, y, w, ToolWindow.RowHeight), tab.Title, ReferenceEquals(tab, _current) ? styles.SelectedButton : styles.Button))
                {
                    Select(tab);
                }
                x += w + 8;
            }
            float bodyTop = y + ToolWindow.RowHeight + 12;
            var body = new Rect(ToolWindow.Padding, bodyTop, bodyWidth, Mathf.Max(80, height - bodyTop - 38));

            if (_current == null)
            {
                GUI.Label(body, "No mod has added a tab yet.", styles.WrappedLabel);
            }
            else if (_current.Failure != null)
            {
                ToolWindow.Fill(body, ToolWindow.InsetColor);
                GUI.Label(new Rect(body.x + 12, body.y + 12, body.width - 24, body.height - 24),
                    $"This tab stopped working and was turned off for this session. See BepInEx/LogOutput.log.\n\n{_current.Owner}: {_current.Failure}", styles.WrappedLabel);
            }
            else
            {
                // Inside a group, so a tab drawn for a taller window is clipped
                // at the body's edge instead of running over the footer line.
                GUI.BeginGroup(body);
                try
                {
                    _current.Draw(new Rect(0, 0, body.width, body.height));
                }
                catch (Exception ex) when (!(ex is ExitGUIException))
                {
                    _current.Failure = ex.GetType().Name + ": " + ex.Message;
                    Log.LogError($"The tool window tab \"{_current.Title}\" of {_current.Owner} threw and was turned off: {ex}");
                    Notice = $"\"{_current.Title}\" stopped working; see the log.";
                }
                finally
                {
                    GUI.EndGroup();
                }
            }

            GUI.Label(new Rect(ToolWindow.Padding, height - 30, width - 54, 24),
                string.IsNullOrEmpty(Notice) ? $"{_toggleKey.Value}: toggle    |    Drag title to move    |    Drag corner to resize" : Notice,
                styles.MutedLabel);

            HandleResize(new Rect(width - GripSize, height - GripSize, GripSize, GripSize));
            // Dragging only by the title, so selecting text or scrolling never
            // moves the window.
            GUI.DragWindow(new Rect(4, 0, width - CloseMargin, HeaderHeight));
        }

        private void HandleResize(Rect grip)
        {
            GUI.Label(grip, "/", ToolWindow.Styles.MutedLabel);
            int control = GUIUtility.GetControlID("DragNWashToolWindowResize".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            Vector2 screenMouse = current.mousePosition + _windowRect.position;
            if (current.type == EventType.MouseDown && current.button == 0 && grip.Contains(current.mousePosition))
            {
                _resizing = true;
                _resizeControl = control;
                GUIUtility.hotControl = control;
                _resizeStartMouse = screenMouse;
                _resizeStartSize = _windowRect.size;
                current.Use();
            }
            if (_resizing && GUIUtility.hotControl == _resizeControl)
            {
                if (current.type == EventType.MouseDrag)
                {
                    _requestedSize = _resizeStartSize + screenMouse - _resizeStartMouse;
                    current.Use();
                }
                else if (current.type == EventType.MouseUp)
                {
                    _resizing = false;
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }
        }
    }
}
