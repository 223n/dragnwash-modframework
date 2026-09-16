using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Marks a plugin class as safe to reload while the game runs (see
    /// <see cref="ModReload"/>). The same as <c>ModInfo.Reloadable = true</c>,
    /// for a mod that does not register a <c>ModInfo</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ReloadableModAttribute : Attribute
    {
    }

    /// <summary>
    /// Reloads a mod's DLL while the game runs, for people who build mods:
    /// after a build copies the new DLL into <c>BepInEx/plugins/&lt;Mod&gt;/</c>,
    /// the new build takes the place of the running one. Only mods that say
    /// they are reloadable (<c>ModInfo.Reloadable</c> or
    /// <see cref="ReloadableModAttribute"/>) are ever reloaded, and only while
    /// developer tools are on. Libraries are never reloaded. Experimental
    /// (core 1.2.0); see docs/MOD_RELOAD.md.
    /// </summary>
    /// <remarks>
    /// On Windows the running DLL is mapped by Mono and cannot be overwritten,
    /// so a build delivers the new file as <c>&lt;Mod&gt;.dll.new</c> next to
    /// it; the framework reloads from that file, and the preloader patcher
    /// makes it the real DLL at the next launch. Where overwriting works
    /// (Linux), the DLL itself is watched too.
    /// <para>
    /// Mono never unloads an assembly: the new build is loaded next to the old
    /// one, everything the old build registered with the framework is taken
    /// out by its GUID and by its assembly, its Harmony patches are removed,
    /// its plugin component is destroyed, and the new build's plugin is added
    /// to BepInEx's manager object, where its Awake runs as at startup.
    /// </para>
    /// </remarks>
    public static class ModReload
    {
        /// <summary>
        /// Raised just before a mod's old build is taken down, with its GUID and
        /// its assembly. A library removes what that mod registered with it here:
        /// by GUID where it kept one, else by assembly with <see cref="Prune"/>.
        /// </summary>
        public static event Action<string, Assembly> Unloading;

        private static ConfigEntry<bool> _watch;
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FileSystemWatcher> Watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> Pending = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly Queue<string> Queued = new Queue<string>();
        private static readonly object Lock = new object();
        private static string MarkerPath => Path.Combine(Paths.ConfigPath, ModFramework.Guid + ".reload-in-progress");

        /// <summary>
        /// True while changed DLLs of reloadable mods are reloaded by themselves
        /// (the framework's <c>[Developer] WatchMods</c> setting, and developer
        /// tools on). <c>mods reload &lt;guid&gt;</c> works regardless.
        /// </summary>
        public static bool Watching
        {
            get => _watch != null && _watch.Value && DeveloperTools.Enabled;
            set { if (_watch != null) _watch.Value = value; }
        }

        /// <summary>True when the mod said it can be reloaded.</summary>
        public static bool IsReloadable(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }
            if (ModFramework.GetInfo(guid)?.Reloadable == true)
            {
                return true;
            }
            return Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) && info.Instance != null
                   && info.Instance.GetType().IsDefined(typeof(ReloadableModAttribute), false);
        }

        /// <summary>GUIDs of the loaded mods that said they can be reloaded.</summary>
        public static IReadOnlyList<string> ReloadableMods()
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, PluginInfo> kv in Chainloader.PluginInfos)
            {
                if (IsReloadable(kv.Key))
                {
                    list.Add(kv.Key);
                }
            }
            return list;
        }

        /// <summary>How many times the mod was reloaded this session.</summary>
        public static int ReloadCount(string guid)
        {
            lock (Lock)
            {
                return guid != null && Counts.TryGetValue(guid, out int n) ? n : 0;
            }
        }

        /// <summary>
        /// Reloads the mod on the next frame (a reload cannot run from inside a
        /// draw or a callback of the mod itself). The result is logged as a
        /// message, which the Console shows. Refused, with the reason returned,
        /// when the mod is not loaded, not reloadable, or developer tools are off.
        /// </summary>
        /// <returns>What was queued, or why not.</returns>
        public static string Reload(string guid)
        {
            if (!DeveloperTools.Enabled)
            {
                return "Mods are reloaded only while developer tools are on (Options > Mods > Drag'n Wash ModFramework).";
            }
            if (string.IsNullOrEmpty(guid) || !Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || info.Instance == null)
            {
                return $"No loaded plugin has the GUID \"{guid}\".";
            }
            if (!IsReloadable(guid))
            {
                return $"{info.Metadata.Name} does not say it is reloadable (ModInfo.Reloadable or [ReloadableMod]); restart the game instead.";
            }
            if (ModFramework.GetInfo(guid)?.IsLibrary == true)
            {
                return $"{info.Metadata.Name} is a library; other mods hold its types, so it needs a restart.";
            }
            lock (Lock)
            {
                if (!Queued.Contains(guid))
                {
                    Queued.Enqueue(guid);
                }
            }
            return $"Reloading {info.Metadata.Name} from {Path.GetFileName(SourceFile(info))}...";
        }

        // The file a reload reads: the delivered <Mod>.dll.new when there is
        // one, else the DLL itself.
        private static string SourceFile(PluginInfo info)
        {
            string pending = info.Location + PendingReloads.NewSuffix;
            return File.Exists(pending) ? pending : info.Location;
        }

        // ---- framework side --------------------------------------------------------

        internal static void Install(ConfigFile config, MonoBehaviour host)
        {
            _watch = config.Bind("Developer", "WatchMods", true,
                new ConfigDescription("While developer tools are on, a reloadable mod whose DLL changes under BepInEx/plugins is reloaded by itself half a second later. Off, only the console's mods reload does it. Switched off by the framework when a reload took the game down.",
                    null, new SettingMeta { DisplayName = "Reload mods when their DLL changes", Advanced = true }));
            if (File.Exists(MarkerPath))
            {
                try { File.Delete(MarkerPath); } catch { }
                ModFramework.Log.LogError("The last mod reload did not come back (the game closed while it ran). Automatic reloading is switched off; turn [Developer] WatchMods back on when you want it. On Direct3D 12, work with -force-d3d11.");
                GameHooks.Unavailable(ModFramework.Guid, "Automatic mod reload", "the last reload took the game down; WatchMods was switched off");
                _watch.Value = false;
            }
            GameEvents.OnGameStarted(ModFramework.Guid, SetUpWatchers);
            DeveloperTools.Changed += SetUpWatchers;
            _watch.SettingChanged += (s, e) => SetUpWatchers();
        }

        // One watcher per reloadable mod's folder, while watching is on.
        private static void SetUpWatchers()
        {
            lock (Lock)
            {
                if (!Watching)
                {
                    foreach (FileSystemWatcher w in Watchers.Values)
                    {
                        w.EnableRaisingEvents = false;
                        w.Dispose();
                    }
                    Watchers.Clear();
                    return;
                }
                foreach (string guid in ReloadableMods())
                {
                    if (Watchers.ContainsKey(guid) || !Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || string.IsNullOrEmpty(info.Location))
                    {
                        continue;
                    }
                    try
                    {
                        string folder = Path.GetDirectoryName(info.Location);
                        string file = Path.GetFileName(info.Location);
                        // The DLL and its .new: a build delivers the .new on Windows,
                        // where the running DLL is locked, and may overwrite the DLL elsewhere.
                        var watcher = new FileSystemWatcher(folder, file + "*") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                        string g = guid;
                        bool Mine(string name) => name.Equals(file, StringComparison.OrdinalIgnoreCase) || name.Equals(file + PendingReloads.NewSuffix, StringComparison.OrdinalIgnoreCase);
                        FileSystemEventHandler changed = (s, e) => { if (Mine(e.Name)) lock (Lock) { Pending[g] = DateTime.UtcNow; } };
                        watcher.Changed += changed;
                        watcher.Created += changed;
                        watcher.Renamed += (s, e) => { if (Mine(e.Name)) lock (Lock) { Pending[g] = DateTime.UtcNow; } };
                        watcher.EnableRaisingEvents = true;
                        Watchers[guid] = watcher;
                        ModFramework.Log.LogInfo($"Watching {file} and {file}{PendingReloads.NewSuffix} for changes (mod reload).");
                    }
                    catch (Exception ex)
                    {
                        ModFramework.Log.LogWarning($"Could not watch {info.Location} for changes: {ex.Message}");
                    }
                }
            }
        }

        // From the core plugin's Update: settled file changes and queued
        // reloads run here, on the main thread and outside any draw.
        internal static void Tick()
        {
            string next = null;
            lock (Lock)
            {
                if (Pending.Count > 0)
                {
                    DateTime now = DateTime.UtcNow;
                    string ready = null;
                    foreach (KeyValuePair<string, DateTime> kv in Pending)
                    {
                        if ((now - kv.Value).TotalMilliseconds >= 500)
                        {
                            ready = kv.Key;
                            break;
                        }
                    }
                    if (ready != null)
                    {
                        Pending.Remove(ready);
                        if (Watching && !Queued.Contains(ready))
                        {
                            Queued.Enqueue(ready);
                        }
                    }
                }
                if (Queued.Count > 0)
                {
                    next = Queued.Dequeue();
                }
            }
            if (next != null)
            {
                ReloadNow(next);
            }
        }

        private static void ReloadNow(string guid)
        {
            if (!Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || info.Instance == null)
            {
                ModFramework.Log.LogWarning($"Mod reload: \"{guid}\" is not loaded.");
                return;
            }
            BaseUnityPlugin old = info.Instance;
            string name = info.Metadata.Name;
            Assembly oldAssembly = old.GetType().Assembly;

            // 1. The new build must load and hold a plugin before anything is taken down.
            Assembly fresh;
            Type pluginType = null;
            string source = SourceFile(info);
            try
            {
                byte[] bytes = File.ReadAllBytes(source);
                string pdb = Path.ChangeExtension(info.Location, ".pdb");
                fresh = File.Exists(pdb) ? Assembly.Load(bytes, File.ReadAllBytes(pdb)) : Assembly.Load(bytes);
                foreach (Type t in fresh.GetTypes())
                {
                    if (typeof(BaseUnityPlugin).IsAssignableFrom(t) && !t.IsAbstract)
                    {
                        BepInPlugin meta = MetadataHelper.GetMetadata(t);
                        if (meta != null && meta.GUID == guid)
                        {
                            pluginType = t;
                            break;
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                ModFramework.Log.LogError($"Mod reload of {name} refused: the new DLL did not load ({ex.LoaderExceptions.Length} type(s) failed: {(ex.LoaderExceptions.Length > 0 ? ex.LoaderExceptions[0].Message : "")}). The running build stays.");
                return;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Mod reload of {name} refused: {ex.Message}. The running build stays.");
                return;
            }
            if (pluginType == null)
            {
                ModFramework.Log.LogError($"Mod reload of {name} refused: the new DLL has no plugin with GUID {guid}. The running build stays.");
                return;
            }

            // 2. Take the old build down. A crash from here on is caught by the
            // marker at the next start; a managed failure leaves the mod down,
            // which the log says plainly.
            try { File.WriteAllText(MarkerPath, $"{guid} {DateTime.Now:O}"); } catch { }
            try
            {
                if (Unloading != null)
                {
                    foreach (Action<string, Assembly> handler in Unloading.GetInvocationList())
                    {
                        try { handler(guid, oldAssembly); }
                        catch (Exception ex) { ModFramework.Log.LogError($"A ModReload.Unloading handler threw: {ex}"); }
                    }
                }
                Sweep(guid, oldAssembly);
                try { new Harmony(guid).UnpatchSelf(); }
                catch (Exception ex) { ModFramework.Log.LogWarning($"Mod reload: could not remove {name}'s Harmony patches (id {guid}): {ex.Message}"); }
                try { UnityEngine.Object.DestroyImmediate(old); }
                catch (Exception ex) { ModFramework.Log.LogWarning($"Mod reload: {name}'s OnDestroy threw: {ex.Message}"); }

                // 3. Start the new build where BepInEx started the old one.
                GameObject manager = Chainloader.ManagerObject;
                Component added = manager.AddComponent(pluginType);
                var instance = added as BaseUnityPlugin;
                PropertyInfo instanceProperty = typeof(PluginInfo).GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (instanceProperty != null && instanceProperty.CanWrite)
                {
                    instanceProperty.SetValue(info, instance, null);
                }
                int count;
                lock (Lock)
                {
                    Counts.TryGetValue(guid, out count);
                    Counts[guid] = ++count;
                }
                ModFramework.Log.LogMessage($"Reloaded {name} ({Ordinal(count)} time) from {Path.GetFileName(source)}." +
                                            (source != info.Location ? " It becomes the installed DLL at the next launch." : ""));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Mod reload of {name} failed after the old build was taken down: {ex}. Restart the game.");
                GameHooks.Unavailable(guid, "Running build", "the reload failed; restart the game");
            }
            finally
            {
                try { File.Delete(MarkerPath); } catch { }
            }
        }

        // Everything the core itself keeps per mod.
        private static void Sweep(string guid, Assembly assembly)
        {
            GameEvents.Remove(guid);
            GameEvents.RemoveFrom(assembly);
            Services.Forget(guid, assembly);
            GameHooks.Forget(guid);
            ModFramework.Forget(guid, assembly);
            DeveloperTools.Forget(assembly);
        }

        /// <summary>
        /// Returns <paramref name="handlers"/> without the entries whose method or
        /// target lives in <paramref name="assembly"/>. For libraries taking a
        /// mod's handlers out of their own events in <see cref="Unloading"/>.
        /// </summary>
        public static Delegate Prune(Delegate handlers, Assembly assembly)
        {
            if (handlers == null || assembly == null)
            {
                return handlers;
            }
            Delegate result = null;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                bool from = d.Method?.DeclaringType?.Assembly == assembly || (d.Target != null && d.Target.GetType().Assembly == assembly);
                if (!from)
                {
                    result = result == null ? d : Delegate.Combine(result, d);
                }
            }
            return result;
        }

        /// <summary>
        /// As <see cref="Prune"/>, on a static event of <paramref name="type"/> by
        /// name: the compiler-generated field behind it is pruned in place.
        /// </summary>
        public static void PruneEvent(Type type, string eventName, Assembly assembly)
        {
            FieldInfo field = type?.GetField(eventName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
            {
                return;
            }
            field.SetValue(null, Prune(field.GetValue(null) as Delegate, assembly));
        }

        private static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }
    }
}
