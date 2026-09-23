using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace DragNWash.ModFramework.Mods
{
    // Which mod has which key. Every BepInEx plugin keeps its shortcuts in its
    // own config file and nobody asks anybody else, so two mods can quietly sit
    // on one key and each wonder why the other one answers as well.
    //
    // The framework can see all of them (a plugin's config entries of type
    // KeyboardShortcut), so it can at least say so. It is a report, not a rule:
    // a key is not taken away from anybody, because a player may well want one
    // key to do two things, and only they know that.
    internal static class KeyBindings
    {
        internal sealed class Bound
        {
            internal string Guid;
            internal string Mod;      // the name the Mods screen shows
            internal string Setting;  // "[Debug] DumpDialogueKey"
            internal KeyboardShortcut Shortcut;
            internal ConfigEntryBase Entry;
        }

        /// <summary>
        /// The other shortcut settings with the same main key as
        /// <paramref name="entry"/>, in any loaded plugin, its own one too.
        /// </summary>
        internal static List<Bound> SharingKeyWith(ConfigEntryBase entry)
        {
            if (entry == null || entry.SettingType != typeof(KeyboardShortcut))
            {
                return new List<Bound>();
            }
            KeyboardShortcut own;
            try
            {
                own = (KeyboardShortcut)entry.BoxedValue;
            }
            catch (Exception)
            {
                return new List<Bound>();
            }
            if (own.MainKey == KeyCode.None)
            {
                return new List<Bound>();
            }
            return All().Where(b => b.Shortcut.MainKey == own.MainKey && !ReferenceEquals(b.Entry, entry)).ToList();
        }

        /// <summary>
        /// A shortcut setting's shared key in one English line, for other
        /// mods and the Inspector (ModFramework.SharedKeyNote), or null when
        /// no other setting uses the key. The Mods screen shows the same thing
        /// in parts a language pack can translate.
        /// </summary>
        internal static string Note(ConfigEntryBase entry)
        {
            List<string> names = NamesSharing(entry);
            if (names == null)
            {
                return null;
            }
            string key = ((KeyboardShortcut)entry.BoxedValue).MainKey.ToString();
            return key + " is also used by " + string.Join(", ", names) + ". " + Answer(names.Count);
        }

        /// <summary>
        /// The settings that share the key of this one, each by the name it has
        /// on its own page with its mod's name after it when that is another
        /// mod, or null when none does.
        /// </summary>
        internal static List<string> NamesSharing(ConfigEntryBase entry)
        {
            List<Bound> others = SharingKeyWith(entry);
            if (others.Count == 0)
            {
                return null;
            }
            string own = OwnerOf(entry);
            return others.Select(b => ConfigItem.TitleOf(b.Entry) + (b.Guid == own ? "" : " (" + b.Mod + ")")).Distinct().ToList();
        }

        // Its own sentence, so a language pack can translate it whole.
        internal static string Answer(int others)
        {
            return others == 1 ? ModsMenu.TextBothAnswer : ModsMenu.TextAllAnswer;
        }

        // The plugin whose config file holds the entry, or null.
        private static string OwnerOf(ConfigEntryBase entry)
        {
            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
            {
                try
                {
                    if (entry.ConfigFile != null && ReferenceEquals(pair.Value?.Instance?.Config, entry.ConfigFile))
                    {
                        return pair.Key;
                    }
                }
                catch (Exception)
                {
                    // A plugin that failed to start has no config to compare.
                }
            }
            return null;
        }

        /// <summary>Every keyboard shortcut every loaded plugin has a setting for.</summary>
        internal static List<Bound> All()
        {
            var found = new List<Bound>();
            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
            {
                ConfigFile config = null;
                try
                {
                    config = pair.Value?.Instance?.Config;
                }
                catch (Exception)
                {
                    // A plugin that failed to start has no config to read.
                }
                if (config == null)
                {
                    continue;
                }
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in config)
                {
                    if (entry.Value == null || entry.Value.SettingType != typeof(KeyboardShortcut))
                    {
                        continue;
                    }
                    KeyboardShortcut shortcut;
                    try
                    {
                        shortcut = (KeyboardShortcut)entry.Value.BoxedValue;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (shortcut.MainKey == KeyCode.None)
                    {
                        continue;
                    }
                    found.Add(new Bound
                    {
                        Guid = pair.Key,
                        Mod = ModFramework.NameOf(pair.Key),
                        Setting = $"[{entry.Key.Section}] {entry.Key.Key}",
                        Shortcut = shortcut,
                        Entry = entry.Value,
                    });
                }
            }
            return found;
        }
    }
}
