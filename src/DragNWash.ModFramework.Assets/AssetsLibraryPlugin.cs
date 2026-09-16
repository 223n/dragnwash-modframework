using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    // The assets library's BepInEx entry point.
    [BepInPlugin(GameFonts.Guid, "DragNWash.ModFramework.Assets", GameFonts.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(global::DragNWash.ModFramework.ToolWindow.ToolWindow.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class AssetsLibraryPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<int> AtlasPointSize;

        private void Awake()
        {
            Log = Logger;
            ModFramework.Register(new ModInfo
            {
                Guid = GameFonts.Guid,
                DisplayName = "Drag'n Wash ModFramework: Assets",
                Description = "Fonts for text the game's own fonts cannot show, and loading of textures and asset bundles, done so they do not crash Direct3D 12.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            AtlasPointSize = Config.Bind("Fonts", "AtlasPointSize", 80,
                "Point size glyphs are rasterized at for the fallback fonts. Higher is sharper. Glyphs are rasterized when a mod prepares its text, usually at startup, so raising this costs loading time rather than performance during play.");

            GameFonts.AddFontFolder(Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "fonts"));

            // Experimental: texture replacements from every mod's assets/textures folder,
            // read now while uploads are safe, and the Assets tab when the Tool window is there.
            AssetReplacements.LoadAll();
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(global::DragNWash.ModFramework.ToolWindow.ToolWindow.Guid))
            {
                InstallTab();
            }

            if (!GameFonts.RuntimeUploadsAreSafe)
            {
                Logger.LogInfo($"Direct3D 12 on Unity {Application.unityVersion}: fonts, textures and asset bundles should be loaded at startup (Unity issue UUM-140564). If the game still crashes, add -force-d3d11 to its Steam launch options.");
            }
        }

        // In its own method so the Tool window types are only loaded when it is installed.
        private static void InstallTab()
        {
            try
            {
                AssetsTab.Install();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"The Assets tab could not be added: {ex.Message}");
            }
        }
    }
}
