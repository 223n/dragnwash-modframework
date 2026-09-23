using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEngine.TextCore.LowLevel;

namespace DragNWash.ModFramework.Assets
{
    // Loading one font face per script: by family name from the system, else
    // from known font file paths and the players' fonts folders.
    public static partial class GameFonts
    {
        private static void LoadGroup(string group, int pointSize)
        {
            if (LoadedGroups.Contains(group)) return;
            LoadedGroups.Add(group);

            string[] candidates;
            string[] files;
            int ttcFace;
            switch (group)
            {
                case GroupJapanese: candidates = JapaneseCandidates; files = JapaneseFiles; ttcFace = 0; break;
                case GroupSimplified: candidates = ChineseCandidates; files = ChineseFiles; ttcFace = 2; break;
                case GroupTraditional: candidates = TraditionalCandidates; files = TraditionalFiles; ttcFace = 3; break;
                case GroupKorean: candidates = KoreanCandidates; files = KoreanFiles; ttcFace = 1; break;
                case GroupHebrew: candidates = HebrewCandidates; files = HebrewFiles; ttcFace = 0; break;
                case GroupThai: candidates = ThaiCandidates; files = ThaiFiles; ttcFace = 0; break;
                default: candidates = WesternCandidates; files = WesternFiles; ttcFace = 0; break;
            }

            TMP_FontAsset face = AddFirstAvailable(candidates, pointSize, group);
            // Steam's Linux runtime container, macOS and stripped-down systems
            // do not always expose fonts by family name, but the files are
            // still there. Try known paths, and a fonts/ folder next to the
            // plugin for anyone who wants to drop in their own.
            if (face == null)
            {
                face = AddFirstFile(files, ttcFace, pointSize, group);
            }
            if (face == null)
            {
                AssetsLibraryPlugin.Log.LogInfo($"[font] No {group} font found on this system.");
                LogSystemFontNames();
                return;
            }
            FaceOfGroup[group] = face;
        }

        private static TMP_FontAsset AddFirstFile(string[] patterns, int ttcFace, int pointSize, string label)
        {
            foreach (string pattern in patterns)
            {
                foreach (string path in Expand(pattern))
                {
                    int face = path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ? ttcFace : 0;
                    string source = "file:" + path + "#" + face;
                    TMP_FontAsset existing;
                    if (FaceBySource.TryGetValue(source, out existing))
                    {
                        AssetsLibraryPlugin.Log.LogInfo($"[font] {label} shares the face already loaded from {path} (face {face}).");
                        return existing;
                    }
                    TMP_FontAsset asset;
                    try
                    {
                        asset = TMP_FontAsset.CreateFontAsset(path, face, pointSize, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    }
                    catch (Exception ex)
                    {
                        AssetsLibraryPlugin.Log.LogInfo($"CJK fallback font file '{path}' could not be loaded: {ex.Message}");
                        continue;
                    }
                    if (asset == null)
                    {
                        continue;
                    }
                    asset.name = "DragNWash_Fallback_" + label + "_" + Path.GetFileNameWithoutExtension(path);
                    Registered.Add(asset);
                    FaceBySource[source] = asset;
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {label}: loaded {path} (face {face}, atlas point size {pointSize}).");
                    FontAtlasCache.Restore(asset);
                    return asset;
                }
            }
            return null;
        }

        private static IEnumerable<string> Expand(string pattern)
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (pattern.StartsWith("~/", StringComparison.Ordinal))
            {
                pattern = Path.Combine(home, pattern.Substring(2));
            }
            else if (pattern.StartsWith("fonts/", StringComparison.Ordinal))
            {
                string rest = pattern.Substring("fonts/".Length);
                foreach (string folder in FontFolders)
                {
                    foreach (string match in Expand(Path.Combine(folder, rest)))
                    {
                        yield return match;
                    }
                }
                yield break;
            }

            string dir = Path.GetDirectoryName(pattern);
            string file = Path.GetFileName(pattern);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                yield break;
            }
            if (file.IndexOf('*') < 0)
            {
                if (File.Exists(pattern)) yield return pattern;
                yield break;
            }
            string[] matches;
            try { matches = Directory.GetFiles(dir, file); } catch { yield break; }
            Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
            foreach (string m in matches) yield return m;
        }

        private static void LogSystemFontNames()
        {
            try
            {
                string[] names = FontEngine.GetSystemFontNames() ?? new string[0];
                var cjk = new List<string>();
                foreach (string n in names)
                {
                    if (n.IndexOf("CJK", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Gothic", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hei", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Hiragino", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("PingFang", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cjk.Add(n);
                        if (cjk.Count >= 12) break;
                    }
                }
                AssetsLibraryPlugin.Log.LogInfo($"[font] The system reports {names.Length} font families; CJK-looking ones: {(cjk.Count == 0 ? "(none)" : string.Join(", ", cjk))}");
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogInfo($"[font] Could not list system fonts: {ex.Message}");
            }
        }

        private static TMP_FontAsset AddFirstAvailable(string[] candidates, int pointSize, string label)
        {
            foreach (string family in candidates)
            {
                string source = "family:" + family;
                TMP_FontAsset existing;
                if (FaceBySource.TryGetValue(source, out existing))
                {
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {label} shares the {family} face already loaded.");
                    return existing;
                }
                TMP_FontAsset asset;
                try
                {
                    asset = TMP_FontAsset.CreateFontAsset(family, "Regular", pointSize);
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogInfo($"CJK fallback font '{family}' could not be created: {ex.Message}");
                    continue;
                }

                if (asset == null)
                {
                    // CreateFontAsset already logged which family name it could not resolve.
                    continue;
                }

                asset.name = "DragNWash_Fallback_" + family.Replace(" ", "");
                Registered.Add(asset);
                FaceBySource[source] = asset;
                AssetsLibraryPlugin.Log.LogInfo($"[font] {label}: loaded {family} (atlas point size {pointSize}).");
                FontAtlasCache.Restore(asset);
                return asset;
            }
            return null;
        }
    }
}
