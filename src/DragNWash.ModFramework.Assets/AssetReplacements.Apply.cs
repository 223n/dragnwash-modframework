using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Assets
{
    // Putting replacements into the materials and sprites that use the
    // original, and taking them back when they no longer apply.
    public static partial class AssetReplacements
    {
        // Takes back what no longer applies and applies what does, everywhere.
        // With extra, those textures (no longer listed anywhere) are taken back too.
        private static void RefreshAll(List<TextureReplacement> extra = null)
        {
            var gone = new HashSet<Texture>();
            if (extra != null)
            {
                foreach (TextureReplacement r in extra)
                {
                    if (r.Texture != null) gone.Add(r.Texture);
                }
            }
            foreach (TextureReplacement r in Suspended)
            {
                if (r.Texture != null) gone.Add(r.Texture);
            }
            TakeBack(gone);
            ApplyNow();
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"An AssetReplacements.Changed handler threw: {ex}");
            }
        }

        // Puts the original back wherever one of these textures, or a replacement
        // that no longer applies, is in use.
        private static void TakeBack(HashSet<Texture> gone)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    Texture current = m.GetTexture(property);
                    if (current == null || !(gone.Contains(current) || IsStale(current)))
                    {
                        continue;
                    }
                    string key = MaterialKey(m, property);
                    if (MaterialOriginal.TryGetValue(key, out Texture original))
                    {
                        m.SetTexture(property, original);
                    }
                }
            }
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && image.sprite != null && (gone.Contains(image.sprite.texture) || IsStale(image.sprite.texture)) &&
                    OriginalSprite.TryGetValue(image.sprite, out Sprite original))
                {
                    image.sprite = original;
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && renderer.sprite != null && (gone.Contains(renderer.sprite.texture) || IsStale(renderer.sprite.texture)) &&
                    OriginalSprite.TryGetValue(renderer.sprite, out Sprite original))
                {
                    renderer.sprite = original;
                }
            }
        }

        // A replacement texture in use that is not what applies to its name now.
        private static bool IsStale(Texture current)
        {
            var t = current as Texture2D;
            if (t == null || !Replacements.Contains(t) && !IsSuspended(t))
            {
                return false;
            }
            TextureReplacement now = Effective(t.name);
            return now == null || now.Texture != t;
        }

        private static bool IsSuspended(Texture2D t)
        {
            foreach (TextureReplacement r in Suspended)
            {
                if (r.Texture == t) return true;
            }
            return false;
        }

        private static string MaterialKey(Material m, string property) => m.GetInstanceID() + "|" + property;

        /// <summary>True when <paramref name="texture"/> is a replacement a mod shipped.</summary>
        public static bool IsReplacement(Texture2D texture)
        {
            return texture != null && Replacements.Contains(texture);
        }

        /// <summary>
        /// Puts every replacement into the materials and sprites that use the
        /// original, now. Runs by itself when a scene loads; call it after the
        /// game created new materials or sprites. Returns how many places changed.
        /// </summary>
        public static int ApplyNow()
        {
            Revision++;
            if (ByName.Count == 0 && LanguageByName.Count == 0)
            {
                return 0;
            }
            int changed = 0;
            try
            {
                changed += ApplyToMaterials();
                changed += ApplyToSprites();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Applying texture replacements failed: {ex}");
            }
            return changed;
        }

        private static int ApplyToMaterials()
        {
            int changed = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    Texture current = m.GetTexture(property);
                    if (current == null)
                    {
                        continue;
                    }
                    TextureReplacement r = Effective(current.name);
                    if (r == null || current == r.Texture)
                    {
                        continue;
                    }
                    string key = MaterialKey(m, property);
                    bool isReplacement = current is Texture2D t2 && (Replacements.Contains(t2) || IsSuspended(t2));
                    if (!isReplacement)
                    {
                        MaterialOriginal[key] = current;
                    }
                    else if (!MaterialOriginal.ContainsKey(key))
                    {
                        // A replacement put there before we could note the original.
                        continue;
                    }
                    m.SetTexture(property, r.Texture);
                    r.Applied++;
                    changed++;
                }
            }
            return changed;
        }

        private static int ApplyToSprites()
        {
            int changed = 0;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && TryReplacementSprite(image.sprite, out Sprite s))
                {
                    image.sprite = s;
                    changed++;
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && TryReplacementSprite(renderer.sprite, out Sprite s))
                {
                    renderer.sprite = s;
                    changed++;
                }
            }
            return changed;
        }

        // A sprite cut from the replacement texture with the original's rect,
        // pivot and pixels-per-unit, made once per original sprite. The
        // replacement image should have the original's size; a different size
        // is scaled to keep the same rect in the texture's proportions.
        private static bool TryReplacementSprite(Sprite current, out Sprite replacement)
        {
            replacement = null;
            if (current == null || current.texture == null)
            {
                return false;
            }
            // A sprite we made stands for the game's sprite it was cut for.
            Sprite original = OriginalSprite.TryGetValue(current, out Sprite o) && o != null ? o : current;
            if (original != current && original.texture == null)
            {
                return false;
            }
            if (original == current && (Replacements.Contains(current.texture) || IsSuspended(current.texture)))
            {
                return false;
            }
            TextureReplacement r = Effective(original.texture.name);
            if (r == null || current.texture == r.Texture)
            {
                return false;
            }
            if (SpriteFor.TryGetValue(original, out replacement) && replacement != null && replacement.texture == r.Texture)
            {
                return true;
            }
            replacement = CutFrom(original, r.Texture);
            SpriteFor[original] = replacement;
            OriginalSprite[replacement] = original;
            r.Applied++;
            return true;
        }
    }
}
