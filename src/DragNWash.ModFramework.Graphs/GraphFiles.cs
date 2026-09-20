using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragNWash.ModFramework.Graphs
{
    // Finding the graph files and reading them. The core finds the mods with no
    // code (DataMods) and lists them on the Mods screen; this library asks it
    // for the ones that carry a graphs/ folder and reads the files in there.
    //
    // Nothing here decides whether a file is sound: a file is read into a Graph
    // and handed to GraphCheck, which fills in either its handlers or its
    // problems. A file that cannot even be opened gets a problem of its own, so
    // it is listed like any other unsound file instead of disappearing.
    internal static class GraphFiles
    {
        /// <summary>
        /// Every graph of every data mod that carries a <c>graphs/</c> folder,
        /// in load order (folder name order, then file name order), checked
        /// against the operations registered now.
        /// </summary>
        internal static List<Graph> Scan(IGraphWorld world)
        {
            var graphs = new List<Graph>();
            foreach (DataMod mod in DataMods.With("graphs"))
            {
                string folder = mod.FolderFor("graphs");
                if (folder == null)
                {
                    continue;
                }
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    GraphsPlugin.Log.LogWarning($"[graphs] {mod.Name}: its graphs folder could not be read ({ex.Message}).");
                    continue;
                }
                foreach (string path in files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    graphs.Add(Read(world, mod, path));
                }
            }
            return graphs;
        }

        private static Graph Read(IGraphWorld world, DataMod mod, string path)
        {
            var graph = new Graph
            {
                ModGuid = mod.Guid,
                ModName = mod.Name,
                Path = path,
                File = "graphs/" + Path.GetFileName(path),
                Name = Path.GetFileNameWithoutExtension(path),
            };

            string text;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > GraphLimits.MaxFileBytes)
                {
                    graph.Problems.Add($"the file is {info.Length / 1024} KB; a graph may be at most {GraphLimits.MaxFileBytes / 1024} KB");
                    return graph;
                }
                text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                graph.Problems.Add($"the file could not be read ({ex.Message})");
                return graph;
            }

            try
            {
                GraphCheck.Read(world, graph, text);
            }
            catch (Exception ex)
            {
                // The checker reports what it understands; anything it did not
                // expect is this library's fault, not the file's, and says so
                // rather than taking the game down with it.
                graph.Problems.Add($"the file could not be checked ({ex.GetType().Name}: {ex.Message})");
                GraphsPlugin.Log.LogWarning($"[graphs] {graph.Where}: the checker threw. {ex}");
            }

            // The variables a run starts from are the file's, kept apart so a
            // reload and a fresh start give the same thing.
            graph.Variables = new Dictionary<string, object>(graph.FirstValues, StringComparer.Ordinal);
            return graph;
        }
    }
}
