using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Assets
{
    // Reading replacement files again when they change: the Reload files
    // button (at once or one file a frame), the swap into whatever used the
    // old texture, and the folder watchers.
    public static partial class AssetReplacements
    {
        /// <summary>
        /// Reads every replacement file again and swaps in the ones that changed,
        /// then applies them. A file that cannot be read keeps its old texture
        /// and is reported. Uploads textures while the game runs, which on
        /// Direct3D 12 can crash it; see <see cref="ReloadDisabled"/>.
        /// </summary>
        public static IReadOnlyList<ReloadResult> ReloadFiles()
        {
            var results = new List<ReloadResult>();
            if (Reloading)
            {
                results.Add(new ReloadResult("(reload)", "a reload is already running"));
                return results;
            }
            List<TextureReplacement> changed = ChangedFiles(results);
            if (changed.Count == 0)
            {
                LastReload = results;
                return results;
            }
            ReloadGuard.Begin(NamesOf(changed));
            try
            {
                foreach (TextureReplacement r in changed)
                {
                    ReloadOne(r, results);
                }
                ApplyNow();
            }
            finally
            {
                ReloadGuard.End();
            }
            LastReload = results;
            return results;
        }

        /// <summary>True while the Assets tab's reload works through the files, one a frame.</summary>
        internal static bool Reloading { get; private set; }

        /// <summary>The file that reload is at, and how many it has done of how many.</summary>
        internal static string ReloadingName { get; private set; }
        internal static int ReloadingDone { get; private set; }
        internal static int ReloadingTotal { get; private set; }

        // The Assets tab's Reload files: what ReloadFiles does, one file a
        // frame, so the window can say how far it is and the game never stops
        // for all of them at once. A coroutine of the library's plugin; the
        // work starts on the frame after the button, outside the window's
        // drawing, and done gets the results.
        internal static System.Collections.IEnumerator ReloadFilesOverFrames(Action<IReadOnlyList<ReloadResult>> done)
        {
            if (Reloading)
            {
                yield break;
            }
            Reloading = true;
            ReloadingName = null;
            ReloadingDone = 0;
            ReloadingTotal = 0;
            var results = new List<ReloadResult>();
            try
            {
                yield return null;
                List<TextureReplacement> changed = ChangedFiles(results);
                if (changed.Count > 0)
                {
                    ReloadingTotal = changed.Count;
                    ReloadGuard.Begin(NamesOf(changed));
                    try
                    {
                        for (int i = 0; i < changed.Count; i++)
                        {
                            // Named on screen a frame before it is read.
                            ReloadingName = changed[i].Name;
                            ReloadingDone = i;
                            yield return null;
                            ReloadOne(changed[i], results);
                        }
                        ReloadingDone = changed.Count;
                        ApplyNow();
                    }
                    finally
                    {
                        ReloadGuard.End();
                    }
                }
                LastReload = results;
            }
            finally
            {
                Reloading = false;
                ReloadingName = null;
            }
            try
            {
                done?.Invoke(results);
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"After reloading texture replacements: {ex}");
            }
        }

        // The plugin went away in the middle of a reload (a mod reload): its
        // coroutine will not finish, so the guard and the flag are put right here.
        internal static void AbandonReload()
        {
            if (!Reloading)
            {
                return;
            }
            ReloadGuard.End();
            Reloading = false;
            ReloadingName = null;
        }

        // Which files differ from what was loaded; what does not is in results already.
        private static List<TextureReplacement> ChangedFiles(List<ReloadResult> results)
        {
            var changed = new List<TextureReplacement>();
            if (ReloadDisabled)
            {
                results.Add(new ReloadResult("(reload)", ReloadDisabledReason ?? "reloading is switched off"));
                return changed;
            }
            foreach (TextureReplacement r in All)
            {
                if (!File.Exists(r.Path))
                {
                    results.Add(new ReloadResult(r.Name, "file removed; the loaded texture stays until the game restarts"));
                    continue;
                }
                if (HashFile(r.Path) == r.ContentHash)
                {
                    results.Add(new ReloadResult(r.Name, "unchanged"));
                    continue;
                }
                changed.Add(r);
            }
            return changed;
        }

        private static string NamesOf(List<TextureReplacement> changed)
        {
            var names = new List<string>();
            foreach (TextureReplacement r in changed)
            {
                names.Add(r.Name);
            }
            return string.Join(" ", names);
        }

        // One file read again and swapped in wherever the old texture was. A
        // failure is that file's result; the others still go.
        private static void ReloadOne(TextureReplacement r, List<ReloadResult> results)
        {
            Revision++;
            try
            {
                Texture2D fresh = GameAssets.LoadTextureFresh(r.Path, out string error);
                if (fresh == null)
                {
                    r.Problem = error;
                    results.Add(new ReloadResult(r.Name, error));
                    AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" was not reloaded: {error}");
                    return;
                }
                fresh.name = r.Name;
                Texture2D old = r.Texture;
                Replacements.Remove(old);
                Replacements.Add(fresh);
                r.Texture = fresh;
                r.Problem = null;
                r.ContentHash = HashFile(r.Path);
                r.Applied = 0;
                var stale = new List<Sprite>();
                foreach (KeyValuePair<Sprite, Sprite> kv in SpriteFor)
                {
                    if (kv.Value != null && kv.Value.texture == old)
                    {
                        stale.Add(kv.Key);
                    }
                }
                foreach (Sprite s in stale)
                {
                    SpriteFor.Remove(s);
                }
                ReplaceEverywhere(old, fresh);
                results.Add(new ReloadResult(r.Name, "reloaded"));
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Reloading the texture replacement \"{r.Name}\" failed: {ex}");
                results.Add(new ReloadResult(r.Name, ex.Message));
            }
        }

        // Materials and sprite users that hold the previous replacement get the new one.
        private static void ReplaceEverywhere(Texture2D old, Texture2D fresh)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    if (m.GetTexture(property) == old)
                    {
                        m.SetTexture(property, fresh);
                    }
                }
            }
            var recut = new Dictionary<Sprite, Sprite>();
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && image.sprite != null && image.sprite.texture == old)
                {
                    image.sprite = ReCut(image.sprite, fresh, recut);
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && renderer.sprite != null && renderer.sprite.texture == old)
                {
                    renderer.sprite = ReCut(renderer.sprite, fresh, recut);
                }
            }
        }

        // The same sprite, cut from the new texture, once per previous sprite;
        // it remembers the same original as the sprite it takes over from.
        private static Sprite ReCut(Sprite previous, Texture2D fresh, Dictionary<Sprite, Sprite> done)
        {
            if (done.TryGetValue(previous, out Sprite made))
            {
                return made;
            }
            Sprite sprite = CutFrom(previous, fresh);
            done[previous] = sprite;
            if (OriginalSprite.TryGetValue(previous, out Sprite original))
            {
                OriginalSprite[sprite] = original;
                SpriteFor[original] = sprite;
            }
            return sprite;
        }

        private static Sprite CutFrom(Sprite previous, Texture2D fresh)
        {
            Rect rect = previous.rect;
            float sx = (float)fresh.width / previous.texture.width, sy = (float)fresh.height / previous.texture.height;
            var scaled = new Rect(rect.x * sx, rect.y * sy, rect.width * sx, rect.height * sy);
            Vector2 pivot = new Vector2(previous.pivot.x / rect.width, previous.pivot.y / rect.height);
            Sprite sprite = Sprite.Create(fresh, scaled, pivot, previous.pixelsPerUnit * sx, 0, SpriteMeshType.FullRect, previous.border);
            sprite.name = previous.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream f = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(f));
                }
            }
            catch
            {
                return null;
            }
        }

        // Watches every replacement folder and asks for a reload on the next
        // Update, half a second after the last change, so an editor saving in
        // several steps triggers one reload. Never on Direct3D 12.
        internal static void WatchFiles()
        {
            if (Watchers.Count > 0 || !GameFonts.RuntimeUploadsAreSafe)
            {
                return;
            }
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextureReplacement r in ByName.Values)
            {
                folders.Add(System.IO.Path.GetDirectoryName(r.Path));
            }
            foreach (string folder in folders)
            {
                try
                {
                    var w = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    w.Changed += (s, e) => { _reloadRequested = true; };
                    w.Created += (s, e) => { _reloadRequested = true; };
                    w.Renamed += (s, e) => { _reloadRequested = true; };
                    w.EnableRaisingEvents = true;
                    Watchers.Add(w);
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"Could not watch {folder}: {ex.Message}");
                }
            }
            if (Watchers.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"Watching {Watchers.Count} texture folder(s) for changes.");
            }
        }

        // From the plugin's Update: runs the reload a watcher asked for.
        internal static void Tick()
        {
            if (ReloadDisabled)
            {
                _reloadRequested = false;
            }
            // The Assets tab's reload is under way; the files it reads are the same.
            if (Reloading)
            {
                return;
            }
            if (!_reloadRequested)
            {
                _reloadRequestedAt = 0f;
                return;
            }
            if (_reloadRequestedAt == 0f)
            {
                _reloadRequestedAt = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _reloadRequestedAt < 0.5f)
            {
                return;
            }
            _reloadRequested = false;
            _reloadRequestedAt = 0f;
            int n = 0;
            foreach (ReloadResult r in ReloadFiles())
            {
                if (r.Status == "reloaded")
                {
                    n++;
                }
            }
            AssetsLibraryPlugin.Log.LogMessage($"Texture files changed on disk: {n} replacement(s) reloaded.");
        }
    }
}
