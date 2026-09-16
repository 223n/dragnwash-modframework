using System;
using System.IO;

namespace DragNWash.ModFramework
{
    // A mod's new build, delivered while the game ran.
    //
    // On Windows a loaded plugin DLL is mapped by Mono and cannot be overwritten,
    // so a build that wants the running game to reload it copies to
    // <Mod>.dll.new next to the DLL instead (see ModReload and GUIDE rule 10).
    // The game reloads from that file at once; this patcher, which runs before
    // any plugin is loaded, makes it the real DLL at the next launch, so the
    // game starts with the build that was last reloaded.
    //
    // This file is compiled into both the patcher and the framework.
    internal static class PendingReloads
    {
        internal const string NewSuffix = ".new";

        internal static void Apply(string pluginPath, Action<string> log)
        {
            if (!Directory.Exists(pluginPath))
            {
                return;
            }
            foreach (string pending in Directory.GetFiles(pluginPath, "*.dll" + NewSuffix, SearchOption.AllDirectories))
            {
                string dll = pending.Substring(0, pending.Length - NewSuffix.Length);
                try
                {
                    File.Copy(pending, dll, true);
                    File.Delete(pending);
                    log($"Applied the reloaded build of {Path.GetFileName(dll)}.");
                }
                catch (Exception ex)
                {
                    log($"Could not apply {Path.GetFileName(pending)} to {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }
    }
}
