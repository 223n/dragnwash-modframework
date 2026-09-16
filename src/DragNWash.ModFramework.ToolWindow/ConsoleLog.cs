using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace DragNWash.ModFramework.ToolWindow
{
    /// <summary>One line in the console: what BepInEx logged, or what a command printed.</summary>
    public sealed class ConsoleEntry
    {
        internal ConsoleEntry(LogLevel level, string source, string text)
        {
            Level = level;
            Source = source ?? "";
            Text = text ?? "";
            Time = DateTime.Now;
        }

        /// <summary>BepInEx level; commands print at Message, their failures at Error.</summary>
        public LogLevel Level { get; }

        /// <summary>Log source name ("DragNWash.ModFramework.Assets", "Unity Log", or "console").</summary>
        public string Source { get; }

        /// <summary>The line.</summary>
        public string Text { get; }

        /// <summary>When it arrived.</summary>
        public DateTime Time { get; }
    }

    /// <summary>
    /// The console's log: every line BepInEx logs, from any mod and from Unity,
    /// kept in memory with its level and source, plus what commands print.
    /// Which lines are shown is the player's choice (<see cref="IsShown"/>);
    /// BepInEx/LogOutput.log still gets everything. Experimental (Tool window 1.1).
    /// </summary>
    public static class ConsoleLog
    {
        /// <summary>Lines kept; older ones are dropped.</summary>
        public const int Capacity = 2000;

        /// <summary>Source name for what commands print.</summary>
        public const string CommandSource = "console";

        private static readonly List<ConsoleEntry> Entries = new List<ConsoleEntry>();
        private static readonly Dictionary<string, LogLevel> MinimumBySource = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
        private static LogLevel _shown = LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info;
        private static LogLevel _defaultMinimum = LogLevel.Info;
        private static LogLevel _unityMinimum = LogLevel.Warning;

        /// <summary>Raised on the logging thread for every new entry. Keep handlers short.</summary>
        public static event Action<ConsoleEntry> Added;

        /// <summary>Errors and fatals that arrived since <see cref="MarkSeen"/>.</summary>
        public static int UnseenErrors { get; private set; }

        /// <summary>Total lines kept right now.</summary>
        public static int Count
        {
            get { lock (Entries) { return Entries.Count; } }
        }

        /// <summary>Levels that are shown at all; a level not in the set is hidden for every source.</summary>
        public static LogLevel Shown
        {
            get => _shown;
            set { _shown = value; Changed?.Invoke(); }
        }

        /// <summary>The least severe level shown for sources without their own setting.</summary>
        public static LogLevel DefaultMinimum
        {
            get => _defaultMinimum;
            set { _defaultMinimum = value; Changed?.Invoke(); }
        }

        /// <summary>The least severe level shown for Unity's own log, which is chatty.</summary>
        public static LogLevel UnityMinimum
        {
            get => _unityMinimum;
            set { _unityMinimum = value; Changed?.Invoke(); }
        }

        /// <summary>Raised when what is shown changes, so the tab can save the settings.</summary>
        public static event Action Changed;

        /// <summary>Sets the least severe level shown for one source; null removes the override.</summary>
        public static void SetMinimum(string source, LogLevel? minimum)
        {
            lock (MinimumBySource)
            {
                if (minimum.HasValue)
                {
                    MinimumBySource[source] = minimum.Value;
                }
                else
                {
                    MinimumBySource.Remove(source);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>The per-source minimums, for saving.</summary>
        public static IReadOnlyDictionary<string, LogLevel> Minimums
        {
            get { lock (MinimumBySource) { return new Dictionary<string, LogLevel>(MinimumBySource, StringComparer.OrdinalIgnoreCase); } }
        }

        /// <summary>The least severe level shown for <paramref name="source"/>.</summary>
        public static LogLevel MinimumFor(string source)
        {
            lock (MinimumBySource)
            {
                if (source != null && MinimumBySource.TryGetValue(source, out LogLevel m))
                {
                    return m;
                }
            }
            return IsUnity(source) ? _unityMinimum : _defaultMinimum;
        }

        /// <summary>Whether the player's settings show this entry. Lower level values are more severe.</summary>
        public static bool IsShown(ConsoleEntry entry)
        {
            return (entry.Level & _shown) != 0 && (int)entry.Level <= (int)MinimumFor(entry.Source);
        }

        /// <summary>A copy of the kept lines, oldest first.</summary>
        public static List<ConsoleEntry> Snapshot()
        {
            lock (Entries)
            {
                return new List<ConsoleEntry>(Entries);
            }
        }

        /// <summary>Adds a line the console itself produced.</summary>
        public static void Print(string text, LogLevel level = LogLevel.Message)
        {
            Add(new ConsoleEntry(level, CommandSource, text));
        }

        /// <summary>Forgets every line.</summary>
        public static void Clear()
        {
            lock (Entries)
            {
                Entries.Clear();
            }
            UnseenErrors = 0;
        }

        /// <summary>The tab was looked at: clears the error badge.</summary>
        public static void MarkSeen()
        {
            UnseenErrors = 0;
        }

        internal static bool IsUnity(string source)
        {
            return source != null && source.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void Add(ConsoleEntry entry)
        {
            lock (Entries)
            {
                Entries.Add(entry);
                if (Entries.Count > Capacity)
                {
                    Entries.RemoveRange(0, Entries.Count - Capacity);
                }
            }
            if ((entry.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
            {
                UnseenErrors++;
            }
            try
            {
                Added?.Invoke(entry);
            }
            catch
            {
                // A listener's failure must not reach the logger.
            }
        }

        /// <summary>Parses a level name as typed by a player ("warning", "W", "info").</summary>
        public static bool TryParseLevel(string text, out LogLevel level)
        {
            level = LogLevel.None;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            switch (text.Trim().ToLowerInvariant())
            {
                case "f": case "fatal": level = LogLevel.Fatal; return true;
                case "e": case "error": level = LogLevel.Error; return true;
                case "w": case "warn": case "warning": level = LogLevel.Warning; return true;
                case "m": case "message": level = LogLevel.Message; return true;
                case "i": case "info": level = LogLevel.Info; return true;
                case "d": case "debug": level = LogLevel.Debug; return true;
                default: return false;
            }
        }

        // The BepInEx listener. One instance, added to Logger.Listeners at startup.
        internal sealed class Listener : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (eventArgs == null)
                {
                    return;
                }
                string source = eventArgs.Source != null ? eventArgs.Source.SourceName : "";
                Add(new ConsoleEntry(eventArgs.Level, source, eventArgs.Data != null ? eventArgs.Data.ToString() : ""));
            }

            public void Dispose() { }
        }
    }
}
