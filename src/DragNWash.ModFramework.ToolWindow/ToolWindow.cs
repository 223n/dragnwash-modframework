using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    /// <summary>
    /// Tool window library: one shared in-game window for developer and debug
    /// tools, opened with F1 by default, where each mod adds its own tabs.
    /// </summary>
    /// <remarks>
    /// The window is drawn with Unity's IMGUI. The library takes care of what
    /// makes that hard in this game: it frees the cursor the game keeps locked
    /// when a gamepad is present, stops clicks and drags on the window from
    /// reaching the game behind it, turns gamepad and Steam Deck trackpad presses
    /// into clicks, lets the sticks scroll, and draws with a font that has
    /// Japanese and Chinese glyphs where the system has one.
    /// <para>
    /// Depend on it with
    /// <c>[BepInDependency(ToolWindow.Guid, BepInDependency.DependencyFlags.HardDependency)]</c>.
    /// </para>
    /// <para>
    /// On Direct3D 12, rasterizing a character the window font has not drawn yet
    /// uploads a texture, and an upload while the game is presenting a frame can
    /// crash the game (Unity UUM-140564). Prepare every non-ASCII character a tab
    /// shows with <see cref="PrepareCharacters"/> from Awake or Update, never
    /// from the draw callback.
    /// </para>
    /// </remarks>
    public static class ToolWindow
    {
        /// <summary>BepInEx GUID of the tool window library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.toolwindow";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.1.0";

        /// <summary>Height of one row of controls, in pixels.</summary>
        public const float RowHeight = 30f;

        /// <summary>Space between the window edge and its content, in pixels.</summary>
        public const float Padding = 16f;

        /// <summary>Header band colour.</summary>
        public static readonly Color PanelColor = new Color(0.09f, 0.11f, 0.15f);

        /// <summary>Background of a tab's content area.</summary>
        public static readonly Color InsetColor = new Color(0.055f, 0.07f, 0.10f);

        /// <summary>Accent colour (selected buttons, the header mark).</summary>
        public static readonly Color AccentColor = new Color(0.32f, 0.78f, 0.72f);

        /// <summary>Colour of secondary text.</summary>
        public static readonly Color MutedColor = new Color(0.60f, 0.66f, 0.73f);

        /// <summary>Errors in the console.</summary>
        public static readonly Color ErrorColor = new Color(0.96f, 0.45f, 0.40f);

        /// <summary>Warnings in the console.</summary>
        public static readonly Color WarningColor = new Color(0.93f, 0.75f, 0.30f);

        internal static readonly List<ToolTab> Tabs = new List<ToolTab>();
        private static int _nextSerial;

        /// <summary>True when the window can be shown on this game build.</summary>
        public static bool IsAvailable { get; internal set; }

        /// <summary>True while the window is open.</summary>
        public static bool IsOpen => ToolWindowPlugin.Instance != null && ToolWindowPlugin.Instance.ShowWindow;

        /// <summary>Raised when the window opens (true) or closes (false).</summary>
        public static event Action<bool> OpenChanged;

        /// <summary>The styles controls in a tab should use. Only valid inside a draw callback.</summary>
        public static ToolWindowStyles Styles { get; } = new ToolWindowStyles();

        /// <summary>The font the window draws with, or null for the IMGUI skin's font.</summary>
        public static Font Font => MenuFont.Font;

        /// <summary>Font size used by every style.</summary>
        public static int FontSize => MenuFont.Size;

        /// <summary>
        /// Adds a tab. <paramref name="draw"/> is called from OnGUI with the tab's
        /// content area in window coordinates. Tabs are ordered by
        /// <paramref name="order"/>, then by when they were added. Dispose the
        /// returned object to remove the tab.
        /// </summary>
        /// <param name="owner">GUID of the mod adding the tab, shown when it fails.</param>
        /// <param name="title">Tab button text. Keep it short and ASCII (see <see cref="PrepareCharacters"/>).</param>
        /// <param name="draw">Draws the tab. An exception is logged with the owner and the tab shows it instead.</param>
        /// <param name="order">Lower comes first.</param>
        public static IDisposable AddTab(string owner, string title, Action<Rect> draw, int order = 0)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(title) || draw == null)
            {
                throw new ArgumentException("A tab needs an owner, a title and a draw callback.");
            }
            var tab = new ToolTab { Owner = owner, Title = title, Draw = draw, Order = order, Serial = _nextSerial++ };
            lock (Tabs)
            {
                Tabs.Add(tab);
                Tabs.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Serial.CompareTo(b.Serial));
            }
            return new Removal(tab);
        }

        /// <summary>
        /// Adds a command to the Console tab (experimental, Tool window 1.1).
        /// <paramref name="run"/> gets the words typed after the name and returns
        /// what to print, one line per '\n'. An exception is printed in red with
        /// the owner and stops nothing else. When another mod already registered
        /// the same name, this one is reachable as <c>owner:name</c> only. Dispose
        /// the returned object to remove the command.
        /// </summary>
        /// <param name="owner">GUID of the mod adding the command.</param>
        /// <param name="name">One lower-case word, e.g. "tl".</param>
        /// <param name="description">One line for <c>help</c>.</param>
        /// <param name="run">Runs the command.</param>
        public static IDisposable AddCommand(string owner, string name, string description, Func<string[], string> run)
        {
            return AddCommand(owner, name, description, run, null);
        }

        /// <summary>
        /// As <see cref="AddCommand(string, string, string, Func{string[], string})"/>, with
        /// completions: <paramref name="complete"/> gets the words typed after the
        /// name so far, the last one possibly partial (or "" right after a space),
        /// and returns what could stand there. The console shows them as the
        /// person types and fills them in on Tab.
        /// </summary>
        public static IDisposable AddCommand(string owner, string name, string description, Func<string[], string> run, Func<string[], IEnumerable<string>> complete)
        {
            ConsoleCommand command = ConsoleCommands.Register(owner, name, description, run, complete);
            return new CommandRemoval(command);
        }

        private sealed class CommandRemoval : IDisposable
        {
            private ConsoleCommand _command;
            public CommandRemoval(ConsoleCommand command) { _command = command; }
            public void Dispose()
            {
                if (_command != null)
                {
                    ConsoleCommands.Unregister(_command);
                    _command = null;
                }
            }
        }

        /// <summary>Opens the window, on the tab with this title when one is given.</summary>
        public static void Open(string tabTitle = null)
        {
            ToolWindowPlugin.Instance?.SetOpen(true, tabTitle);
        }

        /// <summary>
        /// Selects <paramref name="target"/> (a GameObject, a Component or a
        /// Material) in the Inspector tab and opens the window on it
        /// (experimental, Tool window 1.1). Anything else is refused with a note
        /// in the tab. Nothing happens while developer tools are off.
        /// </summary>
        public static void Inspect(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            InspectorTab.Select(target);
            Open(InspectorTab.Title);
        }

        /// <summary>Closes the window.</summary>
        public static void Close()
        {
            ToolWindowPlugin.Instance?.SetOpen(false, null);
        }

        /// <summary>Shows a one-line message in the window's footer until the tab changes.</summary>
        public static void ShowNotice(string message)
        {
            if (ToolWindowPlugin.Instance != null)
            {
                ToolWindowPlugin.Instance.Notice = message ?? string.Empty;
            }
        }

        /// <summary>
        /// Rasterizes these characters into the window font now, so drawing them
        /// later uploads nothing. Call from Awake or Update. Called from a draw
        /// callback, the characters are prepared on the next Update instead.
        /// </summary>
        public static void PrepareCharacters(string characters)
        {
            MenuFont.Prepare(characters);
        }

        /// <summary>
        /// True when every character of <paramref name="text"/> has a glyph in the
        /// window font. Characters it cannot draw are shown as "?".
        /// </summary>
        public static bool CanDraw(string text)
        {
            return MenuText.CanDraw(MenuFont.Font, MenuFont.Size, text);
        }

        /// <summary>Fills a rectangle with a colour.</summary>
        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>
        /// Call just before <c>GUI.BeginScrollView</c> for a scrolling area: moves
        /// it by the gamepad stick or d-pad while the pointer is over it. Returns
        /// true when it moved.
        /// </summary>
        public static bool ApplyScroll(Rect view, ref Vector2 scroll)
        {
            return VirtualClick.ApplyScroll(view, ref scroll);
        }

        // On a mod's reload (ModReload.Unloading): its tabs, its commands and
        // its OpenChanged handlers go, so the new build adds them back cleanly.
        internal static void RemoveOwned(string owner, System.Reflection.Assembly assembly)
        {
            lock (Tabs)
            {
                Tabs.RemoveAll(t => t.Owner == owner);
            }
            ConsoleCommands.UnregisterOwned(owner);
            OpenChanged = (Action<bool>)ModReload.Prune(OpenChanged, assembly);
        }

        internal static void RaiseOpenChanged(bool open)
        {
            if (OpenChanged == null)
            {
                return;
            }
            foreach (Action<bool> handler in OpenChanged.GetInvocationList())
            {
                try
                {
                    handler(open);
                }
                catch (Exception ex)
                {
                    ToolWindowPlugin.Log.LogError($"A tool window OpenChanged handler threw: {ex}");
                }
            }
        }

        private sealed class Removal : IDisposable
        {
            private ToolTab _tab;

            internal Removal(ToolTab tab)
            {
                _tab = tab;
            }

            public void Dispose()
            {
                if (_tab == null)
                {
                    return;
                }
                lock (Tabs)
                {
                    Tabs.Remove(_tab);
                }
                _tab = null;
            }
        }
    }

    internal sealed class ToolTab
    {
        public string Owner;
        public string Title;
        public Action<Rect> Draw;
        public int Order;
        public int Serial;
        public string Failure;
    }

    /// <summary>
    /// The window's styles. They exist from the window's first draw on; use them
    /// only inside a draw callback.
    /// </summary>
    public sealed class ToolWindowStyles
    {
        internal ToolWindowStyles() { }

        /// <summary>Normal one-line text.</summary>
        public GUIStyle Label { get; internal set; }

        /// <summary>Secondary one-line text.</summary>
        public GUIStyle MutedLabel { get; internal set; }

        /// <summary>Secondary text that wraps.</summary>
        public GUIStyle WrappedLabel { get; internal set; }

        /// <summary>Wrapping text for logs, with a little padding.</summary>
        public GUIStyle LogLabel { get; internal set; }

        /// <summary>A button.</summary>
        public GUIStyle Button { get; internal set; }

        /// <summary>A selected or active button.</summary>
        public GUIStyle SelectedButton { get; internal set; }

        /// <summary>
        /// A text field. Use this rather than GUI.skin.textField, whose built-in
        /// textures are uploaded on first draw (see <see cref="ToolWindow.PrepareCharacters"/>).
        /// </summary>
        public GUIStyle TextField { get; internal set; }
    }
}
