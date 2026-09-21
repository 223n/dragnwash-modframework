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
                Operations.Parameter("handler", OperationType.String, "The handler's id (h1).", true),
                Operations.Parameter("mod_guid", OperationType.String, "Which mod's graph, when two mods have one of that name.")), forThePage);

            Only(Operations.Register(g, "graphs.rename",
                "Gives a graph another name, in the mod it is already in. The file is moved, not copied, so there is one graph afterwards and not two.",
                OperationKind.Write, "{ file }", Rename,
                Operations.Parameter("file", OperationType.String, "The graph, as graphs.list gives it.", true),
                Operations.Parameter("name", OperationType.String, "The new name: letters, digits, - and _ (the file becomes name.json).", true),
                Operations.Parameter("mod_guid", OperationType.String, "Which mod's graph, when two mods have one of that name.")), forThePage);

            Only(Operations.Register(g, "graphs.delete",
                "Takes a graph out of its mod. The file is kept beside it as <name>.json.bak, so a delete by mistake is not the end of the work.",
                OperationKind.Write, "{ kept }", Delete,
                Operations.Parameter("file", OperationType.String, "The graph, as graphs.list gives it.", true),
                Operations.Parameter("mod_guid", OperationType.String, "Which mod's graph, when two mods have one of that name.")), forThePage);

            Only(Operations.Register(g, "graphs.reload",
                "Reads the graph files again: what was added, changed or removed outside the editor. Every graph's changes are put back first, and nothing carries over.",
                OperationKind.Write, "what it says in the console", args => new Dictionary<string, object> { ["said"] = GameGraphs.Reload() }), forThePage);

            Only(Operations.Register(g, "graphs.preview",
                "Shows a value in the game while the editor is being used: the same write a graph would make, put back like any other. The editor sends it while a colour is dragged.",
                OperationKind.Write, "what the write returned", Preview,
                Operations.Parameter("path", OperationType.String, "The object's path in the scene.", true),
                Operations.Parameter("component", OperationType.String, "The component's type name.", true),
                Operations.Parameter("member", OperationType.String, "The field or property to set.", true),
                Operations.Parameter("value", OperationType.String, "The value, as the Inspector's rows show it.", true),
                Operations.Parameter("index", OperationType.Number, "Which component, when the object has more than one of that type."),
                Operations.Parameter("private", OperationType.Boolean, "Set a private field or property."),
                Operations.Parameter("scene", OperationType.String, "Only an object in this scene.")), forThePage);

            Only(Operations.Register(g, "graphs.stop",
                "Stops a graph for this session and puts back what it changed.",
                OperationKind.Write, "{ stopped, put_back }", Stop,
                Operations.Parameter("file", OperationType.String, "The graph, as graphs.list gives it.", true),
                Operations.Parameter("mod_guid", OperationType.String, "Which mod's graph, when two mods have one of that name.")), forThePage);
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
            if (!File.Exists(graph.Path))
            {
                throw new InvalidOperationException($"{graph.Where} is not there any more; \"graphs reload\", or Reload on the page, clears it from the list.");
            }
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
            // Quotes, backslashes and anything below a space: a name typed into
            // the page must not be able to write a mod.json the reader refuses.
            var clean = new System.Text.StringBuilder();
            foreach (char c in modName)
            {
                if (c < ' ') { clean.Append(' '); continue; }
                if (c == '\\' || c == '"') { clean.Append('\\'); }
                clean.Append(c);
            }
            string escaped = clean.ToString();
            return "{\n" +
                   "  \"name\": \"" + escaped + "\",\n" +
                   "  \"version\": \"1.0.0\",\n" +
                   "  \"description\": \"Graphs made in the editor.\"\n" +
                   "}\n";
        }

        // The editor showing a value in the game, through the operation that
        // knows how to make and put back that change. The page's door takes a
        // write that is offered to the page alone, and objects.member.set is
        // offered to everybody, so the editor asks for it by this name instead
        // of the door being opened wider.
        private static object Preview(OperationArgs args)
        {
            if (Operations.Find("objects.member.set") == null)
            {
                throw new InvalidOperationException("The Overrides library is not installed, so there is nothing to show it with.");
            }
            var send = new Dictionary<string, object>
            {
                ["path"] = args.String("path"),
                ["component"] = args.String("component"),
                ["member"] = args.String("member"),
                ["value"] = args.String("value"),
            };
            if (args.Has("index")) send["index"] = args.Number("index");
            if (args.Has("private")) send["private"] = args.Bool("private");
            if (!string.IsNullOrEmpty(args.String("scene"))) send["scene"] = args.String("scene");
            OperationResult result = Operations.CallNow("objects.member.set", send, "page preview", OperationAudience.None, false);
            if (!result.Ok)
            {
                throw new InvalidOperationException(result.Error);
            }
            return result.Value;
        }

        private static object Rename(OperationArgs args)
        {
            Graph graph = Graph(args.String("file"), args.String("mod_guid"));
            string name = (args.String("name") ?? "").Trim();
            if (!SafeName.IsMatch(name))
            {
                throw new InvalidOperationException("A graph's name is letters, digits, - and _ only.");
            }
            if (!File.Exists(graph.Path))
            {
                throw new InvalidOperationException($"{graph.Where} is not there any more.");
            }
            string folder = Path.GetDirectoryName(graph.Path);
            string to = Path.Combine(folder, name + ".json");
            if (string.Equals(to, graph.Path, StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object> { ["file"] = graph.File };
            }
            if (File.Exists(to))
            {
                throw new InvalidOperationException($"{name}.json is already in that mod; pick another name.");
            }
            try
            {
                File.Move(graph.Path, to);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{graph.Where} could not be renamed: {ex.Message}");
            }
            GraphsPlugin.Log.LogInfo($"[graphs] {graph.Where} is now graphs/{name}.json.");
            GraphsPlugin.Instance.Reload();
            return new Dictionary<string, object> { ["file"] = "graphs/" + name + ".json" };
        }

        // Out of the mod, but not off the disk: the file becomes its own .bak,
        // which is what a save does with the file it replaces.
        private static object Delete(OperationArgs args)
        {
            Graph graph = Graph(args.String("file"), args.String("mod_guid"));
            string path = graph.Path;
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"{graph.Where} is not there any more.");
            }
            string kept = path + ".bak";
            try
            {
                File.Copy(path, kept, true);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{graph.Where} could not be removed: {ex.Message}");
            }
            GraphsPlugin.Log.LogInfo($"[graphs] {graph.Where} was removed from the page; the file is kept as {Path.GetFileName(kept)}.");
            GraphsPlugin.Instance.Reload();
            return new Dictionary<string, object> { ["kept"] = Path.GetFileName(kept) };
        }

        private static object Run(OperationArgs args)
        {
            Graph graph = Graph(args.String("file"), args.String("mod_guid"));
            string id = args.String("handler");
            GraphHandler handler = graph.Handlers.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.Ordinal));
            if (handler == null)
            {
                throw new InvalidOperationException($"{graph.File} has no handler {id}; it has {string.Join(", ", graph.Handlers.Select(h => h.Id).ToArray())}.");
            }
            if (!graph.Ok)
            {
                throw new InvalidOperationException($"{graph.File} does not run: {string.Join(" ", graph.Problems.ToArray())}");
            }
            if (graph.Waiting != null)
            {
                throw new InvalidOperationException($"{graph.File} is waiting for {graph.Waiting}.");
            }
            // Run is somebody asking for it by hand, so a graph that was stopped
            // for the session - by the button, or by failing three times - starts
            // again here rather than refusing. Its changes were put back when it
            // stopped, so there is nothing to undo first.
            bool again = graph.Stopped;
            if (again)
            {
                graph.Stopped = false;
                graph.StoppedWhy = null;
                graph.Failures = 0;
                GraphsPlugin.Log.LogInfo($"[graphs] {graph.Where} was started again from the page.");
            }
            GraphsPlugin.Instance.StartFromPage(graph, handler);
            return new Dictionary<string, object>
            {
                ["started"] = true,
                ["graph"] = graph.File,
                ["handler"] = handler.Id,
                ["started_again"] = again,
            };
        }

        private static object Stop(OperationArgs args)
        {
            // Through the same lookup as the rest, so mod_guid is honoured: the
            // console's by-name stop is a different road with a different sign.
            Graph graph = Graph(args.String("file"), args.String("mod_guid"));
            string said = GraphsPlugin.Instance.Stop(graph);
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
                throw new InvalidOperationException(
                    $"{graphs.Count} mods have a graph called {file}: " +
                    string.Join(", ", graphs.Select(x => $"{x.ModName ?? x.ModGuid} ({x.ModGuid})").ToArray()) +
                    ". Say which with mod_guid.");
            }
            return graphs[0];
        }
    }
}
