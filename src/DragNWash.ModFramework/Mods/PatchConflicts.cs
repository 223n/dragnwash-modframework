using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace DragNWash.ModFramework.Mods
{
    // Finds game methods that more than one mod patches directly with Harmony.
    //
    // That is where mods most often break each other: two prefixes that both
    // decide whether the original runs, transpilers rewriting the same IL, or
    // patches that assume they run alone. The framework and libraries patch
    // shared hooks on purpose, so their patches do not count; everything else
    // that meets on one method is shown on the Mods screen for each mod
    // involved. It is a warning, not proof: many overlapping patches get along.
    internal static class PatchConflicts
    {
        internal sealed class Conflict
        {
            public string Method;
            public List<string> Guids = new List<string>();
            public bool Risky;
        }

        internal static List<Conflict> Find(List<ModCatalog.Entry> entries)
        {
            var conflicts = new List<Conflict>();
            Dictionary<Assembly, string> byAssembly = PluginAssemblies();
            var shared = new HashSet<string>(entries.Where(e => e.IsFramework || e.IsLibrary).Select(e => e.Guid).Where(g => g != null));

            foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info;
                try
                {
                    info = Harmony.GetPatchInfo(method);
                }
                catch
                {
                    continue;
                }
                if (info == null)
                {
                    continue;
                }

                var owners = new HashSet<string>();
                bool risky = false;
                var groups = new[]
                {
                    (Patches: (IEnumerable<Patch>)info.Prefixes, Kind: "prefix"),
                    (Patches: (IEnumerable<Patch>)info.Postfixes, Kind: "postfix"),
                    (Patches: (IEnumerable<Patch>)info.Transpilers, Kind: "transpiler"),
                    (Patches: (IEnumerable<Patch>)info.Finalizers, Kind: "finalizer"),
                };
                foreach (var group in groups)
                {
                    foreach (Patch patch in group.Patches)
                    {
                        string guid = OwnerOf(patch, byAssembly);
                        if (guid == null || shared.Contains(guid))
                        {
                            continue;
                        }
                        owners.Add(guid);
                        // A bool prefix can skip the original and later prefixes;
                        // a transpiler changes the code the others expect.
                        if (group.Kind == "transpiler" || (group.Kind == "prefix" && patch.PatchMethod?.ReturnType == typeof(bool)))
                        {
                            risky = true;
                        }
                    }
                }

                if (owners.Count >= 2)
                {
                    conflicts.Add(new Conflict
                    {
                        Method = (method.DeclaringType != null ? method.DeclaringType.Name + "." : "") + method.Name,
                        Guids = owners.OrderBy(g => g, StringComparer.Ordinal).ToList(),
                        Risky = risky,
                    });
                }
            }

            if (conflicts.Count > 0)
            {
                ModFramework.Log.LogInfo("Game methods patched by more than one mod: " +
                    string.Join("; ", conflicts.Select(c => c.Method + " (" + string.Join(", ", c.Guids) + ")")));
            }
            return conflicts;
        }

        // Harmony IDs are free text; most mods use their GUID, but not all. The
        // assembly the patch method lives in is the reliable link to a plugin.
        private static string OwnerOf(Patch patch, Dictionary<Assembly, string> byAssembly)
        {
            if (patch.owner != null && Chainloader.PluginInfos.ContainsKey(patch.owner))
            {
                return patch.owner;
            }
            Assembly assembly = patch.PatchMethod?.DeclaringType?.Assembly;
            return assembly != null && byAssembly.TryGetValue(assembly, out string guid) ? guid : null;
        }

        private static Dictionary<Assembly, string> PluginAssemblies()
        {
            var map = new Dictionary<Assembly, string>();
            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                Assembly assembly = plugin.Instance != null ? plugin.Instance.GetType().Assembly : null;
                if (assembly != null && !map.ContainsKey(assembly))
                {
                    map[assembly] = plugin.Metadata.GUID;
                }
            }
            return map;
        }
    }
}
