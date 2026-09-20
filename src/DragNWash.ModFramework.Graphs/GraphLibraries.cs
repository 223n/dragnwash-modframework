using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Graphs
{
    // Which library an operation or an event would come from, by the first part
    // of its name. Only for a name the registry does not know, and only when it
    // knows no other name under the same first part either: a graph written
    // against the Inspector then says "needs the Inspector library" instead of
    // "no operation named inspector.objects.children", which sends its author
    // looking for a typo that is not there. The libraries that are always
    // installed are left out (the core, this one), so a misspelt scene.loaded
    // stays a misspelling.
    internal static class GraphLibraries
    {
        private static readonly Dictionary<string, string> ByFirstPart = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["log"] = "Tool window",
            ["assets"] = "Assets",
            ["dialogue"] = "Dialogue",
            ["text"] = "Text",
            ["saves"] = "Flags and saves",
            ["inspector"] = "Inspector",
            ["code"] = "Inspector",
            ["objects"] = "Overrides",
            ["bridge"] = "Bridge",
        };

        /// <summary>
        /// The library the name belongs to, or null when it is not a library's
        /// name or when <paramref name="known"/> already holds names under the
        /// same first part (so the library is there and the name is wrong).
        /// </summary>
        internal static string For(string name, params string[][] known)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            int dot = name.IndexOf('.');
            if (dot <= 0)
            {
                return null;
            }
            string first = name.Substring(0, dot);
            string library;
            if (!ByFirstPart.TryGetValue(first, out library))
            {
                return null;
            }
            string prefix = first + ".";
            return known.Any(list => list.Any(n => n.StartsWith(prefix, StringComparison.Ordinal))) ? null : library;
        }
    }

    /// <summary>The two events the Graphs library owns and raises itself.</summary>
    internal static class GraphEvents
    {
        internal const string Timer = "timer.every";
        internal const string Key = "key.pressed";
    }
}
