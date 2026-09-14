using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace DragNWash.ModFramework.Mods
{
    // Loads ModInfo.IconPath. Mods register from Awake, which is when a texture
    // can be created safely on Direct3D 12.
    internal static class IconLoader
    {
        private static MethodInfo _loadImage;

        internal static Texture2D Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    ModFramework.Log.LogWarning($"Mod icon not found: {path}");
                    return null;
                }
                // ImageConversion's reference assembly targets netstandard 2.1,
                // which a net472 plugin cannot compile against.
                if (_loadImage == null)
                {
                    Type type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                    _loadImage = type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                    if (_loadImage == null)
                    {
                        ModFramework.Log.LogWarning("Mod icons cannot be loaded on this game build.");
                        return null;
                    }
                }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "ModIcon " + Path.GetFileName(path) };
                if (!(bool)_loadImage.Invoke(null, new object[] { texture, File.ReadAllBytes(path), true }))
                {
                    UnityEngine.Object.Destroy(texture);
                    ModFramework.Log.LogWarning($"Not an image the game can read: {path}");
                    return null;
                }
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return texture;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not load the mod icon {path}: {ex.Message}");
                return null;
            }
        }
    }
}
