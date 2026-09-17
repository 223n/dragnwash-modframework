using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The window's font. IMGUI draws through a dynamic font that grows its
    // texture the first time it is asked for a character; on Direct3D 12 an
    // upload on the frame the window opens is the UUM-140564 crash, so the font
    // is chosen and filled at startup, and characters mods need are prepared
    // from Update, never from OnGUI.
    //
    // IMGUI can only use fonts Unity itself knows: OS fonts by name, or Font
    // assets. Inside Steam's Linux runtime (Steam Deck) no OS font has CJK
    // glyphs, so a Font asset baked into an AssetBundle
    // (dragnwash-menufont.bundle, Noto Sans JP) is used when one is installed
    // anywhere under BepInEx/plugins.
    internal static class MenuFont
    {
        internal const int Size = 14;
        internal const string BundleFileName = "dragnwash-menufont.bundle";

        internal static Font Font { get; private set; }

        // The bundled Noto Sans JP comes out thin in IMGUI; Unity's synthetic
        // bold gives it the weight of the OS fonts.
        internal static bool Bold { get; private set; }

        private static readonly StringBuilder Queued = new StringBuilder();
        private static AssetBundle _bundle;

        internal static void Create(string mode)
        {
            try
            {
                if (mode == "skin")
                {
                    ToolWindowPlugin.Log.LogInfo("Window font: the IMGUI skin's (config).");
                    return;
                }
                if (mode == "builtin")
                {
                    Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                else
                {
                    // Unity hands back a Font even for a family the OS does not
                    // have, which then draws nothing (seen in Steam's Linux
                    // runtime), so skip names the OS does not list and check that
                    // a glyph really renders.
                    var installed = new HashSet<string>(Font.GetOSInstalledFontNames() ?? new string[0], StringComparer.OrdinalIgnoreCase);
                    foreach (string name in new[] { "Yu Gothic UI", "Meiryo UI", "Hiragino Sans", "PingFang SC", "Noto Sans CJK JP", "Noto Sans CJK SC" })
                    {
                        if (installed.Count > 0 && !installed.Contains(name))
                        {
                            continue;
                        }
                        // With a symbol face behind it, where the OS has one: the
                        // Inspector's toolbar glyphs come from there.
                        var names = new List<string> { name };
                        foreach (string symbols in new[] { "Segoe UI Symbol", "Apple Symbols", "Noto Sans Symbols2", "DejaVu Sans" })
                        {
                            if (installed.Contains(symbols))
                            {
                                names.Add(symbols);
                            }
                        }
                        Font candidate = Font.CreateDynamicFontFromOSFont(names.ToArray(), Size);
                        if (candidate == null)
                        {
                            continue;
                        }
                        if (Renders(candidate))
                        {
                            Font = candidate;
                            ToolWindowPlugin.Log.LogInfo($"Window font: {string.Join(" + ", names)}");
                            break;
                        }
                        UnityEngine.Object.Destroy(candidate);
                    }
                    if (Font == null)
                    {
                        Font bundled = LoadBundle();
                        if (bundled != null && Renders(bundled))
                        {
                            Font = bundled;
                            Bold = true;
                        }
                    }
                    if (Font == null)
                    {
                        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        ToolWindowPlugin.Log.LogInfo("Window font: no OS or bundled font renders here; using Unity's built-in font (ASCII only).");
                    }
                }
                if (Font == null)
                {
                    return;
                }
                var ascii = new StringBuilder();
                for (char c = ' '; c <= '~'; c++)
                {
                    ascii.Append(c);
                }
                Request(ascii.ToString());
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not prepare the window font: {ex.Message}");
                Font = null;
            }
        }

        internal static void Prepare(string characters)
        {
            if (string.IsNullOrEmpty(characters))
            {
                return;
            }
            // Inside OnGUI: wait for Update.
            if (Event.current != null)
            {
                lock (Queued)
                {
                    Queued.Append(characters);
                }
                return;
            }
            Request(characters);
        }

        // From Update.
        internal static void FlushQueued()
        {
            string pending;
            lock (Queued)
            {
                if (Queued.Length == 0)
                {
                    return;
                }
                pending = Queued.ToString();
                Queued.Length = 0;
            }
            Request(pending);
        }

        // Every character rasterised so far, so a caller that draws arbitrary
        // text (the console) can tell what is safe to draw this frame.
        private static readonly HashSet<char> Requested = new HashSet<char>();

        internal static bool IsPrepared(char c)
        {
            lock (Requested)
            {
                return Requested.Contains(c);
            }
        }

        private static void Request(string characters)
        {
            if (Font == null)
            {
                return;
            }
            try
            {
                lock (Requested)
                {
                    foreach (char c in characters)
                    {
                        Requested.Add(c);
                    }
                }
                Font.RequestCharactersInTexture(characters, Size, FontStyle.Normal);
                if (Bold)
                {
                    Font.RequestCharactersInTexture(characters, Size, FontStyle.Bold);
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not prepare window font characters: {ex.Message}");
            }
        }

        private static bool Renders(Font font)
        {
            try
            {
                font.RequestCharactersInTexture("A", Size, FontStyle.Normal);
                return font.HasCharacter('A')
                       && font.GetCharacterInfo('A', out CharacterInfo info, Size, FontStyle.Normal)
                       && info.advance > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Font LoadBundle()
        {
            try
            {
                string path = FindBundle();
                if (path == null)
                {
                    return null;
                }
                _bundle = _bundle ?? AssetBundle.LoadFromFile(path);
                Font[] fonts = _bundle != null ? _bundle.LoadAllAssets<Font>() : null;
                if (fonts == null || fonts.Length == 0)
                {
                    ToolWindowPlugin.Log.LogWarning($"The window font bundle holds no font: {path}");
                    return null;
                }
                ToolWindowPlugin.Log.LogInfo($"Window font: {fonts[0].name} from {path}");
                return fonts[0];
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not load the window font bundle: {ex.Message}");
                return null;
            }
        }

        // Next to this library first, then anywhere under plugins (the
        // localization mod ships one).
        private static string FindBundle()
        {
            string own = Path.Combine(Path.GetDirectoryName(typeof(MenuFont).Assembly.Location) ?? "", BundleFileName);
            if (File.Exists(own))
            {
                return own;
            }
            try
            {
                string[] found = Directory.GetFiles(Paths.PluginPath, BundleFileName, SearchOption.AllDirectories);
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                return found.Length > 0 ? found[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
