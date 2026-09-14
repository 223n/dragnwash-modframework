using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragNWash.ModFramework.Saves
{
    /// <summary>A known event flag: its group and what it means.</summary>
    public sealed class FlagInfo
    {
        internal FlagInfo() { }

        /// <summary>Flag id, as stored in the save.</summary>
        public string Id { get; internal set; }

        /// <summary>Group for listing, for example "Level flow".</summary>
        public string Group { get; internal set; }

        /// <summary>What sets it ("game", a Yarn node ...).</summary>
        public string SetBy { get; internal set; }

        /// <summary>What it means.</summary>
        public string Description { get; internal set; }
    }

    /// <summary>
    /// The catalog of event flags the game is known to use. The game's own
    /// registry only lists flags that were set at least once, so the catalog is
    /// what lets a tool show flags that are still unset.
    /// </summary>
    /// <remarks>
    /// Catalogs are CSV files with the columns <c>id,group,set_by,description</c>.
    /// A <c>FlagCatalog.csv</c> next to the library is read, and mods add their own
    /// with <see cref="AddCatalog"/>. The first row for an id wins.
    /// </remarks>
    public static class GameFlags
    {
        private static readonly List<string> Files = new List<string>();
        private static List<FlagInfo> _entries;
        private static Dictionary<string, FlagInfo> _byId;

        /// <summary>Every catalogued flag, in catalog order.</summary>
        public static IReadOnlyList<FlagInfo> Catalog
        {
            get
            {
                EnsureLoaded();
                return _entries;
            }
        }

        /// <summary>The catalog entry for an id, or null.</summary>
        public static FlagInfo Find(string id)
        {
            EnsureLoaded();
            return id != null && _byId.TryGetValue(id, out FlagInfo info) ? info : null;
        }

        /// <summary>Adds a catalog file. It is read the next time the catalog is used.</summary>
        public static void AddCatalog(string csvPath)
        {
            if (string.IsNullOrEmpty(csvPath))
            {
                return;
            }
            lock (Files)
            {
                if (!Files.Contains(csvPath, StringComparer.OrdinalIgnoreCase))
                {
                    Files.Add(csvPath);
                    _entries = null;
                }
            }
        }

        /// <summary>Reads the catalog files again (after one was edited).</summary>
        public static void Reload()
        {
            lock (Files)
            {
                _entries = null;
            }
        }

        private static void EnsureLoaded()
        {
            lock (Files)
            {
                if (_entries != null)
                {
                    return;
                }
                var entries = new List<FlagInfo>();
                var byId = new Dictionary<string, FlagInfo>(StringComparer.Ordinal);
                foreach (string path in Files)
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    try
                    {
                        foreach (Dictionary<string, string> row in CsvReader.ReadRows(path))
                        {
                            if (!row.TryGetValue("id", out string id) || string.IsNullOrEmpty(id) || byId.ContainsKey(id))
                            {
                                continue;
                            }
                            var info = new FlagInfo
                            {
                                Id = id,
                                Group = row.TryGetValue("group", out string g) && g.Length > 0 ? g : "Other",
                                SetBy = row.TryGetValue("set_by", out string s) ? s : "",
                                Description = row.TryGetValue("description", out string d) ? d : "",
                            };
                            entries.Add(info);
                            byId[id] = info;
                        }
                    }
                    catch (Exception ex)
                    {
                        SavesLibraryPlugin.Log.LogWarning($"Could not read the flag catalog {path}: {ex.Message}");
                    }
                }
                _entries = entries;
                _byId = byId;
            }
        }
    }
}
