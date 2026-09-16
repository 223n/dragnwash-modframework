using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    /// <summary>
    /// Loads textures and asset bundles from files, once each, and says so in the
    /// log when that happens late on a renderer where it is risky.
    /// </summary>
    /// <remarks>
    /// On Direct3D 12 with this game's Unity version, creating and uploading
    /// textures while the game runs can crash it (Unity UUM-140564). Load what a
    /// mod needs from its plugin's Awake, and keep the returned objects.
    /// </remarks>
    public static class GameAssets
    {
        private static readonly Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Opens an asset bundle, or returns the one already opened from the same
        /// file, so two mods shipping the same bundle do not fail on the second
        /// load. Returns null when the file is missing or cannot be opened.
        /// </summary>
        public static AssetBundle LoadBundle(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string full = Path.GetFullPath(path);
            lock (Bundles)
            {
                if (Bundles.TryGetValue(full, out AssetBundle cached) && cached != null)
                {
                    return cached;
                }
                if (!File.Exists(full))
                {
                    return null;
                }
                try
                {
                    WarnIfLate("asset bundle", full);
                    AssetBundle bundle = AssetBundle.LoadFromFile(full);
                    if (bundle == null)
                    {
                        AssetsLibraryPlugin.Log.LogWarning($"Could not open the asset bundle {full}.");
                        return null;
                    }
                    Bundles[full] = bundle;
                    return bundle;
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"Could not open the asset bundle {full}: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Loads a PNG or JPG file as a texture, or returns the one already loaded
        /// from the same file. Returns null when it is missing or not an image.
        /// </summary>
        public static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string full = Path.GetFullPath(path);
            lock (Textures)
            {
                if (Textures.TryGetValue(full, out Texture2D cached) && cached != null)
                {
                    return cached;
                }
                if (!File.Exists(full))
                {
                    return null;
                }
                try
                {
                    WarnIfLate("texture", full);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(full) };
                    if (!LoadImage(texture, File.ReadAllBytes(full)))
                    {
                        UnityEngine.Object.Destroy(texture);
                        AssetsLibraryPlugin.Log.LogWarning($"Not an image the game can read: {full}");
                        return null;
                    }
                    texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    Textures[full] = texture;
                    return texture;
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"Could not load the texture {full}: {ex.Message}");
                    return null;
                }
            }
        }

        // Reads a file into a new texture every time, without the cache, so a
        // reload sees the new content. A file an editor is still writing is
        // retried a few times. On failure the reason is returned and nothing is
        // created, so the caller keeps what it had.
        internal static Texture2D LoadTextureFresh(string path, out string error)
        {
            error = null;
            byte[] data = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    data = File.ReadAllBytes(path);
                    break;
                }
                catch (IOException ex)
                {
                    error = ex.Message;
                    System.Threading.Thread.Sleep(150);
                }
            }
            if (data == null)
            {
                error = "the file could not be read (still being saved?): " + error;
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(path) };
            try
            {
                if (!LoadImage(texture, data))
                {
                    UnityEngine.Object.Destroy(texture);
                    error = "not an image the game can read";
                    return null;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Object.Destroy(texture);
                error = ex.Message;
                return null;
            }
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return texture;
        }

        // ImageConversion lives in a module whose reference assembly targets
        // netstandard 2.1, which a net472 plugin cannot compile against.
        private static System.Reflection.MethodInfo _loadImage;

        private static bool LoadImage(Texture2D texture, byte[] data)
        {
            if (_loadImage == null)
            {
                Type type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                _loadImage = type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (_loadImage == null)
                {
                    throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage");
                }
            }
            return (bool)_loadImage.Invoke(null, new object[] { texture, data, true });
        }

        // BepInEx runs plugin Awake before the first scene has rendered much;
        // after a minute of play a load is certainly not at startup.
        private static void WarnIfLate(string what, string path)
        {
            if (!GameFonts.RuntimeUploadsAreSafe && Time.realtimeSinceStartup > 60f)
            {
                AssetsLibraryPlugin.Log.LogWarning($"Loading the {what} {path} while the game runs on Direct3D 12 can crash it; load it at startup instead.");
            }
        }
    }
}
