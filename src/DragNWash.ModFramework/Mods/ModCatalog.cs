using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;

namespace DragNWash.ModFramework.Mods
{
    // Everything the Mods screen lists: plugins BepInEx loaded, plus plugins that
    // are switched off (their DLL is renamed, so BepInEx no longer knows them and
    // the framework remembers them from the switched-off list).
    internal static class ModCatalog
    {
        internal sealed class Entry
        {
            public string Guid;
            public string Name;
            public string Version;
            public ModInfo Info;

            public string DisplayName => string.IsNullOrEmpty(Info?.DisplayName) ? Name : Info.DisplayName;

            // Path under BepInEx/plugins; null when the plugin lives elsewhere and
            // cannot be switched off from here.
            public string RelativePath;

            public bool Loaded;
            public bool IsFramework;

            // What the player wants for the next launch.
            public bool WantOn;

            // GUIDs of loaded plugins that cannot load without this one.
            public List<string> Dependents = new List<string>();

            public bool CanSwitch => RelativePath != null && !IsFramework;
        }

        internal static List<Entry> Build()
        {
            var entries = new List<Entry>();
            List<DisabledMods.Record> desired = DisabledMods.ReadDesired(Paths.ConfigPath);
            var desiredPaths = new HashSet<string>(desired.Select(r => r.RelativePath), StringComparer.OrdinalIgnoreCase);

            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                BepInPlugin meta = plugin.Metadata;
                string rel = RelativeToPlugins(plugin.Location);
                entries.Add(new Entry
                {
                    Guid = meta.GUID,
                    Name = meta.Name,
                    Version = meta.Version.ToString(),
                    Info = ModFramework.GetInfo(meta.GUID),
                    RelativePath = rel,
                    Loaded = true,
                    IsFramework = meta.GUID == ModFramework.Guid || string.Equals(rel, FrameworkRelativePath, StringComparison.OrdinalIgnoreCase),
                    WantOn = rel == null || !desiredPaths.Contains(rel),
                });
            }

            foreach (DisabledMods.Record record in desired)
            {
                if (entries.Any(e => string.Equals(e.RelativePath, record.RelativePath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                entries.Add(new Entry
                {
                    Guid = record.Guid,
                    Name = string.IsNullOrEmpty(record.Name) ? Path.GetFileNameWithoutExtension(record.RelativePath) : record.Name,
                    Version = record.Version,
                    RelativePath = record.RelativePath,
                    Loaded = false,
                    WantOn = false,
                });
            }

            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                foreach (BepInDependency dep in plugin.Dependencies)
                {
                    if ((dep.Flags & BepInDependency.DependencyFlags.HardDependency) == 0)
                    {
                        continue;
                    }
                    Entry target = entries.FirstOrDefault(e => e.Guid == dep.DependencyGUID);
                    target?.Dependents.Add(plugin.Metadata.GUID);
                }
            }

            return entries
                .OrderBy(e => e.IsFramework ? 0 : 1)
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Records what the player wants and writes it for the preloader. Every
        // plugin in the same DLL follows, since the file is what gets renamed.
        internal static void SetWantOn(List<Entry> entries, Entry entry, bool on)
        {
            if (!entry.CanSwitch)
            {
                return;
            }
            foreach (Entry e in entries)
            {
                if (string.Equals(e.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    e.WantOn = on;
                }
            }

            var records = new List<DisabledMods.Record>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in entries)
            {
                if (e.WantOn || e.RelativePath == null || !seen.Add(e.RelativePath))
                {
                    continue;
                }
                records.Add(new DisabledMods.Record { RelativePath = e.RelativePath, Guid = e.Guid, Name = e.DisplayName, Version = e.Version });
            }
            DisabledMods.WriteDesired(Paths.ConfigPath, records);
            ModFramework.Log.LogInfo($"{entry.Name} will be switched {(on ? "on" : "off")} at the next launch.");
        }

        internal static string NameOf(List<Entry> entries, string guid)
        {
            Entry e = entries.FirstOrDefault(x => x.Guid == guid);
            return e != null ? e.DisplayName : guid;
        }

        private static string FrameworkRelativePath => RelativeToPlugins(typeof(ModCatalog).Assembly.Location);

        private static string RelativeToPlugins(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return null;
            }
            try
            {
                string root = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(location);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return DisabledMods.NormalizeRelative(full.Substring(root.Length));
            }
            catch
            {
                return null;
            }
        }
    }
}
