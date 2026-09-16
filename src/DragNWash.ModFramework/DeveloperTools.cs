using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// One switch for everything meant for mod makers and translators rather
    /// than players: the Tool window (F1), its Console and Assets tabs, texture
    /// reloading, and the localization mod's exports and hot reload. Off by
    /// default, so a player who only installed a mod never sees a developer
    /// window or gets a folder of exported text. Turned on in Options → Mods →
    /// Drag'n Wash ModFramework → Developer tools, or in the framework's config.
    /// </summary>
    public static class DeveloperTools
    {
        private static ConfigEntry<bool> _enabled;

        /// <summary>True while developer tools are switched on.</summary>
        public static bool Enabled => _enabled != null && _enabled.Value;

        /// <summary>Raised when the switch changes, from the Mods screen or the config file.</summary>
        public static event Action Changed;

        /// <summary>
        /// Runs <paramref name="onEnabled"/> now if the tools are on, and again
        /// every time they are turned on later. For features that start lazily.
        /// </summary>
        public static void WhenEnabled(Action onEnabled)
        {
            if (onEnabled == null)
            {
                return;
            }
            if (Enabled)
            {
                onEnabled();
            }
            Changed += () =>
            {
                if (Enabled)
                {
                    onEnabled();
                }
            };
        }

        internal static void Install(ConfigFile config)
        {
            _enabled = config.Bind("Developer", "Tools", false,
                "Turns on the tools for mod makers and translators: the Tool window (F1) with the Console and Assets tabs, texture reloading, and mods' own exports and hot reload. Off, nothing of that runs. On Direct3D 12 the Tool window can crash the game (Unity issue UUM-140564); add -force-d3d11 to the game's Steam launch options when you work with it.");
            _enabled.SettingChanged += (sender, args) => Announce(changed: true);
            Announce(changed: false);
        }

        private static void Announce(bool changed)
        {
            if (Enabled)
            {
                ModFramework.Log.LogMessage("Developer tools are on (F1 opens the Tool window)." +
                                            (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                                                ? " This game runs on Direct3D 12, where the Tool window can crash it; -force-d3d11 in the launch options avoids that."
                                                : ""));
            }
            else
            {
                ModFramework.Log.LogInfo("Developer tools are off. Turn them on in Options > Mods > Drag'n Wash ModFramework > Developer tools when you make mods or translations.");
            }
            if (changed)
            {
                try
                {
                    Changed?.Invoke();
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogError($"A developer-tools handler threw: {ex}");
                }
            }
        }
    }
}
