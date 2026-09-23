using System;
using System.Collections.Generic;
using TMPro;

namespace DragNWash.ModFramework.Assets
{
    // Rasterizing the characters a language needs during load: which scripts
    // a text uses, walking the faces in the order TextMeshPro will, and
    // publishing that order as TextMeshPro's global fallback chain.
    public static partial class GameFonts
    {
        private static void PrepareLocale(string locale, IEnumerable<string> texts)
        {
            var chars = new HashSet<char>();
            bool kana = false, han = false, hangul = false, hebrew = false, thai = false, western = false;
            if (texts != null)
            {
                foreach (string text in texts)
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    foreach (char c in text)
                    {
                        // ASCII is covered by the game's own font assets and
                        // never reaches the fallback chain.
                        if (c <= 0x7F) continue;
                        // The zero-width space marks a line-break opportunity
                        // (the Thai pack has one between words); TextMeshPro
                        // lays it out without a glyph, so it is never rasterized.
                        if (c == '\u200B') continue;
                        chars.Add(c);
                        if (c >= 0x3040 && c <= 0x30FF) kana = true;
                        else if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF)) han = true;
                        else if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F)) hangul = true;
                        else if (c >= 0x0590 && c <= 0x05FF) hebrew = true;
                        else if (c >= 0x0E00 && c <= 0x0E7F) thai = true;
                        else if ((c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF)) han = true;
                        else western = true;
                    }
                }
            }
            if (chars.Count == 0) return;

            List<string> groups;
            if (!GroupsOfLocale.TryGetValue(locale, out groups))
            {
                groups = new List<string>();
                GroupsOfLocale[locale] = groups;
            }
            void Need(string g) { if (!groups.Contains(g)) groups.Add(g); }
            if (kana) Need(GroupJapanese);
            if (han) Need(PreferredHanGroup(locale));
            if (hangul) Need(GroupKorean);
            if (hebrew) Need(GroupHebrew);
            if (thai) Need(GroupThai);
            // A CJK face carries accented Latin and usually Cyrillic too; only
            // ask for a Latin face up front when nothing else is involved.
            if (western && groups.Count == 0) Need(GroupWestern);

            int pointSize = AssetsLibraryPlugin.AtlasPointSize != null ? AssetsLibraryPlugin.AtlasPointSize.Value : DefaultAtlasPointSize;
            foreach (string g in groups) LoadGroup(g, pointSize);

            List<char> remaining = WarmAlongChain(ChainFor(locale), chars, locale);

            // Characters no loaded face has: bring in the Latin face for them.
            if (remaining.Count > 0 && !LoadedGroups.Contains(GroupWestern))
            {
                AssetsLibraryPlugin.Log.LogInfo($"[font] {locale}: {remaining.Count} character(s) are in none of its faces; adding a Latin face.");
                LoadGroup(GroupWestern, pointSize);
                Need(GroupWestern);
                remaining = WarmAlongChain(ChainFor(locale), new HashSet<char>(remaining), locale);
            }
            if (remaining.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"[font] {locale}: {remaining.Count} character(s) have no glyph in any available font and will show as boxes.");
            }

            Warmed.UnionWith(chars);
        }

        // Walk the faces in the same order TMP will at runtime and rasterize
        // each character into the first face whose font has it. The faces
        // before that one lack the glyph in their font file, so TMP passes
        // over them without trying to add it - which is what keeps a runtime
        // upload from ever happening for these characters.
        private static List<char> WarmAlongChain(List<TMP_FontAsset> chain, HashSet<char> chars, string locale)
        {
            var remaining = new List<char>(chars);
            foreach (TMP_FontAsset face in chain)
            {
                if (remaining.Count == 0) break;
                HashSet<char> has = SetOf(HasOfAsset, face);
                HashSet<char> lacks = SetOf(LacksOfAsset, face);

                // A face restored from the font cache already holds most of
                // them; only the rest are rasterized. (TextMeshPro would call
                // them all missing if none of them needed rasterizing.)
                Dictionary<uint, TMP_Character> held = face.characterLookupTable;
                var ask = new List<char>();
                int cached = 0;
                foreach (char c in remaining)
                {
                    if (has.Contains(c) || lacks.Contains(c)) continue;
                    if (held != null && held.ContainsKey(c))
                    {
                        has.Add(c);
                        cached++;
                    }
                    else
                    {
                        ask.Add(c);
                    }
                }
                if (cached > 0)
                {
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {locale}: {cached} characters already in {face.name}.");
                }
                if (ask.Count > 0)
                {
                    try
                    {
                        face.TryAddCharacters(new string(ask.ToArray()), out string missing, includeFontFeatures: false);
                        var absent = new HashSet<char>();
                        if (!string.IsNullOrEmpty(missing))
                        {
                            foreach (char c in missing) absent.Add(c);
                        }
                        foreach (char c in ask)
                        {
                            if (absent.Contains(c)) lacks.Add(c); else has.Add(c);
                        }
                        AssetsLibraryPlugin.Log.LogInfo($"[font] {locale}: rasterized {ask.Count - absent.Count}/{ask.Count} characters into {face.name}.");
                        if (absent.Count < ask.Count)
                        {
                            FontAtlasCache.Changed(face);
                        }
                    }
                    catch (Exception ex)
                    {
                        AssetsLibraryPlugin.Log.LogInfo($"[font] Failed to rasterize into {face.name}: {ex.Message}");
                        foreach (char c in ask) lacks.Add(c);
                    }
                }

                var next = new List<char>();
                foreach (char c in remaining)
                {
                    if (!has.Contains(c)) next.Add(c);
                }
                remaining = next;
            }
            return remaining;
        }

        private static HashSet<char> SetOf(Dictionary<TMP_FontAsset, HashSet<char>> map, TMP_FontAsset face)
        {
            HashSet<char> set;
            if (!map.TryGetValue(face, out set))
            {
                set = new HashSet<char>();
                map[face] = set;
            }
            return set;
        }

        // This locale's scripts first, then every other loaded face in the
        // canonical order.
        private static List<TMP_FontAsset> ChainFor(string locale)
        {
            var order = new List<string>();
            List<string> groups;
            if (GroupsOfLocale.TryGetValue(locale ?? string.Empty, out groups))
            {
                order.AddRange(groups);
            }
            foreach (string g in CanonicalOrder)
            {
                if (!order.Contains(g)) order.Add(g);
            }

            var chain = new List<TMP_FontAsset>();
            foreach (string g in order)
            {
                TMP_FontAsset face;
                if (FaceOfGroup.TryGetValue(g, out face) && face != null && !chain.Contains(face))
                {
                    chain.Add(face);
                }
            }
            return chain;
        }

        // Make TMP's global fallback chain match ChainFor(locale), keeping
        // whatever the game had registered after ours.
        private static void PublishFallbacks(string locale)
        {
            if (Registered.Count == 0) return;
            List<TMP_FontAsset> final = ChainFor(locale);
            List<TMP_FontAsset> existing = TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>();
            foreach (TMP_FontAsset asset in existing)
            {
                if (asset != null && !Registered.Contains(asset)) final.Add(asset);
            }
            TMP_Settings.fallbackFontAssets = final;
        }

        private static string PreferredHanGroup(string locale)
        {
            string l = (locale ?? string.Empty).ToLowerInvariant();
            if (l.StartsWith("ja")) return GroupJapanese;
            if (l.StartsWith("zh-hant") || l.StartsWith("zh-tw") || l.StartsWith("zh-hk") || l.StartsWith("zh-mo")) return GroupTraditional;
            if (l.StartsWith("zh")) return GroupSimplified;
            if (l.StartsWith("ko")) return GroupKorean;
            return GroupSimplified;
        }
    }
}
