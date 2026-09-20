using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework.Graphs
{
    // The Graphs library's BepInEx entry point. It owns three things and
    // nothing else: when the files are read, when a run may start, and how much
    // of a frame the runs may have. Everything a graph can reach comes from the
    // operations registry through GraphRegistry.
    //
    // The order matters. A file is checked against the operations and events
    // registered at the moment it is read, so reading too early refuses calls
    // to libraries that simply had not registered yet. ModFramework.Ready is
    // not that moment: the core raises it inside its own Awake, before the
    // other plugins have theirs. The first Update is, because BepInEx creates
    // every plugin in one frame and Unity runs all of their Awake and Start
    // methods before any Update.
    //
    // An event never starts a run where it is raised. Operations.Happened runs
    // inside the library that raised it - in the middle of a dialogue line, a
    // scene load, a save - and a graph that ran there would put its calls, its
    // failures and its time inside someone else's work. They are queued and
    // started in this plugin's own Update, which is also where the frame budget
    // is kept.
    [BepInPlugin(GameGraphs.Guid, "DragNWash.ModFramework.Graphs", GameGraphs.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ToolWindow.ToolWindow.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class GraphsPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static GraphsPlugin Instance;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _budgetMs;
        private GraphRegistry _world;
        private GraphRunner _runner;
        private bool _read;

        // Events that arrived since the last frame, in the order they arrived.
        private readonly List<KeyValuePair<string, IDictionary<string, object>>> _queued =
            new List<KeyValuePair<string, IDictionary<string, object>>>();

        // The keys graphs listen for, worked out once per load: the keyboard is
        // asked about these and no others. BepInEx's KeyboardShortcut does the
        // asking, so this library needs no reference to Unity's legacy input
        // module, which the framework does not carry.
        private readonly List<KeyValuePair<KeyboardShortcut, string>> _keys = new List<KeyValuePair<KeyboardShortcut, string>>();

        private bool _timers;

        internal List<Graph> Graphs => _runner != null ? _runner.Graphs : new List<Graph>();

        /// <summary>What the checker works against: the operations and events registered now.</summary>
        internal IGraphWorld World => _world;

        /// <summary>
        /// Starts one handler because the editor asked, not because its event
        /// happened: the same start the runner makes, with no values, so a
        /// person can try a graph without waiting for the thing it answers.
        /// </summary>
        internal void StartFromPage(Graph graph, GraphHandler handler)
        {
            _runner.Start(graph, handler, null);
        }

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            ModFramework.Register(new ModInfo
            {
                Guid = GameGraphs.Guid,
                DisplayName = "Drag'n Wash ModFramework: Graphs",
                Description = "Runs mods that have no code: files that say when something happens, do these things. A graph may only call the operations the libraries registered. Experimental.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            _enabled = Config.Bind("General", "Enabled", true,
                "Run the graphs of the mods in BepInEx/plugins (folders with mod.json and graphs/). Each mod can also be switched off on the Mods screen.");
            _budgetMs = Config.Bind("Graphs", "FrameBudgetMs", 1f,
                new ConfigDescription(
                    "How much of each frame all graphs together may use, in milliseconds. What is left over waits for the next frame; a single operation call is never cut in half.",
                    new AcceptableValueRange<float>(0.1f, 8f)));

            _world = new GraphRegistry();
            _runner = new GraphRunner(_world);

            RegisterOwnEvents();
            GraphsOperations.Register();

            // Queued here, started in Update: see the note at the top.
            Operations.Happened += Queue;

            // A library that goes away takes its operations with it. The runner
            // marks the graphs that used them as waiting instead of failing, and
            // picks them up again when the operations come back.
            ModReload.Unloading += (guid, assembly) => _runner.CheckOperations();

            AddConsoleCommand();
        }

        // The two events this library owns. Everything else a graph can answer
        // is registered by the library that hooks it.
        private void RegisterOwnEvents()
        {
            try
            {
                Operations.RegisterEvent(GameGraphs.Guid, GraphEvents.Timer,
                    "Every so many seconds, while the game runs. A graph says how often (every, at least 0.5 seconds).",
                    new OperationParameter { Name = "seconds", Type = OperationType.Number, Description = "How long since this handler last ran." });
                Operations.RegisterEvent(GameGraphs.Guid, GraphEvents.Key,
                    "A key on the keyboard was pressed. A graph says which key; the framework's own key (F1) is refused.",
                    new OperationParameter { Name = "key", Type = OperationType.String, Description = "The key's name, as Unity writes it (F6, G, LeftBracket)." });
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[graphs] The library's own events could not be registered: {ex.Message}");
            }
        }

        // ---- reading the files -------------------------------------------------

        private void Load()
        {
            List<Graph> graphs = GraphFiles.Scan(_world);
            _runner.Graphs.Clear();
            _runner.Graphs.AddRange(graphs);
            _read = true;

            WatchedKeys();
            _timers = graphs.Any(g => g.Handlers.Any(h => h.Event == GraphEvents.Timer));

            foreach (Graph graph in graphs)
            {
                if (!graph.Ok)
                {
                    Log.LogWarning($"[graphs] {graph.Where}: {graph.Problems.Count} problem(s), so it does not run.");
                    foreach (string problem in graph.Problems)
                    {
                        Log.LogWarning($"[graphs]   {problem}");
                    }
                    // The Mods screen marks the mod, as it does for a graph that
                    // is switched off after failing.
                    _world.Unavailable(graph, $"{graph.Problems.Count} problem(s) in the file: {graph.Problems[0]}");
                    continue;
                }
                string needs = graph.Needs.Count > 0 ? $"; needs {string.Join(", ", graph.Needs.ToArray())}" : "";
                Log.LogInfo($"[graphs] {graph.Where}: {graph.Handlers.Count} handler(s), {graph.Uses.Count} operation(s){needs}.");
            }

            if (graphs.Count == 0)
            {
                Log.LogInfo("[graphs] No graphs: a folder in BepInEx/plugins with mod.json and graphs/*.json is one.");
            }
            else if (!_enabled.Value)
            {
                Log.LogInfo("[graphs] [General] Enabled is off, so nothing runs.");
            }

            AddModsPages(graphs);
        }

        private void WatchedKeys()
        {
            _keys.Clear();
            foreach (Graph graph in _runner.Graphs)
            {
                foreach (GraphHandler handler in graph.Handlers)
                {
                    if (handler.Event != GraphEvents.Key || string.IsNullOrEmpty(handler.Key))
                    {
                        continue;
                    }
                    KeyCode code;
                    if (!Enum.TryParse(handler.Key, true, out code) || _keys.Any(k => k.Key.MainKey == code))
                    {
                        continue;
                    }
                    _keys.Add(new KeyValuePair<KeyboardShortcut, string>(new KeyboardShortcut(code), handler.Key));
                }
            }
        }

        // ---- the frame ---------------------------------------------------------

        private void Queue(OperationEvent kind, IDictionary<string, object> values)
        {
            if (!_enabled.Value || !_read || kind == null)
            {
                return;
            }
            // The library's own two are raised from Update, where their rules
            // (which key, and how long since last time) are known.
            if (kind.Name == GraphEvents.Timer || kind.Name == GraphEvents.Key)
            {
                return;
            }
            _queued.Add(new KeyValuePair<string, IDictionary<string, object>>(kind.Name, values));
        }

        private void Update()
        {
            if (!_read)
            {
                // The first frame: every plugin has had its Awake and its Start,
                // so the registry holds every operation and event there will be.
                Load();
            }
            if (!_enabled.Value || !_read)
            {
                return;
            }

            // Whichever event started it, a run begins here.
            if (_queued.Count > 0)
            {
                // A copy, because a run that starts here may raise something
                // else before the loop is over.
                var now = _queued.ToArray();
                _queued.Clear();
                foreach (KeyValuePair<string, IDictionary<string, object>> e in now)
                {
                    _runner.Raise(e.Key, e.Value);
                }
            }

            if (_timers)
            {
                // Each handler keeps its own next-due time; the runner skips the
                // ones that are not due yet.
                _runner.Raise(GraphEvents.Timer, null);
            }

            for (int i = 0; i < _keys.Count; i++)
            {
                if (_keys[i].Key.IsDown())
                {
                    _runner.Raise(GraphEvents.Key, new Dictionary<string, object> { ["key"] = _keys[i].Value });
                }
            }

            _runner.Frame(_budgetMs.Value);
        }

        // ---- the console -------------------------------------------------------

        private void AddConsoleCommand()
        {
            try
            {
                RegisterCommand();
            }
            catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is TypeLoadException || ex is MissingMethodException)
            {
                // No Tool window: no console, nothing to add.
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RegisterCommand()
        {
            ToolWindow.ToolWindow.AddCommand(GameGraphs.Guid, "graphs",
                "graphs | graphs reload | graphs stop <file or name>  (the graphs, what they answer and how they are going; reload reads the files again; stop ends one for this session and puts back what it changed)",
                Command);
        }

        private string Command(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                return Reload();
            }
            if (args.Length > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                return args.Length > 1
                    ? StopOne(string.Join(" ", args.Skip(1).ToArray()))
                    : "Which graph? graphs stop <file or name>";
            }
            if (Graphs.Count == 0)
            {
                return "No graphs: a folder in BepInEx/plugins with mod.json and graphs/*.json is one.";
            }
            var lines = new List<string>();
            foreach (GameGraphs.GraphReport r in GameGraphs.Loaded)
            {
                lines.Add($"{r.Mod} {r.File} ({r.Name}): {r.State}");
                if (r.Events.Count > 0) lines.Add("  answers " + string.Join(", ", r.Events.ToArray()));
                if (r.Reads.Count > 0) lines.Add("  reads " + string.Join(", ", r.Reads.ToArray()));
                if (r.Changes.Count > 0) lines.Add("  changes " + string.Join(", ", r.Changes.ToArray()));
                if (r.Needs.Count > 0) lines.Add("  needs " + string.Join(", ", r.Needs.ToArray()));
                foreach (string p in r.Problems) lines.Add("  " + p);
                if (r.Started > 0) lines.Add($"  {r.Running} run(s) going, {r.Started} started, {r.Failures} failure(s) in a row");
            }
            if (!_enabled.Value) lines.Add("[General] Enabled is off: nothing runs.");
            return string.Join("\n", lines.ToArray());
        }

        // ---- reload and stop ---------------------------------------------------

        internal string Reload()
        {
            int back = 0;
            foreach (Graph graph in Graphs.ToList())
            {
                // Quietly: a graph that is about to be read again did nothing
                // wrong, and a warning per graph would say it did.
                back += _runner.StopForReload(graph);
            }
            // The folders may have changed too (a mod added by hand): the core
            // looks again, and every library that reads data mods sees it.
            DataMods.Rescan();
            Load();
            string line = $"Graphs reloaded: {back} change(s) put back, {Graphs.Count} graph(s) read, {Graphs.Count(g => g.Ok)} of them sound.";
            Log.LogInfo("[graphs] " + line);
            return line;
        }

        internal string StopOne(string which)
        {
            if (string.IsNullOrEmpty(which))
            {
                return "Which graph? graphs stop <file or name>";
            }
            Graph graph = Graphs.FirstOrDefault(g =>
                string.Equals(g.File, which, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(System.IO.Path.GetFileName(g.File), which, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(g.Name, which, StringComparison.OrdinalIgnoreCase));
            if (graph == null)
            {
                return $"No graph called \"{which}\". The names are: " +
                       string.Join(", ", Graphs.Select(g => g.File).ToArray());
            }
            int back = _runner.StopByHand(graph);
            string line = $"{graph.Where} stopped for this session; {back} change(s) put back.";
            Log.LogInfo("[graphs] " + line);
            return line;
        }

        // ---- the Mods screen ---------------------------------------------------

        private readonly HashSet<string> _paged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // One Graphs page per mod that has graphs, beside its Settings button.
        // An older core has no pages: the graphs still run, and the Mods screen
        // shows the mod without one.
        private void AddModsPages(List<Graph> graphs)
        {
            foreach (string guid in graphs.Select(g => g.ModGuid).Distinct().ToList())
            {
                if (!_paged.Add(guid))
                {
                    continue;
                }
                try
                {
                    AddPage(guid);
                }
                catch (MissingMethodException)
                {
                    Log.LogInfo("[graphs] The framework core is older than 1.4.0, so the graphs are not shown on the Mods screen.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[graphs] A graphs page could not be added: {ex.Message}");
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void AddPage(string guid)
        {
            ModFramework.AddModsPage(new ModsScreenPage
            {
                Guid = guid,
                Title = "Graphs",
                Build = panel => GraphsPage.Build(panel, guid),
            });
        }
    }
}
