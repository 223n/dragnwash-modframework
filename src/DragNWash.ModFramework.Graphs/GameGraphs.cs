using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Graphs
{
    /// <summary>
    /// Graphs: mods with no code that say <em>when this happens, do these
    /// things</em>. A folder in BepInEx/plugins with a <c>mod.json</c> and
    /// <c>graphs/*.json</c> answers the events the libraries raise
    /// (<see cref="Operations.RegisterEvent"/>) and calls the operations they
    /// registered (<see cref="Operations"/>) — nothing else: no methods by name,
    /// no reflection, no files, no network. Every file is checked before
    /// anything runs, all the graphs together get a millisecond a frame, and a
    /// graph that fails three times in a row is switched off for the session.
    /// Experimental. See docs/GRAPHS.md.
    /// </summary>
    public static class GameGraphs
    {
        /// <summary>The library's BepInEx GUID, for <c>[BepInDependency]</c>.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.graphs";

        /// <summary>The library's version.</summary>
        public const string Version = "0.1.0";

        /// <summary>What was read from one graph file, and how it is going.</summary>
        public sealed class GraphReport
        {
            /// <summary>GUID of the mod the graph belongs to.</summary>
            public string ModGuid { get; internal set; }
            /// <summary>That mod's name on the Mods screen.</summary>
            public string Mod { get; internal set; }
            /// <summary>The graph's name, or its file name when it gives none.</summary>
            public string Name { get; internal set; }
            /// <summary>Where it is, under the mod's folder: <c>graphs/scene-notes.json</c>.</summary>
            public string File { get; internal set; }
            /// <summary>The events it answers.</summary>
            public IReadOnlyList<string> Events { get; internal set; }
            /// <summary>The operations it calls that change nothing.</summary>
            public IReadOnlyList<string> Reads { get; internal set; }
            /// <summary>The operations it calls that change something.</summary>
            public IReadOnlyList<string> Changes { get; internal set; }
            /// <summary>Libraries it needs that are not installed.</summary>
            public IReadOnlyList<string> Needs { get; internal set; }
            /// <summary>What is wrong with the file; while there is any, it does not run.</summary>
            public IReadOnlyList<string> Problems { get; internal set; }
            /// <summary>Runs going now.</summary>
            public int Running { get; internal set; }
            /// <summary>Runs started this session.</summary>
            public int Started { get; internal set; }
            /// <summary>Failures in a row; three switch it off.</summary>
            public int Failures { get; internal set; }
            /// <summary>Whether it runs, and if not, why not.</summary>
            public string State { get; internal set; }
        }

        /// <summary>Every graph found at startup, or at the last <see cref="Reload"/>.</summary>
        public static IReadOnlyList<GraphReport> Loaded =>
            (GraphsPlugin.Instance != null ? GraphsPlugin.Instance.Graphs : new List<Graph>()).Select(Report).ToList();

        /// <summary>
        /// Stops every graph, puts back what they changed, reads the files again
        /// and checks them against the operations registered now. Nothing carries
        /// over, so a reload is the same as a fresh start. Returns a line for the
        /// log.
        /// </summary>
        public static string Reload() => GraphsPlugin.Instance != null ? GraphsPlugin.Instance.Reload() : "The Graphs library is not running.";

        /// <summary>
        /// Stops one graph for this session and puts back what it changed;
        /// <paramref name="which"/> is its file name or its name. Returns a line
        /// for the log.
        /// </summary>
        public static string Stop(string which) => GraphsPlugin.Instance != null ? GraphsPlugin.Instance.StopOne(which) : "The Graphs library is not running.";

        internal static GraphReport Report(Graph graph)
        {
            return new GraphReport
            {
                ModGuid = graph.ModGuid,
                Mod = graph.ModName,
                Name = graph.Name,
                File = graph.File,
                Events = graph.Handlers.Select(h => h.Event).Distinct().ToList(),
                Reads = graph.Uses.Where(u => !u.Value).Select(u => u.Key).ToList(),
                Changes = graph.Uses.Where(u => u.Value).Select(u => u.Key).ToList(),
                Needs = graph.Needs.ToList(),
                Problems = graph.Problems.ToList(),
                Running = graph.Runs.Count(r => !r.Done),
                Started = graph.RunsStarted,
                Failures = graph.Failures,
                State = State(graph),
            };
        }

        internal static string State(Graph graph)
        {
            if (!graph.Ok) return $"{graph.Problems.Count} problem(s): it does not run";
            if (graph.Stopped) return "switched off for this session" + (graph.StoppedWhy != null ? " because " + graph.StoppedWhy : "");
            if (graph.Waiting != null) return $"waiting for {graph.Waiting}";
            return "runs";
        }
    }
}
