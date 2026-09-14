using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace DragNWash.ModFramework.Mods
{
    // A mod's settings page on the Mods screen, built from its BepInEx config
    // entries. Any BepInEx plugin has one; the mod does not need to know about
    // the framework. Setting a value saves the config file at once (BepInEx's
    // SaveOnConfigSet), so there is no separate Save step.
    internal sealed class ConfigItem
    {
        internal enum Kind
        {
            Toggle,
            Choice,
            Number,
            ReadOnly,
        }

        internal ConfigEntryBase Entry;
        internal Kind Type;
        internal object[] Choices;
        internal bool HasRange;
        internal double Min;
        internal double Max;
        internal bool IsInteger;

        internal string Section => Entry.Definition.Section;
        internal string Key => Entry.Definition.Key;
        internal string Description => Entry.Description?.Description;

        internal bool IsDefault => Equals(Entry.BoxedValue, Entry.DefaultValue);

        internal static List<ConfigItem> For(ModCatalog.Entry mod)
        {
            var items = new List<ConfigItem>();
            if (mod?.Guid == null || !mod.Loaded || !Chainloader.PluginInfos.TryGetValue(mod.Guid, out PluginInfo plugin))
            {
                return items;
            }
            ConfigFile config = plugin.Instance?.Config;
            if (config == null)
            {
                return items;
            }
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
            {
                ConfigEntryBase entry = pair.Value;
                if (entry == null || Hidden(entry))
                {
                    continue;
                }
                items.Add(Describe(entry));
            }
            return items
                .OrderBy(i => i.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Plugins that support BepInEx.ConfigurationManager can hide an entry with
        // a tag object carrying Browsable = false; honour it the same way.
        private static bool Hidden(ConfigEntryBase entry)
        {
            object[] tags = entry.Description?.Tags;
            if (tags == null)
            {
                return false;
            }
            foreach (object tag in tags)
            {
                if (tag == null)
                {
                    continue;
                }
                object browsable = tag.GetType().GetField("Browsable")?.GetValue(tag) ?? tag.GetType().GetProperty("Browsable")?.GetValue(tag, null);
                if (browsable is bool b && !b)
                {
                    return true;
                }
            }
            return false;
        }

        private static ConfigItem Describe(ConfigEntryBase entry)
        {
            var item = new ConfigItem { Entry = entry, Type = Kind.ReadOnly };
            Type type = entry.SettingType;
            AcceptableValueBase acceptable = entry.Description?.AcceptableValues;

            if (type == typeof(bool))
            {
                item.Type = Kind.Toggle;
            }
            else if (acceptable != null && acceptable.GetType().IsGenericType &&
                     acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueList<>))
            {
                var values = acceptable.GetType().GetProperty("AcceptableValues")?.GetValue(acceptable, null) as Array;
                if (values != null && values.Length > 0)
                {
                    item.Type = Kind.Choice;
                    item.Choices = values.Cast<object>().ToArray();
                }
            }
            else if (type.IsEnum)
            {
                item.Type = Kind.Choice;
                item.Choices = Enum.GetValues(type).Cast<object>().ToArray();
            }
            else if (IsNumber(type))
            {
                item.Type = Kind.Number;
                item.IsInteger = type != typeof(float) && type != typeof(double) && type != typeof(decimal);
                if (acceptable != null && acceptable.GetType().IsGenericType &&
                    acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
                {
                    item.HasRange = true;
                    item.Min = Convert.ToDouble(acceptable.GetType().GetProperty("MinValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                    item.Max = Convert.ToDouble(acceptable.GetType().GetProperty("MaxValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                }
            }
            return item;
        }

        private static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) ||
                   t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
                   t == typeof(ushort) || t == typeof(sbyte) || t == typeof(decimal);
        }

        internal string ValueText => Format(Entry.BoxedValue);

        internal string DefaultText => Format(Entry.DefaultValue);

        // Toggle values use the same "On"/"Off" words as the rest of the screen,
        // so translation packs cover them.
        internal string Format(object value)
        {
            if (value == null)
            {
                return "";
            }
            if (value is bool b)
            {
                return b ? ModsMenu.TextOn : ModsMenu.TextOff;
            }
            if (value is float f)
            {
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (value is double d)
            {
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // One step left (-1) or right (+1).
        internal void Step(int direction)
        {
            switch (Type)
            {
                case Kind.Toggle:
                    Set(!(bool)Entry.BoxedValue);
                    break;
                case Kind.Choice:
                {
                    int index = Array.FindIndex(Choices, c => Equals(c, Entry.BoxedValue));
                    int next = ((index < 0 ? 0 : index + direction) % Choices.Length + Choices.Length) % Choices.Length;
                    Set(Choices[next]);
                    break;
                }
                case Kind.Number:
                {
                    double current = Convert.ToDouble(Entry.BoxedValue, CultureInfo.InvariantCulture);
                    double step;
                    if (HasRange)
                    {
                        step = (Max - Min) / 20.0;
                        if (IsInteger)
                        {
                            step = Math.Max(1.0, Math.Round(step));
                        }
                    }
                    else
                    {
                        step = IsInteger ? 1.0 : 0.1;
                    }
                    double next = current + direction * step;
                    if (HasRange)
                    {
                        next = Math.Max(Min, Math.Min(Max, next));
                    }
                    if (IsInteger)
                    {
                        next = Math.Round(next);
                    }
                    else
                    {
                        next = Math.Round(next, 6);
                    }
                    Set(Convert.ChangeType(next, Entry.SettingType, CultureInfo.InvariantCulture));
                    break;
                }
            }
        }

        internal void ResetToDefault()
        {
            if (Type != Kind.ReadOnly)
            {
                Set(Entry.DefaultValue);
            }
        }

        private void Set(object value)
        {
            try
            {
                Entry.BoxedValue = value;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not change {Section}.{Key}: {ex.Message}");
            }
        }
    }
}
