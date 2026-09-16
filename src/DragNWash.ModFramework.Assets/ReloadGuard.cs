using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    // A texture upload while the game runs can crash it on Direct3D 12, and a
    // native crash cannot be caught. So a reload writes a marker file before it
    // uploads and deletes it after; a marker still there at the next start means
    // the last reload took the game down. The count of such crashes is kept in
    // a second file, and reloading is switched off after one and refused on
    // Direct3D 12 after two, until the player turns it back on.
    internal static class ReloadGuard
    {
        private static string MarkerPath => Path.Combine(Paths.ConfigPath, GameFonts.Guid + ".reload-in-progress");
        private static string CountPath => Path.Combine(Paths.ConfigPath, GameFonts.Guid + ".reload-crashes");

        /// <summary>How many times the game did not come back from a reload.</summary>
        public static int CrashCount { get; private set; }

        /// <summary>What the marker said when the game started with one present, or null.</summary>
        public static string LastCrash { get; private set; }

        // Called once at startup. Returns true when the last reload crashed.
        public static bool CheckAtStartup()
        {
            try
            {
                if (File.Exists(CountPath) && int.TryParse(File.ReadAllText(CountPath).Trim(), out int n))
                {
                    CrashCount = n;
                }
                if (!File.Exists(MarkerPath))
                {
                    return false;
                }
                LastCrash = File.ReadAllText(MarkerPath).Trim();
                File.Delete(MarkerPath);
                CrashCount++;
                File.WriteAllText(CountPath, CrashCount.ToString());
                return true;
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogWarning($"Could not read the reload marker: {ex.Message}");
                return false;
            }
        }

        public static void Begin(string what)
        {
            try
            {
                File.WriteAllText(MarkerPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {SystemInfo.graphicsDeviceType} {what}");
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogWarning($"Could not write the reload marker: {ex.Message}");
            }
        }

        public static void End()
        {
            try
            {
                if (File.Exists(MarkerPath))
                {
                    File.Delete(MarkerPath);
                }
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogWarning($"Could not remove the reload marker: {ex.Message}");
            }
        }

        // The player turned reloading back on: forget the crashes.
        public static void ResetCount()
        {
            CrashCount = 0;
            try
            {
                if (File.Exists(CountPath))
                {
                    File.Delete(CountPath);
                }
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogWarning($"Could not reset the reload crash count: {ex.Message}");
            }
        }
    }
}
