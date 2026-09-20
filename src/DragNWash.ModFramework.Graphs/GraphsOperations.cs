using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DragNWash.ModFramework.Graphs
{
    // What the editor on the Bridge's page needs from the game (docs/GRAPHS.md,
    // "What the page needs from the Bridge"). Every one of these is for the page
    // and the console only: an AI client over MCP never sees them, and neither
    // does a graph - a graph that could write graphs would be a way around every
    // check this library makes.
    //
    // The three that change something (save, run, stop) are the first writes the
    // page's door accepts, and it accepts them *because* they are page-only.
    internal static class GraphsOperations
    {
        // A graph's file name: what a person types in the editor, and what ends
        // up in a folder. Letters, digits, - and _, so nothing here can climb
        // out of the mod's graphs folder.
        private static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

        internal static void Register()
        {
            string g = GameGraphs.Guid;
            var forThePage = OperationAudience.Console | OperationAudience.Page;

            Only(Operations.Register(g, "graphs.catalog",
                "Every operation a graph may call and every event it may answer, with their parameters: what the editor offers.",
                OperationKind.Read, "{ operations: [ { name, library, kind, description, returns, parameters } ], events: [ { name, description, values } ] }",
                args => Catalog()), forThePage);

            Only(Operations.Register(g, "graphs.list",
                "The graphs loaded: their mod, file, state, what they use and how they are going.",
                OperationKind.Read, "a list of { mod, mod_guid, file, name, state, events, reads, changes, needs, problems, running, started, failures }",
                args => GameGraphs.Loaded.Select(Report).ToList()), forThePage);

            Only(Operations.Register(g, "graphs.read",
                "One graph file's text, as it is on disk.",
                OperationKind.Read, "{ mod_guid, file, text }", Read,
                Operations.Parameter("mod_guid", OperationType.String, "The mod the graph belongs to.", true),
                Operations.Parameter("file", OperationType.String, "The graph's file, as graphs.list gives it (graphs/name.json).", true)), forThePage);

            Only(Operations.Register(g, "graphs.check",
                "Checks a graph given as text against the operations and events registered now, without saving it.",
                OperationKind.Read, "{ ok, problems: [ text ], events, reads, changes, needs }", Check,
                Operations.Parameter("text", OperationType.String, "The graph file's text.", true)), forThePage);

            Only(Operations.Register(g, "graphs.log",
                "The lines this library wrote after a given number, for the editor's live log.",
                OperationKind.Read, "{ next, lines: [ { at, level, text } ] }", Log,
                Operations.Parameter("after", OperationType.Number, "The number the last call returned as next; 0 for the start.")), forThePage);

            Only(Operations.Register(g, "graphs.save",
                "Writes a graph into a data mod's graphs folder and reads the files again. The mod is made when it does not exist yet; the file it replaces is kept as .bak.",
                OperationKind.Write, "{ mod_guid, file, path, created }", Save,
                Operations.Parameter("folder", OperationType.String, "The mod's folder name under BepInEx/plugins.", true),
                Operations.Parameter("name", OperationType.String, "The graph's name: letters, digits, - and _ (the file becomes name.json).", true),
                Operations.Parameter("text", OperationType.String, "The graph file's text; it has to pass the same check the game makes.", true),
                Operations.Parameter("mod_name", OperationType.String, "The name for a new mod on the Mods screen (the folder's name when left out).")), forThePage);

            Only(Operations.Register(g, "graphs.run",
                "Starts one handler of a graph now, with the values the editor gives, instead of waiting for its event.",
                OperationKind.Write, "{ started, graph, handler }", Run,
                Operations.Parameter("file", OperationType.String, "The graph, as graphs.list gives it.", true),
                Operations.Parameter("handler", OperationType.String, "The handler's id (h1).", true)), forThePage);

            Only(Operations.Register(g, "graphs.stop",
                "Stops a graph for this session and puts back what it changed.",
                OperationKind.Write, "{ stopped, put_back }", Stop,
                Operations.Parameter("file", OperationType.String, "The graph, as graphs.list gives it.", true)), forThePage);
        }

        private static void Only(Operation op, OperationAudience audience)
        {
            if (op != null)
            {
                op.Audience = audience;
            }
        }

        // ---- reads -------------------------------------------------------------

        private static object Catalog()
        {
            var operations = Operations.All
                .Where(o => (o.Audience & OperationAudience.Graphs) != 0)
                .Select(o => (object)new Dictionary<string, object>
                {
                    ["name"] = o.Name,
                    ["library"] = ModFramework.NameOf(o.Owner) ?? o.Owner,
                    ["kind"] = o.Kind == OperationKind.Write ? "write" : "read",
                    ["lasting"] = o.Lasting,
                    ["description"] = o.Description,
                    ["returns"] = o.Returns,
                    ["parameters"] = o.Parameters.Select(p => (object)new Dictionary<string, object>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type == OperationType.Number ? "number" : p.Type == OperationType.Boolean ? "boolean" : "text",
                        ["required"] = p.Required,
                        ["description"] = p.Description,
                        ["choices"] = p.Choices,
                    }).ToList(),
                }).ToList();

            var events = Operations.AllEvents.Select(e => (object)new Dictionary<string, object>
            {
                ["name"] = e.Name,
                ["description"] = e.Description,
                ["library"] = ModFramework.NameOf(e.Owner) ?? e.Owner,
                ["values"] = e.Values.Select(v => (object)new Dictionary<string, object>
                {
                    ["name"] = v.Name,
                    ["type"] = v.Type == OperationType.Number ? "number" : v.Type == OperationType.Boolean ? "boolean" : "text",
                    ["description"] = v.Description,
                }).ToList(),
            }).ToList();

            return new Dictionary<string, object> { ["operations"] = operations, ["events"] = events };
        }

        private static object Report(GameGraphs.GraphReport r)
        {
            return new Dictionary<string, object>
            {
                ["mod"] = r.Mod,
                ["mod_guid"] = r.ModGuid,
                ["file"] = r.File,
                ["name"] = r.Name,
                ["state"] = r.State,
                ["events"] = r.Events.ToList(),
                ["reads"] = r.Reads.ToList(),
                ["changes"] = r.Changes.ToList(),
                ["needs"] = r.Needs.ToList(),
                ["problems"] = r.Problems.ToList(),
                ["running"] = r.Running,
                ["started"] = r.Started,
                ["failures"] = r.Failures,
                ["clashes"] = r.Clashes.ToList(),
                ["shares"] = r.Shares.ToList(),
            };
        }

        private static object Read(OperationArgs args)
        {
            Graph graph = Graph(args.String("file"), args.String("mod_guid"));
            string text;
            try
            {
                text = File.ReadAllText(graph.Path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{graph.File} could not be read ({ex.Message}).");
            }
            return new Dictionary<string, object>
            {
                ["mod_guid"] = graph.ModGuid,
                ["file"] = graph.File,
                ["text"] = text,
            };
        }

        private static object Check(OperationArgs args)
        {
            string text = args.String("text") ?? "";
            if (text.Length > GraphLimits.MaxFileBytes)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["problems"] = new List<object> { $"the text is {text.Length / 1024} KB; a graph may be at most {GraphLimits.MaxFileBytes / 1024} KB" },
                };
            }
            var graph = new Graph { ModGuid = "page", ModName = "the editor", File = "graphs/unsaved.json", Name = "unsaved" };
            try
            {
                GraphCheck.Read(GraphsPlugin.Instance.World, graph, text);
            }
            catch (Exception ex)
            {
                graph.Problems.Add($"the text could not be checked ({ex.GetType().Name}: {ex.Message})");
            }
            return new Dictionary<string, object>
            {
                ["ok"] = graph.Ok,
                ["problems"] = graph.Problems.Select(p => (object)p).ToList(),
                ["events"] = graph.Handlers.Select(h => (object)h.Event).Distinct().ToList(),
                ["reads"] = graph.Uses.Where(u => !u.Value).Select(u => (object)u.Key).ToList(),
                ["changes"] = graph.Uses.Where(u => u.Value).Select(u => (object)u.Key).ToList(),
                ["needs"] = graph.Needs.Select(n => (object)n).ToList(),
            };
        }

        private static object Log(OperationArgs args)
        {
            int after = args.Int("after");
            return GraphLog.Since(after);
        }

        // ---- writes, for the page alone ----------------------------------------

        private static object Save(OperationArgs args)
        {
            string folder = (args.String("folder") ?? "").Trim();
            string name = (args.String("name") ?? "").Trim();
            string text = args.String("text") ?? "";

            if (!SafeName.IsMatch(folder))
            {
                throw new InvalidOperationException("The mod's folder is letters, digits, - and _ only.");
            }
            if (!SafeName.IsMatch(name))
            {
                throw new InvalidOperationException("A graph's name is letters, digits, - and _ only.");
            }
            if (text.Length > GraphLimits.MaxFileBytes)
            {
                throw new InvalidOperationException($"The text is {text.Length / 1024} KB; a graph may be at most {GraphLimits.MaxFileBytes / 1024} KB.");
            }

            // The same check the game makes at load: a file that would not run
            // is not written.
            var probe = new Graph { ModGuid = "page", ModName = "the editor", File = "graphs/" + name + ".json", Name = name };
            GraphCheck.Read(GraphsPlugin.Instance.World, probe, text);
            if (!probe.Ok)
            {
                throw new InvalidOperationException("The graph has " + probe.Problems.Count + " problem(s): " + string.Join("; ", probe.Problems.Take(3).ToArray()));
            }

            string plugins = BepInEx.Paths.PluginPath;
            string modFolder = Path.Combine(plugins, folder);
            bool created = !Directory.Exists(modFolder);
            if (!created)
            {
                // Never into a mod with code: that folder belongs to its author,
                // and BepInEx loads whatever is in it.
                if (Directory.GetFiles(modFolder, "*.dll").Length > 0)
                {
                    throw new InvalidOperationException($"{folder} is a mod with code; graphs go in a folder of their own.");
                }
            }

            string graphsFolder = Path.Combine(modFolder, "graphs");
            Directory.CreateDirectory(graphsFolder);

            string manifest = Path.Combine(modFolder, "mod.json");
            if (!File.Exists(manifest) && !File.Exists(manifest + ".disabled"))
            {
                string modName = args.String("mod_name");
                modName = string.IsNullOrEmpty(modName) ? folder : modName;
                File.WriteAllText(manifest, Manifest(modName), new System.Text.UTF8Encoding(false));
            }

            string file = Path.Combine(graphsFolder, name + ".json");
            if (File.Exists(file))
            {
                // One step back, so a save that turns out wrong is not the end of
                // the work it replaced.
                try
                {
                    File.Copy(file, file + ".bak", true);
                }
                catch (Exception ex)
                {
                    GraphsPlugin.Log.LogWarning($"[graphs] {name}.json could not be kept as .bak ({ex.Message}); saving anyway.");
                }
            }
            File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
            GraphsPlugin.Log.LogInfo($"[graphs] The editor saved {folder}/graphs/{name}.json ({text.Length} characters){(created ? ", in a new mod" : "")}.");

            // Read everything again, so the page sees the graph as the game does.
            GraphsPlugin.Instance.Reload();
            Graph saved = GraphsPlugin.Instance.Graphs.FirstOrDefault(x =>
                string.Equals(x.File, "graphs/" + name + ".json", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(x.Path))), folder, StringComparison.OrdinalIgnoreCase));

            return new Dictionary<string, object>
            {
                ["mod_guid"] = saved?.ModGuid,
                ["file"] = "graphs/" + name + ".json",
                ["path"] = folder + "/graphs/" + name + ".json",
                ["created"] = created,
            };
        }

        private static string Manifest(string modName)
        {
            string escaped = modName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\n" +
                   "  \"name\": \"" + escaped + "\",\n" +
                   "  \"version\": \"1.0.0\",\n" +
                   "  \"description\": \"Graphs made in the editor.\"\n" +
                   "}\n";
        }

        private static object Run(OperationArgs args)
        {
            Graph graph = Graph(args.String("file"), null);
            string id = args.String("handler");
            GraphHandler handler = graph.Handlers.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.Ordinal));
            if (handler == null)
            {
                throw new InvalidOperationException($"{graph.File} has no handler {id}; it has {string.Join(", ", graph.Handlers.Select(h => h.Id).ToArray())}.");
            }
            if (!graph.CanRun)
            {
                throw new InvalidOperationException($"{graph.File} is not running: {GameGraphs.State(graph)}.");
            }
            GraphsPlugin.Instance.StartFromPage(graph, handler);
            return new Dictionary<string, object>
            {
                ["started"] = true,
                ["graph"] = graph.File,
                ["handler"] = handler.Id,
            };
        }

        private static object Stop(OperationArgs args)
        {
            string said = GameGraphs.Stop(args.String("file"));
            return new Dictionary<string, object>
            {
                ["stopped"] = said.Contains("stopped"),
                ["said"] = said,
            };
        }

        // ---- finding a graph ---------------------------------------------------

        private static Graph Graph(string file, string modGuid)
        {
            if (string.IsNullOrEmpty(file))
            {
                throw new InvalidOperationException("Which graph? Its file, as graphs.list gives it.");
            }
            List<Graph> graphs = GraphsPlugin.Instance.Graphs
                .Where(x => (modGuid == null || string.Equals(x.ModGuid, modGuid, StringComparison.OrdinalIgnoreCase))
                            && (string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Path.GetFileName(x.File), file, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (graphs.Count == 0)
            {
                throw new InvalidOperationException($"No graph called {file}.");
            }
            if (graphs.Count > 1 && modGuid == null)
            {
                throw new InvalidOperationException($"{graphs.Count} graphs are called {file}; say which mod with mod_guid.");
            }
            return graphs[0];
        }
    }
}
